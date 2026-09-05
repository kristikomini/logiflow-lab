# Module 25 — Distributed systems and integration

> The moment your process talks to a second process — a database, a broker, another team's API —
> a set of facts becomes true that were not true before. This module is those facts, and the
> handful of patterns that follow from them. In an interview this is the section where "writes
> endpoints" and "designs systems" separate.

Module 06 already builds an outbox; module 07 already handles concurrency in one database. This
module is the general theory those two are instances of.

---

## Microservices — the word this module is underneath

Adverts in this region ask for *"interesse per architetture a microservizi"*, and the only other
place the word appears in this course is module 08 saying what CQRS **is not**. So claim it here,
because everything below is the substance people mean by it.

**Microservices are an organisational solution, not a technical one.** What they buy is independent
deployment for independent teams. Every cost is technical and lands on everyone: each call can now
fail, `COMMIT` stops working across the boundary, and joins become somebody else's database. Worth
it at fifteen teams; at two you have paid for all of it and bought nothing.

**The default answer is a modular monolith** — real, enforced internal boundaries, one deployable,
one transaction. That is what this repository is: the architecture tests fail the build if Domain
reaches for Infrastructure (module 05), and it still commits atomically (module 07). Most teams
asking for microservices want *that*, because their actual complaint is tangled code rather than
teams blocking each other on releases.

**Cut by business capability, never by layer or by table.** Ordering, Shipping, Invoicing — not an
"API service" and a "data service". The practical signal is where the vocabulary changes: if
*order* means a picking list to the warehouse and an invoiceable event to finance, that is a
boundary. And each side keeping its own copy of the data it needs is correct, not a normalisation
failure — it is that service's own record of what it was told.

**The failure mode has a name.** Services split by layer must be released together, so you pay for
the network, the eventual consistency and the extra pipelines while still deploying in lockstep:
a *distributed monolith*. The test is whether you could deploy one of them on a Tuesday afternoon
without telling anybody.

