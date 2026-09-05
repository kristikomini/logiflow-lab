# Module 05 — Clean Architecture and Domain-Driven Design

> The module about *where code goes* and *why it matters*. This is what interviewers mean when
> they ask about "architecture", and it is what separates a mid-level developer from a senior one.

---

## In this module

The sections below are summaries. Each links to a chapter that goes further — the code, the traps,
and the interview answer.

| | Chapter | |
|---|---|---|
| 2 | [Entities vs value objects](02-entities-and-value-objects.md) | identity, immutability, and the EF proxy equality trap |
| 3 | [Aggregates — the transactional boundary](03-aggregates.md) | where to draw the line, and one transaction per aggregate |
| 4 | [`Result<T>` instead of exceptions](05-result-vs-exceptions.md) | expected failure is a return value |
| 5 | [Domain events](04-domain-events.md) | facts in the past tense, dispatched on save |
| 6 | [State machines as data](06-state-machines.md) | the lifecycle in one table |

---

## 1. One rule: dependencies point inward

```
        ┌─────────────────────────────────────────┐
        │  Api          (HTTP, JSON, auth)        │  ← knows everything
        │  ┌───────────────────────────────────┐  │
        │  │  Infrastructure  (EF, SQL, Redis) │  │
        │  │  ┌─────────────────────────────┐  │  │
        │  │  │  Application  (use cases)   │  │  │
        │  │  │  ┌───────────────────────┐  │  │  │
        │  │  │  │  Domain (the rules)   │  │  │  │  ← knows nothing
        │  │  │  └───────────────────────┘  │  │  │
        │  │  └─────────────────────────────┘  │  │
        │  └───────────────────────────────────┘  │
        └─────────────────────────────────────────┘
```

Open [`src/LogiFlow.Domain/LogiFlow.Domain.csproj`](../../src/LogiFlow.Domain/LogiFlow.Domain.csproj).
There is **not a single `<PackageReference>` or `<ProjectReference>`**. That emptiness is the
architecture. Business rules cannot accidentally depend on EF Core because those types are not
reachable from there.

### It is enforced, not hoped for

```bash
dotnet test tests/LogiFlow.ArchitectureTests
```

Eight tests over the *dependency graph*. Add `using Microsoft.EntityFrameworkCore;` to a domain
class and the build goes red. Conventions decay; tests do not.

📂 [`tests/LogiFlow.ArchitectureTests/LayeringTests.cs`](../../tests/LogiFlow.ArchitectureTests/LayeringTests.cs)

### The point of the inversion

The Application layer needs to save an order. It must not know about SQL. So it *declares* what
it needs and Infrastructure supplies it:

```csharp
// Application — declares the need
public interface IOrderRepository { void Add(Order order); }

// Infrastructure — supplies it
public sealed class OrderRepository(LogiFlowDbContext context) : IOrderRepository { ... }
```

The arrow now points inward: Infrastructure depends on Application, not the reverse. That is
**dependency inversion** — not "use an IoC container", which is a different thing people
routinely confuse it with.

---

## 2. Entities vs value objects

The distinction that organises the whole Domain layer.

| | Entity | Value object |
|---|---|---|
| Defined by | **identity** | **attributes** |
| Equality | same `Id` | all fields equal |
| Mutable? | yes, through behaviour | never |
| Example | `Order`, `Customer` | `Money`, `Address`, `Sku` |

Order #1234 is still Order #1234 after you change its shipping address — identity persists
through change. €10 is €10; there is no "which ten euros".

**The classic beginner mistake** is modelling `Address` as an entity with an `AddressId`. You get
a pointless join table, orphan rows, and code that cannot answer "are these two addresses the
same?" without a database round trip.

📂 [`Domain/ValueObjects/Money.cs`](../../src/LogiFlow.Domain/ValueObjects/Money.cs) — and note two
things it enforces that a `decimal` cannot:

- **Never `double` for money.** `0.1 + 0.2 == 0.30000000000000004` in binary floating point.
  `decimal` is base-10 and stores it exactly.
- **An amount without a currency is meaningless.** Bundling them makes `euros + dollars` a
  runtime guard instead of a silently wrong answer.

`Money.Allocate(int parts)` is worth reading on its own — splitting €10 three ways without losing
a cent is a genuine interview question.

---

## 3. Aggregates — the transactional boundary

An aggregate is **a consistency boundary, not a folder**. Everything inside it is saved together,
atomically, and its invariants are true at the end of every transaction.

`Order` is a root; `OrderLine` is not. You can never load or save an `OrderLine` alone — only
through its `Order`. That is what lets `Order` guarantee "the total always equals the sum of the
lines".

