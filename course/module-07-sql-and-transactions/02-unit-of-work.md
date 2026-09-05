# 2. Transactions and isolation levels

> Part of [Module 07 — SQL, transactions and concurrency](README.md), section 2.
> Previous: [1. Indexes](README.md#1-indexes) ·
> Next: [3. Optimistic concurrency — the lost update](04-concurrency.md)

---

A transaction is a promise about four things — **ACID** — and only one of them is interesting in
practice.

| | Means | Who provides it |
|---|---|---|
| **A**tomicity | all of it or none of it | the database, reliably |
| **C**onsistency | constraints hold at commit | your schema |
| **I**solation | concurrent transactions do not corrupt each other | **the level you choose** |
| **D**urability | committed means survived a crash | the database, reliably |

A, C and D are essentially free. **Isolation is a dial**, and every position on it trades correctness
for concurrency. Knowing which position you are on — and which one your ORM chose for you — is the
part interviews probe.

## The anomalies, in the order they get worse

Read these as *the things a level permits*, not as abstract definitions:

**Dirty read** — you read a row another transaction has changed but not committed. It then rolls
back, and you acted on a number that never existed.

**Non-repeatable read** — you read a row, somebody commits a change, you read it again in the same
transaction and get a different value. A report whose subtotal and total disagree.

**Phantom read** — you run `WHERE Status = 'Submitted'` twice in one transaction and the second run
returns an extra row, because somebody inserted one. Not a changed row — a *new* one.

**Lost update** — two transactions read 10, both add one, both write 11. One increment vanished. This
one is not on the ANSI list and it is the one you will actually hit; it is
[the next chapter](04-concurrency.md).

## The levels

| Level | Dirty | Non-repeatable | Phantom | Cost |
|---|---|---|---|---|
| `READ UNCOMMITTED` | ✅ allowed | ✅ | ✅ | none — and no guarantees |
| `READ COMMITTED` | ❌ | ✅ | ✅ | **SQL Server's default** |
| `REPEATABLE READ` | ❌ | ❌ | ✅ | holds shared locks to commit |
| `SERIALIZABLE` | ❌ | ❌ | ❌ | range locks; real contention |
| `SNAPSHOT` | ❌ | ❌ | ❌ | row versions in tempdb, no read locks |

Three things worth carrying out of that table:

**SQL Server's default is `READ COMMITTED`,** and by default it implements it with *locks* — a reader
blocks behind a writer. PostgreSQL's `READ COMMITTED` uses MVCC and does not block. So "read
committed" does not describe the same runtime behaviour across databases, which is why blocking
problems appear when a team ports a workload.

**`READ_COMMITTED_SNAPSHOT` is the setting most SQL Server databases should have on.** It keeps the
same isolation level and implements it with row versions instead of shared locks, so readers stop
blocking writers. The cost is tempdb usage. This is a genuinely good thing to raise in an interview
because it is a one-line change with a large effect that many teams have never made.

**`NOLOCK` is `READ UNCOMMITTED`.** It is extremely common in Italian *gestionale* codebases as a
reflex fix for blocking, and it does not merely risk dirty reads: under page splits it can return the
same row twice or **skip rows entirely**. If reads are blocking writes, the answer is
`READ_COMMITTED_SNAPSHOT`, not `NOLOCK`.

## Where transactions come from here

You will not find `BeginTransaction` in a handler. Two mechanisms cover it:

**`SaveChangesAsync` is already transactional.** EF wraps everything in one `SaveChanges` call in an
implicit transaction. For the common case — change one aggregate, save — that is the whole story, and
adding an explicit transaction around it buys nothing.

**`TransactionBehavior` wraps every command.** Because commands go through a
[pipeline](../module-08-cqrs/05-pipeline-behaviors.md), the transaction boundary is declared once
rather than remembered per handler. Queries do not get one, deliberately: a read does not need it and
would only take locks.

When an explicit transaction is genuinely needed — several `SaveChanges` calls that must commit
together — `IUnitOfWork.ExecuteInTransactionAsync` is the door, and it carries the detail people get
wrong:

```csharp
IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

return await strategy.ExecuteAsync(async token =>
{
    await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(token);
    T result = await operation(token);
    await context.SaveChangesAsync(token);
    await transaction.CommitAsync(token);
    return result;
}, ct);
```

With `EnableRetryOnFailure` on — and you want it against Azure SQL — a retry *inside* a manual
transaction would replay part of the work. EF refuses to let you:

> The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
> user-initiated transactions.

Putting the whole transaction inside the strategy means a retry replays it from `BeginTransaction`.

## Keep them short

Every rule about transactions reduces to one: **a transaction holds locks, so hold it for as little
time as possible.**

```csharp
// ✗ An HTTP call inside a transaction. Locks held for a network round trip.
await using var tx = await db.Database.BeginTransactionAsync(ct);
order.Submit();
await emailClient.SendAsync(...);        // 400 ms, or a timeout, with locks held
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

Nothing external belongs inside — that is what the [outbox](../module-06-efcore/07-outbox-pattern.md)
is for. Neither does user interaction, and neither does a `Thread.Sleep` somebody added while
debugging.

## Deadlocks

Two transactions each holding what the other needs. SQL Server detects the cycle, picks a victim, and
throws error 1205 — *Transaction was deadlocked on lock resources*.

```
Transaction A: UPDATE Orders     … then UPDATE StockItems
Transaction B: UPDATE StockItems … then UPDATE Orders
```

Both are individually correct. Together they deadlock about half the time under load.

The fixes, in order:

1. **Consistent ordering.** If every transaction touches tables — and rows — in the same order, the
   cycle cannot form. This is the real fix and it is free.
2. **Shorter transactions.** Fewer locks, held less long.
3. **Cover the query with an index.** A scan locks far more rows than a seek, so an index is a
   concurrency fix as much as a speed one — see [section 1](README.md#1-indexes).
4. **Retry.** A deadlock victim is a *transient* failure and retrying is legitimate, which is what
   `EnableRetryOnFailure` does. Retrying without the first three is treating a symptom.

## The mistakes

**`NOLOCK` everywhere.** Turn on `READ_COMMITTED_SNAPSHOT` instead.

**A transaction spanning a user's think time.** Load, show a form, wait for a human, commit. The
answer is [optimistic concurrency](04-concurrency.md), not a long transaction.

**Assuming `SERIALIZABLE` is "just safer".** It takes range locks and will deadlock under
concurrency you previously survived.

**Not knowing the default.** "What isolation level does your code run at?" is a fair question and
"I have never thought about it" is a real answer people give.

## Try it

```bash
```

Open two SSMS or `sqlcmd` sessions against `logiflow`. In the first:

```sql
BEGIN TRAN;
UPDATE logiflow.Orders SET CancellationReason = 'test' WHERE Id = (SELECT TOP 1 Id FROM logiflow.Orders);
-- do NOT commit
```

In the second, `SELECT` that row. It hangs — a reader blocked by a writer under default
`READ COMMITTED`. Now roll back the first, run
`ALTER DATABASE LogiFlow SET READ_COMMITTED_SNAPSHOT ON;` and repeat: the read returns the old value
immediately, no blocking. Same isolation level, completely different behaviour.

## What to remember

- A, C and D come free; **I** is the dial you choose.
- SQL Server defaults to `READ COMMITTED` implemented with locks, so readers block writers.
- `READ_COMMITTED_SNAPSHOT` fixes that for the cost of tempdb, and most databases should have it on.
- `NOLOCK` is `READ UNCOMMITTED` and can return duplicated or missing rows. Not a fix for blocking.
- `SaveChangesAsync` is already a transaction; explicit ones are for spanning several saves.
- Wrap manual transactions in `CreateExecutionStrategy` or retries break them.
- Nothing external — HTTP, email, user input — inside a transaction.
- Deadlocks: order your writes consistently first, retry last.

**Code:** [`UnitOfWork.cs`](../../src/LogiFlow.Infrastructure/Persistence/UnitOfWork.cs) ·
[`TransactionBehavior.cs`](../../src/LogiFlow.Application/Behaviors/TransactionBehavior.cs)

**Next:** [3. Optimistic concurrency — the lost update](04-concurrency.md)
