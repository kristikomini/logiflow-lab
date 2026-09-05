# 4. The N+1 problem

> Part of [Module 06 — EF Core in depth](README.md), section 4.
> Previous: [3. Value conversions](02-value-conversions.md) ·
> Next: [6. Repositories and the specification pattern](05-repositories-and-uow.md)

---

The single most common performance bug in every ORM ever written, and the one an interviewer is most
likely to ask you to spot. It is called N+1 because that is exactly how many queries you send: one to
get the list, then one more for each item in it.

```csharp
List<Order> orders = await db.Orders.ToListAsync(ct);          // 1 query

foreach (Order order in orders)
{
    // Each access to a navigation that was not loaded issues its own query.
    Console.WriteLine($"{order.OrderNumber}: {order.Lines.Count} lines");   // N queries
}
```

One hundred orders, one hundred and one round trips. On a local machine with 2 ms latency that is
200 ms and nobody notices. Against a database in another availability zone at 15 ms it is a second
and a half, and it scales linearly with your success.

## Spotting it

**In the log.** Turn on EF's SQL logging in development and look for the same query shape repeated
with a different parameter. That is the whole diagnosis:

```
SELECT ... FROM logiflow.OrderLines WHERE OrderId = @__p_0
SELECT ... FROM logiflow.OrderLines WHERE OrderId = @__p_0
SELECT ... FROM logiflow.OrderLines WHERE OrderId = @__p_0
```

**In review.** Any loop whose body touches a navigation property. Any `.Select(x => x.Child.Name)`
over a materialised list. Any mapping method that takes an entity and reaches through it.

**In tests.** You can assert on it — count commands with a `DbCommandInterceptor` and fail the test
above a threshold. That turns a performance regression into a red build, which is the only way it
stays fixed.

## The fixes, in order of preference

### 1. Project to exactly what you need

```csharp
List<OrderSummaryDto> summaries = await db.Orders
    .AsNoTracking()
    .Select(o => new OrderSummaryDto(
        o.Id,
        o.OrderNumber.Value,
        o.Lines.Count,                       // becomes a correlated subquery — one round trip
        o.Lines.Sum(l => l.Quantity)))
    .ToListAsync(ct);
```

**This is the right answer far more often than `Include`.** One query, only the columns the screen
needs, no entities materialised, no tracking. `Lines.Count` inside a projection is translated to SQL
rather than executed in memory, so it costs nothing extra.

### 2. `Include`, when you genuinely need the entities

```csharp
Order? order = await db.Orders
    .Include(o => o.Lines)
    .FirstOrDefaultAsync(o => o.Id == id, ct);
```

Which is what [`OrderRepository.GetWithLinesAsync`](../../src/LogiFlow.Infrastructure/Persistence/Repositories/OrderRepository.cs)
does — correctly, because a command handler that is about to call `order.AddLine(...)` needs the real
aggregate with its rules, not a DTO.

Note the naming: `GetAsync` and `GetWithLinesAsync` are separate methods. Loading the lines is a cost
and the caller says whether it wants to pay it, rather than every caller paying for the worst case.

### 3. `AsSplitQuery`, when `Include` explodes

`Include` emits a `JOIN`, and a join across two collections multiplies rows. An order with 50 lines
and 20 shipments returns **1000 rows**, each repeating the whole order — the *cartesian explosion*.

```csharp
await db.Orders
    .Include(o => o.Lines)
    .Include(o => o.Shipments)
    .AsSplitQuery()            // three small queries instead of one enormous one
    .FirstOrDefaultAsync(o => o.Id == id, ct);
```

The trade is real and you should be able to state it: split queries send more round trips and are
**not atomic** — the second query runs after the first, so a concurrent write can produce an
inconsistent picture unless you are inside a transaction. Use it for the explosion case, not by
default.

## Lazy loading: the cause, not a fix

EF Core can enable lazy loading with proxies, so touching `order.Lines` silently issues a query. It
makes the N+1 above invisible rather than absent.

This repository does not enable it, deliberately:

- Every query becomes implicit. You cannot see the cost at the call site.
- It **cannot work after the context is disposed** — the classic
  `ObjectDisposedException` when a DTO mapper runs outside the request scope.
- It requires `virtual` navigations and a runtime proxy type, which is the reason `Entity<TId>`
  compares with `GetType() == other.GetType()`
  ([module 05 section 2](../module-05-clean-architecture/02-entities-and-value-objects.md)).

Explicit is better here. If a navigation is not loaded, you want a null or an empty collection you
notice, not a query you did not ask for.

## The version that hides in the mapper

The loop is easy to spot. This is the one that survives review:

```csharp
// The query looks innocent.
List<Order> orders = await db.Orders.AsNoTracking().ToListAsync(ct);

// The N+1 is in here, one layer away.
return orders.Select(OrderMapper.ToDto).ToList();

// …because:
public static OrderDto ToDto(Order order) =>
    new(order.Id, order.Lines.Sum(l => l.LineTotal.Amount));   // ← a query per order
```

The fix is the same — project in the query — but the lesson is that **the bug is not where the loop
is, it is where the navigation is touched.** Any method taking an entity and returning a DTO is a
candidate.

## The same bug elsewhere

Worth recognising, because interviewers like the connection:

- **In the browser.** A list component where each row fetches its own detail — fifty HTTP calls where
  one shaped endpoint would do. See [site chapter 18](../../site/chapters/18-spa-and-typescript.html).
- **In a screening test.** A loop inside a loop over the same collection, which a dictionary turns
  from O(n²) into O(n). Same shape, different costume.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Call the orders list endpoint and watch the SQL log. Then deliberately break it: load orders without
`Include`, map them in a loop that touches `Lines`, and count the queries. Then convert it to a
projection and watch one query replace all of them. The difference in the log is more convincing than
any explanation.

## What to remember

- N+1 is one query for the list plus one per item, caused by touching a navigation after loading.
- Diagnose it by looking for a repeated query shape in the SQL log.
- Projection is the best fix: one query, only the needed columns, nothing tracked.
- `Include` is for when you need the real aggregate to call behaviour on it.
- `AsSplitQuery` fixes the cartesian explosion and costs atomicity. Not a default.
- Lazy loading hides N+1 rather than solving it, and breaks once the context is disposed.
- The bug often lives in a mapper, not in an obvious loop.
- Assert on query counts in tests if you want it to stay fixed.

**Code:** [`OrderRepository.cs`](../../src/LogiFlow.Infrastructure/Persistence/Repositories/OrderRepository.cs) ·
[`Queries/OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs)

**Next:** [6. Repositories and the specification pattern](05-repositories-and-uow.md)
