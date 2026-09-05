# Views, functions and procedures

> The three objects a team saves a query inside the database as — what each is for, which one is a
> performance trap that looks helpful, and what changes when EF Core has to call it.

Everything so far in this module has been a query you send. This chapter is about a query somebody
already saved, that you now have to read, call, or defend a decision about.

It matters commercially: adverts in this region ask for *"nozioni base di stored procedure, viste o
funzioni SQL"* as a named requirement. When T-SQL is listed before any ORM, it means people on that
team open SSMS and write SQL by hand — and some of that SQL lives in the schema rather than in the
repository.

---

## 1. A function answers, a procedure acts

| | View | Function | Stored procedure |
|---|---|---|---|
| Takes parameters | no | yes | yes |
| Usable inside a `SELECT` | yes — like a table | yes | **no** |
| Can modify data | rarely, with limits | **no** | yes |
| Transactions, control flow | no | limited | yes |
| Composable from LINQ | yes | yes | **no** |

The line that decides between them: **if it needs a transaction it is a procedure; if it belongs in
a `WHERE` clause it is a function or a view.**

---

## 2. A view is a saved query, not saved data

```sql
CREATE OR ALTER VIEW logiflow.vw_OrderSummary
AS
SELECT   o.Id, o.OrderNumber, o.CreatedAtUtc, c.Id AS CustomerId, c.Name AS CustomerName,
         SUM(l.Quantity * l.UnitPrice) AS Total
FROM     logiflow.Orders o
JOIN     logiflow.Customers c ON c.Id = o.CustomerId
JOIN     logiflow.OrderLines l ON l.OrderId = o.Id
GROUP BY o.Id, o.OrderNumber, o.CreatedAtUtc, c.Id, c.Name;
```

At execution the definition is **expanded into your query** and the whole thing is optimised as one
statement. So `SELECT * FROM vw_OrderSummary WHERE Total > 1000` costs exactly what the full query
costs. A view saves typing, not work.

Which makes the interview question — *"does a view improve performance?"* — a test of whether you
have ever looked at a plan. The answer is no, and then the three things it is actually for:

- **A stable contract over an unstable schema.** Ten reports select from the view; you split
  `Orders` into two tables and fix the view once.
- **A permission boundary.** Grant `SELECT` on the view and not on the base table, and a user sees
  order totals without seeing the customer's payment columns. Enforced by the engine.
- **Naming a join people get wrong.** If four developers have written the same five-table join and
  two of them made it an inner join by accident, write it once.

> **Nested views** are how a two-column query ends up touching twelve tables. Each layer looks
> reasonable. When something is mysteriously slow, expand every view in it before suspecting the
> optimiser.

### The exception: an indexed view really is stored

`WITH SCHEMABINDING` plus a unique clustered index materialises it — real, persisted, maintained
automatically. The costs are the point:

- **Every write to the base tables now maintains that index**, inside the same transaction. You
  moved cost from reads onto writes. On a table written far more often than the aggregate is read,
  that is a bad trade.
- **`SCHEMABINDING` locks the base tables.** You cannot alter a referenced column until the view is
  dropped — correctness at the price of a migration that has to drop and recreate it.
- The rules are strict: no outer joins, no subqueries, deterministic expressions, and
  `COUNT_BIG(*)` whenever you aggregate.
- **Automatic matching is an Enterprise feature.** Elsewhere you must reference the view and add
  `WITH (NOEXPAND)`.

---

## 3. The scalar function trap

```sql
-- Reads well. Behaves terribly.
CREATE OR ALTER FUNCTION logiflow.fn_OrderTotal(@OrderId INT)
RETURNS DECIMAL(18,2)
AS
BEGIN
    RETURN (SELECT SUM(Quantity * UnitPrice) FROM logiflow.OrderLines WHERE OrderId = @OrderId);
END;
GO

SELECT Id, logiflow.fn_OrderTotal(Id) FROM logiflow.Orders;   -- once per row
```

Four things go wrong at once, and the third is why it is so hard to find:

1. It executes **per row** instead of folding into the set operation.
2. The optimiser **cannot see inside it**, so it costs the call at roughly nothing.
3. Therefore **the execution plan looks cheap while the query takes minutes.** The plan lies to
   you — and this module has otherwise taught you to trust the plan.
