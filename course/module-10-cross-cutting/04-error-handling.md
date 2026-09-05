# 3. Error handling

> Part of [Module 10 — Cross-cutting concerns](README.md), section 3.
> Previous: [2. Minimal APIs](02-minimal-apis.md) · Next: [4. Caching](03-caching.md)

---

There are exactly two kinds of failure and they need opposite treatment.

**Expected failure** — an empty order, a missing customer, a stale concurrency token. Somebody
reasonable did something reasonable and the answer is no. These travel as
[`Result`](../module-05-clean-architecture/05-result-vs-exceptions.md) values and become 4xx.

**Unexpected failure** — a null reference, a dropped connection, a bug. Nobody's business rule is
involved and no caller can recover. These are exceptions, become 500, and must be logged with
everything you have.

Mixing them is what produces both of the classic bad APIs: the one that returns `200 OK` with
`{"success": false}` inside, and the one that returns a stack trace to the internet.

## One translation, at the boundary

The domain never mentions HTTP. `ErrorType` carries the classification, and one file turns it into a
status code:

```csharp
public static IResult ToHttpResult(this Result result) =>
    result.IsSuccess
        ? TypedResults.NoContent()
        : Problem(result.Error);

private static IResult Problem(Error error) => TypedResults.Problem(
    title:      error.Code,
    detail:     error.Description,
    statusCode: error.Type switch
    {
        ErrorType.Validation   => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden    => StatusCodes.Status403Forbidden,
        ErrorType.NotFound     => StatusCodes.Status404NotFound,
        ErrorType.Conflict     => StatusCodes.Status409Conflict,
        _                      => StatusCodes.Status500InternalServerError,
    });
```

Every endpoint ends `return result.ToHttpResult();`. No endpoint decides a status code, so no two
endpoints disagree about what "not found" means.

The status codes that carry real information and are routinely got wrong:

- **400** the request is malformed or fails validation.
- **401** you are not authenticated. **403** you are, and still no. (See
  [module 24](../module-24-security/) — this pair is asked constantly.)
- **404** it does not exist — *or* it does and telling you would leak something.
- **409** the state conflicts: a duplicate, or a lost update.
- **422** semantically invalid though well-formed. Legitimate; 400 is also defensible. Pick one and
  be consistent.

## Problem Details

RFC 9457 (formerly 7807) defines a standard error body, and ASP.NET Core produces it natively:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Order.Empty",
  "status": 400,
  "detail": "Cannot submit an order with no lines.",
  "traceId": "00-8f3a1c9e4b7d2f01-a1b2c3d4e5f60718-01"
}
```

Use it rather than inventing a shape. Clients and gateways already understand it, and the generated
TypeScript client in [site chapter 18](../../site/chapters/18-spa-and-typescript.html) can be typed
against it once for every endpoint.

**`traceId` is the field that matters operationally.** It is the same id
[structured logging](../module-11-observability/02-structured-logging.md) attaches to every log line
for the request, so a customer quoting it lets you retrieve the whole request in one query. Returning
it costs nothing and turns "it broke this morning" into a filter.

## The global handler

```csharp
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);

        await Results.Problem(
            title:      "An unexpected error occurred.",
            detail:     null,                                   // never the message
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?> { ["traceId"] = Activity.Current?.Id })
            .ExecuteAsync(context);

        return true;
    }
}
```

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
app.UseExceptionHandler();
```

**`detail: null` on a 500, deliberately.** An exception message can contain a connection string, a
file path, a SQL fragment or a customer's data. The client gets a trace id; the log gets everything.
That asymmetry is the whole design.

**`IExceptionHandler` rather than custom middleware.** It is the .NET 8+ mechanism, it composes —
several handlers run in registration order until one returns `true` — and it plays correctly with
`UseExceptionHandler`, which also re-executes the pipeline cleanly.

**Log at the boundary, once.** The `catch`-log-`throw` pattern at every layer turns one failure into
five stack traces and an on-call engineer counting incidents. Handle it or log it, never both — and
if you must rethrow, use `throw;` not `throw ex;`, which resets the stack trace to the line you are
standing on.

## Where each layer draws the line

| Layer | Expected failure | Unexpected failure |
|---|---|---|
| Domain | returns `Result` with an `Error` | throws for programmer error (`ArgumentNullException`) |
| Application | propagates the `Result` | lets it bubble |
| Infrastructure | translates known ones (`DbUpdateConcurrencyException` → `Conflict`) | lets it bubble |
| API | `ToHttpResult()` | `GlobalExceptionHandler` |

Infrastructure translating `DbUpdateConcurrencyException` into `ErrorType.Conflict` is worth
noticing: an infrastructure exception becomes an expected domain outcome **at the layer that knows
what it means**, and the API layer never learns that EF Core exists.

## Validation errors are a shape of their own

A failed validation should return every problem at once, not the first:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Lines": ["An order must have at least one line."],
    "CustomerId": ["Customer id is required."]
  }
}
```

`TypedResults.ValidationProblem(...)` produces exactly this, and it is what `ValidationBehavior`
feeds by collecting failures from every validator rather than stopping at the first —
[module 08 section 4](../module-08-cqrs/05-pipeline-behaviors.md). A form that reports one error per
round trip takes six submissions.

## The mistakes

**`200 OK` with an error inside.** Every client, cache, proxy and monitoring tool now believes it
succeeded.

**Exceptions for control flow.** Expensive, dishonest in the signature, and it hides the ordinary
path inside a `try`.

**Catch, log, rethrow at every layer.** One failure, five stack traces.

**`throw ex;`.** Destroys the stack trace.

**Leaking the exception message in production.** Trace id out, detail to the log.

**Swallowing exceptions.** `catch (Exception) { }` is how a system becomes undiagnosable.

**A different error shape per endpoint.** Clients cannot handle errors generically, so they stop
trying.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
curl -i http://localhost:5000/api/orders/00000000-0000-0000-0000-000000000000
```

A 404 with Problem Details and a `traceId`. Now grep the console for that id and read the whole
request. Then throw deliberately inside a handler and call it again: a 500 with **no** detail in the
body and the full stack trace in the log — which is exactly the split you want.

## What to remember

- Two kinds of failure: expected is a `Result` and a 4xx; unexpected is an exception and a 500.
- Map `ErrorType` to a status code once, at the boundary. No endpoint decides.
- 401 is "who are you", 403 is "not you", 409 is "not now".
- Use Problem Details rather than inventing a shape, and always return a `traceId`.
- Never return an exception message to the client; log it and hand back the id.
- `IExceptionHandler` plus `AddProblemDetails`, registered with `UseExceptionHandler`.
- Log once, at the boundary. `throw;` never `throw ex;`.
- Collect every validation failure, not the first.

**Code:** [`GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs) ·
[`ResultExtensions.cs`](../../src/LogiFlow.Api/Infrastructure/ResultExtensions.cs) ·
[`ValidationBehavior.cs`](../../src/LogiFlow.Application/Behaviors/ValidationBehavior.cs)

**Next:** [4. Caching](03-caching.md)
