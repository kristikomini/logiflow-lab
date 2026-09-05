# Module 10 — Cross-cutting concerns

> DI, minimal APIs, error handling, caching, rate limiting. The plumbing that every .NET service
> needs and that interviews probe because it is where subtle production bugs live.

---

## Deeper chapters

Some sections below have a chapter that goes further — the code, the traps, and the interview answer.

| | Chapter | |
|---|---|---|
| 1 | [Dependency injection and lifetimes](01-dependency-injection.md) | scoped by default, and the captive dependency |
| 2 | [Minimal APIs](02-minimal-apis.md) | routing without conventions, and thin endpoints |
| 3 | [Error handling](04-error-handling.md) | two kinds of failure, one translation, Problem Details |
| 4 | [Caching](03-caching.md) | invalidation, naming, and the stampede |
| 5 | [Sending email](05-mailing.md) | queue in the transaction, claim atomically, never mail from staging |

---

## 1. Dependency injection and lifetimes

| Lifetime | One instance per | Use for |
|---|---|---|
| **Transient** | resolution | cheap, stateless things |
| **Scoped** | HTTP request | `DbContext`, repositories, anything per-request |
| **Singleton** | process | caches, configuration, `TimeProvider`, hosted services |

```
   ┌─ the process ─────────────────────────────────────────────────────────────┐
   │  SINGLETON   cache · TimeProvider · IConfiguration · OutboxProcessor      │
   │                                                                           │
   │  ┌─ request 1 ──────────────────┐   ┌─ request 2 ──────────────────┐      │
   │  │ SCOPED  DbContext, repos     │   │ SCOPED  DbContext, repos     │      │
   │  │   ┌ TRANSIENT ┐ ┌ TRANSIENT ┐│   │   ┌ TRANSIENT ┐              │      │
   │  │   └───────────┘ └───────────┘│   │   └───────────┘              │      │
   │  └──────────────────────────────┘   └──────────────────────────────┘      │
   └───────────────────────────────────────────────────────────────────────────┘

   CAPTIVE DEPENDENCY — a singleton that holds a scoped service
       singleton ──holds──► the DbContext from request 1 … for the life of the process
       ⇒ tracked entities accumulate (a leak) · stale data · not thread-safe under load
       ⇒ scope validation catches it: ON in Development, OFF in Production.
         So the failure mode is "fine on my machine, wrong in production".
```

### The captive dependency

**A singleton that depends on a scoped service captures it forever.**

```csharp
services.AddSingleton<IOrderService, OrderService>();   // ❌ if it takes a DbContext
```

One `DbContext` now lives for the process lifetime: it accumulates tracked entities (a memory
leak), serves stale data, and blows up under concurrency because it is not thread-safe.

.NET's container catches this at startup **if scope validation is on** — it is by default in
Development, and off in Production. So the failure mode is: works locally, degrades in prod.

📂 [`Outbox/OutboxProcessor.cs`](../../src/LogiFlow.Infrastructure/Persistence/Outbox/OutboxProcessor.cs)
is a singleton `BackgroundService` and therefore takes `IServiceScopeFactory`, creating a scope
per iteration. That is the correct pattern.

### Each layer registers itself

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddLogiFlowAuth(builder.Configuration);
```

Without this, `Program.cs` becomes a 300-line file edited every time anyone adds a class — and a
permanent merge-conflict magnet.

### Assembly scanning, and its cost

📂 [`Application/DependencyInjection.cs`](../../src/LogiFlow.Application/DependencyInjection.cs)
registers ~40 handlers by reflection instead of by hand.

**Stated honestly:** it costs reflection at startup (single-digit ms here) and makes registration
implicit — you cannot ctrl-click from an interface to find where it was registered. It also needs
care under Native AOT trimming. Worth it for an app this size; usually not for a library.

Note `GetTypes()` rather than `GetExportedTypes()`: handlers are deliberately `internal`, so
nothing outside the Application layer can call one directly and skip the pipeline.

---

## 2. Minimal APIs

📂 [`Api/Endpoints/OrderEndpoints.cs`](../../src/LogiFlow.Api/Endpoints/OrderEndpoints.cs)

Both minimal APIs and controllers are fully supported. Minimal APIs are the modern default: less
ceremony, measurably faster (no reflection-based action invocation), and a better fit for the
one-endpoint-one-handler shape CQRS already imposes. Controllers still win for OData, complex
model binding, or a large inherited filter hierarchy.

**Route groups** configure cross-cutting concerns once:

```csharp
RouteGroupBuilder group = app.MapGroup("/api/orders")
    .WithTags("Orders")
    .RequireAuthorization();
