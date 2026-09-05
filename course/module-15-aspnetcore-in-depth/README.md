# Module 15 — ASP.NET Core in depth

> The framework itself: the host, the pipeline, routing, binding, filters, options and auth.
> Module 10 showed you *what* is wired up in this application. This module explains *how the
> framework does it*, which is the difference between "I can add an endpoint" and "I can explain
> why the endpoint returns 401 when the token is obviously valid".

**Prerequisite:** modules 02 (delegates), 04 (async) and 10 (cross-cutting).

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 1 | [Versioning an API you have already published](02-api-versioning.md) | URL vs header vs media type, the compatibility rewrite, and the `UseRouting` trap |

---

## 1. What ASP.NET Core actually is

Strip away the marketing and it is four things bolted together:

| Part | Job | Type you touch |
|---|---|---|
| **A host** | Starts things, holds configuration, runs background work, shuts down cleanly | `IHost`, `WebApplication` |
| **A DI container** | Builds your objects and controls their lifetime | `IServiceCollection`, `IServiceProvider` |
| **A server** | Speaks HTTP on a socket, produces an `HttpContext` | Kestrel (`IServer`) |
| **A pipeline** | A chain of functions that turns that `HttpContext` into a response | `RequestDelegate` |

**That is the entire model.** Everything else — MVC, minimal APIs, Blazor, SignalR, gRPC, Razor
Pages — is a library that plugs into those four. This is why you can reason about a part of the
framework you have never used: you ask "what does it register in DI, and where does it sit in the
pipeline?"

> **ASP.NET Core is not ASP.NET.** "ASP.NET" (2002–, `System.Web`, `Global.asax`, WebForms,
> IIS-only, Windows-only) and "ASP.NET Core" (2016–, cross-platform, DI-first, self-hosted) share a
> name and almost no code. You will meet ASP.NET Framework 4.x in companies with long-lived
> products — see module 17. Know that `HttpContext.Current` is the old world's static ambient
> context, and that its absence in Core is deliberate, not an omission.

---

## 2. The host, and what `CreateBuilder` silently did for you

📂 [`Api/Program.cs`](../../src/LogiFlow.Api/Program.cs)

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
```

One line. It configured:

**Configuration sources, in this order — later wins:**

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User secrets (Development only)
4. Environment variables
5. Command-line arguments

So `ConnectionStrings__SqlServer` as an environment variable overrides the JSON file, and
`--ConnectionStrings:SqlServer=...` overrides that. The double underscore is the portable separator
for `:`, because `:` is not legal in an environment variable name on Linux. **This is how a
container gets its connection string, and it is a routine interview question.**

**Also configured:** the Kestrel server, console and debug logging, `IHostEnvironment`,
`IHostApplicationLifetime`, and DI **scope validation when the environment is Development**.

That last one matters. Scope validation is what catches a captive dependency (module 10). It is on
in Development and **off in Production**, so the classic failure mode is "worked on my machine,
leaked memory in prod". You can force it on everywhere:

```csharp
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes  = true;
    o.ValidateOnBuild = true;   // fails at startup on an unresolvable graph, not at first request
});
```

`ValidateOnBuild` is the more valuable of the two: a missing registration becomes a startup crash
instead of a 500 at 3am on the one endpoint nobody tested.

### `IHostedService` and graceful shutdown

📂 [`Outbox/OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs)

`BackgroundService` is the base class you almost always want. Two rules people get wrong:

**Do not block in `StartAsync`.** The host awaits it before it starts accepting traffic, so a
`BackgroundService` whose `ExecuteAsync` never yields will hang startup — put an `await` before any
long loop, or your health check never comes up.

**Honour the token in `StopAsync`.** On SIGTERM the host signals the stopping token and then waits a
bounded window (`HostOptions.ShutdownTimeout`) before the process dies. Work that ignores the token
gets torn off mid-flight. This is exactly why the outbox is *at-least-once* and its consumers must
be idempotent — a killed process re-processes the message on restart.