4. In a `WHERE` clause it also destroys SARGability (section 1): no seek is possible on
   `WHERE fn_Something(Col) = 5`.

SQL Server 2019 added scalar UDF inlining, which rewrites many of these into the surrounding query
automatically. It needs compatibility level 150, it does not cover every function, and plenty of
production databases run lower. Know that it exists; do not rely on it.

### The version that is fine

```sql
CREATE OR ALTER FUNCTION logiflow.fn_OrdersForCustomer(@CustomerId INT)
RETURNS TABLE            -- no BEGIN/END: that is what makes it inline
AS
RETURN
(
    SELECT o.Id, o.OrderNumber, o.Total
    FROM   logiflow.Orders o
    WHERE  o.CustomerId = @CustomerId
);
```

An **inline table-valued function** is expanded like a view, so the optimiser sees straight through
it. It is, in effect, *a view that takes parameters*, and it should be your default whenever you
were reaching for a function. It composes with `WHERE`, and joins per row with `CROSS APPLY` while
still producing one set-based plan.

A **multi-statement** TVF — `RETURNS @t TABLE (…)` with a `BEGIN` — materialises into a table
variable the optimiser cannot see through, and historically guessed a fixed row count. Same failure
shape as the table-variable estimate. If it can be rewritten as inline, rewrite it.

---

## 4. A procedure that is worth copying

```sql
CREATE OR ALTER PROCEDURE logiflow.usp_ArchiveOrders
    @CutoffUtc DATETIME2(3),
    @Archived  INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;      -- those "(n rows affected)" messages are network round trips,
                         -- and some clients mistake them for result sets
    SET XACT_ABORT ON;   -- on any error, kill the transaction. Without it a statement can
                         -- fail and leave the transaction OPEN, holding locks (section 2)

    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO logiflow.OrdersArchive (Id, OrderNumber, CreatedAtUtc)
        SELECT Id, OrderNumber, CreatedAtUtc
        FROM   logiflow.Orders WITH (UPDLOCK, HOLDLOCK)
        WHERE  CreatedAtUtc < @CutoffUtc;

        SET @Archived = @@ROWCOUNT;

        DELETE FROM logiflow.Orders WHERE CreatedAtUtc < @CutoffUtc;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;   -- bare THROW re-raises the ORIGINAL error, number and line intact.
                 -- RAISERROR would create a new one and lose both.
    END CATCH
END;
```

`THROW;` versus `RAISERROR` is exactly `throw;` versus `throw ex;` in C# (module 10 section 4), and
interviewers who know T-SQL enjoy the parallel.

`RETURN` in a procedure can only return an `INT` and conventionally means a status code. Anything
that is data belongs in an `OUTPUT` parameter or a result set — using `RETURN` for a row count is a
legacy habit that confuses every later reader.

### Parameter sniffing — "it was fast yesterday"

The plan is compiled on first execution using **the parameter values it was given that time**, then
cached and reused. Compiled for the customer with three orders, it uses a seek and a nested loop;
applied to the customer with 400,000 it is catastrophic. Nothing in your code changed, which is
what makes the report so confusing.

In order of preference: fix the indexes first (sniffing hurts most when both plans are bad), then
`OPTION (RECOMPILE)` on the offending statement, then `OPTIMIZE FOR UNKNOWN` when deliberately
mediocre for everyone is what you want, then split into two procedures when there really are two
shapes of query. SQL Server 2022's Parameter Sensitive Plan optimisation reduces the problem; it
does not remove the interview question.

---

## 5. Calling them from EF Core

```csharp
// Rows that match an entity, or a keyless type. Interpolation here IS parameterised.
var orders = await db.Orders
    .FromSqlInterpolated($"EXEC logiflow.usp_GetCustomerOrders {customerId}")
    .ToListAsync(ct);

// A procedure that just does something.
await db.Database.ExecuteSqlInterpolatedAsync(
    $"EXEC logiflow.usp_ArchiveOrders {cutoffUtc}", ct);
```

**You cannot compose LINQ on top of a procedure call.** Adding `.Where(...)` after `FromSql…` on an
`EXEC` throws, because EF wraps your SQL as a derived table and SQL Server will not accept `EXEC`
there.

That is the same mechanism as the `NEXT VALUE FOR` failure in section 5 of this module's README:
**EF's raw-SQL helpers are still composable query builders.** Two symptoms, one cause — and noticing
that is worth more than memorising either.