```
   ┌── Order  (aggregate ROOT) ─────────────────────┐        ┌── Customer (another root) ──┐
   │   Status · ShippingAddress · RowVersion        │        │                             │
   │   ┌──────────────┐  ┌──────────────┐           │        │                             │
   │   │  OrderLine   │  │  OrderLine   │           │◄─ id ─►│                             │
   │   └──────────────┘  └──────────────┘           │        │                             │
   │                                                │        │                             │
   │   loaded and saved as ONE unit, atomically     │        │  its own transaction        │
   │   invariants hold at the end of every save     │        │  its own invariants         │
   └────────────────────────────────────────────────┘        └─────────────────────────────┘

   Inside one box: everything is reached through the root. There is no repository for OrderLine.
   Across two boxes: an ID. Never an object reference — that is how one transaction quietly
   starts mutating two aggregates and neither can guarantee its own invariants any more.
```

### The two rules that matter

**(a) Aggregates reference each other by ID, never by object reference.**

```csharp
public CustomerId CustomerId { get; private set; }   // ✅
public Customer Customer { get; private set; }       // ❌ — now you can mutate two aggregates
```

📂 [`Infrastructure/.../OrderConfiguration.cs`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs)
maps the FK column but deliberately gives EF **no navigation property**, so the shortcut is not
available even by accident.

**(b) Keep them small.** If two pieces of data need not be consistent *immediately*, they belong
in different aggregates and can be reconciled with an event. "Stock must drop the instant the
order is confirmed" is a claim worth challenging — usually eventual consistency is both correct
and much faster.

### Encapsulation in practice

```csharp
private readonly List<OrderLine> _lines = [];
public IReadOnlyList<OrderLine> Lines => _lines;    // read-only to the outside
```

Expose `List<OrderLine>` and any caller can `.Add()` or `.Clear()`, bypassing every rule in
`AddLine` and every domain event. The whole aggregate becomes decoration.

📂 [`Domain/Orders/Order.cs`](../../src/LogiFlow.Domain/Orders/Order.cs) — read it top to bottom.
Notice what is absent: no `DbContext`, no `ILogger`, no HTTP, no `async`. It is instantiable in a
unit test with no mocks, which is why `OrderTests` runs 30 tests in 364 ms.

### The anti-pattern to recognise by name

An **anaemic domain model** is a class of public get/set properties with all the logic in an
`OrderService`. It is extremely common and it is a trap: once anyone can write
`order.Status = Shipped`, every rule has to be re-checked at every call site, forever. Here, an
invalid `Order` cannot be constructed at all.

---

## 4. `Result<T>` instead of exceptions

Exceptions are for the *exceptional*: a dropped connection, a bug, a full disk. "This customer
tried to cancel an order that already shipped" is none of those — it is an ordinary business
outcome that happens hundreds of times a day.

Using exceptions for expected failures costs three things:

1. **Honesty.** `Task<Order> Cancel(...)` claims it always returns an Order. It is a lie.
   `Task<Result<Order>>` tells the truth and the compiler makes callers acknowledge it.
2. **Performance.** Measured, in this repo:
   ```bash
   cd labs/Labs.Benchmarks && dotnet run -c Release --filter '*ErrorHandling*'
   ```
   **1,405,365 ns and 216,000 bytes** vs **311 ns and 0 bytes**. About 4,500×.
3. **Debuggability.** When business rules throw, your logs fill with exceptions and real bugs
   stop standing out.

**The rule of thumb: if a human could reasonably cause it, return a `Result`. If only a bug or
the infrastructure could cause it, throw.**

📂 [`Domain/Results/Result.cs`](../../src/LogiFlow.Domain/Results/Result.cs) ·
[`Domain/Orders/OrderErrors.cs`](../../src/LogiFlow.Domain/Orders/OrderErrors.cs)

Note the `Code`/`Description` split. Clients branch on `Order.AlreadyShipped`; they must never
branch on the message, which is free to change and to be translated.

---

## 5. Domain events

Consider: "when an order ships, email the customer, decrement inventory, notify the carrier."
Putting those three calls inside `Order.MarkShipped()` drags SMTP, inventory and HTTP clients
into the Domain — the exact inversion this architecture prevents.

Instead `MarkShipped()` records a fact and returns. Handlers elsewhere react.

**Naming is part of the contract:** `OrderPlacedDomainEvent`, not `PlaceOrderEvent`. A command
is a request that may be refused; an event is a historical fact that cannot be. If you want to
"reject" an event, you actually modelled a command.

### When do they fire?

Not when raised. They are collected on the aggregate and dispatched during `SaveChangesAsync`,
**inside the same transaction** as the state change.

