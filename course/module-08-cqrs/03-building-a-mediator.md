# 3. Building a mediator

> Part of [Module 08 — CQRS and the mediator pattern](README.md), section 3.
> Previous: [2. Vertical slices](02-vertical-slices.md) ·
> Next: [4. Pipeline behaviours](05-pipeline-behaviors.md)

---

A mediator decouples senders from handlers. An endpoint says "handle this request" and never learns
which class did it, which means new operations are added without touching any dispatch code.

This repository **hand-writes** its mediator rather than using MediatR, for three reasons in order of
honesty:

1. **You should know how it works.** The interview question is "how does MediatR find a handler when
   `Send` only knows the response type?" — and the answer is the reflection-plus-cached-wrapper trick
   below.
2. **MediatR is no longer free for commercial use.** Version 13 moved to a paid licence, and plenty
   of teams have had to answer that question recently. Knowing the pattern rather than the package is
   what makes that news a non-event. (The same thing happened to MassTransit in 2026.)
3. **It is genuinely small.** About two hundred lines. Not every abstraction needs a dependency.

Everything here maps one-to-one onto MediatR, so the concepts transfer directly:
`IDispatcher` ≈ `IMediator`, `IRequestHandler` is identical, `IPipelineBehavior` is identical,
`IEventHandler` ≈ `INotificationHandler`.

## The problem to solve

```csharp
public interface IDispatcher
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
```

Look carefully at what the compiler knows at the call site. `TResponse` is known. The **concrete
request type is not** — the parameter is typed as `IRequest<TResponse>`, so at compile time all we
have is the interface.

But the handler we need is `IRequestHandler<SubmitOrderCommand, Result>`, which requires *both* type
arguments. We have one and a runtime `Type` for the other. C# generics cannot bridge that gap
directly, and that is the entire puzzle.

## The solution: a generic wrapper, created reflectively, cached

```csharp
public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
{
    Type requestType = request.GetType();          // the concrete type, at runtime

    var wrapper = (RequestHandlerWrapperBase<TResponse>)RequestHandlerCache.GetOrAdd(
        requestType,
        static (type, responseType) =>
        {
            Type wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(type, responseType);
            return Activator.CreateInstance(wrapperType)!;
        },
        typeof(TResponse));

    return wrapper.HandleAsync(request, serviceProvider, ct);
}
```

The move is: **construct a closed generic type at runtime whose base class is known at compile
time.**

- `RequestHandlerWrapper<SubmitOrderCommand, Result>` knows both type arguments, so it can resolve
  `IRequestHandler<SubmitOrderCommand, Result>` from the container normally.
- It derives from `RequestHandlerWrapperBase<Result>`, which the caller *can* name, because
  `TResponse` is known there.

Reflection crosses the gap once; everything after the cast is ordinary strongly-typed code.

```csharp
private abstract class RequestHandlerWrapperBase<TResponse>
{
    public abstract Task<TResponse> HandleAsync(object request, IServiceProvider sp, CancellationToken ct);
}

private sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapperBase<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> HandleAsync(object request, IServiceProvider sp, CancellationToken ct)
    {
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        // Behaviours wrap the handler, outermost first. See the next chapter.
        Task<TResponse> Next() => handler.HandleAsync((TRequest)request, ct);

        return sp.GetServices<IPipelineBehavior<TRequest, TResponse>>()
                 .Reverse()
                 .Aggregate((RequestHandlerDelegate<TResponse>)Next,
                            (next, behavior) => () => behavior.HandleAsync((TRequest)request, next, ct))();
    }
}
```

That `Aggregate` over a reversed list is how the pipeline is built —
[the next chapter](05-pipeline-behaviors.md) unpacks it.

## Caching, and why it is static

```csharp
private static readonly ConcurrentDictionary<Type, object> RequestHandlerCache = new();
```

**Static**, so the reflection cost is paid once per process rather than once per request. Creating a
closed generic type and calling `Activator.CreateInstance` is not free; doing it on every request
would put reflection on the hot path for no reason.

**`ConcurrentDictionary`**, because ASP.NET Core dispatches from many threads at once. And note the
`static` lambda with the state argument — `GetOrAdd(key, factory, state)` rather than a closure —
which avoids allocating a display class on every call. That is
[module 02](../module-02-delegates-and-closures/) applied where it actually matters.

The factory may run more than once under contention. That is fine here: the wrapper is stateless, so
a duplicate is harmless. It would **not** be fine if construction had side effects, which is the
`GetOrAdd` caveat worth remembering.

