# Module 09 — Advanced LINQ

> Grouping, joins and aggregation — and the SQL each one generates. Work through this with the
> SQL log on and the API running.

📂 The whole module dissects one file:
[`Infrastructure/Persistence/Queries/ReportingQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/ReportingQueries.cs)

Run the reports first so you have something to look at:

```bash
cd src/LogiFlow.Api && dotnet run
# then hit /api/reports/sales, /api/reports/top-products, /api/reports/customers
```

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 5–7 | [Dynamic predicates, composable filters and safe sorting](05-dynamic-predicates.md) | building queries conditionally without injection or a table scan |

---

## 1. `GroupBy` — where LINQ-to-SQL beginners come unstuck

In **LINQ to Objects**, `GroupBy` gives you `IGrouping<TKey, TElement>` and you can enumerate each
group's members freely.

In **LINQ to SQL you cannot.** `GROUP BY` produces exactly one row per group, containing the key
and aggregates. There is no way to get the individual rows back.

```
   LINQ to Objects — GroupBy                    SQL — GROUP BY
   ┌────────────────────────────────┐           ┌──────────┬─────────┬─────────┐
   │ "ELE" → [ line, line, line ]   │           │ "ELE"    │  SUM    │  COUNT  │
   │ "MEC" → [ line ]               │           │ "MEC"    │  SUM    │  COUNT  │
   └────────────────────────────────┘           └──────────┴─────────┴─────────┘
    the elements are still there;                the rows are GONE. One row per group,
    g.ToList() hands them to you                 key and aggregates only.
                                                 g.ToList() has nothing to return ⇒ it throws
```

```csharp
// ✅ translates
.GroupBy(o => o.Year).Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })

// ❌ throws — asks for the group's members
.GroupBy(o => o.Year).Select(g => new { g.Key, Orders = g.ToList() })
```

EF Core 3.0 onwards **throws** on the second rather than silently pulling the table into memory
and grouping there, which earlier versions did. That change broke a lot of applications and made
a lot of them much faster.

### Grouping on a computed key

```csharp
.GroupBy(o => new { o.SubmittedAtUtc!.Value.Year, o.SubmittedAtUtc!.Value.Month })
```

becomes `GROUP BY DATEPART(year, ...), DATEPART(month, ...)`. Note the consequence: grouping on a
*computed expression* cannot use an index on `SubmittedAtUtc`, so it is a scan of the filtered
range. Acceptable for a report; not for a hot path.

---

## 2. Aggregate in SQL, apply business rules in C#

The line this codebase holds, stated explicitly in `OrderQueries.GetDetailAsync`:

```csharp
// In SQL — the database is far better at this than you are
LineSubtotal = x.order.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity)) ?? 0m,

// In C# — the tier discount lives in the domain; re-expressing it as SQL would
// duplicate business logic somewhere nobody would think to update
decimal discount = row.LineSubtotal * row.CustomerTier.DiscountRate();
```

**Aggregate over many rows in SQL. Apply rules to the few rows you fetched, in C#.**

Note the `(decimal?)` cast before `Sum`. SQL's `SUM` over zero rows returns `NULL`, not `0`.
Without the nullable cast and `?? 0m`, EF materialises `NULL` into a non-nullable `decimal` and
throws. This catches everyone once.

---

## 3. Joins

`Order` has **no `Customer` navigation property** — that was a deliberate aggregate-boundary
decision (module 05). So the read side joins explicitly:

```csharp
.Join(context.Customers,
      order    => order.CustomerId,
      customer => customer.Id,
      (order, customer) => new { order, customer })
```

This is CQRS earning its keep: the write model stays strict, the read model does what it needs.

### `COUNT(DISTINCT ...)`

```csharp
OrderCount = g.Select(x => x.OrderId).Distinct().Count()
```

"How many separate orders included this product" — counting *lines* instead gives a plausible-
looking wrong number, which is the worst kind of reporting bug.

### `HAVING` vs `WHERE`

```csharp
.GroupBy(...)
.Select(g => new { ..., OrderCount = g.Count() })
.Where(x => x.OrderCount >= minimumOrders)   // ← AFTER the Select ⇒ HAVING
```

A `Where` **before** `GroupBy` filters individual rows; a `Where` on an aggregate **after** it
becomes `HAVING`. Completely different questions, and a classic SQL interview trap.

```
   rows ──► Where ──► GroupBy ──► Select(aggregates) ──► Where ──► results
             │                                            │
           WHERE                                        HAVING
    "only submitted orders"                    "only customers with 3 or more orders"
    filters ROWS, before grouping              filters GROUPS, after aggregating

   the position of the Where in the chain is the entire difference
