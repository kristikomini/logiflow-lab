# Read models and projections

> Part of [Module 08 — CQRS and the mediator pattern](README.md), section 1.
> Previous: [4. Pipeline behaviours](05-pipeline-behaviors.md) · Back to [the module](README.md)

---

[Section 1](01-what-cqrs-actually-is.md) claimed the read side deserves its own model. This is what
that looks like in practice, and why the read side is allowed to break rules the write side must
obey.

## The write model is the wrong tool for a list

```csharp
// ✗ Correct, and unusable at scale.
IReadOnlyList<Order> orders = await repository.ListAsync(spec, ct);
return orders.Select(o => new OrderSummaryDto(o.Id, o.OrderNumber.Value, o.Total.Amount)).ToList();
```

`o.Total` is a computed property that sums the lines, so every order must be loaded **with all its
lines** to render one number in a table. Fifty orders averaging twenty lines is a thousand rows
materialised into aggregates, each with a change-tracker snapshot, to display fifty.

Nothing here is wrong in the sense of incorrect. It is wrong in the sense of costing a hundred times
what it needs to.

```csharp
// ✓ The projection. One query, five columns, no entities.
return await context.Orders
    .AsNoTracking()
    .Where(o => o.Status == OrderStatus.Submitted)
    .OrderByDescending(o => o.CreatedAtUtc)
    .Select(o => new OrderSummaryDto(
        o.Id,
        o.OrderNumber.Value,
        o.Status,
        o.Lines.Count,                                          // correlated subquery
        o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity)))     // aggregated in SQL
    .ToListAsync(ct);
```

`Lines.Count` and `Lines.Sum(...)` inside a `Select` are **translated into SQL** — a subquery or a
join with a `GROUP BY`, decided by the provider. The database does the arithmetic over an index and
returns fifty rows of five columns. No aggregates, no lines, no tracking.

## Why the read side gets its own abstraction

```csharp
public interface IOrderQueries      // Application layer, alongside IOrderRepository
{
    Task<OrderDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<OrderSummaryDto>> SearchAsync(string? term, int take, CancellationToken ct = default);
}
```

Separate from `IOrderRepository`, deliberately, and the separation is the whole design:

| | `IOrderRepository` | `IOrderQueries` |
|---|---|---|
| Returns | `Order` aggregates | DTOs |
| Purpose | protect invariants during a write | shape data for a screen |
| Tracking | yes — you are going to save | `AsNoTracking`, always |
| Free to use | EF only | EF, Dapper, raw SQL, a view |
| Callers | command handlers | query handlers |

**The read side has no invariants to protect**, so the containment a repository provides buys nothing
and its cost — hiding `IQueryable`, forcing named methods — buys nothing either. What the read side
needs is *freedom to be fast*, and that is what a separate abstraction grants.

## Dropping to SQL is allowed here

This is the part people find surprising, and it is correct:

```csharp
public async Task<IReadOnlyList<RevenueByCategoryDto>> RevenueByCategoryAsync(
    DateOnly from, DateOnly to, CancellationToken ct)
{
    const string sql = """
        SELECT   p.Category                          AS Category,
                 SUM(ol.UnitPrice * ol.Quantity)     AS Revenue,
                 COUNT(DISTINCT o.Id)                AS OrderCount
        FROM     logiflow.Orders o
        JOIN     logiflow.OrderLines ol ON ol.OrderId = o.Id
        JOIN     logiflow.Products   p  ON p.Id = ol.ProductId
        WHERE    o.CreatedAtUtc >= @from AND o.CreatedAtUtc < @to
        GROUP BY p.Category
        ORDER BY Revenue DESC;
        """;

    return await connection.QueryAsync<RevenueByCategoryDto>(sql, new { from, to });
}
```

A reporting query with three joins, a `GROUP BY` and a `COUNT(DISTINCT)` is clearer as SQL than as
LINQ, and you can paste it into a query plan window. Dapper materialises it faster than EF because
there is no model, no tracking and no change detection.

