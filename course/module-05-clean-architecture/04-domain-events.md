# 5. Domain events

> Part of [Module 05 — Clean Architecture and Domain-Driven Design](README.md), section 5.
> Previous: [4. `Result<T>` instead of exceptions](05-result-vs-exceptions.md) ·
> Next: [6. State machines as data](06-state-machines.md)

---

[Aggregates](03-aggregates.md) established that one transaction changes one aggregate. That leaves an
obvious question: when an order is submitted, *something* has to reserve stock, send a confirmation
and notify the warehouse. If the order cannot do it, who does?

A domain event is the answer. The aggregate does not call anybody. It **states a fact** and stops
caring.

## The shape

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}

public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

**Past tense, always.** `OrderSubmittedDomainEvent`, not `SubmitOrder`. The name is the test: a
command can be refused, a fact cannot. If your event name is imperative you have written a
disguised method call and coupled the publisher to a handler again.

**`Guid.CreateVersion7()`, not `NewGuid()`.** Version 7 GUIDs are time-ordered, so when these ids
land in an index — and they do, in the outbox table — inserts append at the end rather than
scattering across the B-tree. Random GUIDs as a clustered key are one of the classic SQL Server
performance mistakes; [module 07 section 1](../module-07-sql-and-transactions/README.md#1-indexes)
has the page-split explanation.

**`EventId` is also the consumer's deduplication key.** That is not incidental — it is the entire
reason at-least-once delivery is survivable. See the
[outbox](../module-06-efcore/07-outbox-pattern.md).

## Raising, and why they queue

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

`Raise` does not dispatch. It **appends to a list on the aggregate**, and that list sits there until
somebody saves. This is the design decision that makes everything else work, and it is worth
understanding why the obvious alternative is wrong.

If `Raise` dispatched immediately, handlers would run *before* `SaveChangesAsync`. A confirmation
email would go out for an order that then fails to save because of a concurrency conflict. The
customer has an email for an order that does not exist, and there is no way to take it back.

Queuing means the events are dispatched **after** the state change is safely part of the transaction.
The aggregate stays ignorant — it does not know a dispatcher exists — and the timing is correct.

## Where they are dispatched

```csharp
// Infrastructure/Persistence/Interceptors/DomainEventDispatchingInterceptor.cs
```

An EF Core `SaveChangesInterceptor` walks the change tracker for entities implementing
`IHasDomainEvents`, collects their events, clears them, and dispatches. Two details:

**Collect and clear *before* dispatching.** A handler that modifies another aggregate raises more
events; if you iterate the live list while handlers append to it, you either loop forever or get an
`InvalidOperationException` for modifying a collection during enumeration.

**Handlers run inside the same transaction as the state change.** This repository does that
deliberately: an `OrderSubmitted` handler that reserves stock must not commit unless the order
commits. The consequence is the one stated in `IDispatcher` — an exception in one handler aborts the
rest, on purpose. Swallowing failures would give you a committed order whose stock was never
reserved, which is worse than a failed request.

For anything that must *not* be inside the transaction — an email, an HTTP call to another system —
the handler writes an outbox row instead of doing the work.
[Module 06 section 8](../module-06-efcore/07-outbox-pattern.md) is that mechanism.

## Domain events vs integration events

The distinction is asked in interviews and confused constantly:

| | Domain event | Integration event |
|---|---|---|
| Audience | this process, this transaction | other services, later |
| Contains | domain types, ids | a stable serialised contract |
| Coupling | internal, free to change | published API, versioned |
| Delivery | in-process, synchronous | broker, at-least-once |
| Example | `OrderSubmittedDomainEvent` | `order.submitted.v1` on the bus |

`OrderSubmittedDomainEvent` carries `OrderId` and can be refactored freely. The moment the same fact
leaves the process, it becomes a contract somebody else compiles against, and changing it breaks
them. Keep them as separate types even when the fields are identical — the day they diverge, you
will be glad the boundary already existed.

## What an event should carry

Enough for a handler to do its job without another query, and no more:

```csharp
public sealed record OrderSubmittedDomainEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    Money Total) : DomainEventBase;
```

**Not the aggregate itself.** Passing `Order` lets a handler mutate it — reintroducing exactly the
coupling the event removed — and it means the event's meaning changes as the object does.

**Not so little that every handler re-loads.** An event carrying only `OrderId` forces three handlers
to fetch the same order three times. That is an N+1 with extra steps.

## The mistakes

**Imperative names.** `SendConfirmationEmailEvent` is a command wearing a costume. The publisher now
knows what happens next, which is what you were trying to avoid.

**Dispatching in `Raise`.** Handlers run before the save. Covered above; it is the single most
common implementation error.

**Doing external I/O in a handler inside the transaction.** An HTTP call in an event handler holds
a database transaction open for the length of a network round trip, and it cannot be rolled back if
the transaction later fails. This repository ships one deliberately flawed handler —
`SendConfirmationOnOrderSubmitted` — labelled as such, and "convert it to use the outbox" is one of
the exercises in [SOLUTIONS.md](../SOLUTIONS.md).

**Using events to avoid thinking about aggregate boundaries.** If two things genuinely must be
consistent at every instant, they belong in one aggregate. An event between them buys eventual
consistency you did not want.

## Try it

Put a breakpoint in `DomainEventDispatchingInterceptor` and submit an order through the API. Watch
the order arrive in the change tracker with a populated `DomainEvents` list, and watch the list being
cleared before a single handler runs. Then move the `Raise` call in `Order.Submit()` to *after* the
status change and see that nothing about the observable behaviour changes — because the dispatch is
decoupled from the raise. That is the property you are buying.

## What to remember

- Events are facts in the past tense. If the name is imperative, it is a command.
- `Raise` queues on the aggregate; it never dispatches.
- Dispatch happens on save, so handlers never run for a change that was rolled back.
- Handlers here run in the same transaction — so one failure aborts the rest, deliberately.
- Anything external goes through the outbox, not through a handler doing I/O.
- Domain events are internal; integration events are a versioned contract. Keep the types separate.
- Carry ids and the few values handlers need — never the aggregate itself.
- `Guid.CreateVersion7()` for time-ordered ids that index well.

**Code:** [`Common/IDomainEvent.cs`](../../src/LogiFlow.Domain/Common/IDomainEvent.cs) ·
[`Orders/Events/OrderEvents.cs`](../../src/LogiFlow.Domain/Orders/Events/OrderEvents.cs) ·
[`DomainEventDispatchingInterceptor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Interceptors/DomainEventDispatchingInterceptor.cs)

**Next:** [6. State machines as data](06-state-machines.md)
