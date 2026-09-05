# 1. What CQRS actually is

> Part of [Module 08 — CQRS and the mediator pattern](README.md), section 1.
> Next: [2. Vertical slices](02-vertical-slices.md)

---

**Command Query Responsibility Segregation: use a different model for writing than for reading.**

That is the entire claim. Everything else people attach to the acronym — event sourcing, two
databases, eventual consistency, microservices — combines well with it and is not it. Being able to
separate them cleanly is most of what an interviewer is checking.

## The two shapes, and why they diverge

**Submitting an order** must load the whole `Order` aggregate, because the invariants live in it: at
least one line, a shipping address, a legal state transition. It changes state, runs in a
transaction, and returns almost nothing.

**Rendering the orders screen** needs six columns from three tables for fifty rows, sorted and paged.
It has no invariants to protect and no behaviour to call. Loading fifty aggregates with all their
lines to display six columns is pure waste — and it is an
[N+1](../module-06-efcore/04-n-plus-one.md) waiting to happen.

Force both through one model and you get one of two outcomes, both common:

- **Slow reads**, because every query drags a full aggregate and its collections into memory.
- **An anaemic domain**, because the model was flattened until it was convenient to query, and the
  rules moved out into services.

CQRS is the observation that you do not have to choose.

## In this codebase: two interfaces

```csharp
public interface IRequest<out TResponse>;                       // marker

public interface ICommand<out TResponse> : IRequest<TResponse>; // changes state
public interface IQuery<out TResponse>   : IRequest<TResponse>; // reads state
```

Empty marker interfaces, which normally deserve the analyser warning they get — and here the marker
*is* the point, so it is suppressed deliberately. Being able to ask "is this a command?" as a type
question is what lets the [pipeline](05-pipeline-behaviors.md) treat the two differently without any
handler opting in:

```csharp
// TransactionBehavior wraps commands. Queries get no transaction — they do not need one,
// and it would only take locks.
if (request is not ICommand<TResponse>) return await next();

// CachingBehavior caches queries. Caching a command would be a catastrophe.
if (request is not IQuery<TResponse>) return await next();
```

One type-level distinction, and two cross-cutting concerns become automatic and correct.

## The write side

```csharp
public sealed record SubmitOrderCommand(OrderId OrderId) : ICommand<Result>;

public sealed class SubmitOrderCommandHandler(
    IOrderRepository orders,
    IStockReservationService stock) : IRequestHandler<SubmitOrderCommand, Result>
{
    public async Task<Result> HandleAsync(SubmitOrderCommand request, CancellationToken ct)
    {
        Order? order = await orders.GetWithLinesAsync(request.OrderId, ct);
        if (order is null) return OrderErrors.NotFound(request.OrderId);

        Result result = order.Submit();          // the rules live in the aggregate
        if (result.IsFailure) return result;

        return await stock.ReserveForAsync(order, ct);
    }
}
```

Note what the handler does **not** contain: no SQL, no transaction, no logging, no validation, no
caching. It loads an aggregate, calls a method on it, and returns. Everything else is a
[behaviour](05-pipeline-behaviors.md) wrapped around it.

**Commands are imperative and return as little as possible.** `SubmitOrderCommand`, not
`OrderSubmitted` — a command can be refused, which is what distinguishes it from a
[domain event](../module-05-clean-architecture/04-domain-events.md). Returning the id of a created
entity is normal and pragmatic; returning the whole entity means the caller is using a command as a
query.

## The read side

Queries do not go through repositories or aggregates at all:

```csharp
public sealed record SearchOrdersQuery(string? Term, int PageSize, string? Cursor)
    : IQuery<Result<Page<OrderSummaryDto>>>;

// Infrastructure/Persistence/Queries/OrderQueries.cs
public async Task<IReadOnlyList<OrderSummaryDto>> SearchAsync(...)
{
    return await context.Orders
        .AsNoTracking()
        .Where(...)
        .Select(o => new OrderSummaryDto(          // projection: only these columns leave SQL
            o.Id,
            o.OrderNumber.Value,
            o.Status,
            o.Lines.Count,
            o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity)))
        .ToListAsync(ct);
}
```

`IOrderQueries` and `IReportingQueries` are separate abstractions from `IOrderRepository`,
deliberately. The repository serves the write side and returns aggregates; the queries serve the read
side and return DTOs. Their implementations are free to use EF projections, raw SQL or Dapper —
whatever is fastest — because there are no invariants to protect.

That freedom is the practical payoff. [Section 4](04-read-models-and-projections.md) is the detail.

## What CQRS is not

**Not two databases.** You may add a read replica or a denormalised store later; that is a separate,
much more expensive decision that brings eventual consistency with it. This repository has one SQL
Server and full CQRS.

**Not event sourcing.** Event sourcing stores events as the source of truth and derives state. It
pairs naturally with CQRS and is entirely independent of it. Conflating the two is the single most
common misunderstanding, and saying so unprompted lands well.

**Not eventual consistency.** Because there is one database, a query immediately after a command sees
the change. Eventual consistency arrives only when you split the stores.

**Not microservices.** Unrelated axis entirely.

## When it is not worth it

Say this in an interview; it is the half that shows judgement.

For a CRUD screen over nine tables with no business rules, CQRS is two folders and a naming
convention protecting nothing. A controller with a `DbContext` is the right answer, and the honest
test is: **does this application have invariants that must hold regardless of the caller?** If not,
the write model has nothing to protect and separating it from the read model is ceremony.

The cost is also real where it *is* worth it: two models to keep in step, more files, and a DTO layer
that must be maintained. The benefit is that neither side compromises for the other.

## Try it

Open [`Features/Orders/`](../../src/LogiFlow.Application/Features/Orders/) and compare two files:

- `SubmitOrder.cs` — loads an aggregate, calls `Submit()`, no SQL anywhere.
- `SearchOrders.cs` — no aggregate at all, straight to a DTO.

Then try to write `SearchOrders` using `IOrderRepository`. You will find yourself loading every order
with its lines to count them, and the reason for the second abstraction becomes obvious in about
thirty seconds.

## What to remember

- CQRS is one claim: different models for reading and writing.
- The shapes diverge because writes protect invariants and reads shape data for a screen.
- Two marker interfaces let the pipeline treat commands and queries differently, automatically.
- Commands are imperative, transactional, and return as little as possible.
- Queries skip repositories and aggregates and project straight to DTOs.
- It is **not** event sourcing, two databases, eventual consistency or microservices.
- With one database there is no eventual consistency — reads see writes immediately.
- It is not worth it for forms over data. Know the test and say it.

**Code:** [`Messages.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Messages.cs) ·
[`Features/Orders/`](../../src/LogiFlow.Application/Features/Orders/) ·
[`Queries/OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs)

**Next:** [2. Vertical slices](02-vertical-slices.md)
