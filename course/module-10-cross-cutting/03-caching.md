# 4. Caching

> Part of [Module 10 — Cross-cutting concerns](README.md), section 4.
> Previous: [3. Error handling](04-error-handling.md) ·
> Next: [5. Rate limiting](README.md#5-rate-limiting)

---

A cache trades **correctness for speed**, and the trade is always the same: you serve data that might
be stale in exchange for not doing the work again. Everything difficult about caching follows from
that sentence, and the two hard parts are the ones in the old joke — **invalidation** and **naming
things** — plus a third that only appears under load.

## Where it lives here

Caching is a [pipeline behaviour](../module-08-cqrs/05-pipeline-behaviors.md), not a line inside
handlers:

```csharp
public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
{
    if (request is not IQuery<TResponse> or not ICacheable cacheable)
        return await next();
    ...
}
```

**Queries only.** One `if`, and caching a command becomes structurally impossible — the payoff for
the [marker interfaces](../module-08-cqrs/01-what-cqrs-actually-is.md).

It also sits **outside** the transaction behaviour, so a cache hit does no database work at all. That
ordering is the whole point of having a cache and it is decided by registration order.

## In-memory vs distributed

| | `IMemoryCache` | `IDistributedCache` (Redis) |
|---|---|---|
| Scope | one process | all instances |
| Speed | nanoseconds | a network round trip, ~1 ms |
| Survives restart | no | yes |
| Serialisation | none — stores objects | you serialise, usually JSON |
| Limit | your process memory | the Redis instance |

**The moment you scale to two instances, `IMemoryCache` becomes a correctness problem**, not just a
smaller cache. Instance A caches an order, instance B updates it and evicts its own copy, and A keeps
serving the old one until its TTL expires. The user refreshes and sees two different answers
depending on which instance the load balancer picked — which is a genuinely maddening bug to receive
a report of.

`HybridCache` (.NET 9+) combines an in-process L1 with a distributed L2 and solves the stampede
problem below for you. It is the right default for new work.

## Naming: the key

```csharp
private static string Key(int id) => $"order:v1:{id}";
//                                          ↑ version the key
```

**Version the key.** Change the shape of the cached DTO — add a field, rename one — and old entries
deserialise into the new type as garbage or throw. Bumping `v1` to `v2` makes the whole old
generation unreachable in one edit, with no flush and no coordination.

**Prefix by application.** `InstanceName = "logiflow:"` on the Redis registration, because several
applications sharing one Redis is normal and key collisions are silent.

**Include everything that varies the result.** A key for a search must include the filters, the page,
the sort *and the user* if results are user-scoped. A cache key that omits the tenant is a data-leak
bug, not a performance bug.

## Invalidation: pick a strategy and say it out loud

**Time-based (TTL).** The only one that is simple, and it is right far more often than people admit.
"This may be up to sixty seconds stale" is an acceptable answer for a product catalogue, a dashboard
or a report. Always set one — an entry with no expiry is a memory leak with better branding.

**Event-based.** Evict on the domain event that makes it stale:

```csharp
// in an OrderCancelled handler
await cache.RemoveAsync(Key(orderId), ct);
```

Precise, and it fails in one direction: miss an event and the cache is wrong **forever**. Always pair
it with a TTL as a backstop, so the worst case is bounded staleness rather than permanent wrongness.

**Never invalidate.** Legitimate for genuinely immutable data — a completed order from 2019, a
reference table. Say so explicitly rather than letting it be an accident.

**Add a jitter to the TTL.** Cache a thousand items in one warm-up loop with the same 60-second TTL
and they all expire in the same second, producing a synchronised thundering herd every minute.
`TimeSpan.FromSeconds(60 + Random.Shared.Next(0, 15))` costs nothing and removes an entire class of
periodic latency spike.

## The stampede

The failure that only appears under load, and the one worth being able to describe:

```
A popular key expires at 09:00:00.000
  → 500 concurrent requests all miss
  → all 500 query the database
  → the database falls over
  → nothing gets cached, so the next 500 do it again
```

The cache did not fail. It worked perfectly and then stopped for one instant, and the instant was
enough.

The fix is **single flight**: one caller loads, everyone else waits for that result.

```csharp
SemaphoreSlim gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
await gate.WaitAsync(ct);
try
{
    if (cache.TryGetValue(key, out T? cached)) return cached;   // double-check inside the gate
    T value = await load(ct);
    cache.Set(key, value, options);
    return value;
}
finally { gate.Release(); }
```

Three details, all of which Lab 06 exercise 4 makes you get right: **you cannot `await` inside a
`lock`**, so it is a `SemaphoreSlim`; **release in a `finally`**, or one exception holds the gate
shut forever; and **one gate per key**, or a slow load of key A blocks every request for key B.

`HybridCache` does this for you — a single flight per key — which is the main reason to prefer it.

## What not to cache

- **Anything user-specific in a shared key.** The tenant-leak bug.
- **Data that must be correct.** A stock level shown on a product page, cached: you have built an
  oversell ([module 07 section 4](../module-07-sql-and-transactions/05-inventory-concurrency.md)).
- **Cheap queries.** A primary-key lookup on an indexed table is already sub-millisecond. Caching it
  adds a network hop and an invalidation problem to save nothing.
- **Write paths.** Structurally prevented here.

**Measure before you cache.** The most common caching mistake is caching a query that was never the
problem, and thereby acquiring an invalidation bug for free. The hit rate tells you whether it was
worth it: `INFO stats` in Redis gives `keyspace_hits` and `keyspace_misses`, and a hit rate below
about 80% usually means the key is too specific or the TTL too short.

## The mistakes

**No expiry.** A leak.

**`IMemoryCache` behind a load balancer.** Different answers per instance.

**Caching without a version in the key.** A deployment changes the DTO and old entries poison the
new code.

**Forgetting the tenant or user in the key.** A data leak.

**No stampede protection on a hot key.** Fine until it is not, and then it takes the database.

**Uniform TTLs set in a loop.** Synchronised expiry, periodic herd.

**Caching to hide a missing index.** The query is still slow; you have only moved when you notice.

## Try it

The application runs without Redis — `ConnectionStrings:Redis` is empty by default and the cache
falls back to `AddDistributedMemoryCache`. To poke at a real one, start the optional compose stack
(which maps Redis to host port **6380**, not 6379):

```bash
docker compose up -d redis
docker exec -it logiflow-redis redis-cli
```

No container runtime? Any Redis will do — a native Windows build such as Memurai, or one on another
machine. Point `ConnectionStrings:Redis` at it and the application cannot tell the difference. That
portability is the whole reason the code depends on `IDistributedCache` and not on Redis.

```
SET order:1 "{\"id\":1}"
TTL order:1                 # -1 → no expiry. That is the bug.
EXPIRE order:1 60
INFO stats                  # keyspace_hits vs keyspace_misses = your hit rate
```

Then cache one endpoint in this API, watch the SQL log go quiet on the second request, update the
row directly in SSMS, and watch the endpoint keep serving the old value until the TTL runs out. That
is not a bug — it is the trade, made visible.

## What to remember

- A cache trades correctness for speed. Everything else follows.
- Caching is a pipeline behaviour on queries only, outside the transaction.
- `IMemoryCache` stops being correct the moment there are two instances.
- Version the key, prefix by application, and include everything that varies the result.
- Always set a TTL, even with event-based eviction, as a bounded worst case.
- Jitter the TTL or you build a synchronised herd.
- Protect hot keys with single flight: `SemaphoreSlim`, released in a `finally`, one gate per key.
- Measure first. Never cache a stock level, a write path, or a query that was already fast.

**Code:** [`CachingBehavior.cs`](../../src/LogiFlow.Application/Behaviors/CachingBehavior.cs) ·
[`Lab06_Disposal.cs`](../../labs/Labs.Exercises/Exercises/Lab06_Disposal.cs)

**Next:** [5. Rate limiting](README.md#5-rate-limiting)