```

**Look at how little each endpoint does:** bind, dispatch, map the result. No business logic, no
validation, no try/catch, no `SaveChanges`. An endpoint that grows an `if` about business state
is a smell.

### `[AsParameters]` and a binding gotcha worth knowing

```csharp
private static async Task<IResult> SearchOrders([AsParameters] SearchOrdersQuery query, ...)
```

binds the whole query string onto the record. But:

```csharp
public int Page { get; init; } = 1;    // ❌ "Required parameter 'int Page' was not provided"
public int? Page { get; init; }        // ✅ nullable ⇒ optional
```

**`[AsParameters]` treats a non-nullable property as required.** A C# property initialiser is
invisible to the binder — it only sees the type, and a non-nullable `int` cannot represent
"absent". This is a real bug that was hit and fixed while building this course.

📂 [`Application/Common/Pagination.cs`](../../src/LogiFlow.Application/Common/Pagination.cs) — the fix is
a nullable wire-facing property plus a computed resolved value, which also draws a useful
distinction: `Page` is what the client sent, `PageNumber` is what we will use.

### REST decisions worth being able to defend

- **POST, not PUT, to create** — the server picks the id, so it is not idempotent, and PUT
  promises idempotency. Get it backwards and proxies retry your creates into duplicates.
- **`POST /orders/{id}/cancel`, not `DELETE`** — cancelling is a state transition that keeps the
  order and records why. Modelling business verbs as sub-resource POSTs is the pragmatic middle
  ground most production APIs settle on.
- **PATCH for a partial update** (`/products/{id}/price`), PUT for a full replacement.
- **201 must carry a `Location` header.** Everyone omits it; it is what lets a client follow the
  response without guessing your URL scheme.

---

## 3. Error handling

📂 [`Api/Infrastructure/ResultExtensions.cs`](../../src/LogiFlow.Api/Infrastructure/ResultExtensions.cs) ·
[`GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs)

**This is the only place in the solution that knows HTTP status codes.** The Domain says
"`Order.AlreadyShipped`, and that is a Conflict"; this decides a Conflict is 409.

Responses are **RFC 9457 Problem Details** — the standard machine-readable error format, which
clients, gateways and tooling already understand. Inventing `{ "success": false, "msg": "..." }`
means every consumer writes bespoke handling.

```json
{
  "title": "Conflict with current state",
  "status": 409,
  "detail": "An order in status 'Submitted' can no longer be modified.",
  "code": "Order.NotEditable",
  "traceId": "00-93df16803d04aa97-77c2732310786ff5-01"
}
```

`code` is the extension member clients branch on. **Never branch on `detail`** — it is free to
change and to be translated.

### The status codes people get wrong

- **401 vs 403** — 401 means "I do not know who you are", send credentials. 403 means "I know
  exactly who you are and you still cannot". Returning 401 for an authorisation failure sends
  clients into a login loop.
- **400 vs 409** — 400 is malformed, fix the request. 409 is well-formed but conflicts with
  current state; reload and retry.
- **404 on a collection** — an empty list is a successful search that matched nothing: 200 with
  `[]`. Reserve 404 for a specific resource.
- **A binding failure is 400, not 500.** `BadHttpRequestException` reaching a catch-all becomes a
  500 and tells the caller "we broke" when their request was invalid — and pollutes your error
  alerting with other people's typos. This exact bug was found and fixed while building this
  course; see the `BadHttpRequestException` arm in `GlobalExceptionHandler`.

### Never leak stack traces

```csharp
Detail = environment.IsDevelopment() ? $"{detail} — {exception.Message}" : detail,
```

Stack traces disclose namespaces, file paths, library versions and sometimes connection strings.
The detail goes to the log; the client gets a `traceId` to quote at support.

---

## 4. Caching

Three layers, each solving a different problem:

| Layer | Stops the request from… | Where |
|---|---|---|
| **Output cache** | reaching the application at all | `.CacheOutput(...)` on an endpoint |
| **Distributed cache (Redis)** | reaching the database | `CachingBehavior` |
| **EF query cache** | being re-compiled | automatic |

```
   client ──► [ output cache ] ──► app ──► [ Redis ] ──► [ EF query cache ] ──► SQL Server
                    │                          │                │
        stops the request reaching     stops the query    stops the expression tree
        your application at all        reaching the DB    being re-translated
```

