# 8. The transactional outbox

> Part of [Module 06 — EF Core in depth](README.md), section 8.
> Previous: [7. Interceptors](08-interceptors.md) · Next: [9. Migrations](09-migrations.md)

---

You have just saved an order. Now something outside this process needs to know: a confirmation email,
a message on a broker, a webhook to the customer's ERP. There are exactly two obvious ways to write
it, and **both are wrong**.

```csharp
await db.SaveChangesAsync(ct);          // 1. committed
await bus.PublishAsync(orderPlaced);    // 2. process dies HERE
// → the order exists and nobody was ever told.

await bus.PublishAsync(orderPlaced);    // swap them, and get the mirror bug:
await db.SaveChangesAsync(ct);          // → a message about an order that never committed.
```

This is the **dual-write problem**. You cannot commit to a database and a broker atomically — there
is no shared transaction — so no ordering of those two lines is safe. A crash, a timeout, a pod being
rescheduled mid-request: any of them lands you in one of the two failure modes, and both are silent.

## The fix: make it one write

The message becomes a **row**, inserted in the same transaction as the state change:

```
┌─ ONE database transaction ────────────────────────┐
│  UPDATE Orders SET Status = 'Submitted' ...       │
│  INSERT INTO OutboxMessages (Id, Type, Content)   │   ← the message is a ROW
└───────────────────────────────────────────────────┘
                       │  commit: both, or neither
                       ▼
   OutboxProcessor (a BackgroundService) polls unprocessed rows,
   publishes each, and marks it done.
```

Either both land or neither does, because it is one transaction against one database. There is no
distributed transaction, no two-phase commit, no coordinator — which is exactly why the pattern is
popular: it solves the problem with the tool you already have.

```csharp
public sealed class OutboxMessage
{
    public Guid Id { get; }                     // also the consumer's deduplication key
    public string Type { get; }                 // assembly-qualified event type name
    public string Content { get; }              // JSON payload
    public DateTimeOffset OccurredAtUtc { get; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? Error { get; private set; }
}
```

## The guarantee you get, exactly

**At-least-once. Not exactly-once.**

If the processor publishes successfully and then dies before marking the row processed, it publishes
again on restart. That is not a flaw to be engineered away — exactly-once delivery is impossible in a
distributed system, and any library claiming it is doing at-least-once plus deduplication somewhere.

Which is why `Id` is on the message and on the wire. The consumer deduplicates:

```csharp
if (await db.ProcessedMessages.AnyAsync(m => m.Id == message.Id, ct)) return;

await DoTheWork(message, ct);
db.ProcessedMessages.Add(new(message.Id, DateTimeOffset.UtcNow));
await db.SaveChangesAsync(ct);      // same transaction as the work
```

Better still, make the operation naturally idempotent — *set* the status to Shipped rather than
toggling it — and a duplicate is harmless with no bookkeeping at all. See
[module 25](../module-25-distributed-systems/).

## The processor, and three things about `BackgroundService`

```csharp
public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                LogiFlowDbContext db = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
                ...
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox iteration failed");   // never let this escape
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

**It is a singleton, so it cannot inject a `DbContext`.** It takes `IServiceScopeFactory` and creates
a scope per iteration. Injecting a context directly would capture one for the life of the process —
the [captive dependency](01-dbcontext-and-change-tracking.md#lifetime-scoped-and-why) trap, here with
an accumulating change tracker attached.

**An unhandled exception in `ExecuteAsync` kills the host**, by default since .NET 6
(`BackgroundServiceExceptionBehavior.StopHost`). That default is right — a background worker dying
silently is worse than a crash — but it means the loop body must catch everything itself.

**`stoppingToken` must be honoured**, or every deploy waits the full shutdown timeout and the
orchestrator eventually `SIGKILL`s you mid-write.

## Details that matter in production

**Order.** Process by `OccurredAtUtc` and take a bounded batch. Unbounded means one slow iteration
after an incident tries to publish forty thousand messages at once.

**Concurrency.** Two instances of the API mean two processors polling the same table, and both will
grab the same rows. On SQL Server the fix is a claim query — `UPDATE TOP (n) ... WITH (READPAST,
UPDLOCK, ROWLOCK) OUTPUT inserted.*` — which atomically marks and returns rows so no two workers see
the same one. Polling with a plain `SELECT` and hoping is a real duplicate-message source.

**Poison messages.** A row that always fails will be retried forever and will block everything behind
it. Count attempts, record `Error`, and move it aside past a threshold. The outbox table needs the
same dead-letter thinking as a broker.

**Deserialising a type named in data.** `OutboxProcessor` keeps an **allow-list** of event types
rather than calling `Type.GetType(row.Type)`. Deserialising an arbitrary named type is how
insecure-deserialisation vulnerabilities happen: an attacker who can write to the table names a
"gadget" type whose construction has side effects. Rows here are written by our own code, but defence
in depth costs one `FrozenDictionary`.

**Cleanup.** Processed rows must be deleted or archived on a schedule, or the table becomes the
largest in the database.

**Latency.** Polling every five seconds means up to five seconds of delay. For most business events
that is invisible. When it is not, keep the outbox for durability and add a notification so the
processor wakes immediately.

## What it does not solve

Be precise about this in an interview:

- It does not make delivery exactly-once. Consumers still need idempotency.
- It does not give ordering across aggregates — only the order rows were written in.
- It does not remove the need for a broker; it guarantees the handoff *to* the broker.
- It does not help if the consumer is broken. It guarantees the message leaves, not that it lands.

## Where this repository is deliberately wrong

`SendConfirmationOnOrderSubmitted` sends an email **inside** the domain event handler, inside the
transaction. That is the flaw this chapter exists to fix, it is labelled as such in the source, and
"convert it to use the outbox" is one of the exercises in [SOLUTIONS.md](../SOLUTIONS.md).

Two things go wrong with the current version: an SMTP round trip holds a database transaction open,
and if the transaction later rolls back the email has already gone.

The tooling to fix it is already in the repository. `IEmailQueue` writes a message as a row through
the caller's own `DbContext` — the same idea as this chapter, specialised for mail — and
`SendCancellationNoticeOnOrderCancelled` a few files away is the correct version of the same job,
kept beside the wrong one deliberately. See
[module 10 section 6](../module-10-cross-cutting/05-mailing.md).

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Submit an order, then look at the table before the processor's next tick:

```sql
SELECT Id, Type, OccurredAtUtc, ProcessedAtUtc FROM logiflow.OutboxMessages ORDER BY OccurredAtUtc DESC;
```

The row is there, unprocessed, in the same transaction as the order. Now stop the API before the tick
and restart it: the message is still published, because durability came from the database rather than
from the process staying alive. That is the entire point, visible in thirty seconds.

## What to remember

- The dual-write problem: you cannot commit to a database and a broker atomically.
- The outbox makes it one write — the message is a row in the same transaction.
- The guarantee is at-least-once, so consumers must be idempotent on the message id.
- A `BackgroundService` is a singleton: create a scope per iteration, never inject a context.
- Catch everything in the loop, honour `stoppingToken`, and process bounded batches in order.
- Claim rows atomically, or two instances publish everything twice.
- Allow-list the types you deserialise.
- Delete processed rows on a schedule.

**Code:** [`OutboxMessage.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxMessage.cs) ·
[`OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs) ·
[`module 25`](../module-25-distributed-systems/)

**Next:** [9. Migrations](09-migrations.md)