Views and inline TVFs *do* compose, which is a concrete reason to prefer them when the thing is a
query rather than an action:

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    b.Entity<OrderSummary>().HasNoKey().ToView("vw_OrderSummary");

    // An inline TVF, usable INSIDE a LINQ query and still translated to SQL.
    b.HasDbFunction(() => OrdersForCustomer(default)).HasName("fn_OrdersForCustomer");
}
```

For a procedure returning several result sets, or a shape that changes by parameter, EF will fight
you and **Dapper is the better tool**. A team that otherwise uses EF Core keeping Dapper for exactly
this is showing judgement, not inconsistency (module 26).

---

## 6. Triggers, and why this codebase has none

A trigger fires **once per statement, not once per row** — `inserted` and `deleted` are tables.
Logic written as though it were per-row silently handles only the first row of a multi-row `UPDATE`,
passes every hand test, and fails on the first batch.

This repository has none, for three reasons worth being able to state:

- **Action at a distance.** An `UPDATE` that also writes two other tables, with nothing at the call
  site saying so.
- **It runs inside your transaction**, widening every lock and rolling back a statement the caller
  believed was simple.
- **It breaks EF Core's fast path.** EF uses an `OUTPUT` clause to read back generated values, and
  SQL Server forbids that on a table with triggers — so saving throws until the entity declares
  `.ToTable(t => t.HasTrigger("trg_…"))`, which switches EF to a slower, less batched strategy. A
  trigger added by a DBA can break an application nobody touched, with an error that names no
  trigger.

When a trigger *is* right: when you do not control every writer. If rows arrive from an integration
job, a nightly import and someone in SSMS as well as from your API, application-level auditing only
records the polite callers. Then say the alternative you would prefer — **temporal tables**
(`SYSTEM_VERSIONING`) give full row history maintained by the engine with no procedural code to get
wrong.

---

## 7. So where should the logic live?

There is no correct answer, only a considered one. Argue both sides:

| In the database | In C# |
|---|---|
| One place, whoever calls it — the API, the nightly job, the report tool, the person in SSMS | One language, one repository, one review process |
| Set-based work stays next to the data; updating a million rows should not cross a network | Testable without a database, debuggable with a debugger |
| Fixable in production without a deployment | Versioned with the code that calls it; a migration is reviewable and repeatable |
| …which is also the problem: changed without review, invisible to source control | …which is also the problem: a row-by-row loop where one `UPDATE` would do |

The position this repository takes: **business rules in the domain, set-based data work in the
database.** "An order cannot ship twice" is an invariant and belongs in C# where it is tested
(module 05). Archiving two million rows belongs in a procedure, because pulling them into memory to
send them back is absurd.

And whatever the team already does, **consistency beats your preference** — a codebase with logic in
both places and a rule in neither is the worst of the three outcomes. Say that too.

---

## Golden rules

1. **A view is a saved `SELECT`, not saved data.** It makes nothing faster; it makes things
   nameable, and it is a real permission boundary.
2. **An indexed view is real data** — `SCHEMABINDING`, a unique clustered index, `COUNT_BIG(*)` —
   and it moves cost from reads onto every write.
3. **Scalar UDFs run per row and lie in the plan.** Prefer an inline table-valued function: a view
   that takes parameters.
4. **A function answers, a procedure acts.** If it needs a transaction, it is a procedure.
5. **`SET NOCOUNT ON` and `SET XACT_ABORT ON` at the top of every procedure.** One saves chatter,
   the other stops an abandoned open transaction holding locks.
6. **Bare `THROW` preserves the original error; `RAISERROR` loses it** — the same rule as `throw;`
   versus `throw ex;`.
7. **Parameter sniffing means the plan was compiled for somebody else's parameter.** Indexes first,
   then `OPTION (RECOMPILE)`.
8. **You cannot compose LINQ over `EXEC`** — same cause as `NEXT VALUE FOR` failing in
   `SqlQuery<T>`. Views and inline TVFs compose; procedures do not.
9. **A trigger fires once per statement, not once per row** — and it breaks EF's `OUTPUT` fast path.
   Prefer temporal tables for history.
10. **Business rules in the domain, set-based work in the database** — and consistency with the
    existing team beats both.

---

← [Pagination that does not lie](06-pagination.md) · [Module 07](README.md)