The rules that make this safe rather than a regression:

- **It lives in Infrastructure**, behind `IReportingQueries`. The application layer sees an interface.
- **Parameters, always.** `@from` and `@to` are parameters, never interpolation. Concatenating user
  input into SQL is [injection](../module-24-security/), and `FromSqlRaw` with an interpolated string
  is the EF-flavoured version of the same mistake.
- **It is read-only.** Raw SQL on the write side bypasses the change tracker, the interceptors and
  the domain events, and would silently break the outbox.

## Where a read model can go further

Projections are the default and cover most needs. Two escalations, in increasing cost:

**A database view or an indexed view.** When the same projection is needed by several queries, put it
in the schema and map a keyless entity to it. Free from the application's perspective, and it keeps
the shaping where the data is.

**A denormalised table maintained by handlers.** `OrderSubmitted` writes a row into
`OrderSummaryReadModel`. Reads become a single-table select with no joins at all.

The second is where **eventual consistency finally appears** — the read table lags the write by the
time it takes an event to be handled. That is a real cost: a user who submits an order and is
redirected to a list may not see it yet. Do not pay it until the projection is measurably too slow,
and when you do, be explicit about it in the UI.

This is also the honest boundary of the CQRS pattern: everything up to here has been free. A second
store is not.

## DTOs are not entities with fewer fields

**One DTO per screen, not one per entity.** `OrderSummaryDto` for the list and `OrderDetailDto` for
the page are two records, not one with nullable fields for whichever screen did not need them.
Sharing them means every screen's requirements are coupled to every other's.

**Flatten deliberately.** A DTO with a nested `CustomerDto` invites a second query. If the screen
needs a customer name, project `CustomerName` as a string.

**No domain types in a DTO.** `Money`, `OrderId` and `OrderStatus` are the domain's vocabulary; a DTO
crossing an HTTP boundary carries `decimal`, `Guid` and `string`. Otherwise a domain refactor becomes
a breaking API change — and the front end's generated client stops compiling for a reason that has
nothing to do with the front end
([site chapter 18](../../site/chapters/18-spa-and-typescript.html)).

## The mistakes

**Projecting after materialising.** `.ToListAsync()` and *then* `.Select(...)` runs the projection in
memory over everything you just loaded. The `Select` must be inside the query.

**Calling a C# method inside a projection.** `.Select(o => new Dto(Format(o.Total)))` cannot be
translated; depending on the version you get an exception or silent client-side evaluation.

**Reusing the write model because "it's already there".** The commonest cause of a slow list
endpoint.

**Raw SQL on the write side.** Bypasses tracking, interceptors, domain events and the outbox.

**A read model nobody invalidates.** If you take the denormalised-table step, a handler that misses
an update leaves the read side permanently wrong — with no error.

## Try it

Open [`OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs) and
[`ReportingQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/ReportingQueries.cs)
and note that neither mentions `Order` the aggregate. Then rewrite `SearchAsync` using
`IOrderRepository` and watch the SQL log: you will load every line of every order to display a count.
The difference is the chapter.

## What to remember

- The write model protects invariants; the read model shapes data. Do not make one do both.
- Project inside the query. `Select` before `ToListAsync`, always.
- Aggregations inside a projection are translated to SQL and cost nothing extra.
- `IOrderQueries` is separate from `IOrderRepository` because reads have nothing to protect.
- Dapper or raw SQL is fine on the read side — parameterised, in Infrastructure, read-only.
- Never raw SQL on the write side: it bypasses tracking, interceptors and the outbox.
- One DTO per screen, flattened, with no domain types.
- A denormalised read table is where eventual consistency starts. Delay that decision.

**Code:** [`IOrderQueries.cs`](../../src/LogiFlow.Application/Features/Orders/IOrderQueries.cs) ·
[`OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs) ·
[`OrderDtos.cs`](../../src/LogiFlow.Application/Features/Orders/OrderDtos.cs)

**Back to:** [Module 08](README.md)
