# Module 07 — SQL, transactions and concurrency

> The module where senior candidates separate themselves. Anyone can write a `Where`. Explaining
> why it does a table scan, and what happens when two people submit at once, is the differentiator.

Keep `Microsoft.EntityFrameworkCore.Database.Command: Information` on for this whole module.

---

## In this module

The sections below are summaries. Each links to a chapter that goes further — the code, the traps,
and the interview answer.

| | Chapter | |
|---|---|---|
| 2 | [Transactions and isolation levels](02-unit-of-work.md) | the dial you choose, and why `NOLOCK` is not a fix |
| 3 | [Optimistic concurrency](04-concurrency.md) | the lost update, and `rowversion` |
| 4 | [Inventory: where oversells come from](05-inventory-concurrency.md) | check-then-act, and three ways to fix it |
| 6 | [Pagination that does not lie](06-pagination.md) | why `Skip`/`Take` repeats and skips rows |
| 7 | [Views, functions and procedures](07-views-functions-procedures.md) | the scalar-UDF trap, and why the plan lies |

---

## 1. Indexes

An index is a sorted copy of some columns with pointers back to the rows. It makes reads fast and
writes slower — every `INSERT` must update every index.

### Column order is not arbitrary

```csharp
builder.HasIndex(o => new { o.CustomerId, o.CreatedAtUtc });
```

**Leftmost-prefix rule:** this index serves
`WHERE CustomerId = @x ORDER BY CreatedAtUtc`, and `WHERE CustomerId = @x` alone.
It does **not** help `WHERE CreatedAtUtc > @d` on its own.

Rule of thumb: **equality columns first, then range/sort columns.**

```
   INDEX (CustomerId, CreatedAtUtc)   —   a phone book sorted by (Surname, FirstName)

   WHERE CustomerId = @c                          seek    "all the Rossis"
   WHERE CustomerId = @c ORDER BY CreatedAtUtc    seek    "the Rossis, already in date order"
   WHERE CustomerId = @c AND CreatedAtUtc > @d    seek    "the Rossis since March"
   WHERE CreatedAtUtc > @d                        SCAN    "everyone called Marco" — the second
                                                          column is only sorted WITHIN a surname
```

### Filtered indexes

```csharp
builder.HasIndex(o => o.Status).HasFilter("[Status] IN (2, 3)");
```

Only submitted and confirmed orders are indexed, so the index stays small even when the table
holds ten years of delivered orders. One of SQL Server's genuinely underused features.

📂 [`Configurations/OrderConfiguration.cs`](../../src/LogiFlow.Infrastructure/Persistence/Configurations/OrderConfiguration.cs)

### The unique-index NULL trap

```csharp
builder.HasIndex(s => s.TrackingReference)
    .IsUnique()
    .HasFilter("[TrackingReference] IS NOT NULL");
```

**SQL Server treats multiple NULLs as duplicates in a unique index** — unlike the SQL standard.
Without that filter, the second shipment still in `Preparing` (tracking reference null) violates
the constraint. This bites people regularly and the fix is not obvious.

### SARGability

A predicate is *SARGable* if an index seek can be used. Wrapping a column in a function kills it:

```sql
WHERE YEAR(CreatedAtUtc) = 2026                                    -- ❌ scan
WHERE CreatedAtUtc >= '2026-01-01' AND CreatedAtUtc < '2027-01-01' -- ✅ seek
```

📂 See `ShipmentRepository.GetByTrackingNumberAsync` — it normalises the *parameter* to upper case
before the query rather than calling `ToUpperInvariant()` on the column inside the expression,
specifically to keep the lookup an index seek.

### Reading an execution plan

```sql
SET STATISTICS IO ON;
SET SHOWPLAN_TEXT ON;
```

In SSMS or Azure Data Studio, "Include Actual Execution Plan". What to look for:

