# 1. `DbContext` is a session, and it is not thread-safe

> Part of [Module 06 — EF Core in depth](README.md), section 1.
> Next: [2. Fluent configuration, not attributes](03-fluent-configuration.md)

---

Almost every EF Core bug that reaches production comes from a wrong mental model of what a
`DbContext` *is*. It is not a connection. It is not a repository. It is a **unit of work plus an
identity map for one logical operation** — a short-lived session that remembers everything it has
seen.

Get that one sentence right and lifetimes, tracking, `AsNoTracking` and the thread-safety rules all
follow from it.

## The change tracker, concretely

```csharp
var order = await db.Orders.FirstAsync(o => o.Id == id, ct);   // ① query, materialise, track
order.Cancel("customer changed their mind");                    // ② mutate a plain C# object
await db.SaveChangesAsync(ct);                                  // ③ diff, generate UPDATE, commit
```

There is no `db.Update(order)` in step ②, and that surprises people. When EF materialises an entity
it stores a **snapshot** of every property value alongside the instance. `SaveChangesAsync` compares
the current values with the snapshot and writes only the columns that actually changed. If you
changed nothing, no SQL is sent at all.

Two consequences worth having at hand:

**Tracking costs memory and time.** Every tracked entity carries a full copy of its original values,
and `SaveChanges` walks all of them. Loading 50 000 rows to display them tracks 50 000 snapshots for
no purpose whatsoever — which is what `AsNoTracking` is for, below.

**The identity map means one instance per key per context.** Query the same order twice and you get
*the same object* back, not two copies. That is why you cannot "reload" an entity by querying again
— by default EF returns the tracked instance and ignores the fresh database values. Use
`AsNoTracking()`, a new context, or explicitly `Entry(entity).ReloadAsync()`.

## The four states

| State | Means | Produced by |
|---|---|---|
| `Added` | new, not in the database | `db.Add(entity)` |
| `Unchanged` | loaded, no differences | a tracking query |
| `Modified` | loaded, some property differs | mutating a tracked entity |
| `Deleted` | to be removed | `db.Remove(entity)` |
| `Detached` | not tracked at all | `AsNoTracking`, or `new Order()` |

You can inspect it directly, and it is the first thing to do when a save does not do what you expect:

```csharp
foreach (EntityEntry entry in db.ChangeTracker.Entries())
{
    Console.WriteLine($"{entry.Entity.GetType().Name}: {entry.State}");
}
```

`db.ChangeTracker.DebugView.LongView` in the watch window is the more complete version and is worth
knowing about before you need it.

## Lifetime: scoped, and why

`AddDbContext` registers it as **scoped** — one per HTTP request — and that default is correct for
three separate reasons:

**It is not thread-safe, at all.** One `DbContext` used from two threads concurrently throws
`InvalidOperationException: A second operation was started on this context instance before a previous
operation completed`, or, worse, corrupts the change tracker silently. The most common cause is a
missing `await` inside a `Task.WhenAll` over the same context.

**It accumulates.** A long-lived context tracks every entity it has ever seen, forever. Memory
climbs and `SaveChanges` gets slower with every operation, because the diff walks a growing list.

**It caches staleness.** The identity map means a context that has been alive for an hour is serving
an hour-old view of any entity it loaded.

The two places this bites:

```csharp
// ✗ Captive dependency: a singleton capturing a scoped context. The context now lives forever.
services.AddSingleton<IOrderCache>(sp =>
    new OrderCache(sp.GetRequiredService<LogiFlowDbContext>()));

// ✗ BackgroundService is a singleton, so it cannot inject a DbContext either.
public sealed class Worker(LogiFlowDbContext db) : BackgroundService   // wrong
```

The fix in both cases is to inject a factory and create a context per operation — which is exactly
what [`OutboxProcessor`](07-outbox-pattern.md) does with `IServiceScopeFactory`, and what a
long-lived Blazor Server component must do with `IDbContextFactory<T>`
(see [module 18](../module-18-blazor/)).

## `AsNoTracking`, and when not to

```csharp
// A read that will never be saved. No snapshots, no identity map, measurably faster.
List<OrderSummaryDto> summaries = await db.Orders
    .AsNoTracking()
    .Where(o => o.Status == OrderStatus.Submitted)
    .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber.Value, o.Total.Amount))
    .ToListAsync(ct);
```

**The rule: track writes, do not track reads.** Every query behind a `GET` endpoint should be
`AsNoTracking`.

Two details people miss:

**A projection to a DTO is already effectively untracked** — EF cannot track an anonymous or DTO
type, only entities. So `Select(o => new Dto(...))` gets most of the benefit without the call. The
call still matters when you materialise entities.

**`AsNoTrackingWithIdentityResolution`** exists for the case where you project untracked but still
want repeated references to the same row to be the same object. It costs some of the saving; reach
for it only when duplicate instances actually cause a problem.

You can flip the default per context with
`ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking`, and this repository does
**not**, deliberately: an implicit global default makes the tracking behaviour of any given query
invisible at the call site, which is worse than a little repetition.

## Attaching detached entities

An entity that arrived over HTTP is `Detached` — EF has never seen it and has no snapshot:

```csharp
db.Update(order);            // marks EVERY property Modified — writes all columns
db.Attach(order);            // Unchanged; you then mark what actually changed
```

`Update` is blunt: it cannot know what changed, so it writes everything, which defeats concurrency
checks on unrelated columns and produces noisy audit logs. This repository avoids the question
entirely by never binding HTTP payloads onto entities — the handler loads the aggregate, calls a
method on it, and saves. That is also the over-posting defence from
[module 24](../module-24-security/).

## The mistakes

**Sharing a context across threads.** `Task.WhenAll(ids.Select(id => db.Orders.FindAsync(id)))` is
the canonical version. Either await sequentially or create a context per task.

**A `DbContext` in a singleton.** Covered above; it is the captive-dependency trap and it is silent
until load.

**Calling `SaveChanges` in a loop.** Each call is a round trip and a transaction. Make the changes,
then save once — that is what a unit of work is *for*
(see [module 07 section 2](../module-07-sql-and-transactions/02-unit-of-work.md)).

**Tracking a large read.** The single easiest performance win in most EF codebases is adding
`AsNoTracking` to list endpoints.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
```

Load an order, change nothing, and call `SaveChangesAsync`. Watch the SQL log: nothing is sent. Then
change one property and watch the `UPDATE` include exactly that column and the `RowVersion` check.
Then query the same order twice in one request and compare the two variables with
`ReferenceEquals` — they are the same object, which is the identity map made visible.

## What to remember

- A `DbContext` is a short-lived session: unit of work plus identity map.
- Change detection is a snapshot diff, which is why you never call `Update` on a tracked entity.
- One instance per key per context — a second query returns the tracked object, not fresh data.
- Scoped lifetime. Not thread-safe, accumulates state, and serves stale reads if kept alive.
- Background services and singletons need a factory or a scope, never an injected context.
- `AsNoTracking` on everything that will not be saved.
- Save once per operation, never inside a loop.

**Code:** [`LogiFlowDbContext.cs`](../../src/LogiFlow.Infrastructure/Persistence/LogiFlowDbContext.cs) ·
[`UnitOfWork.cs`](../../src/LogiFlow.Infrastructure/Persistence/UnitOfWork.cs)

**Next:** [2. Fluent configuration, not attributes](03-fluent-configuration.md)
