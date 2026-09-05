# Module 08 — CQRS and the mediator pattern

> How to organise application logic so a codebase stays workable at 200 use cases instead of
> collapsing into a `Services/` folder nobody can navigate.

---

## In this module

The sections below are summaries. Each links to a chapter that goes further — the code, the traps,
and the interview answer.

| | Chapter | |
|---|---|---|
| 1 | [What CQRS actually is](01-what-cqrs-actually-is.md) | one claim, and four things it is not |
| 1 | [Read models and projections](04-read-models-and-projections.md) | why the read side may drop to SQL |
| 2 | [Vertical slices](02-vertical-slices.md) | group by feature, not by artefact type |
| 3 | [Building a mediator](03-building-a-mediator.md) | the reflection trick MediatR uses |
| 4 | [Pipeline behaviours](05-pipeline-behaviors.md) | decorators around every request, and why order is behaviour |

---

## 1. What CQRS actually is

**Command Query Responsibility Segregation**: reading and writing are different enough to deserve
different models.

| | Command | Query |
|---|---|---|
| Changes state? | yes | no |
| Named | imperative — `SubmitOrder` | interrogative — `GetOrderById` |
| Enforces business rules | yes | no |
| Runs in a transaction | yes | no |
| Returns | as little as possible (an id) | a purpose-built DTO |
| Can be cached | no | yes |

**What CQRS is not:** two databases, event sourcing, or microservices. Those combine well with it
and are constantly confused for it. At its core it is these two interfaces:

📂 [`Application/Abstractions/Messaging/Messages.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Messages.cs)

### Why bother?

Because the shapes genuinely diverge.

- **Submitting an order** must load the full `Order` aggregate with its lines to enforce
  invariants.
- **Rendering an order list** needs six columns from three tables and no behaviour at all.

Force both through one model and you get either slow reads or an anaemic domain. In this
codebase:

- writes go through `IOrderRepository` → full aggregates, tracked;
- reads go through `IOrderQueries` → flat DTOs projected in SQL, untracked.

```
   WRITE   POST /api/orders/{id}/submit
   ────────────────────────────────────────────────────────────────────────────────────────
   endpoint ─► Dispatcher ─► behaviours ─► handler ─► IOrderRepository ─► Order AGGREGATE
                                                                          tracked, with its
                                                                          lines, invariants
                                                                          and domain events
                                                                              │
                                                                    SaveChanges, one transaction

   READ    GET /api/orders?status=Submitted&page=2
   ────────────────────────────────────────────────────────────────────────────────────────
   endpoint ─► Dispatcher ─► behaviours ─► handler ─► IOrderQueries ─────► AsNoTracking()
                                                                          + Select(…) in SQL
                                                                              │
                                                                          a flat DTO. No
                                                                          aggregate is ever
                                                                          constructed

   same pipeline, two completely different models — that is the whole of CQRS
```

📂 [`Features/Orders/IOrderQueries.cs`](../../src/LogiFlow.Application/Features/Orders/IOrderQueries.cs)

**Forcing reads through the repository is the mistake that makes people conclude "Clean
Architecture is slow".** A 25-row list would load 25 aggregates with all their children and
register them all with the change tracker — to display six fields.

### The cost, stated honestly

The read side bypasses the domain model, so logic expressed in C# properties (`Order.Total`) has
to be re-expressed in the projection. That duplication is real. The mitigation is a test that
builds an order through the domain, reads it back through the projection, and asserts the totals
agree. See the comment on `OrderQueries.CalculateShipping`.

---

## 2. Vertical slices

Command, validator and handler live in **one file**:

📂 [`Features/Orders/CreateOrder.cs`](../../src/LogiFlow.Application/Features/Orders/CreateOrder.cs)

This is the opposite of the layer-per-folder habit (`Commands/`, `Validators/`, `Handlers/`).
The reason: you change those three things together, always. Splitting them means three files open
and three navigations per change — and deleting a feature becomes archaeology.

**Everything that changes together, lives together.** When a feature is retired, one file goes.

---

## 3. Building a mediator

📂 [`Abstractions/Messaging/Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs)

This codebase does not use MediatR. It implements the pattern in ~200 lines, for three reasons —
in order of honesty:

1. **You should know how it works.** Interviewers ask it.
2. **MediatR is no longer free for commercial use** (v13 moved to a paid licence). Plenty of teams
   have had to answer this question recently.
3. **It is genuinely small.** Not every abstraction needs a dependency.

### The problem it solves

```csharp
Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request);
```