```csharp
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
```

---

## 3. The middleware pipeline, actually explained

Everyone can recite "order matters". Here is *why*, which is the answer that lands.

A middleware is a function that takes the next function and returns a new function:

```csharp
public delegate Task RequestDelegate(HttpContext context);

// what app.Use() collects:
Func<RequestDelegate, RequestDelegate>
```

At `builder.Build()` the framework walks the registered components **in reverse**, starting from a
terminal delegate that returns 404, and wraps each one around the accumulated result:

```csharp
RequestDelegate app = ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; };

foreach (var component in components.Reverse())
    app = component(app);
```

The result is **one deeply nested function**. There is no list being iterated at request time, no
scheduler, no magic. `app.UseA(); app.UseB();` produces `A(B(404))`.

```
   app.UseA();   app.UseB();   app.MapGet(…);

   built ONCE, in reverse, at Build():        terminal  = ctx => 404
                                              pipeline  = A( B( endpoints( terminal ) ) )

   request  ──►  A  ──►  B  ──►  endpoint
                 │       │           │
                 │       │      the response comes back up the SAME nesting
                 ▼       ▼           │
   response ◄──  A  ◄──  B  ◄────────┘
                 ▲       ▲
                 │       └─ code AFTER  await next() runs on the way OUT
                 └───────── code BEFORE await next() runs on the way IN

   not calling next() short-circuits — which is how rate limiting returns 429
   and authorization returns 401 without your endpoint ever running
```

That single fact explains everything else:

- The request travels **down** the registrations and the response travels **back up**. Anything you
  want to observe about the *response* must be registered **before** whatever produces it.
- `await next(context)` is the hinge. Code before it runs on the way in; code after it runs on the
  way out.
- **Not calling `next` short-circuits.** That is not an error — it is how rate limiting returns 429
  and how authorization returns 401 without your endpoint ever running.

### Writing one by hand

📂 [`Api/Infrastructure/Authentication.cs`](../../src/LogiFlow.Api/Infrastructure/Authentication.cs)
— `CurrentUserMiddleware`

```csharp
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    // The constructor runs ONCE, at pipeline build time. This class is effectively a
    // singleton, so you must NOT inject scoped services here.
    public async Task InvokeAsync(HttpContext context, ICurrentUserSetter setter)
    {
        // Scoped services are injected per-request, as METHOD parameters.
        setter.Set(context.User);
        await next(context);
    }
}
```

**The constructor/method split is the whole trick, and it is a top-tier interview question.**
Convention-based middleware is instantiated once; `InvokeAsync`'s extra parameters are resolved from
the request scope on every call. Inject a `DbContext` into the constructor and you have built a
captive dependency that will serve stale data and throw under concurrency.

If you prefer it explicit, implement `IMiddleware` — that one *is* resolved from DI per request, but
you must register it yourself.

### Reading the real pipeline

Look at `Program.cs` from `app.UseExceptionHandler()` downwards, and note the reasoning attached to
each ordering decision. The four that bite people:

| Mistake | Symptom |
|---|---|
| `UseAuthorization` before `UseAuthentication` | Every authorized endpoint returns 401 with a perfectly valid token |
| Exception handling registered late | Errors from earlier middleware escape it and surface as a raw, unformatted 500 |
| Rate limiting after authentication | An unauthenticated flood still costs a full JWT validation each |
| `UseCors` after the endpoint | Preflight `OPTIONS` never gets its headers; the browser blocks it and the server log looks clean |

---

## 4. Endpoint routing is *two* steps, and the gap between them is the point

This is the part that makes the ordering rules stop being arbitrary.

```
UseRouting()      →  MATCH: work out which endpoint this URL resolves to.
                     Store it on the context. Do NOT execute it.

   ... middleware in the gap can call context.GetEndpoint() and read its metadata ...

UseEndpoints()    →  EXECUTE the endpoint that was selected.
```

