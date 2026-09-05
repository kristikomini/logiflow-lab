# 6. Pagination that does not lie

> Part of [Module 07 — SQL, transactions and concurrency](README.md), section 6.
> Previous: [4. Inventory: where oversells come from](05-inventory-concurrency.md) ·
> Back to [the module](README.md)

---

Every list endpoint needs paging, everyone writes `Skip(n).Take(m)`, and it has two problems that
both only appear once the system is worth using: it gets **slower the deeper you go**, and it
**silently repeats and skips rows** while data changes underneath it.

## Problem one: offset is not free

```sql
SELECT * FROM logiflow.Orders
ORDER BY CreatedAtUtc DESC
OFFSET 100000 ROWS FETCH NEXT 20 ROWS ONLY;
```

The database cannot jump to row 100 001. `OFFSET` means *read rows and discard them*, so it reads a
hundred thousand rows, throws them away, and returns twenty. Page 1 is instant, page 5 000 is a
table scan, and the cost grows linearly with the page number.

The symptom in production is a monitoring graph where a handful of requests are two orders of
magnitude slower than the rest — usually a crawler or an export job walking to the end.

## Problem two: it lies

This is the worse one, because nothing is slow and nothing errors.

```
Page 1 (rows 1–20)   ← the reader gets orders 100…81
   … meanwhile somebody submits a new order …
Page 2 (rows 21–40)  ← everything shifted down by one
```

Order 81 was the last row of page 1. After the insert it is the *first* row of page 2, so the reader
sees it twice — and the row that should have been at the boundary is never returned at all.

For a human clicking through a UI this is mildly confusing. For an export job or a nightly
synchronisation it means **duplicated and missing records**, discovered weeks later by an accountant.

## Keyset pagination

Instead of "skip 100 000 rows", say **"give me the rows after this one"**:

```sql
SELECT TOP (21) *
FROM   logiflow.Orders
WHERE  (CreatedAtUtc < @lastCreatedAt)
    OR (CreatedAtUtc = @lastCreatedAt AND Id < @lastId)
ORDER  BY CreatedAtUtc DESC, Id DESC;
```

An index on `(CreatedAtUtc DESC, Id DESC)` turns that into a **seek** straight to the position and a
scan of twenty-one rows. Page 5 000 costs exactly what page 1 costs. And because the position is a
*value* rather than a count, an insert elsewhere cannot shift it — no repeats, no skips.

### The three details that make it correct

**1. The tie-breaker is not optional.** `CreatedAtUtc` is not unique. Ordering by it alone is not a
*total* order, so the database may return equal-timestamped rows in any order — and in a different
order on the next execution. A unique column in the sort makes the position meaningful.

**2. "After" is a compound comparison.** The naive `WHERE CreatedAtUtc < @last` silently drops every
row sharing the cursor's timestamp. The correct predicate is the row-value comparison
`(CreatedAtUtc, Id) < (@at, @id)`, written as the `OR` above. Getting this wrong loses rows without
any error, which is the worst failure mode a paging bug can have.

**3. Fetch one extra row.** `TOP (21)` for a page size of 20. If twenty-one come back there is
another page; discard the extra and only its *existence* is used. The alternative is a second
`COUNT(*)` over the same predicate, which on a large table costs more than the page itself.

## The cursor

The client must not be handed `?lastCreatedAt=2026-03-14T15:09:26.5350000%2B01:00&lastId=6f0f…`. That
is your sort key as a public contract — change the ordering and every bookmarked URL breaks.

Encode it instead:

```csharp
public string Encode() =>
    Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Id:D}"));
```

Three requirements, each of which is a bug if missed:

- **The `"O"` format specifier.** Round-trip format: full sub-second precision and the offset. Any
  shorter format truncates, and a cursor rounded to the second skips every row sharing that second.
- **URL-safe.** No `+`, `/` or `=`, because it travels in a query string.
- **`TryDecode`, never `Decode`.** It arrives from users, crawlers, and bookmarks three releases old.
  Malformed input must be a `false`, not a 500.

And be clear that **opaque is not secure**. Anyone can decode it; that is fine, because it carries no
secret. Encoding is not encryption.

## When offset is still right

Do not turn this into an absolute — the interview answer is the trade-off, not the slogan.

**Offset wins when the user needs numbered pages** — "page 7 of 43", jump to the last page. Keyset
cannot do that, because it has no idea how far in it is. An admin screen over ten thousand rows with
a page-number control is a perfectly good use of `OFFSET`.

**Keyset wins for infinite scroll, APIs, exports and anything past a few thousand rows** — anywhere
the reader moves forward and correctness matters more than knowing the page number.

A common compromise: offset for the UI's first few pages, cursors for the API that machines consume.

## The mistakes

**Ordering by a non-unique column with no tie-breaker.** Non-deterministic results, and keyset
paging that quietly skips rows.

**Sorting by a user-supplied column name with string concatenation.** SQL injection with extra steps.
Map the input to a known expression — see
[module 09 section 7](../module-09-advanced-linq/README.md).

**A `COUNT(*)` on every page.** Doubles the work for a number the user rarely reads. Fetch `n + 1`,
or return `hasMore` instead of a total.

**Paging without an `ORDER BY`.** SQL does not guarantee row order without one, so the "pages" are
arbitrary and may overlap. `OFFSET` without `ORDER BY` is not even legal on SQL Server, which is the
one place the database saves you.

## Try it

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab07"
```

Lab 07 exercise 3 is this chapter, executable. One test inserts a row between page one and page two
and asserts nothing is repeated or skipped — implement it with `Skip`/`Take` first and watch that
test fail, then with a cursor and watch it pass. That single test is the argument.

Then, against the real database:

```sql
SET STATISTICS IO ON;
SELECT * FROM logiflow.Orders ORDER BY CreatedAtUtc DESC OFFSET 50000 ROWS FETCH NEXT 20 ROWS ONLY;
```

and compare the logical reads with the keyset version. The difference is not subtle.

## What to remember

- `OFFSET` reads and discards rows, so deep pages get linearly slower.
- Concurrent inserts shift offsets, so `Skip`/`Take` repeats and skips rows silently.
- Keyset paging seeks by value: constant cost, and stable under concurrent writes.
- Always include a unique tie-breaker in the sort.
- "After" is a compound comparison — `(a, b) < (@a, @b)`, not `a < @a`.
- Fetch `n + 1` to know whether there is a next page; avoid `COUNT(*)` per page.
- Cursors are opaque, URL-safe, round-trip formatted, and decoded with `Try`.
- Offset is still right when the user needs numbered pages. Say the trade-off, not the slogan.

**Code:** [`Common/Pagination.cs`](../../src/LogiFlow.Application/Common/Pagination.cs) ·
[`Queries/OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs) ·
[`Lab07_Specifications.cs`](../../labs/Labs.Exercises/Exercises/Lab07_Specifications.cs)

**Back to:** [Module 07](README.md)
