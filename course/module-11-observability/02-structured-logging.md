# 2. Structured logging

> Part of [Module 11 — Observability](README.md), section 2.
> Previous: [1. The three signals](README.md#1-the-three-signals) ·
> Next: [3. OpenTelemetry](README.md#3-opentelemetry)

---

The difference between a log you can query and a log you can only read is **one character**:

```csharp
logger.LogInformation($"Handled {name} in {ms}ms");        // ✗ one opaque string
logger.LogInformation("Handled {Name} in {Ms}ms", name, ms); // ✓ a message plus queryable fields
```

The second form reaches Seq, Application Insights or Loki with `Name` and `Ms` as **separately
indexed properties**. You can then ask:

> every request over 500 ms, grouped by name, in the last 24 hours

as a query. With interpolation you get a million unique strings, no fields, and the only tool left is
substring search — which cannot group, cannot aggregate, and cannot answer that question at all.

The Roslyn analyser **CA2254** exists to catch exactly this. Turn it on and let the compiler enforce
it, because in review it is invisible: both lines read identically to a human.

## The template is the identity

The message template is also how a log backend groups occurrences. `"Handled {Name} in {Ms}ms"` is
one event type with a million instances; `$"Handled {name}…"` is a million event types with one
instance each. That is why interpolation destroys not just filtering but *counting* — "how often does
this happen" has no answer.

Keep placeholder names **PascalCase and stable**. They are field names in a database, and renaming
one breaks every saved query and dashboard that used it.

## Scopes: the id on every line

Half of structured logging is the fields you pass. The other half is the fields you do not:

```csharp
using (logger.BeginScope(new Dictionary<string, object>
{
    ["CorrelationId"] = Activity.Current?.TraceId.ToString() ?? traceId,
    ["RequestName"]   = typeof(TRequest).Name
}))
{
    logger.LogInformation("Handling {RequestName}", name);
    TResponse response = await next();
    logger.LogInformation("Handled {RequestName} in {Ms}ms", name, sw.ElapsedMilliseconds);
    return response;
}
```

Everything logged inside that `using` — including EF Core's own SQL logging, three layers down —
carries `CorrelationId`. When a customer reports a failure at 14:32, you filter on one value and see
the entire request: the endpoint, the handler, every query it ran, and the exception.

Two things make it work end to end:

**`Activity.Current.TraceId` is set by the framework**, so you do not have to invent an id or
propagate it manually. It is the same W3C trace id that flows across service boundaries in the
`traceparent` header, which is what makes a distributed trace stitch together.

**Return it to the caller.** Problem Details carries `traceId`
([module 10 section 3](../module-10-cross-cutting/04-error-handling.md)), so the user quoting an error id hands you the exact
filter. That one field turns "it broke this morning" into a query.

## Doing it once, not everywhere

`LoggingBehavior` wraps every request, so no handler contains logging code at all. One class, and
every operation gets a start line, a duration, an outcome and a scope.

It also flags slow requests:

```csharp
private const int SlowRequestThresholdMs = 500;

if (elapsed > SlowRequestThresholdMs)
    logger.LogWarning("SLOW {RequestName} took {Ms}ms", name, elapsed);
```

That turns "the app feels slow sometimes" — which is unactionable — into a warning-level query with a
name and a number attached.

## Levels, and what each one means operationally

| Level | Use when | Who is woken |
|---|---|---|
| `Trace` | firehose; local only | nobody |
| `Debug` | developer detail; off in production | nobody |
| `Information` | a business thing happened | nobody |
| `Warning` | recovered, or a limit is near | a dashboard |
| `Error` | this request failed; a user is affected | an alert |
| `Critical` | the application cannot continue | a human, at night |

**The test for `Error`: would you want to be told about a hundred of these in an hour?** If not, it
is a `Warning`. If everything is an `Error`, nothing is, and the alert gets muted — which is how a
real outage goes unnoticed.

## What must never be logged

```csharp
// ✗ Whatever is in that object is now in your log backend, replicated, for ninety days.
logger.LogInformation("Login attempt {@Request}", request);
```

Passwords, tokens, card numbers, IBANs, addresses, full names. Under GDPR that is a processing
activity you have probably not documented, and in Italy it is the kind of thing a *DPO* asks about
directly.

**Log identifiers, not people.** `CustomerId`, not name and email. And never serialise a whole
request or entity with `{@Object}` unless you know every field on it — and can promise you will know
every field somebody adds next year.

Serilog destructuring policies can redact by property name, and that is worth configuring. It is a
safety net, not a substitute for not logging the thing.

## The other two mistakes

**Log and rethrow at every layer.**

```csharp
catch (Exception ex) { logger.LogError(ex, "Failed"); throw; }
```

One failure, five stack traces, and an on-call engineer counting incidents that are all the same
incident. Log **once**, at the boundary that decides what the caller sees — the global exception
handler. And `throw;`, never `throw ex;`, which resets the stack trace to the line you are standing
on.

**Logging inside a tight loop.** A million-iteration loop with a log line is a self-inflicted denial
of service on your log backend and a genuinely surprising bill. Log the summary, not the iteration.

## Sinks and cost

Console in development, plus a real backend in production — Seq, Application Insights, Loki. The
`Serilog.Sinks.OpenTelemetry` package sends to anything speaking OTLP, which keeps the backend a
configuration choice rather than a rewrite ([section 3](README.md#3-opentelemetry)).

Two operational realities worth knowing before the invoice arrives: **ingestion is usually billed by
volume**, so `Debug` in production is expensive as well as noisy; and **sinks must be asynchronous
and buffered**, or a slow log backend becomes your API's latency.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
curl -i http://localhost:5000/api/orders/00000000-0000-0000-0000-000000000000
```

Take the `traceId` from the Problem Details body and grep the console for it: the endpoint, the
handler, the EF query, all carrying the same id.

Then the experiment that settles the argument — change one call in `LoggingBehavior` from a message
template to string interpolation and restart. The line still reads perfectly to a human, and the
structured fields are gone.

## What to remember

- Message templates with named placeholders. Never interpolation. Enable CA2254.
- The template is the event's identity; interpolation destroys grouping and counting.
- Placeholder names are field names — PascalCase, and stable.
- `BeginScope` puts the correlation id on every line inside, including the framework's own.
- `Activity.Current.TraceId` is free and crosses service boundaries.
- Return the trace id to the caller in Problem Details.
- Log identifiers, never people. No passwords, tokens, IBANs or addresses.
- Log once, at the boundary. `throw;` not `throw ex;`. Never log in a tight loop.
- If everything is an Error, nothing is.

**Code:** [`LoggingBehavior.cs`](../../src/LogiFlow.Application/Behaviors/LoggingBehavior.cs) ·
[`GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs)

**Next:** [3. OpenTelemetry](README.md#3-opentelemetry)
