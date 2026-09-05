# 7. Interceptors

> Part of [Module 06 — EF Core in depth](README.md), section 7.
> Previous: [6b. The specification pattern](06-specification-pattern.md) ·
> Next: [8. The transactional outbox](07-outbox-pattern.md)

---

An interceptor is a hook into EF Core's own pipeline. It lets you run code at a point EF controls —
before a save, around a command, when a connection opens — without every call site having to
remember to call you.

That is the whole value: **things that must happen on every save should not depend on every developer
remembering.**

## The one this repository lives on

```csharp
public sealed class DomainEventDispatchingInterceptor(IDispatcher dispatcher) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        DbContext? context = eventData.Context;
        if (context is null) return result;

        // 1. Collect from every aggregate the tracker knows about.
        List<IDomainEvent> events = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToList();

        // 2. Clear BEFORE dispatching.
        foreach (EntityEntry<IHasDomainEvents> entry in context.ChangeTracker.Entries<IHasDomainEvents>())
        {
            entry.Entity.ClearDomainEvents();
        }

        // 3. Dispatch. Handlers run inside this transaction, by design.
        foreach (IDomainEvent domainEvent in events)
        {
            await dispatcher.PublishAsync(domainEvent, cancellationToken);
        }

        return result;
    }
}
```

Three details, and each is a bug if you get it wrong.

**Collect and clear before dispatching.** A handler that modifies another aggregate raises new events.
Iterate the live list while handlers append to it and you get either an infinite loop or
`InvalidOperationException: Collection was modified`. Snapshot first.

**`SavingChangesAsync`, not `SavedChangesAsync`.** Dispatching *before* the write means the handlers'
own changes join the same `SaveChanges` and the same transaction. Dispatch after, and a handler's
work is a second transaction that can fail independently — which is the whole problem the
[outbox](07-outbox-pattern.md) exists to solve, reintroduced.

**The aggregate never learns a dispatcher exists.** `Order.Submit()` calls `Raise`, which appends to
a list. Everything else is EF's pipeline. That is what keeps the Domain project referencing nothing —
the property `LayeringTests` enforces.

## The kinds worth knowing

| Interceptor | Hook | Typical use |
|---|---|---|
| `SaveChangesInterceptor` | before/after `SaveChanges` | domain events, auditing, soft delete, timestamps |
| `DbCommandInterceptor` | around every SQL command | logging, query-count assertions, slow-query alerts |
| `DbConnectionInterceptor` | connection open/close | multi-tenant connection strings, access tokens |
| `DbTransactionInterceptor` | begin/commit/rollback | transaction tracing |

They are registered on the context, not globally:

```csharp
services.AddDbContext<LogiFlowDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddInterceptors(sp.GetRequiredService<DomainEventDispatchingInterceptor>());
});
```

**Note the `(sp, options)` overload.** The interceptor has a constructor dependency —
`IDispatcher` — so it must come from the container. The commonest mistake here is
`new DomainEventDispatchingInterceptor(...)` inline, which cannot resolve anything and forces you to
smuggle a service locator in.

## The lifetime trap

The interceptor is registered as **scoped**, alongside the `DbContext` it serves. It has to be: it
depends on `IDispatcher`, which resolves scoped handlers, which resolve scoped repositories, which
resolve the same `DbContext`.

Register it as a singleton and you get a
[captive dependency](01-dbcontext-and-change-tracking.md#lifetime-scoped-and-why) — one dispatcher
holding one context forever, across every request in the process. It usually appears to work in
development, with one user, and corrupts under load.

## Two more worth writing

**Auditing**, which is the classic argument for interceptors, because doing it per-handler is a
guarantee that somebody forgets:

```csharp
public override ValueTask<InterceptionResult<int>> SavingChangesAsync(...)
{
    foreach (EntityEntry<IAuditable> entry in eventData.Context!.ChangeTracker.Entries<IAuditable>())
    {
        if (entry.State is EntityState.Added)
        {
            entry.Entity.CreatedAtUtc = timeProvider.GetUtcNow();
            entry.Entity.CreatedBy = currentUser.Id;
        }
        else if (entry.State is EntityState.Modified)
        {
            entry.Entity.ModifiedAtUtc = timeProvider.GetUtcNow();
        }
    }
    return base.SavingChangesAsync(...);
}
```

Take `TimeProvider` rather than calling `DateTimeOffset.UtcNow` — the clock is a dependency, and a
test that cannot control it cannot assert on timestamps.

**Query counting in tests**, which turns the [N+1 problem](04-n-plus-one.md) from a thing you notice
into a thing the build notices:

```csharp
public sealed class CountingInterceptor : DbCommandInterceptor
{
    public int Commands { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(...)
    {
        Commands++;
        return base.ReaderExecutingAsync(...);
    }
}

// in an integration test
counter.Commands.ShouldBeLessThan(5, "the orders endpoint should not N+1");
```

## Interceptors vs the alternatives

**vs overriding `SaveChangesAsync` on the context.** Works, and puts unrelated concerns in the
context, which then grows. Interceptors are separate classes, individually testable, and can be
added and removed by registration. Prefer them past one concern.

**vs a global query filter.** `HasQueryFilter` is the right tool for soft delete and multi-tenancy —
it is applied to *queries*, which is what those are about, and it is applied by the model rather than
at save time. Use `IgnoreQueryFilters()` when you deliberately need the excluded rows.

**vs a [pipeline behaviour](../module-08-cqrs/05-pipeline-behaviors.md).** A behaviour wraps a
*request*; an interceptor wraps a *save*. Logging and validation are request concerns. Auditing and
domain-event dispatch are save concerns — they must happen even if a save is triggered from a path
that is not a command handler at all.

## The mistakes

**Dispatching without clearing first.** Infinite loop or a modified-collection exception.

**Long or external work in a save interceptor.** It runs inside the transaction. An HTTP call there
holds a database transaction open for a network round trip.

**Registering it as a singleton.** Captive dependency; silent under light load.

**Assuming it runs for raw SQL.** `ExecuteUpdate`, `ExecuteDelete` and `FromSqlRaw` bypass the change
tracker entirely, so `SaveChangesInterceptor` never fires. Anything that must always happen —
auditing especially — needs a plan for those paths, and this is a genuinely good interview question
because most people have not thought about it.

## Try it

Put a breakpoint in `SavingChangesAsync` and submit an order. Inspect
`context.ChangeTracker.Entries<IHasDomainEvents>()` and watch the populated event list, then step
past the clear and watch it empty before a single handler runs. Then comment out the clear and submit
again — the loop is instructive.

## What to remember

- Interceptors hook EF's pipeline so cross-cutting work does not depend on anybody remembering.
- Collect and clear domain events **before** dispatching.
- Dispatch in `SavingChangesAsync` so handlers join the same transaction.
- Register with the `(sp, options)` overload; the interceptor has dependencies.
- Scoped, never singleton — a captive `DbContext` is the failure mode.
- Take `TimeProvider`, not `DateTimeOffset.UtcNow`.
- Nothing external inside a save interceptor; write an outbox row instead.
- `ExecuteUpdate` / `ExecuteDelete` / raw SQL skip the change tracker, so interceptors do not fire.

**Code:** [`DomainEventDispatchingInterceptor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Interceptors/DomainEventDispatchingInterceptor.cs) ·
[`DependencyInjection.cs`](../../src/LogiFlow.Infrastructure/DependencyInjection.cs)

**Next:** [8. The transactional outbox](07-outbox-pattern.md)