- **Clustered Index Seek** — good.
- **Clustered Index Scan / Table Scan** — reading everything. Fine on 100 rows, fatal on 10M.
- **Key Lookup** — the index found the row but not all requested columns, so it went back to the
  table per row. Fix by adding an `INCLUDE`.
- A **thick arrow** means many rows moving between operators.

---

## 2. Transactions and isolation levels

A transaction is atomic, consistent, isolated, durable. The interesting letter is **I**.

| Level | Dirty read | Non-repeatable read | Phantom | Notes |
|---|---|---|---|---|
| Read Uncommitted | ✅ possible | ✅ | ✅ | `WITH (NOLOCK)`. Reads uncommitted garbage. |
| **Read Committed** | ❌ | ✅ | ✅ | **SQL Server default** |
| Repeatable Read | ❌ | ❌ | ✅ | holds read locks |
| Serializable | ❌ | ❌ | ❌ | correct, and the most contention |
| Snapshot | ❌ | ❌ | ❌ | row versioning; readers never block writers |

**`WITH (NOLOCK)` is not a performance trick.** It reads uncommitted data, and can return the
same row twice or skip it entirely during a page split. Using it on financial data is how you get
reports that do not reconcile.

`SaveChangesAsync` already wraps itself in a transaction. You only need an explicit one when a
use case must read, decide and write across several saves.

📂 [`Persistence/UnitOfWork.cs`](../../src/LogiFlow.Infrastructure/Persistence/UnitOfWork.cs)

### The execution-strategy trap

This is worth reading carefully — most codebases have this bug latent.

With `EnableRetryOnFailure` configured (and you should, because SQL Server drops connections and
Azure SQL does so routinely), the obvious transaction code throws:

```
The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
user-initiated transactions.
```

The retry strategy can only retry an operation it controls end to end. If you open the
transaction yourself, a retry would re-run half the work inside a rolled-back transaction. EF
refuses rather than corrupt your data.

The fix is to hand the **whole block** to the strategy via `IExecutionStrategy.ExecuteAsync` —
which also means **the operation must be idempotent**, because it may genuinely run twice.

---

## 3. Optimistic concurrency — the lost update

Two users load order #1234. Both edit. Both save. **The second silently overwrites the first.**
No error. The first user's change simply never existed.

```
   time ──────────────────────────────────────────────────────────────────►
   User A    read v1 ──── edits ──── SAVE ok
   User B          read v1 ──── edits ─────────── SAVE ok
                                                   └─ A's change is gone. Nothing was logged.
                                                      Nobody finds out until A looks again.

   with rowversion on the row:
   User B                                          UPDATE … WHERE Id = @id AND RowVersion = v1
                                                   0 rows matched
                                                   ⇒ DbUpdateConcurrencyException
                                                   ⇒ 409 Conflict: reload and retry
```

### The fix: `rowversion`

```csharp
builder.Property(o => o.RowVersion).IsRowVersion();
```

SQL Server bumps this 8-byte value on every `UPDATE`. EF adds it to the `WHERE` clause:

```sql
UPDATE Orders SET Status = 2 WHERE Id = @id AND RowVersion = @originalRowVersion
```

If someone wrote first, zero rows match and EF throws `DbUpdateConcurrencyException` —
which [`GlobalExceptionHandler`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs) maps
to **409 Conflict**, not 500. That distinction matters: 409 tells the client to reload and retry;
500 tells them to give up.

### Optimistic vs pessimistic

- **Optimistic** (detect the clash): no locks held, scales well, needs retry logic. Right for
  typical web traffic where conflicts are rare — and locks cannot survive a stateless HTTP
  request anyway.
- **Pessimistic** (`UPDLOCK`, hold a lock): guarantees success once acquired, at the cost of
  contention and deadlock risk. Right when conflicts are frequent and retrying is expensive.

---

## 4. Inventory: where oversells come from

📂 [`Domain/Inventory/StockItem.cs`](../../src/LogiFlow.Domain/Inventory/StockItem.cs)

