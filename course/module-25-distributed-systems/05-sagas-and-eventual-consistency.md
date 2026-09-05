# 5. Sagas and eventual consistency

> Part of [Module 25 — Distributed systems and integration](README.md), section 5.
> Previous: [4. Retries, and the four ways they go wrong](README.md#4-retries-and-the-four-ways-they-go-wrong) ·
> Next: [6. Observability across a boundary](README.md#6-observability-across-a-boundary)

---

> **Note on where this lives.** The source code originally pointed at this chapter as
> `module-12-testing/06-sagas-and-eventual-consistency.md`. Sagas are not a testing topic, so it
> lives here instead, next to the outbox and idempotency material it depends on.

---

[Aggregates](../module-05-clean-architecture/03-aggregates.md) established the rule: one transaction,
one aggregate. That is fine while everything is in one database. The moment a business operation
spans two services — or two databases — the rule has a consequence you cannot avoid:

**There is no transaction that covers both.** Two-phase commit exists, and essentially nobody uses it
across services: it holds locks across a network, blocks everybody when the coordinator dies, and
does not work at all against most cloud databases and message brokers.

So the business operation has to be broken into steps that each commit independently — and something
has to handle a step failing after earlier ones already committed. That something is a **saga**.

## What a saga actually is

A sequence of local transactions, each with a **compensating action** that semantically undoes it.

```
Place order:
  1. Order        → Submitted        compensate: cancel the order
  2. Inventory    → reserve stock    compensate: release the reservation
  3. Payment      → charge the card  compensate: refund
  4. Shipping     → book a courier   compensate: cancel the booking
```

If step 3 fails, the saga runs the compensations for steps 2 and 1, in reverse.

**Compensation is not rollback.** A rollback makes it as though nothing happened. A refund is a *new
transaction* that leaves both the charge and the refund on the customer's statement. Some steps
cannot be compensated at all — an email that has been sent is sent. That asymmetry is the honest
difference between a transaction and a saga, and it is what an interviewer is checking you understand.

## Orchestration or choreography

**Choreography** — each service reacts to events and publishes its own. No central coordinator.

```
Orders     → publishes OrderSubmitted
Inventory  → hears it, reserves, publishes StockReserved
Payments   → hears that, charges, publishes PaymentTaken
Shipping   → hears that, books
```

Simple to start, and it has one serious weakness: **nobody knows the state of the whole process.**
When an order is stuck, you find out by reading four services' logs and reconstructing what happened.
Adding a step means changing two services that must agree.

**Orchestration** — one component owns the sequence and tells each service what to do.

```
OrderSaga: Submit → Reserve → Charge → Ship
           and on failure at step N, compensate N-1 … 1
```

The flow is in one readable place, the state is queryable ("which orders are stuck at payment?"), and
adding a step is one edit. The cost is a component that must itself be durable — it has to survive
its own restart mid-saga, which means persisting the saga's state.

**The rule of thumb:** choreography for two or three steps, orchestration past that, and orchestration
immediately if anyone will ever ask "where is order 1042 in the process?".

## Eventual consistency, and saying it out loud

A saga means that between step 1 and step 4 the system is **inconsistent in a way that is visible**:
the order exists, the stock is not yet reserved, the card is not yet charged.

That is not a bug. It is the price, and the job is to make it **explicit rather than hidden**:

- **Model the intermediate states.** `Submitted` → `AwaitingPayment` → `Confirmed` is honest.
  `Submitted` meaning three different things depending on timing is not — which is why
  [state machines as data](../module-05-clean-architecture/06-state-machines.md) matters here.
- **Show it in the UI.** "Payment pending" is fine. A screen that implies the order is complete when
  it is not produces support calls.
- **Bound the window.** A saga stuck for thirty seconds is normal; stuck for two hours is an
  incident. Alert on age, not just on failure.

And know where **not** to use it. This repository deliberately reserves stock **inside** the order's
transaction rather than as a saga step, because the inconsistency window is a window in which the
same unit can be sold twice — argued in
[module 07 section 4](../module-07-sql-and-transactions/05-inventory-concurrency.md). One database
means you can still choose a transaction, and you should when correctness is worth more than the
decoupling.

## The mechanics you already have

A saga is built from the three things earlier sections established, which is why it comes last:

**The [outbox](../module-06-efcore/07-outbox-pattern.md)** — each step's state change and the message
announcing it commit together, so a step cannot happen without being announced or vice versa.

**[Idempotency](README.md#3-idempotency-concretely)** — delivery is at-least-once, so every step and
every compensation will eventually run twice. A compensation that refunds twice is worse than the
original bug. Deduplicate on the message id, or make the operation naturally idempotent.

**Retries with backoff** — a step failing transiently should be retried before compensating. The
distinction between *transient* and *permanent* failure is what decides between retrying and
unwinding, and getting it wrong means either giving up too early or hammering a broken service.

Add one thing sagas need specifically: **a timeout per step.** A step that never returns must
eventually be treated as failed, or the saga waits forever and the order is stuck with nobody
noticing.

## Testing them

The part most teams skip, and the reason this chapter was originally filed under testing:

- **Each step and each compensation, in isolation.** Ordinary
  [handler tests](../module-12-testing/02-unit-testing-handlers.md).
- **The unhappy paths, explicitly.** Fail step 3 and assert that 2 and 1 were compensated. This is
  the test that matters, and it is the one that does not exist in most codebases.
- **Duplicate delivery.** Deliver every message twice and assert the end state is identical.
- **Out-of-order delivery**, if the transport does not guarantee ordering — which it usually does
  not.

If you cannot write the "step 3 fails" test, you do not yet know what your system does in that case —
and it *will* happen.

## The mistakes

**Treating compensation as rollback.** A refund is a new fact, not an erasure. Say so in the model
and in the UI.

**No timeout per step.** Sagas that hang forever with nobody alerted.

**Choreography past three steps.** Nobody can answer "where is it stuck?".

**Non-idempotent compensations.** Duplicate refunds.

**Using a saga where a transaction would do.** If it is one database and consistency matters, use a
transaction. Distributed-systems machinery is not a sign of sophistication when it is unnecessary.

**Hiding eventual consistency from the user.** The window exists; design for it rather than pretending.

## Try it

Follow one saga you already have. `OrderSubmitted` is written to the outbox in the order's
transaction; `OutboxProcessor` publishes it; a handler reacts. That is step 1 of a saga with the
durability already solved.

Then write the missing half on paper: what compensates a submitted order if the next step fails, who
runs it, how it knows, and what the customer sees while it is happening. Being able to answer those
four questions is what the pattern is actually for.

## What to remember

- No transaction spans services, and two-phase commit is not the answer in practice.
- A saga is local transactions plus compensating actions, run in reverse on failure.
- Compensation is a new transaction, not a rollback — and some steps cannot be compensated.
- Choreography for two or three steps; orchestration when you need to ask where it is stuck.
- Model the intermediate states, show them in the UI, and alert on saga age.
- Sagas are built from the outbox, idempotency and bounded retries — plus a per-step timeout.
- Test the failure paths and duplicate delivery, or you do not know what happens.
- If one database can give you a transaction and correctness matters, take the transaction.

**Code:** [`Outbox/`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/) ·
[`OrderSubmittedHandlers.cs`](../../src/LogiFlow.Application/Features/Orders/EventHandlers/OrderSubmittedHandlers.cs)

**Next:** [6. Observability across a boundary](README.md#6-observability-across-a-boundary)