## Events: fan-out, not routing

```csharp
public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
{
    Type handlerType = EventHandlerTypeCache.GetOrAdd(
        domainEvent.GetType(),
        static type => typeof(IEventHandler<>).MakeGenericType(type));

    IEnumerable<object?> handlers = serviceProvider.GetServices(handlerType);

    foreach (object? handler in handlers)
    {
        object? task = handlerType.GetMethod(nameof(IEventHandler<IDomainEvent>.HandleAsync))!
            .Invoke(handler, [domainEvent, ct]);

        if (task is Task awaitable) await awaitable.ConfigureAwait(false);
    }
}
```

Three deliberate differences from `SendAsync`:

**`GetServices`, not `GetRequiredService`.** Zero handlers is legal and normal — nobody is obliged to
care that something happened. That is the semantic difference between an event and a command, made
concrete: a request with no handler is a bug, an event with no handler is Tuesday.

**Sequential, not `Task.WhenAll`.** These run inside a `SaveChanges`
([module 06 section 7](../module-06-efcore/08-interceptors.md)), sharing one `DbContext`, which is
[not thread-safe](../module-06-efcore/01-dbcontext-and-change-tracking.md). Parallelising them would
throw or corrupt the change tracker.

**An exception aborts the rest, on purpose.** They run in the same transaction as the state change,
so swallowing a failure would give you a committed order whose stock was never reserved. Failing the
whole thing is the correct behaviour here — and it is the reason anything that must *not* participate
goes through the [outbox](../module-06-efcore/07-outbox-pattern.md) instead.

Reflection per handler per event is acceptable because it only happens inside a save, not on a
request-serving hot path.

## Registration

```csharp
services.Scan(scan => scan.FromAssemblyOf<IDispatcher>()
    .AddClasses(c => c.AssignableTo(typeof(IRequestHandler<,>)))
        .AsImplementedInterfaces().WithScopedLifetime()
    .AddClasses(c => c.AssignableTo(typeof(IEventHandler<>)))
        .AsImplementedInterfaces().WithScopedLifetime());
```

Assembly scanning, so adding a handler is adding a file. The alternative — a registration line per
handler — is a file everyone edits and a merge conflict on every feature branch, and it fails at
runtime rather than at build when somebody forgets.

**Scoped**, matching the `DbContext` lifetime. A singleton handler would capture a scoped context —
the [captive dependency](../module-06-efcore/01-dbcontext-and-change-tracking.md#lifetime-scoped-and-why)
trap.

## What a mediator costs

Be able to say this; the pattern has genuine critics.

**Navigation.** "Go to definition" on `SendAsync` lands in the dispatcher, not the handler. Modern
IDEs and the naming convention mitigate it; it remains a real cost.

**Indirection for its own sake in small apps.** If there are twelve endpoints and no cross-cutting
concerns, calling a service directly is clearer and the mediator is ceremony.

**Deeper stack traces.** Wrapper, behaviours, handler. Worth it for what the pipeline buys, but the
first exception you read will be longer than you expect.

The counterweight is the pipeline: once every operation has the same shape, logging, validation,
transactions and caching become one class each instead of a line in two hundred methods. That is the
trade, and it is worth making the moment cross-cutting concerns exist.

## Try it

Set a breakpoint in `RequestHandlerWrapper<,>.HandleAsync` and submit an order. Inspect `TRequest`
and `TResponse` in the locals window — concrete types, at runtime, from a call site that only knew an
interface. Then call `SendAsync` twice and confirm the cache factory runs once.

## What to remember

- The problem: `SendAsync` knows `TResponse` but not the concrete request type.
- The fix: build a closed generic wrapper reflectively whose *base* type is nameable.
- Cache the wrapper statically in a `ConcurrentDictionary` — reflection once per process.
- Use `GetOrAdd` with a static lambda and state to avoid a closure allocation per call.
- Commands use `GetRequiredService`; events use `GetServices`, because zero handlers is legal.
- Event handlers run sequentially, sharing one `DbContext`, and a failure aborts the rest.
- Scan for handlers; register them scoped.
- The costs are navigation and indirection. The payoff is the pipeline.

**Code:** [`Dispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/Dispatcher.cs) ·
[`IDispatcher.cs`](../../src/LogiFlow.Application/Abstractions/Messaging/IDispatcher.cs) ·
[`DependencyInjection.cs`](../../src/LogiFlow.Application/DependencyInjection.cs)

**Next:** [4. Pipeline behaviours](05-pipeline-behaviors.md)
