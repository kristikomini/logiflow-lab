# 1. Dependency injection and lifetimes

> Part of [Module 10 — Cross-cutting concerns](README.md), section 1.
> Next: [2. Minimal APIs](02-minimal-apis.md)

---

Dependency injection is not a framework feature. It is one sentence:
**a class asks for what it needs and never constructs it.**

```csharp
public sealed class CreateOrderCommandHandler(
    ICustomerRepository customers,
    IOrderRepository orders,
    IOrderNumberGenerator numbers) : IRequestHandler<CreateOrderCommand, Result<Guid>>
```

The container supplies them. The handler cannot know whether `IOrderRepository` is EF Core, Dapper or
a substitute in a test — which is exactly why the test can construct it in one line and why
`LayeringTests` can assert that the Application layer never mentions Infrastructure.

The three benefits, and you should be able to name them separately:
**testability** (substitute anything), **lifetime management** (something else disposes what needs
disposing), and **composition in one place** (`Program.cs` is the only file that knows an interface's
implementation).

## The three lifetimes

| Lifetime | One instance per | Use for |
|---|---|---|
| `Transient` | every resolution | cheap, stateless helpers |
| `Scoped` | HTTP request (or explicit scope) | `DbContext`, repositories, handlers, unit of work |
| `Singleton` | the process | configuration, caches, `HttpClient` factories, `TimeProvider` |

Almost everything in this repository is **scoped**, and that is not laziness. Scoped is the lifetime
that matches a unit of work: a request gets one `DbContext`, and every repository and handler in that
request shares it, so `SaveChangesAsync` commits everything they did together.

Make a repository transient and each one resolves its own `DbContext` — two change trackers, two
transactions, and a save that commits half the work.

## The captive dependency

The one lifetime bug that matters, and a reliable interview question:

> **A service can only safely depend on something whose lifetime is at least as long as its own.**

```csharp
// ✗ Singleton capturing a scoped DbContext.
services.AddSingleton<IOrderCache>(sp =>
    new OrderCache(sp.GetRequiredService<LogiFlowDbContext>()));
```

The singleton is created once and holds that context **forever**. It accumulates tracked entities,
serves stale reads, and is used concurrently from every request —
[and `DbContext` is not thread-safe](../module-06-efcore/01-dbcontext-and-change-tracking.md).

Worse: it usually works in development. One developer, one request at a time, and the corruption only
appears under load.

Two fixes:

```csharp
// Inject a factory and create a scope per operation.
public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory) : BackgroundService
{
    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
}

// Or use the typed factory, which is what a long-lived Blazor component wants.
services.AddDbContextFactory<LogiFlowDbContext>(...);
```

**`BackgroundService` is a singleton**, so this is not an edge case — it is the shape of every worker
you will write.

Turn the detector on in development and the container will refuse to start rather than let you find
out in production:

```csharp
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes  = context.HostingEnvironment.IsDevelopment();
    options.ValidateOnBuild = context.HostingEnvironment.IsDevelopment();
});
```

`ValidateOnBuild` also catches a missing registration at startup instead of on the first request that
needs it.

## Registration

```csharp
services.Scan(scan => scan.FromAssemblyOf<IDispatcher>()
    .AddClasses(c => c.AssignableTo(typeof(IRequestHandler<,>)))
        .AsImplementedInterfaces().WithScopedLifetime());
```

Assembly scanning, so adding a handler is adding a file rather than editing a shared registration
file that everyone conflicts on and someone eventually forgets.

The trade is honest: scanning is less explicit, and a typo in a marker interface means a handler
silently is not registered. `ValidateOnBuild` catches most of that at startup.

Two ordering rules worth knowing:

- **Last registration wins** for a single resolution — which is how a test host overrides a real
  service with a fake.
- **All registrations are returned** by `GetServices<T>`, in registration order. That is how the
  [pipeline](../module-08-cqrs/05-pipeline-behaviors.md) collects its behaviours, and why their
  registration order is their execution order.
- `TryAdd*` registers only if nothing is registered yet — the right call in a library, so a consumer
  can substitute.

## Injecting the awkward things

**Configuration** — the options pattern, not `IConfiguration`:

```csharp
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

public sealed class TokenService(IOptions<JwtOptions> options) { … }
```

`IOptions<T>` is a singleton snapshot; `IOptionsSnapshot<T>` is scoped and re-reads per request;
`IOptionsMonitor<T>` notifies on change and is the one a singleton needs if the value can change.

**The clock** — `TimeProvider`, never `DateTimeOffset.UtcNow`. A hardcoded clock is a dependency you
cannot substitute, and every test about expiry, shifts or "yesterday" becomes flaky or impossible.

**`HttpClient`** — `IHttpClientFactory`, never `new HttpClient()` per call. Disposing one does not
release its socket immediately, so a busy endpoint exhausts ports on a machine that looks idle; a
static one never picks up DNS changes. The factory solves both.

## What not to do

**Service locator.** Injecting `IServiceProvider` and resolving inside a method hides dependencies
from the constructor, so nothing tells you what a class needs until it throws at runtime. It also
defeats `ValidateOnBuild`. The legitimate exception is a factory that genuinely must resolve by
runtime type — which is what `Dispatcher` does, deliberately and in one place.

**Constructor injection of ten things.** Not a DI problem — a
[Single Responsibility](../module-26-patterns-and-solid/) problem. The constructor is telling you the
class does too much, and it is worth listening.

**Registering concrete types "just in case".** Every registration is a public surface.

**Doing work in a constructor.** Constructors run during resolution. An I/O call there turns object
graph construction into a network operation, and an exception surfaces as an opaque container error.

## Try it

Add a deliberately captive dependency:

```csharp
services.AddSingleton<SomethingThatTakes<LogiFlowDbContext>>();
```

With `ValidateScopes` on in development, the application refuses to start and names the offending
registration. Turn validation off and it starts happily — which is precisely how these reach
production.

Then look at `Program.cs` and note that it is the only file in the solution that mentions both an
interface and its implementation.

## What to remember

- DI is "ask, never construct". Testability, lifetimes, and one place that composes everything.
- Scoped is the default here, because scoped matches a unit of work.
- A service may only depend on something living at least as long — otherwise it is captive.
- `BackgroundService` is a singleton: use `IServiceScopeFactory` or `IDbContextFactory`.
- Turn on `ValidateScopes` and `ValidateOnBuild` in development.
- Last registration wins for one; `GetServices` returns all, in order.
- `IOptions` for config, `TimeProvider` for the clock, `IHttpClientFactory` for HTTP.
- Service locator hides dependencies and defeats validation. A fat constructor is an SRP smell.

**Code:** [`Program.cs`](../../src/LogiFlow.Api/Program.cs) ·
[`DependencyInjection.cs`](../../src/LogiFlow.Application/DependencyInjection.cs) ·
[`OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs)

**Next:** [2. Minimal APIs](02-minimal-apis.md)
