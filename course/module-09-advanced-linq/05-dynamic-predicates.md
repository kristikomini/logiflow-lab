# 5–7. Dynamic predicates, composable filters and safe sorting

> Part of [Module 09 — Advanced LINQ](README.md), sections 5 to 7.
> Previous: [4. Multiple aggregates, one pass](README.md#4-multiple-aggregates-one-pass) ·
> Back to [the module](README.md)

---

Every real list screen has the same requirement: a search box, four optional filters, and a sortable
column header. It is also where LINQ codebases quietly acquire their worst code — usually one of two
shapes.

## The two bad shapes

**The if-chain that materialises early:**

```csharp
// ✗ ToList() runs the query NOW. Every filter after it is in memory.
List<Order> orders = await db.Orders.ToListAsync(ct);

if (status is not null)   orders = orders.Where(o => o.Status == status).ToList();
if (customerId is not null) orders = orders.Where(o => o.CustomerId == customerId).ToList();
```

Two million rows over the wire to return twelve.

**String-built SQL:**

```csharp
// ✗ Injection, and no plan reuse.
var sql = $"SELECT * FROM Orders WHERE Status = {status} ORDER BY {sortColumn}";
```

## Composing on `IQueryable`

The fix for the first is to remember that **`IQueryable` is lazy**. Nothing executes until you
enumerate, so you can build the query up conditionally and it still becomes one SQL statement:

```csharp
IQueryable<Order> query = db.Orders.AsNoTracking();

if (status is not null)
    query = query.Where(o => o.Status == status);

if (customerId is not null)
    query = query.Where(o => o.CustomerId == customerId);

if (!string.IsNullOrWhiteSpace(term))
    query = query.Where(o => o.OrderNumber.Value.Contains(term));

return await query
    .OrderByDescending(o => o.CreatedAtUtc)
    .Take(pageSize)
    .Select(o => new OrderSummaryDto(...))
    .ToListAsync(ct);
```

Each `Where` appends a node to the expression tree. EF folds them into a single `WHERE a AND b AND c`
and only touches the database at `ToListAsync`.

**The one rule: never materialise mid-build.** No `ToList()`, no `AsEnumerable()`, no `foreach` until
the query is complete — [module 03 section 4](../module-03-linq-internals/03-expression-trees.md)
explains why that single call moves everything after it into memory.

### Why not `Where(o => status == null || o.Status == status)`

It is tempting, and it produces `WHERE (@status IS NULL OR Status = @status)`. That is a
**non-SARGable** predicate: the optimiser cannot use an index on `Status` because the condition
depends on a parameter's nullness, so you get a scan. It also creates one plan that must serve every
combination of filters — the classic *parameter sniffing* problem, where a plan cached for one shape
of input is terrible for the next.

Building the tree conditionally produces a **different query for each combination**, each with its
own plan, each able to use the right index. More plans in the cache, and every one of them good.

## `OR` needs a specification

`if`-chaining gives you `AND` for free and cannot express `OR`. For that you need to combine
expression trees, which is [the specification pattern](../module-06-efcore/06-specification-pattern.md):

```csharp
Specification<Order> spec = OrderSpecifications.Submitted()
    .Or(OrderSpecifications.Confirmed())
    .And(OrderSpecifications.ForCustomer(customerId));

IReadOnlyList<Order> orders = await repository.ListAsync(spec, ct);
```

The composition rewrites both sides onto one parameter and joins them with an `AndAlso`/`OrElse`
node, so it stays one translatable tree. Composing with `Compile()` gives correct answers and
untranslatable SQL.

## Sorting by a runtime-chosen column

The requirement is a column name from a query string. The naive version is injection; the correct
version is an **allow-list**:

```csharp
private static readonly FrozenDictionary<string, Expression<Func<Order, object>>> Sortable =
    new Dictionary<string, Expression<Func<Order, object>>>(StringComparer.OrdinalIgnoreCase)
    {
        ["number"]   = o => o.OrderNumber,
        ["created"]  = o => o.CreatedAtUtc,
        ["status"]   = o => o.Status,
        ["customer"] = o => o.CustomerId,
    }.ToFrozenDictionary();

public static IQueryable<Order> ApplySort(this IQueryable<Order> query, string? sort, bool descending)
{
    if (sort is null || !Sortable.TryGetValue(sort, out var selector))
        selector = Sortable["created"];        // a deterministic default, always

    return descending ? query.OrderByDescending(selector) : query.OrderBy(selector);
}
```

Four properties, and all four matter:

**No string ever reaches SQL.** The input selects a pre-built expression; it is never concatenated.

**Only indexed columns are offered.** `Expression.Property(parameter, name)` — the fully dynamic
version — would let a caller sort by any property, including unindexed ones, and table-scan your
database on request. The dictionary is a performance control as much as a security one.

**An unknown value falls back rather than throwing.** Sort order arrives from URLs, bookmarks and
crawlers. A 500 for `?sort=banana` is not a good answer.

**There is always a deterministic default.** Which matters more than it looks: without a total
ordering, paging repeats and skips rows —
[module 07 section 6](../module-07-sql-and-transactions/06-pagination.md). In production, add the id
as a tie-breaker after whichever column was chosen.

`FrozenDictionary` because it is built once and read on every request
([module 20](../module-20-equality-and-collections/)).

## Predicates you cannot index

Worth stating alongside, because the composition above can quietly produce them:

```csharp
// ✗ A function on the column: the index on OrderNumber cannot be used.
.Where(o => o.OrderNumber.Value.ToLower().Contains(term.ToLower()))

// ✗ Leading wildcard: index seek impossible, scan guaranteed.
.Where(o => EF.Functions.Like(o.OrderNumber.Value, "%" + term + "%"))

// ✓ Prefix match: the index seeks.
.Where(o => o.OrderNumber.Value.StartsWith(term))
```

**Wrapping a column in a function makes it non-SARGable.** For case-insensitive matching, fix the
*collation* rather than calling `ToLower()` — SQL Server's default collation is already
case-insensitive, so the `ToLower()` is usually both useless and harmful.

And if the requirement genuinely is "contains, anywhere, across several fields", stop making the
relational database do it: that is what
[full-text search](../../site/chapters/21-search-elastic.html) exists for.

## The mistakes

**Materialising before the filters are applied.** The single most common cause of a slow list
endpoint.

**`status == null || o.Status == status`.** Non-SARGable, one bad plan for every shape.

**Dynamic property access with no allow-list.** Injection-adjacent, and a table scan on demand.

**Sorting with no tie-breaker.** Non-deterministic results and broken paging.

**Composing with `Compile()`.** Right answers, wrong machine.

**`ToLower()` on both sides.** Kills the index for no benefit.

## Try it

Turn on SQL logging and call the orders search endpoint with no filters, then with three. Compare the
generated statements: different `WHERE` clauses, one query each, no `ToList` in the middle. Then add
`.ToListAsync()` after the first `Where` and watch the rest of the filtering disappear from the SQL
while the results stay identical — silent, and a hundred times slower.

Then request `?sort=banana` and confirm you get the default ordering rather than a 500.

## What to remember

- `IQueryable` is lazy: build conditionally, execute once at the end.
- Never materialise mid-build — one `ToList()` moves everything after it into memory.
- `if`-chaining gives `AND`; `OR` needs composed expression trees.
- Prefer a conditional tree over `p == null || col == p`, which is non-SARGable and sniffs badly.
- Sort via an allow-list dictionary of pre-built expressions, never a dynamic property name.
- Always fall back to a deterministic default sort, and add a unique tie-breaker.
- A function around a column kills the index. Fix the collation instead of calling `ToLower()`.

**Code:** [`OrderSpecifications.cs`](../../src/LogiFlow.Domain/Orders/OrderSpecifications.cs) ·
[`OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs) ·
[`Common/Pagination.cs`](../../src/LogiFlow.Application/Common/Pagination.cs)

**Back to:** [Module 09](README.md)