📂 [`Infrastructure/.../DomainEventDispatchingInterceptor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Interceptors/DomainEventDispatchingInterceptor.cs)

You can watch this work end to end:

```
POST /api/orders/{id}/submit
   └─ Order.Submit() raises OrderSubmittedDomainEvent
      └─ ReserveStockOnOrderSubmitted → warehouse.Reserve() → raises StockReservedDomainEvent
         └─ dispatch loop picks that up too, in the same transaction
```

That cascade is why the interceptor drains events in a **loop with an iteration cap** rather than
recursing — a handler that re-raises its own event gets a clear exception instead of a
`StackOverflowException`, which cannot be caught and takes the process down.

Anything crossing a process boundary goes through the **transactional outbox** instead — see
module 06.

---

## 6. State machines as data

```csharp
[OrderStatus.Draft]     = [Submitted, Cancelled],
[OrderStatus.Submitted] = [Confirmed, Cancelled],
[OrderStatus.Confirmed] = [Shipped, Cancelled],
[OrderStatus.Shipped]   = [Delivered],          // once it is on a lorry, "cancel" is a return
[OrderStatus.Delivered] = [],
[OrderStatus.Cancelled] = [],
```

📂 [`Domain/Orders/OrderStatus.cs`](../../src/LogiFlow.Domain/Orders/OrderStatus.cs)

```
   Draft ──────► Submitted ──────► Confirmed ──────► Shipped ──────► Delivered
     │              │                  │               ▲
     │              │                  │               └─ the point of no return: after this,
     ▼              │                  │                  "cancel" is a RETURN — a different
   Cancelled ◄──────┴──────────────────┘                  business process, not a transition

   Delivered and Cancelled go nowhere.   Every arrow above is one row in one table, and every
                                         lifecycle method goes through EnsureCanTransitionTo.
```

The alternative is a guard clause at the top of eight methods, and someone eventually updates
seven of them. One table is reviewable in a screen and exhaustively testable.

Every transition in `Order` routes through one private `EnsureCanTransitionTo`, so a new
lifecycle method cannot forget to check.

---

## 7. Break something

The fastest way to understand a guard is to remove it.

1. Open `Domain/Orders/Order.cs`, find `public IReadOnlyList<OrderLine> Lines => _lines;`
   and change it to `public List<OrderLine> Lines => _lines;`.
2. Run `dotnet test tests/LogiFlow.Domain.Tests`.
3. Watch `Lines_IsNotDirectlyMutable` fail, and think about what a caller could now do.
4. `git checkout .`

Then try: delete the `RevenueStatuses` filter from `ReportingQueries` and see cancelled orders
start counting as revenue. There is no test for that one — which is itself a lesson about where
your coverage is thin.

---

## 8. Golden rules

> The card. This module is what interviewers mean when they say "architecture".

1. **Dependencies point inward, and a test says so.** The Domain project has no package and no
   project references; `LayeringTests` keeps it that way after you have stopped watching.
2. **Inner layers declare what they need; outer layers supply it.** That is dependency inversion —
   not "we use a DI container", which is a different thing people routinely confuse it with.
3. **Identity means entity. Attributes mean value object.** Two customers with the same name are
   two customers; two addresses with the same fields are one address.
4. **An aggregate is a consistency boundary, not a folder.** Everything inside it is saved in one
   transaction, and its invariants are true at the end of every one.
5. **Reference other aggregates by id.** An object reference is an invitation to mutate two
   aggregates in one transaction — which is why the mapping here withholds the navigation.
6. **Keep aggregates small.** If two things need not be consistent *immediately*, they belong
   apart and an event reconciles them.
7. **Expose read-only collections.** A public `List<T>` bypasses every rule in `AddLine` and every
   domain event, and the aggregate becomes decoration.
8. **If a human could reasonably cause it, return a `Result`. If only a bug or the infrastructure
   could, throw.** Roughly 4,500x cheaper here — but the real win is that failure is in the
   signature and the compiler makes callers acknowledge it.
9. **Clients branch on a stable `Code`, never on a message.** The message is free to change and to
   be translated.
10. **Events are facts, in the past tense.** `OrderPlacedDomainEvent`, not `PlaceOrderEvent`. If
    you want to reject one, you modelled a command.
11. **Keep the state machine as data, in one table.** A guard clause at the top of eight methods
    means someone eventually updates seven of them.
12. **Never `double` for money.** `decimal` is base-10 and stores 0.1 exactly; binary floating
    point cannot.

---

## 9. Interview questions

**"What is Clean Architecture?"**
A layering where dependencies point inward, so business rules do not depend on frameworks,
databases or delivery mechanisms. Inner layers declare interfaces; outer layers implement them.
The practical payoff is that the Domain is testable with no mocks and no I/O, and infrastructure
choices become swappable. The cost is more indirection and more files — worth it above a certain
size, overhead below it.

**"Entity or value object?"**
Ask whether identity matters. Two customers with the same name are different customers →
entity. Two addresses with the same fields are the same address → value object.

**"How do you decide aggregate boundaries?"**
By transactional consistency. Things that must be consistent *immediately* go in one aggregate;
everything else is a separate aggregate reconciled by events. Start small — an aggregate that is
too big becomes a lock contention point.

**"Why not just throw exceptions?"**
Expected business outcomes are not exceptional. `Result` puts failure in the signature so callers
must handle it, avoids the ~4,500× cost of a throw, and keeps error logs meaningful. Reserve
exceptions for bugs and infrastructure failure.

**"Isn't this over-engineering for a CRUD app?"**
Yes, and say so. For a form over data, this is pure overhead — use minimal APIs and EF Core
directly. It earns its keep when business rules are non-trivial, when several people work on the
codebase, or when it will live for years. Naming the condition under which you would *not* do it
is the answer they are looking for.

---

## Next

→ [Module 06 — EF Core in depth](../module-06-efcore/)