**Why distributed rather than `IMemoryCache`?** In-memory is faster and simpler, and breaks the
moment you run two instances: each has its own copy, they disagree, and invalidating one does
nothing to the other. Users see data flicker depending on which pod answers.

**A cache failure must never fail the request.** If Redis is down the correct behaviour is a
cache miss and a slower response — not a 500. `CacheService` catches broadly and logs, which is
one of the few places that is right.

**What caching does not solve: invalidation.** An order cached for 60 seconds is stale for up to
60 seconds. Short TTLs are the crude answer; explicit eviction from the command side is the
precise one.

**Cache stampede:** `GetOrCreateAsync` does not prevent two concurrent misses both running the
factory. Whether that matters depends entirely on how expensive the factory is.

---

## 5. Rate limiting

📂 [`Program.cs`](../../src/LogiFlow.Api/Program.cs)

Partitioned **per user, falling back to IP**. A single global limiter lets one noisy client
exhaust the budget for everyone — a denial of service you built yourself.

```csharp
QueueLimit = 0   // reject immediately rather than queue; a queued request still holds resources
```

And return `Retry-After`. Without it a well-behaved client has to guess, and most guess
"immediately".

**Position matters:** `UseRateLimiter()` goes **before** authentication, so a flood of
unauthenticated requests is cheap to reject rather than costing a full token validation each.

---

## 6. Middleware order is behaviour

```
UseExceptionHandler        ← first: catches everything downstream
UseStatusCodePages
UseSerilogRequestLogging
UseHttpsRedirection
UseRateLimiter            ← before auth: reject floods cheaply
UseAuthentication         ← who are you?
UseAuthorization          ← may you?          (strictly after authentication)
UseMiddleware<CurrentUserMiddleware>   ← after auth, so context.User is populated
UseOutputCache
[endpoints]
```

Common ordering bugs:

- **`UseAuthorization` before `UseAuthentication`** → nobody is ever authenticated, so every
  protected endpoint returns 401 no matter what token is sent.
- **Exception handling registered late** → exceptions from earlier middleware escape it and
  surface as an unformatted 500.

---

## 7. Configuration and options

📂 [`Api/Infrastructure/Authentication.cs`](../../src/LogiFlow.Api/Infrastructure/Authentication.cs)

```csharp
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey is not configured.")
    .ValidateOnStart();
```

**`ValidateOnStart` is the important line.** A missing signing key should stop the process from
starting, loudly — not produce 500s an hour later when the first user tries to log in.

Fail fast at startup for anything the application cannot run without.

---

## 8. Sending email

📂 [`Infrastructure/Mailing/`](../../src/LogiFlow.Infrastructure/Mailing/) ·
[chapter](05-mailing.md)

Almost every business system sends email, and almost every tutorial stops at `SmtpClient.Send`.
The gap is where the production pain lives.

```
   ┌─ your transaction ─────────────────────────┐
   │  order.Cancel()                            │
   │  emailQueue.EnqueueAsync(message)  ← a row │
   │  SaveChangesAsync()                ← ONE commit
   └────────────────────────────────────────────┘
                    │  seconds later, another process
                    ▼
        claim a batch atomically → send → MarkSent / MarkFailed(backoff)
```

**Enqueue; never send inside a transaction.** `IEmailQueue` writes through the *caller's*
`DbContext` and does not save. The order and the intent to email therefore commit together or not
at all — no SMTP round trip holding locks, and no confirmation for an order that rolled back. It is
the [outbox pattern](../module-06-efcore/07-outbox-pattern.md) applied to email.

**Delivery is at-least-once, so the message carries a deduplication key** derived from the fact
(`shipment-dispatched:{trackingNumber}`), backed by a unique *filtered* index — filtered because
SQL Server treats `NULL`s as equal for uniqueness, unlike the standard.

**Two instances must not both send everything.** The worker claims a batch in one statement with
`UPDLOCK, READPAST` and `OUTPUT INSERTED.Id`, incrementing the attempt count and pushing a
visibility lease forward. A worker that dies mid-send loses its lease and another picks the message
up.

**4xx is "not now"; 5xx is "not ever".** Only transient failures are retried, exponentially with
jitter. After the last attempt the message is dead-lettered and kept — evidence, not litter.

**Redirect all mail in every non-production environment.** `Mailing:RedirectAllTo` re-addresses
every message; the decorator that does it wraps whichever transport is configured, so no transport
knows it exists. The incident it prevents — staging restored from a production backup, emailing
real customers — is routine.