At compile time we know `TResponse`. But the handler we need is
`IRequestHandler<SubmitOrderCommand, Result>` — and `SubmitOrderCommand` is only known at
*runtime*, from `request.GetType()`. You cannot write
`GetRequiredService<IRequestHandler<request.GetType(), TResponse>>()`; generics are resolved by
the compiler.

### The standard solution

1. Build the closed type with `Type.MakeGenericType(requestType, responseType)`.
2. Instantiate it once via `Activator.CreateInstance`.
3. Call it through a **non-generic base** where `TResponse` *is* statically known.
4. **Cache it forever**, keyed on request type.

That cache is what makes the reflection irrelevant — after the first `SubmitOrderCommand`,
dispatching one is a dictionary lookup and a virtual call.

This maps one-to-one onto MediatR, so the concepts transfer: `IDispatcher` ≈ `IMediator`,
`IRequestHandler` identical, `IPipelineBehavior` identical, `IEventHandler` ≈
`INotificationHandler`.

---

## 4. Pipeline behaviours

Russian dolls around every handler:

```
request  ──► Logging ──► Validation ──► Caching ──► Transaction ──► Handler
response ◄── Logging ◄── Validation ◄── Caching ◄── Transaction ◄── Handler
```

Same idea as ASP.NET Core middleware, one layer down. It is why **no handler in this codebase
contains a try/catch, a validation call, or a `SaveChangesAsync`.**

📂 [`Behaviors/`](../../src/LogiFlow.Application/Behaviors/) ·
[`DependencyInjection.cs`](../../src/LogiFlow.Application/DependencyInjection.cs)

### Order is behaviour, not style

Each position is a decision:

1. **Logging outermost** — its timing must cover everything, including validation and the cache
   lookup. Inside, your "request took 4ms" excludes the 300ms you did not know about.
2. **Validation before caching** — never cache anything derived from an invalid request, and
   never let a malformed key reach Redis.
3. **Caching before the transaction** — a cache hit must not open a database transaction. That is
   most of the point of caching.
4. **Transaction innermost** — open for the shortest possible time. Every layer outside it that
   holds it open is a lock someone else is waiting on.

**Try it:** swap `ValidationBehavior` and `TransactionBehavior` in `RegisterPipeline`, send an
invalid request, and watch a transaction open to run validation that was about to reject it.

### Two implementation details worth stealing

**The `ICommandMarker` trick.** `ICommand` and `ICommand<T>` are unrelated interfaces to the CLR,
so a runtime check would need reflection on every request. One shared non-generic marker turns it
into `request is ICommandMarker` — a couple of instructions. This pattern recurs whenever you
have a generic interface family that runtime code must recognise.

**Building the chain inside-out.** In `RequestHandlerWrapper`, note that both the behaviour and
the `next` delegate are copied into locals inside the loop. Capturing the loop variable `i`
directly would give every closure the *same* variable — the classic closure trap, which C# 5
fixed for `foreach` but **not** for `for`. See module 02.

---

## 5. Validation: input vs business rules

`ValidationBehavior` runs every registered `IValidator<T>` before the handler. It handles
**input** validation: shape, ranges, required fields — things checkable from the request alone.

It must **never** contain business rules.

| Belongs in the validator | Belongs in the domain |
|---|---|
| Quantity must be positive | Customer's credit limit allows this order |
| Email must look like an email | This email is not already registered |
| Currency is three letters | The product is priced in the order's currency |

The line is: **shape in the validator, state in the handler and domain.** A validator that
queries the database has quietly become a handler — and its result is stale by the time the
handler runs anyway, since nothing holds a lock between them.

**Note also that validation short-circuits.** `next()` is never called, so the handler never
runs and `TransactionBehavior` (registered inside it) never opens a transaction.

---

## 6. Caching

Opt-in, never automatic — a behaviour that cached every query would eventually cache one that
must be fresh, and that bug is invisible until a customer sees stale data.

```csharp
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDetailDto>, ICacheableQuery
{
    public string CacheKey => $"order:detail:{OrderId}";
    public TimeSpan? CacheDuration => TimeSpan.FromSeconds(60);
}
```

**The cache key must include every parameter that changes the result** — and the tenant or user
id whenever results are scoped to a caller. Leaking one customer's data to another through a
shared cache key is a real, regularly-shipped security bug.

Note the TTLs are chosen per query, not copied:

| Query | TTL | Why |
|---|---|---|
| `GetProductBySku` | 10 min | catalogue changes rarely, is read constantly |
| `GetOrderById` | 60 s | status moves through fulfilment; users refresh |
| `GetSalesReport` | 15 min | expensive aggregate, barely moves minute to minute |
| `SearchOrders` | **not cached** | eight filters ⇒ almost every key is unique |
| `GetStockLevel` | **never cached** | stale stock ⇒ orders that cannot be fulfilled |

---

## 7. Read it in order

To follow one request end to end:

1. [`Api/Endpoints/OrderEndpoints.cs`](../../src/LogiFlow.Api/Endpoints/OrderEndpoints.cs) — binds and dispatches
2. [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs) — resolves the handler, builds the pipeline
3. [`LoggingBehavior.cs`](../../src/LogiFlow.Application/Behaviors/LoggingBehavior.cs) → [`ValidationBehavior.cs`](../../src/LogiFlow.Application/Behaviors/ValidationBehavior.cs) → [`TransactionBehavior.cs`](../../src/LogiFlow.Application/Behaviors/TransactionBehavior.cs)
4. [`SubmitOrder.cs`](../../src/LogiFlow.Application/Features/Orders/SubmitOrder.cs) — the handler, all six lines of it
5. [`Order.Submit()`](../../src/LogiFlow.Domain/Orders/Order.cs) — the actual rule, raising an event
6. [`DomainEventDispatchingInterceptor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Interceptors/DomainEventDispatchingInterceptor.cs) — dispatches it inside the transaction
7. [`ReserveStockOnOrderSubmitted`](../../src/LogiFlow.Application/Features/Orders/EventHandlers/OrderSubmittedHandlers.cs) — reacts

Notice how little each step does. That is the goal.

---

## 8. Golden rules

> The card.

1. **Commands change state and return as little as possible; queries change nothing and return a
   purpose-built DTO.** Naming follows: imperative for one, interrogative for the other.
2. **CQRS is not two databases, not event sourcing, not microservices.** It is two models. Being
   able to say that is half the interview question.
3. **Never load aggregates to render a list.** Twenty-five aggregates with all their children,
   registered with the change tracker, to display six columns, is why people conclude that Clean
   Architecture is slow.
4. **Everything that changes together lives together.** Command, validator and handler in one
   file; retiring a feature is deleting a file, not archaeology.
5. **Pipeline order is behaviour, not style.** Logging outermost so its timing covers everything;
   validation before caching; caching before the transaction; the transaction innermost so it is
   held for the shortest possible time.
6. **Validators check shape; the domain checks state.** A validator that queries the database has
   become a handler — and its answer is stale by the time the handler runs anyway.
7. **Cache by opt-in, never automatically.** A behaviour that cached everything would eventually
   cache something that must be fresh, and that bug is invisible until a customer sees it.
8. **A cache key must contain every parameter that changes the answer** — including the tenant or
   user when the result is scoped to a caller. Leaking one customer's data to another through a
   shared key is a regularly-shipped security bug.
9. **Some things are never cached.** Stock levels, above all: stale stock means orders you cannot
   fulfil.
10. **Handlers stay `internal`.** Nothing outside the Application layer should be able to call one
    directly and skip the pipeline.

---

## 9. Interview questions

**"What is CQRS?"**
Separating the model used for writes from the model used for reads, because they have different
requirements. Writes need the full aggregate to enforce invariants; reads need a flat projection.
Add that it does *not* require two databases or event sourcing — that distinction is what they
are testing.

**"How does MediatR resolve a handler when `Send` only knows the response type?"**
Reflection plus a cached generic wrapper: build the closed handler type with `MakeGenericType`,
instantiate it once, invoke through a non-generic base, cache by request type. Point at
`Dispatcher.cs` — being able to say "I implemented it" is a strong answer.

**"What are pipeline behaviours for, and does order matter?"**
Cross-cutting concerns — logging, validation, transactions, caching — written once instead of in
every handler. Order matters enormously: transaction innermost so it is held briefly, validation
outside it so an invalid request never opens one, logging outermost so timings include
everything.

**"Where does validation belong?"**
Input validation in a validator running in the pipeline; business rules in the domain. If a check
needs to load state, it is a business rule.

**"Isn't a mediator just an over-complicated method call?"**
Fair challenge, and worth conceding partly. It adds indirection and makes call-sites harder to
navigate. It buys you a single place to add cross-cutting behaviour, uniform handler shape, and
one-dependency-per-use-case instead of a service with fifteen. For a small app, call the service
directly.

---

## Next

→ [Module 09 — Advanced LINQ](../module-09-advanced-linq/)