**`UseAuthorization` must sit in that gap.** It works by reading `IAuthorizeData` metadata off the
*already-selected* endpoint. Before `UseRouting` there is no selected endpoint, so it has nothing to
check and lets everything through. That is the real reason for the ordering rule — mechanics, not
convention.

In minimal hosting (`WebApplication`) you rarely call either: `UseRouting` is inserted at the front
and `UseEndpoints` at the back automatically. **Calling `UseRouting()` explicitly moves it**, which
is how you deliberately place middleware before matching.

```csharp
app.Use(async (ctx, next) =>
{
    Endpoint? endpoint = ctx.GetEndpoint();          // null before UseRouting
    var policy = endpoint?.Metadata.GetMetadata<SomeAttribute>();
    await next();
});
```

**Route constraints** (`{orderId:guid}` in `OrderEndpoints.cs`) are part of *matching*, not
validation. A non-GUID does not reach your handler and does not produce a validation error — it
produces a **404**, because no route matched. People who expect a 400 there are misreading which
layer is speaking.

---

## 5. Parameter binding in minimal APIs

Given a handler parameter, the framework resolves it in this order:

1. An explicit attribute — `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`, `[FromServices]`, `[FromForm]`
2. Special types — `HttpContext`, `HttpRequest`, `ClaimsPrincipal`, `CancellationToken`, `IFormFile`
3. A `static BindAsync` on the parameter type → full control
4. A `static TryParse` on the parameter type → bound from a route/query string
5. A route value with a matching name
6. A query-string value with a matching name
7. A registered DI service
8. Otherwise → **the JSON body** (at most one parameter, and only where a body is allowed)

Rules 3 and 4 are the extension points worth knowing: they are how a strongly-typed ID
(📂 [`Domain/Common/Ids.cs`](../../src/LogiFlow.Domain/Common/Ids.cs)) can bind straight from a route
segment without a DTO in between.

### The `[AsParameters]` trap

📂 [`Application/Common/Pagination.cs`](../../src/LogiFlow.Application/Common/Pagination.cs)

```csharp
public int  Page { get; init; } = 1;   // "Required parameter 'int Page' was not provided"
public int? Page { get; init; }        // nullable ⇒ optional
```

**The binder never sees your property initialiser.** It sees a type, and a non-nullable `int` cannot
represent "absent". The fix — a nullable wire-facing property plus a computed resolved value — also
draws a genuinely useful distinction: `Page` is *what the client sent*, `PageNumber` is *what we will
use*.

### `IResult`, and why `TypedResults` is better

```csharp
return Results.Ok(dto);        // IResult      — opaque to a unit test
return TypedResults.Ok(dto);   // Ok<OrderDto> — the type carries the status AND the payload
```

`TypedResults` lets a unit test assert on the returned object without spinning up HTTP, and lets
OpenAPI infer the response shape without a `.Produces<T>()` call. Prefer it in new code.

---

## 6. Three places to intercept, and how to choose

This most often separates a mid from a senior in a .NET interview, because this codebase has all
three and they are genuinely different tools.

| | Runs for | Sees | Use it for |
|---|---|---|---|
| **Middleware** | Every request, including 404s and static files | Raw `HttpContext`. No model binding yet; no endpoint at all before `UseRouting` | HTTP-wide concerns: HTTPS redirect, CORS, rate limiting, exception handling, request logging |
| **Endpoint filter** | One endpoint or route group | The **bound arguments** (`EndpointFilterInvocationContext`) and the endpoint's metadata | Per-endpoint HTTP concerns: validating a bound DTO, idempotency keys, per-route auditing |
| **Pipeline behaviour** | Every dispatched command/query — **including from a background job or a test** | The command object. **Knows nothing about HTTP** | Transport-independent concerns: validation, transactions, caching, logging |