**The transport is configuration, not code.** `Log` (default, mails nobody), `File` (a real `.eml`
per message, openable in Outlook), `Smtp` (MailKit, one pooled connection behind a `SemaphoreSlim`
because its client is not thread-safe).

---

## 9. Golden rules

> The card. This is where the subtle production bugs live.

1. **Transient per resolution, scoped per request, singleton per process.** `DbContext` is scoped
   because it is a unit of work and is not thread-safe.
2. **A singleton must never hold a scoped service.** If it needs one, inject
   `IServiceScopeFactory` and open a scope per unit of work.
3. **Each layer registers itself.** Otherwise `Program.cs` becomes a 300-line permanent merge
   conflict.
4. **An endpoint binds, dispatches and maps the result.** An `if` about business state inside an
   endpoint is a smell.
5. **A non-nullable property is a required parameter.** `[AsParameters]` never sees your property
   initialiser — make optional query parameters nullable and resolve the default yourself.
6. **POST to create; POST a verb sub-resource for a state transition; PATCH for a partial
   update** — and a 201 always carries a `Location` header.
7. **Errors are RFC 9457 Problem Details with a stable `code`.** Clients branch on the code, never
   on the human-readable detail.
8. **401 is "I do not know who you are"; 403 is "I know, and no".** 400 is malformed; 409 is
   well-formed and conflicting. An empty search result is 200 with `[]`.
9. **A binding failure is the caller's 400, not your 500.** Otherwise your error alerting fills up
   with other people's typos.
10. **Never return a stack trace.** The detail goes to the log; the client gets a `traceId`.
11. **A cache failure must degrade, never fail.** Redis down means a cache miss and a slower
    response, not an error page.
12. **Rate limit before authentication, authenticate before authorise, and register exception
    handling first.** Middleware order is behaviour, not style.
13. **`ValidateOnStart` on everything the application cannot run without.** A missing signing key
    should stop the process from starting, not produce 500s an hour later.
14. **Queue email inside the transaction; deliver it outside.** Sending during a transaction holds
    locks on a mail server's schedule and mails customers about orders that then roll back.
15. **At-least-once delivery means the message needs an idempotency key derived from the fact** —
    never from `Guid.NewGuid()`, which is the same as having no key.
16. **Claim queue rows atomically (`UPDLOCK, READPAST`, `OUTPUT`) and count the attempt when you
    claim**, or two instances send everything twice and a poison message is immortal.
17. **Retry 4xx, never 5xx, exponentially with jitter, then dead-letter and keep it.** A cleanup
    job that deletes the evidence of its own failures is worse than none.
18. **Redirect all outbound mail in every non-production environment.** The incident this prevents
    happens somewhere every month and is always found by the customers.

---

## 10. Interview questions

**"Explain DI lifetimes and the captive dependency problem."**
Transient per resolution, scoped per request, singleton per process. A singleton capturing a
scoped service holds it forever — a `DbContext` that leaks memory, serves stale data and is not
thread-safe. Scope validation catches it in Development but is off in Production.

**"Minimal APIs or controllers?"**
Minimal APIs by default: faster, less ceremony, good fit for one-endpoint-one-handler.
Controllers when you need OData, complex model binding, or a deep filter hierarchy. Both are
supported long-term; it is not a migration.

**"Does middleware order matter?"**
Enormously. Authentication before authorization or nothing is ever authenticated. Exception
handling first or it cannot catch what runs before it. Rate limiting before auth so floods are
cheap to reject.

**"How do you design API errors?"**
RFC 9457 Problem Details, with a stable machine-readable `code` extension that clients branch on
and a human-readable `detail` that is free to change. Never leak stack traces outside
Development. Map error *categories* to status codes in one place.

**"How would you send transactional email reliably?"**
Write the message as a row in the same transaction as the business change — an outbox — so the two
commit together. A background worker claims rows atomically and delivers them, retrying transient
SMTP failures (4xx) with exponential backoff and jitter, and dead-lettering permanent ones (5xx).
The message carries an idempotency key derived from the business fact, because the delivery
guarantee is at-least-once. And every non-production environment redirects all recipients, because
otherwise staging eventually mails real customers.

**"In-memory or distributed cache?"**
In-memory is faster but breaks when you scale out — each instance has its own copy and
invalidation does not propagate. If you will ever run more than one instance, start with Redis.

---

## Next

→ [Module 11 — Observability](../module-11-observability/)