Naive systems store one `Quantity` and oversell. This one stores three:

```
QuantityOnHand    units physically on the shelf
QuantityReserved  of those, how many are promised to submitted orders
QuantityAvailable = OnHand − Reserved      ← what you may still sell
```

The distinction between "on hand" and "available" is the whole thing. Stock is only physically
removed when a picker takes it, but it stops being *sellable* the moment an order is submitted.
Track one number and you either oversell (decrement too late) or show phantom stock-outs
(decrement too early).

**Picking reduces both `OnHand` and `Reserved` by the same amount**, so `Available` is unchanged —
that invariant is what proves the model is consistent.

```
   OnHand     ████████████████████  20   physically on the shelf
   Reserved   ████████               8   of those, promised to submitted orders
   Available  ········████████████  12   = OnHand − Reserved   ← what you may still sell

   submit an order for 3    Reserved +3        Available 12 → 9    nothing has moved yet
   a picker takes them      OnHand −3          Available  9 → 9    ← the invariant that proves
                            Reserved −3                              the model is consistent

   track ONE number instead and you either oversell (decrement too late)
   or show phantom stock-outs (decrement too early). There is no third option.
```

### The in-memory check is not enough

`Warehouse.Reserve` checks availability in memory. Two concurrent requests can both load a row
showing 10 available, both pass the check for 8, and both save. **16 units promised out of 10.**

The aggregate cannot solve this alone. Three layers do:

1. **`rowversion`** makes the second save throw.
2. **A retry** with freshly-read data.
3. **A CHECK constraint** as the last line of defence:
   ```csharp
   t.HasCheckConstraint("CK_StockItems_ReservedNotExceedingOnHand",
       "[QuantityReserved] <= [QuantityOnHand] AND [QuantityReserved] >= 0");
   ```
   The database is the only guard that applies to *every* writer — including a bad migration or a
   DBA at 2am.

---

## 5. Sequences, and gapless numbering

```sql
SELECT NEXT VALUE FOR logiflow.OrderNumbers
```

Atomic and lock-free — a dedicated allocator, not a row anyone contends on.

**The wrong way**, which appears in a lot of production code:
`SELECT MAX(Number) + 1 FROM Orders`. Two concurrent requests read the same maximum and produce
the same reference. Works in every test; fails on the first busy morning.

**A sequence is not gapless.** A value consumed by a transaction that rolls back is lost, so
numbers can skip. If your accountants require a gapless series — and in some jurisdictions
invoice numbering legally must be — a sequence is the wrong tool and you need a counter table
with a real lock, accepting the contention.

### The EF trap in this file

📂 [`Services/Services.cs`](../../src/LogiFlow.Infrastructure/Services/Services.cs) — `OrderNumberGenerator`

The obvious implementation fails at runtime:

```csharp
context.Database.SqlQuery<int>($"SELECT NEXT VALUE FOR logiflow.OrderNumbers")
```
```
Msg 11719: NEXT VALUE FOR function is not allowed in ... sub-queries, derived tables ...
```

`SqlQuery<T>` **composes** — EF wraps your statement as a derived table so it can apply `Where`
and `OrderBy`. SQL Server forbids `NEXT VALUE FOR` there. The fix is a real `DbCommand`.

Generalise it: **EF's raw-SQL helpers are still composable query builders.** Anything that must
execute verbatim needs an actual command.

---

## 6. Pagination that does not lie

📂 [`Queries/OrderQueries.cs`](../../src/LogiFlow.Infrastructure/Persistence/Queries/OrderQueries.cs)

### The count must come before paging

```csharp
int totalCount = await orders.CountAsync(ct);   // ← BEFORE Skip/Take
orders = orders.Skip(...).Take(...);
```

Otherwise you count the page, and every client sees "Page 1 of 1". The cost is a second round
trip, and on a large filtered set the `COUNT` can be slower than the page itself.