So: the three facts in section 1 are not microservice facts. They are what becomes true the moment
there are two processes — and microservices are simply the architecture that makes you confront all
of them at once, on purpose.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 5 | [Sagas and eventual consistency](05-sagas-and-eventual-consistency.md) | compensation is not rollback |
| 6 | [Pushing to a browser: SignalR](06-real-time.md) | groups, the backplane, and why the push comes from the outbox |
| 7 | [gRPC, and when it is the right answer](07-grpc.md) | a compiled contract, field numbers, streaming, and what you give up |
| 8 | [Calling someone else's API](08-calling-someone-elses-api.md) | typed clients, a token that refreshes itself, and why handler order is a behaviour |

---

## 1. The three facts everything else follows from

**1. The network is not a method call.** A remote call can be slow, can fail, and — the one people
forget — **can succeed while you are told it failed**. A timeout tells you nothing about whether
the work happened.

```
   you ──── request ────► them        they process it, commit, and reply
   you ◄─── X                         the reply is lost
   you: "it failed."   them: "it succeeded."   Both are correct.
```

Everything in this module exists because of that diagram.

**2. You cannot atomically write to two systems.** No ordering of "save to the database" and
"publish to the broker" is safe:

```csharp
await db.SaveChangesAsync();          // committed
await bus.PublishAsync(orderPlaced);  // process dies here → the order exists, nobody was told

await bus.PublishAsync(orderPlaced);  // published
await db.SaveChangesAsync();          // fails → a message about an order that does not exist
```

This is **the dual-write problem**, and the resolution is always the same: make it one write.
📂 [`Persistence/Outbox/OutboxMessage.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxMessage.cs)
states it in its own XML docs, because the file is the answer.

**3. Exactly-once delivery does not exist.** You get *at-most-once* (fast, loses messages) or
*at-least-once* (safe, duplicates). Since losing an order is worse than processing one twice, you
choose at-least-once — and then you make the consumer **idempotent**, which is where "exactly-once
processing" actually comes from.

---

## 2. The outbox, and why it is the shape it is

```
   ┌─ ONE database transaction ────────────────────────┐
   │  UPDATE Orders SET Status = 'Submitted' ...       │
   │  INSERT INTO OutboxMessages (Id, Type, Payload)   │   ← the message is a ROW
   └───────────────────────────────────────────────────┘
                          │  commit: both, or neither
                          ▼
   OutboxProcessor (a BackgroundService) polls unprocessed rows,
   publishes each, and marks it done.
```

📂 [`OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs)

Three details in that file are worth taking with you:

- **It is a singleton, so it cannot inject a `DbContext`.** It takes `IServiceScopeFactory` and
  creates a scope per iteration. This is the captive-dependency rule from module 10, met in the
  wild.
- **An unhandled exception in `ExecuteAsync` stops the host** (since .NET 6). That is the right
  default — a silently dead background worker is worse than a crash — and it means the loop body
  must catch everything itself.
- **The type allow-list is a security control.** Deserializing a type *named in data* is how
  insecure-deserialization vulnerabilities work: an attacker who can write to the table names a
  gadget type whose construction has side effects. One `FrozenDictionary` closes it.

**The inbox is the mirror image.** On the consuming side, record the message id you have processed
in the same transaction as its effect. A duplicate delivery finds the id and does nothing.

**The cost of the outbox is latency and a table to clean up.** It is polled, so messages are
delivered within your polling interval, not instantly. Delete or archive processed rows, or the
table becomes the largest one in the database within a year.

---

## 3. Idempotency, concretely

> **Idempotent: doing it twice has the same effect as doing it once.**

You cannot bolt it on afterwards; it is a design property. In practice there are exactly four
techniques, and you should be able to name them:

1. **A natural key with a unique constraint.** `INSERT` an order with the caller's idempotency key
   as a unique column; a duplicate violates the constraint and you return the original result. The
   database does the work, which is the point — it is the only participant every writer passes.
2. **A processed-message table** (the inbox), written in the same transaction as the effect.
3. **Conditional updates.** `UPDATE ... WHERE Status = 'Pending'` affects zero rows the second
   time. A state machine that only allows legal transitions is idempotent almost for free —
   📂 [`Domain/Orders/Order.cs`](../../src/LogiFlow.Domain/Orders/Order.cs) is already this shape.
4. **Set the value, do not increment it.** `SET Quantity = 5` is idempotent; `SET Quantity =
   Quantity - 1` is emphatically not. When you must decrement, do it conditionally with a version
   check (module 07).

**For a public API**, take an `Idempotency-Key` header on every POST, store key → response, and
replay the stored response on a repeat. This is what Stripe does, and it is what a senior
candidate is expected to describe when asked "how do you stop a double-clicked payment button
charging twice?"

---

## 4. Retries, and the four ways they go wrong

Retrying is obvious. Retrying *correctly* is not:

```csharp
// Microsoft.Extensions.Http.Resilience / Polly
builder.Services.AddHttpClient<LogiFlowApiClient>()
    .AddStandardResilienceHandler();     // retry + circuit breaker + timeout + rate limiter
```

- **Only retry what is retryable.** A 500, a 503, a timeout, a transient network fault: yes. A 400
  or a 404: never — the answer will not change, and you have turned one bad request into five.
- **Exponential backoff, with jitter.** Fixed-interval retries from a thousand clients synchronise
  into a **thundering herd** that keeps the recovering service down. Jitter — a random spread — is
  not a refinement; it is the part that works.
- **Only retry idempotent operations.** Retrying a non-idempotent POST after a timeout is how a
  customer gets charged twice, and remember from §1 that a timeout does not mean it failed.
- **Cap the total time, not just the count.** Three retries with backoff behind a 30-second
  timeout each is a 2-minute request, and by then the caller has given up and retried — which is
  now *your* thundering herd.

Do not take the jitter point on trust — it is the one people quietly skip because it sounds like a
refinement. Watch it:

```bash
cd labs/Labs.Playground
dotnet run retry
```

240 clients, four retries each, the same delays both times, drawn on one shared scale. Fixed
backoff produces four towers of **240 requests inside a single 25ms window**; the same policy with
jitter peaks at **84** and is otherwise a flat smear. Jitter does not reduce the number of
requests — it removes the *correlation* between them, and the correlation was the harmful part.

**The circuit breaker** stops you hammering something already broken:

```
   CLOSED ──(failures exceed threshold)──► OPEN ──(after a delay)──► HALF-OPEN
      ▲                                     │ fail fast, do not call     │
      └──────────(a trial call succeeds)────┴────────────────────────────┘
```

The value is not just protecting *them*. It is protecting *you*: without it, every one of your
threads is blocked on a service that is not answering, and their outage becomes your outage. That
is a **cascading failure**, and the bulkhead pattern — a concurrency cap per dependency, so one
slow dependency cannot consume every thread — is the other half of the defence.

**Timeouts everywhere.** A call with no timeout is a call that can hang forever, and one hung call
holds a thread, a connection, and often a transaction. Every `HttpClient`, every command, every
`await` in a pipeline should be bounded — and the token should be threaded all the way down
(module 04).

---

## 5. Consistency, and how to talk about it

**Strong consistency** — everyone sees the write immediately. One database, one transaction. It is
what you should use whenever the boundary is inside your own service, and it is why an aggregate
is a *transactional* boundary (module 05).

**Eventual consistency** — the write propagates; readers may briefly see the old value. It is the
price of crossing a process boundary, and it is not a defect: the read model in a CQRS system, a
cache, a search index and a replica are all eventually consistent by design.

**The engineering question is never "is it consistent?" but "how stale may this be, and what does
the user see meanwhile?"** A dashboard: seconds are fine. A stock count at the moment of
reservation: no staleness at all, which is why that check belongs inside the aggregate's
transaction rather than in a read model.

**Sagas** are how a business transaction spans services when a distributed transaction is not
available (and two-phase commit, which is, is a bad idea for exactly the reasons in §1 — it holds
locks across a network):

```
   reserve stock  ──►  take payment  ──►  book courier
        │                   │                  │ fails
        ▼                   ▼                  ▼
   release stock  ◄──  refund payment  ◄── COMPENSATE, in reverse
```

Each step has a **compensating action**. Note that compensation is not a rollback: the refund is a
new, visible business fact, not an undo. Explaining that distinction is usually enough to
establish you have actually thought about it.

**Ordering.** Most brokers guarantee order only within a partition. If two messages about the same
order can be processed out of sequence, either partition by the aggregate id so they land on the
same consumer in order, or make the handlers order-insensitive (a version number the consumer
checks, and stale messages are dropped).

---

## 6. Observability across a boundary

Everything in module 11 becomes non-negotiable the moment there are two services. The one idea:

**Trace context propagates.** `traceparent` (W3C) flows over HTTP and through message headers, so
one request across five services is one trace with a shared trace id. `.NET`'s `Activity` does
this automatically for `HttpClient` and ASP.NET Core; for a custom transport, you propagate it
yourself or the trace stops at your door.

**A correlation id on every log line**, and on the error you return to the user (module 24 does
this in the `ProblemDetails`). Without it, "the thing failed at 14:32" is unanswerable across five
services.

**Health checks distinguish liveness from readiness.** *Liveness*: am I alive — restart me if not.
*Readiness*: can I serve traffic — take me out of the load balancer if not. Conflating them is
how a dependency's outage causes an infinite restart loop of a perfectly healthy service.

---

## 7. Do this

1. **Read the outbox end to end** — 📂 `OutboxMessage.cs`, then `OutboxProcessor.cs`, then
   `DomainEventDispatchingInterceptor.cs`. The XML docs are the lesson; the code is the proof.

2. **Break it deliberately.** In `OutboxProcessor`, throw after publishing but before marking the
   row processed. Restart. Watch the message go out twice — that is at-least-once delivery,
   demonstrated rather than asserted, and now the case for an idempotent consumer is not
   theoretical.

3. **Make the consumer idempotent.** Add a `ProcessedMessages` table and write the message id in
   the same transaction as the effect. Run the previous experiment again; the duplicate now does
   nothing.

4. **Design one on paper**, which is the actual interview: *"A customer places an order. Stock must
   be reserved in the warehouse service, payment taken by a third party, and a courier booked.
   What happens if the courier booking fails?"* Write the saga, name each compensating action, and
   say which steps are idempotent and why.

---

## 8. Golden rules

1. **A timeout tells you nothing about whether the work happened.** The reply can be lost after
   the commit — which is why every retryable operation must be idempotent.
2. **You cannot atomically write to two systems.** Save-then-publish loses messages;
   publish-then-save invents them. Make it one write: the outbox.
3. **Exactly-once delivery does not exist.** Choose at-least-once and make the consumer
   idempotent; that is what "exactly-once processing" really means.
4. **Idempotency is designed in, not added.** Unique constraints, a processed-message table,
   conditional updates, and setting values rather than incrementing them.
5. **`SET x = 5` is idempotent; `SET x = x - 1` is not.** When you must decrement, do it with a
   version check.
6. **Never retry a non-idempotent operation, and never retry a 4xx.** The answer will not change,
   and you have multiplied one bad request.
7. **Exponential backoff with jitter.** Fixed intervals synchronise thousands of clients into a
   thundering herd that keeps the recovering service down.
8. **Cap total elapsed time, not just the retry count.** Otherwise the caller times out and
   retries on top of you.
9. **A circuit breaker protects the caller as much as the callee.** Without one, their outage
   consumes all your threads and becomes your outage.
10. **Every remote call has a timeout, and the cancellation token is threaded all the way down.**
11. **Eventual consistency is a design decision, not a defect** — but "how stale may this be?" is
    a question you must answer explicitly, per read.
12. **A saga compensates; it does not roll back.** A refund is a new business fact, visible to the
    customer, not an undo.
13. **Order is guaranteed only within a partition.** Partition by aggregate id, or make handlers
    order-insensitive with a version check.
14. **Propagate the trace context.** One request across five services must be one trace, or you
    are debugging by timestamp.
15. **Liveness and readiness are different questions.** Conflating them turns a dependency's
    outage into a restart loop.
16. **Microservices buy independent deployment for independent teams.** The benefit is
    organisational; every cost is technical. At two teams you have paid for all of it and bought
    nothing — the default is a modular monolith.
17. **Cut services by business capability, never by layer or table.** The seam is where the
    vocabulary changes; services that must be released together are a distributed monolith.

---

## 9. Interview questions

**"How do you save to the database and publish a message atomically?"**
You do not — you cannot write atomically to two systems. You write the message as a row in the
same transaction as the state change (the outbox), and a background worker publishes it
afterwards. That converts a distributed atomicity problem into a local one, at the cost of latency
and at-least-once delivery.

**"Then how do you avoid processing a message twice?"**
You do not avoid delivery twice — you make processing it twice harmless. A unique constraint on a
natural key, a processed-message table written in the same transaction as the effect, or a
conditional update that affects zero rows the second time.

**"A payment API times out. Do you retry?"**
Only if the operation is idempotent, because a timeout does not tell you whether it succeeded.
For payments that means sending an idempotency key so the provider recognises the retry and
returns the original result rather than charging again.

**"What does a circuit breaker do, and who does it protect?"**
After a threshold of failures it opens and fails fast without calling the dependency, then
half-opens to test recovery. It protects the failing service from being hammered, and it protects
the *caller* from having every thread blocked on something that is not answering — which is how a
single dependency's outage cascades.

**"Why jitter?"**
Because identical backoff schedules across many clients synchronise their retries into a spike
that knocks the recovering service back down. Randomising the delay spreads the load, and it is
the part that makes backoff actually work.

**"Explain eventual consistency to a product manager."**
The change is definitely saved, and everyone will see it — some views may show the previous value
for a moment. Then the engineering follow-up: which views, for how long, and does anything
important depend on reading it immediately? If it does, that read belongs inside the transaction.

**"What is a saga, and how is it different from a transaction?"**
A sequence of local transactions across services, each with a compensating action if a later step
fails. Unlike a transaction it is not atomic and not isolated — intermediate states are visible —
and compensation is a new business fact, such as a refund, rather than a rollback.

---

## Next

→ [Module 26 — Design patterns and SOLID, in this codebase](../module-26-patterns-and-solid/)