📂 [`Application/Behaviors/`](../../src/LogiFlow.Application/Behaviors/) —
`Logging → Validation → Caching → Transaction`, ordered deliberately (module 08).

```
   ┌─ MIDDLEWARE ───────────────────────────────────────────────────────┐
   │  every request, including 404s and static files. Raw HttpContext.  │
   │  ┌─ ENDPOINT FILTER ──────────────────────────────────────────┐    │
   │  │  one endpoint or group. Sees the BOUND arguments.          │    │
   │  │  ┌─ PIPELINE BEHAVIOUR ────────────────────────────────┐   │    │
   │  │  │  every dispatch — HTTP, background job or test.     │   │    │
   │  │  │  Knows nothing about HTTP.                          │   │    │
   │  │  │            ┌─ handler ─┐                            │   │    │
   │  │  │            └───────────┘                            │   │    │
   │  │  └─────────────────────────────────────────────────────┘   │    │
   │  └────────────────────────────────────────────────────────────┘    │
   └────────────────────────────────────────────────────────────────────┘

   the decisive question: would this still have to happen if the command arrived
   from a message queue instead of HTTP?   yes ⇒ behaviour.   no ⇒ middleware or filter.
```

**The decisive test: would this still need to happen if the command arrived from a message queue
instead of HTTP?** If yes, it is a behaviour. If no, it is middleware or a filter. That is why
`TransactionBehavior` is not middleware — the outbox processor dispatches commands with no
`HttpContext` anywhere in sight, and those still need a transaction.

```csharp
group.MapPost("/", CreateOrder)
     .AddEndpointFilter<IdempotencyFilter>();   // a filter: it needs the Idempotency-Key HEADER
```

For controllers the equivalents are MVC filters, which run in a fixed order:
**Authorization → Resource → Model binding → Action → Exception → Result.**

---

## 7. Configuration and the Options pattern

Three interfaces, one difference that matters:

| Interface | Lifetime | Re-reads configuration? | Use for |
|---|---|---|---|
| `IOptions<T>` | Singleton | Never | Settings fixed at startup — the common case |
| `IOptionsSnapshot<T>` | **Scoped** | Once per request/scope | Settings that may change without a restart |
| `IOptionsMonitor<T>` | Singleton | Yes, with `OnChange` callbacks | Singletons and background services needing live values |

A `BackgroundService` is a singleton, so it **cannot** inject `IOptionsSnapshot<T>` — that is a
captive dependency, and scope validation will reject it. Use `IOptionsMonitor<T>`.

### Validate at startup, not at first use

📂 [`Api/Infrastructure/Authentication.cs`](../../src/LogiFlow.Api/Infrastructure/Authentication.cs)

```csharp
services.AddOptions<JwtOptions>()
        .Bind(configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters")
        .ValidateOnStart();        // without this, validation is lazy and fires on first login
```

`ValidateOnStart()` is the whole point. Without it, a missing signing key is discovered by your first
user, in production, as a 500. With it, the process refuses to start. **Fail at deploy, not at
runtime** — which is why the API deliberately throws `Jwt:SigningKey is not configured` rather than
quietly falling back to a default.

---

## 8. Authentication vs authorization

Two words used interchangeably that are not the same thing.

- **Authentication** — *who are you?* Validates the credential and populates `HttpContext.User` with
  a `ClaimsPrincipal`. On failure: **401**.
- **Authorization** — *may you do this?* Evaluates policies against that principal. On failure:
  **403**.

401 with an apparently valid token means authentication silently failed and left an unauthenticated
principal — check issuer, audience, signing key and clock skew, in that order.

### Policies, not roles, at the call site

📂 `AuthorizationPolicies` in `Authentication.cs`

```csharp
.RequireAuthorization(Roles = "Admin,Manager")                 // the rule now lives in six files
.RequireAuthorization(AuthorizationPolicies.CatalogManager)    // defined once
```