```

---

## 4. Multiple aggregates, one pass

```csharp
.Select(g => new
{
    OrderCount        = g.Count(),
    TotalSpent        = g.Sum(x => x.Total),
    AverageOrderValue = g.Average(x => x.Total),
    LargestOrderValue = g.Max(x => x.Total),
    FirstOrderUtc     = g.Min(x => x.SubmittedAtUtc!.Value),
    LastOrderUtc      = g.Max(x => x.SubmittedAtUtc!.Value),
})
```

Six numbers, **one** `GROUP BY`. SQL Server computes them together while scanning the group once.
Six separate queries for six numbers is the mistake this avoids.

---

## 5. Predicates you cannot index

```csharp
.Where(s => s.QuantityOnHand - s.QuantityReserved <= s.ReorderThreshold)
```

Valid SQL, and **no index can help** — it compares columns to each other, so the engine evaluates
the arithmetic for every row. On a large stock table the fix is a persisted computed column:

```sql
ALTER TABLE StockItems ADD QuantityAvailable AS (QuantityOnHand - QuantityReserved) PERSISTED;
CREATE INDEX IX_StockItems_Available ON StockItems (QuantityAvailable);
```

"Why is my perfectly-indexed table still scanning?" is a question worth being able to answer.

---

## 6. Composable filters

```csharp
IQueryable<Order> orders = context.Orders.AsNoTracking();
if (query.Status is { } status) orders = orders.Where(o => o.Status == status);
if (query.FromUtc is { } from)  orders = orders.Where(o => o.CreatedAtUtc >= from);
```

Nothing executes until `ToListAsync`. The SQL contains exactly the predicates supplied — no
`WHERE (@status IS NULL OR ...)` catch-all, which gives SQL Server one cached plan that is
usually bad for every combination.

### Dynamic predicates

For "match any of these conditions", build the tree:

```csharp
var filter = ExpressionExtensions.False<Order>();
foreach (var status in wantedStatuses)
{
    var captured = status;                      // module 02: copy before capturing
    filter = filter.Or(o => o.Status == captured);
}
var results = await context.Orders.Where(filter).ToListAsync();
```

📂 [`ExpressionExtensions.cs`](../../src/LogiFlow.Domain/Common/Specifications/ExpressionExtensions.cs) —
and EF's optimiser folds the `false` seed away, so it costs nothing in SQL.

---

## 7. Sorting without string-based property lookup

```csharp
IOrderedQueryable<Order> sorted = sortBy switch
{
    OrderSortField.CreatedAt => ascending ? orders.OrderBy(o => o.CreatedAtUtc)
                                          : orders.OrderByDescending(o => o.CreatedAtUtc),
    ...
};
```

Verbose compared with `EF.Property<object>(o, sortByString)` — and much better. It is
compile-time checked, it cannot be handed an arbitrary column name by a caller, and with no
`default` arm, adding a sort field without handling it **fails the build**.

Accepting a sort column as free text is a real injection vector when it reaches raw SQL.

---

## 8. Exercises

Extend `IReportingQueries` with:

1. **Revenue by country** — join through customer to the billing address, group by country code.
2. **Repeat-customer rate** — the percentage of customers with more than one order. (Hint: two
   aggregates over one grouping, then arithmetic in C#.)
3. **Slowest-moving products** — in the catalogue but with zero sales in a window. Needs a left
   join: `GroupJoin` + `DefaultIfEmpty`, or a `Where(!...Any(...))` subquery. Compare the SQL of
   both.
4. **Month-on-month growth** — you have `SalesByPeriodDto` ordered by period; compute the delta.
   Do it in C# after materialising and say why that is the right call here.

For each, read the generated SQL and check it is doing what you expected.

---

## 9. Golden rules

> The card.

1. **Aggregate over many rows in SQL; apply business rules to the few rows you fetched, in C#.**
   The database is better at the first and has no idea about the second.
2. **In SQL, a group is its key and its aggregates. The rows are gone.** Asking for the members
   throws, and that is EF Core 3.0+ protecting you.
3. **Cast to nullable before `Sum`.** SQL's `SUM` over zero rows is `NULL`, not `0`:
   `Sum(x => (decimal?)x.Amount) ?? 0m`.
4. **A `Where` before `GroupBy` is a `WHERE`; after the aggregate projection it is a `HAVING`.**
   Two different questions that look almost identical in C#.
5. **Count the distinct thing you mean.** Counting lines when you meant orders produces a
   plausible wrong number, which is the worst kind of reporting bug.
6. **Six aggregates over one grouping cost one scan.** Six queries for six numbers is the mistake
   this avoids.
7. **Grouping on a computed key cannot use an index on the underlying column.** Fine for a report,
   wrong for a hot path.
8. **A predicate comparing two columns can never seek.** If it matters, add a persisted computed
   column and index that.
9. **Compose filters conditionally; never write `WHERE (@x IS NULL OR Col = @x)`.** One cached
   plan for every combination is usually a bad plan for all of them.
10. **Never accept a sort or filter column as free text.** A `switch` over an enum is compile-time
    checked, cannot be handed an arbitrary column name, and fails the build when you add a field
    and forget it.
11. **Read the generated SQL for every report query you write.** Reports are where a single
    careless operator turns into a table scan nobody notices for a year.

---

## 10. Interview questions

**"Why can't I call `.ToList()` inside a `GroupBy` projection in EF Core?"**
Because SQL `GROUP BY` returns one row per group — the individual rows are gone. Only aggregates
are available. EF Core 3.0+ throws rather than silently loading the table and grouping in memory.

**"`WHERE` vs `HAVING`?"**
`WHERE` filters rows before grouping; `HAVING` filters groups after. In LINQ, a `Where` before
`GroupBy` becomes `WHERE`; one after the aggregate projection becomes `HAVING`.

**"How do you build a filter whose conditions are only known at runtime?"**
Compose `IQueryable` conditionally for AND, or build the expression tree with
`Expression.OrElse` / `AndAlso` for OR — rewriting parameters with an `ExpressionVisitor` so both
sides share one parameter node.

**"Why does `Sum` sometimes throw on an empty result?"**
SQL `SUM` over zero rows returns `NULL`. Cast to the nullable type and coalesce:
`Sum(x => (decimal?)x.Amount) ?? 0m`.

---

## Next

→ [Module 10 — Cross-cutting concerns](../module-10-cross-cutting/)