### The tiebreaker is not decoration

```csharp
return sorted.ThenBy(o => o.Id);
```

`OFFSET/FETCH` over a **non-unique** sort key has undefined ordering among ties, so two rows with
the same `CreatedAtUtc` can appear on both page 1 and page 2 — or on neither. Appending a unique
column makes the order total.

**Almost every paginated endpoint in the wild has this bug.**

### Offset vs keyset

Offset pagination makes SQL Server read and discard 50,000 rows to serve page 2,000, and it skips
or repeats rows when data changes between requests.

**Keyset** ("give me the 25 after this id") fixes both:

```csharp
.Where(o => o.CreatedAtUtc < lastSeenDate || (o.CreatedAtUtc == lastSeenDate && o.Id > lastSeenId))
.OrderByDescending(o => o.CreatedAtUtc).ThenBy(o => o.Id).Take(25)
```

Constant time at any depth. The trade-off: no page numbers, only next/previous. Use keyset for
infinite scroll, offset for an admin table with page numbers — which is why this codebase uses
offset.

---

## 7. Views, functions and procedures

→ **[Full chapter: views, functions and procedures](07-views-functions-procedures.md)**

Three ways a team saves a query inside the database, and adverts here name all three
(*"nozioni base di stored procedure, viste o funzioni SQL"*).

**A view is a saved `SELECT`, not saved data.** Its definition is expanded into your query and
optimised as one statement, so it costs exactly what the full query costs — it saves typing, not
work. What it is genuinely for: a stable contract over a schema you may change, and a permission
boundary (`SELECT` on the view, not on the base table). The exception is an *indexed* view, which is
materialised and pays for it on every write to the base tables.

**Scalar functions are the trap.** They execute once per row, and the optimiser cannot see inside
them — so it costs the call at nearly nothing and **the plan looks cheap while the query takes
minutes**. After a whole module spent learning to trust the plan, this is the case where it lies.
Use an inline table-valued function instead: `RETURNS TABLE` with no `BEGIN`, expanded like a view,
which is really *a view that takes parameters*.

**Procedures act.** `SET NOCOUNT ON` and `SET XACT_ABORT ON` at the top, `TRY`/`CATCH` with a bare
`THROW` to re-raise the original error, `OUTPUT` parameters for data and `RETURN` only for status.
And when one is suddenly slow with no code change, suspect parameter sniffing: the cached plan was
compiled for whoever called it first.

**From EF Core**, `FromSqlInterpolated($"EXEC …")` works but **cannot be composed** — no `Where` or
`OrderBy` afterwards, because EF wraps your SQL as a derived table. That is the same mechanism as
the `NEXT VALUE FOR` failure in section 5: EF's raw-SQL helpers are still composable query builders.
Views and inline TVFs compose; procedures do not.

---

## 8. Try it

```bash
# Watch the concurrency guard work
dotnet test tests/LogiFlow.Api.IntegrationTests --filter Insufficient_stock

# Look at the reservation state directly
sqlcmd -S localhost -E -C -d LogiFlow \
  -Q "SELECT QuantityOnHand, QuantityReserved, QuantityOnHand-QuantityReserved AS Available FROM logiflow.StockItems WHERE QuantityReserved > 0"
```

Then try to break the CHECK constraint by hand and watch the database refuse:

```sql
UPDATE logiflow.StockItems SET QuantityReserved = QuantityOnHand + 1;
```

---

## 9. Golden rules

> The card. This is the module that gets people hired in this market — module 17 says why.

1. **Equality columns first, then range and sort columns.** The leftmost-prefix rule decides
   whether your index gets used at all.
2. **A function around a column kills the seek.** Compare the column raw; normalise the
   *parameter* instead.
3. **In SQL Server, several NULLs collide in a unique index.** Nullable uniqueness needs a
   filtered index — `WHERE [Col] IS NOT NULL`.