The named policy can later become "has the `catalog:write` scope" or "is in this Entra ID group"
without touching a single endpoint. **Roles are data; policies are decisions.** Keep the decision in
one place.

For anything genuinely conditional — "may edit *this* order" — you need a resource-based check
(an `AuthorizationHandler<TRequirement, TResource>` invoked through `IAuthorizationService`), because
an attribute-style check cannot see the entity.

---

## 9. Kestrel, and what sits in front of it

Kestrel is a real, fast, cross-platform HTTP server supporting HTTP/1.1, HTTP/2 and HTTP/3. It can
face the internet directly, but in practice it usually sits behind a reverse proxy (nginx, IIS, YARP,
or an ingress controller) for TLS termination, shared ports and per-host routing.

**The one thing you must remember behind a proxy:**

```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
```

Without it, every client IP is your proxy's IP — so the per-user rate limiter in `Program.cs`
partitions *all anonymous traffic into a single bucket* — and `Request.Scheme` is `http`, so
`UseHttpsRedirection` can loop. Register it **first**, before anything that reads the scheme or the
remote IP.

---

## 10. The rest of the family, in one paragraph each

You will be asked which you would choose. Have an opinion.

- **Minimal APIs** — the modern default for services. Less ceremony, no reflection-based action
  invocation, a natural fit for one-endpoint-one-handler. What this codebase uses.
- **MVC controllers** — still fully supported and not legacy. Worth it for OData, complex model
  binding, or a large inherited filter hierarchy. Just heavier.
- **Blazor** — C# in the browser (WebAssembly) or over a socket (Server). **Pay attention to this one
  for the Italian market**: it lets a company with fifteen years of WinForms/WPF and no JavaScript
  team build web UI in the language it already has, which is exactly the migration many manufacturing
  software houses in Emilia-Romagna are in the middle of. Blazor Server's trade-off is a stateful
  circuit per user: cheap to start, expensive to scale, and it dies with the connection.
- **SignalR** — real-time push over WebSockets with fallbacks. Live dashboards, machine status.
- **gRPC** — contract-first binary RPC over HTTP/2. Excellent service-to-service, poor for browsers
  without a proxy.

---

## Do it

**1. Prove the pipeline is nested function calls.** Add this to `Program.cs` immediately after
`app.UseExceptionHandler()`, then hit any endpoint:

```csharp
app.Use(async (ctx, next) =>
{
    Console.WriteLine($"IN   {ctx.Request.Path}  endpoint={ctx.GetEndpoint()?.DisplayName ?? "none"}");
    await next();
    Console.WriteLine($"OUT  {ctx.Request.Path}  status={ctx.Response.StatusCode}");
});
```

Now move the same block to just before `app.MapOrderEndpoints()`. **The `endpoint=` value changes
from `none` to the matched endpoint** — you have just watched `UseRouting` do its half of the job.

**2. Break the ordering on purpose.** Swap `app.UseAuthentication()` and `app.UseAuthorization()`.
Mint a token from `/api/dev/token` and call `GET /api/orders`. You get 401 with a perfectly valid
token. Now you will recognise that symptom in three seconds instead of over an afternoon.
`git checkout .` to restore.

**3. Find the captive dependency.** Change `CurrentUserMiddleware` to take `LogiFlowDbContext` in its
**constructor**. It compiles. Run it in Development and read the exception — scope validation catches
it. Then set `ASPNETCORE_ENVIRONMENT=Production` and watch it start cleanly and be wrong. That gap is
the bug class this module exists to teach.

**4. Write an endpoint filter.** Add an `IdempotencyFilter` to `POST /api/orders` that reads an
`Idempotency-Key` header and returns the previous response for a repeated key. Note what you had
access to that middleware would not have had: the bound `CreateOrderCommand`.

---

## Golden rules