4. **`WITH (NOLOCK)` is Read Uncommitted, not a performance switch.** It can read a row twice,
   skip it entirely, or return data that was rolled back. If you need non-blocking reads, use
   snapshot isolation.
5. **Optimistic concurrency for web traffic.** No lock survives a stateless HTTP request: detect
   the clash with `rowversion`, return 409, retry.
6. **A real guarantee needs three layers** — the aggregate for a friendly error, `rowversion` for
   the race, and a CHECK constraint because the database is the only guard that every writer
   passes, including a bad migration and a DBA at 2am.
7. **`COUNT` before `Skip`/`Take`**, or every client is told it is on page 1 of 1.
8. **Every paginated `ORDER BY` needs a unique tiebreaker.** Without one, `OFFSET/FETCH` can show
   a row on two pages and another on none. Almost every paginated endpoint in the wild has this
   bug.
9. **Keyset for infinite scroll, offset for page numbers.** Offset gets slower with depth because
   the server produces and discards every preceding row.
10. **`SELECT MAX(id) + 1` is a race.** Use a sequence — and know that a sequence is not gapless,
    which matters where invoice numbering is a legal requirement.
11. **With `EnableRetryOnFailure`, hand the whole transaction to `IExecutionStrategy`** — and make
    that block idempotent, because it may genuinely run twice.
12. **Look at the execution plan before changing anything.** Seek is good, scan on a large table
    is not, and a key lookup means you are one `INCLUDE` away from a covering index.
13. **A view is a saved `SELECT`, not saved data.** It costs what the full query costs; what it
    buys is a stable contract and a permission boundary.
14. **A scalar UDF runs once per row and lies in the plan.** The optimiser cannot see inside it, so
    it costs the call at nearly nothing — the one place this module's "trust the plan" advice
    breaks. Use an inline table-valued function instead.
15. **EF's raw-SQL helpers are composable query builders.** That single fact explains both why
    `NEXT VALUE FOR` fails inside `SqlQuery<T>` and why you cannot put a `Where` after an `EXEC`.

---

## 10. Interview questions

**"How would you speed up a slow query?"**
Look at the execution plan first. Scan where you expected a seek → missing or unusable index.
Key lookup → add `INCLUDE` columns. Then check SARGability: a function around a column prevents
a seek. Only then consider denormalising. Say "measure first" — that is most of the answer.

**"What is a covering index?"**
One that contains every column the query needs, so the engine never touches the table. Built with
`INCLUDE` for non-key columns.

**"Optimistic vs pessimistic locking?"**
Optimistic detects conflicts at write time via a version column and retries; no locks held, good
when conflicts are rare, and the only workable option across stateless HTTP requests. Pessimistic
holds a lock; better when conflicts are frequent, at the cost of contention and deadlocks.

**"How do you prevent overselling?"**
Separate on-hand from reserved. Then enforce it in three places: the aggregate for a friendly
error, `rowversion` plus retry to catch the race, and a CHECK constraint so the database refuses
regardless of which code path wrote it.

**"What is wrong with `WITH (NOLOCK)`?"**
It is Read Uncommitted. You can read uncommitted data that is later rolled back, read the same
row twice, or miss it entirely. It is not a free performance win — if you need non-blocking
reads, use snapshot isolation.

**"Why does OFFSET get slower on later pages?"**
The server must produce and discard every preceding row. Keyset pagination avoids it by seeking
directly with a `WHERE` on the last-seen key.

---

---

## Do the lab

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab07"
```

[`Lab07_Specifications.cs`](../../labs/Labs.Exercises/Exercises/Lab07_Specifications.cs) is section 6
of this module made executable: an opaque cursor that does not lose precision, and keyset pagination
that does not repeat a row when somebody inserts one mid-paging — the test proves the bug that
`OFFSET` has and this does not. Answers in [SOLUTIONS.md](../SOLUTIONS.md).

## Next

→ [Module 08 — CQRS](../module-08-cqrs/)