> The card. Half of these are the answer to "why does it return 401 with a valid token?"

1. **The pipeline is one nested function, composed in reverse at build time.** Down on the way in,
   up on the way out, `await next()` is the hinge, and not calling it short-circuits.
2. **Order is behaviour.** Exception handling first, rate limiting before authentication,
   authentication before authorization, forwarded headers before anything that reads the scheme or
   the client IP.
3. **Routing matches; endpoints execute; authorization lives in the gap.** Before `UseRouting`,
   `GetEndpoint()` is null and there is nothing to authorize against.
4. **A route constraint is matching, not validation.** `{id:guid}` given "abc" produces a 404,
   because no route matched — not a 400.
5. **Convention-based middleware is constructed once.** Scoped services go in `InvokeAsync`'s
   parameters, never the constructor.
6. **Would this still need to happen if the command came from a queue?** Yes ⇒ pipeline
   behaviour. No ⇒ middleware or endpoint filter.
7. **A non-nullable parameter is a required parameter.** The binder never sees your property
   initialiser.
8. **`TypedResults` over `Results`.** The type carries the status and the payload, so a unit test
   can assert on it and OpenAPI can infer the shape.
9. **`IOptions` for startup values, `IOptionsSnapshot` per request, `IOptionsMonitor` in
   singletons.** A `BackgroundService` taking `IOptionsSnapshot` is a captive dependency.
10. **`ValidateOnStart()`, always.** Fail at deploy, not at the first login.
11. **401 is authentication, 403 is authorization.** A valid-looking token returning 401 means
    authentication failed silently: check issuer, audience, signing key and clock skew, in that
    order.
12. **Policies at the call site, roles inside the policy.** Roles are data; policies are decisions,
    and a decision should live in one place.
13. **Behind a proxy, `UseForwardedHeaders` first** — or every client IP is the proxy's, and your
    per-user rate limiter partitions all anonymous traffic into one bucket.

---

## Interview questions

**"Explain the middleware pipeline."**
A set of `Func<RequestDelegate, RequestDelegate>` composed in reverse at build time into one nested
function. The request goes down, the response comes back up, `await next()` is the hinge, and not
calling `next` short-circuits. Order is behaviour, not style.

**"Why must `UseAuthorization` come after `UseRouting`?"**
Because authorization reads authorize metadata off the endpoint that routing selected. Routing
matches but does not execute; the gap between matching and execution is where authorization has
something to inspect. Before `UseRouting`, `GetEndpoint()` is null and everything passes.

**"Middleware, endpoint filter, or pipeline behaviour?"**
Middleware for HTTP-wide concerns; endpoint filters for per-endpoint concerns needing the bound
arguments; behaviours for concerns that must also apply when the command arrives from a queue or a
test. The test is whether HTTP is essential to the concern.

**"Transient, scoped or singleton?"**
Per-resolution, per-request, per-process. `DbContext` is scoped because it is a unit of work and is
not thread-safe. A singleton depending on a scoped service is a captive dependency — caught by scope
validation in Development, silently broken in Production.

**"Why can't my `BackgroundService` inject `DbContext`?"**
It is a singleton. Inject `IServiceScopeFactory` and create a scope per iteration — see
`OutboxProcessor`.

**"`IOptions` vs `IOptionsSnapshot` vs `IOptionsMonitor`?"**
Singleton/never reloads, scoped/per-request, singleton/with change notification. Singletons must use
`IOptionsMonitor`, never `IOptionsSnapshot`.

**"401 or 403?"**
401 = we do not know who you are (authentication). 403 = we know, and you may not (authorization).

**"How does configuration get into a container?"**
Environment variables override the JSON files, with `__` standing in for `:`. Secrets come from a
vault or the orchestrator, never from a committed file.

---

**Next:** [Module 16 — The layer map](../module-16-the-layer-map/) puts this framework next to C#,
LINQ and SQL and draws the boundaries between them.
