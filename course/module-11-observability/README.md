# Module 11 — Observability

> The difference between "the site is slow" and "the `GetSalesReport` query is doing a table scan
> for customers on the Platinum tier". Almost nobody learns this properly, which makes it a
> genuine differentiator.

Open <http://localhost:18888> with the API running and click through while you read.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 2 | [Structured logging](02-structured-logging.md) | the one character that decides whether you can query your logs |

---

## 1. The three signals

| | Answers | Cost | Example |
|---|---|---|---|
| **Logs** | *what happened* in one request | high per event | "Order ORD-2026-000417 failed: insufficient stock" |
| **Metrics** | *how much / how often*, aggregated | very low | "p99 latency 340ms, error rate 0.2%" |
| **Traces** | *where the time went* across services | medium | "18ms API, 290ms SQL, 40ms Redis" |

You need all three. Metrics tell you something is wrong, traces tell you where, logs tell you
what. Reaching for logs to answer a metrics question — "how many orders failed today?" — is how
you end up with a logging bill larger than your compute bill.

```
   "something is wrong"      ──►  METRICS   cheap, aggregated, alertable        DETECT
   "where did the time go?"  ──►  TRACES    one request across all components   LOCALISE
   "what exactly happened?"  ──►  LOGS      expensive per event, full detail    DIAGNOSE

   reach for them in that order. Answering "how many orders failed today?" out of logs is
   how you end up with a logging bill larger than your compute bill.
```

---

## 2. Structured logging

The single most important habit in this module:

```csharp
logger.LogInformation($"Handled {name} in {ms}ms");        // ✗ one opaque string
logger.LogInformation("Handled {Name} in {Ms}ms", name, ms); // ✓ a message plus queryable fields
```

The second reaches your log server with `Name` and `Ms` as **separate indexed properties**, so
you can ask "every request over 500ms, grouped by name" without parsing text. With interpolation
you get a million unique strings and no way to aggregate.

Analyser **CA2254** exists to catch exactly this.

📂 [`Behaviors/LoggingBehavior.cs`](../../src/LogiFlow.Application/Behaviors/LoggingBehavior.cs)

### Log scopes

```csharp
using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>
{
    ["RequestName"] = requestName,
    ["CorrelationId"] = correlationId,
});
```

Every log line written **anywhere inside** — including deep in EF Core — carries these
properties. When a customer reports a failure at 14:32, you filter on one id and see the whole
request.

### Log levels, and what they should mean

| Level | Meaning | Wakes someone up? |
|---|---|---|
| Trace/Debug | development detail | no — usually off in production |
| **Information** | notable business events | no |
| **Warning** | expected failure, or something degraded | no, but trend on it |
| **Error** | unexpected failure needing investigation | maybe |
| **Critical** | the service cannot function | yes |

**A rejected order is a Warning, not an Error.** This matters more than it sounds:

```csharp
if (response is Result { IsFailure: true } failure)
    logger.LogWarning("{RequestName} failed with {ErrorCode}: ...", ...);
```

Logging every business rejection at Error is how teams end up ignoring their error logs entirely.
Alert fatigue is a production outage waiting to happen.

### Never log secrets

Passwords, tokens, card numbers, and — under GDPR — often names, emails and addresses.

📂 [`Infrastructure/DependencyInjection.cs`](../../src/LogiFlow.Infrastructure/DependencyInjection.cs):

```csharp
if (IsDevelopment())
    options.EnableSensitiveDataLogging();   // puts PARAMETER VALUES in the log
```

Invaluable when debugging a query; a GDPR incident in production, because customer emails and
addresses end up in your log aggregator. Guarded by an environment check, never by "I will
remember to turn it off".

---

## 3. OpenTelemetry

The vendor-neutral standard for all three signals. Instrument once, export anywhere — Grafana,
Jaeger, Honeycomb, Datadog, Application Insights. Your **application code does not change**; only
the endpoint does.

📂 [`Program.cs`](../../src/LogiFlow.Api/Program.cs)

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("LogiFlow.Api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.RecordException = true)
                       .AddHttpClientInstrumentation()
                       .AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddOtlpExporter());
```

That portability is the entire argument. A Serilog sink for Datadog would tie your code to
Datadog; OTLP does not.

### Traces

A **trace** is one request end to end; a **span** is one operation within it.

```
Trace: POST /api/orders/{id}/submit                              348ms
├─ span: SubmitOrderCommand                                      340ms
│  ├─ span: SELECT Orders + OrderLines                            12ms
│  ├─ span: ReserveStockOnOrderSubmitted                         310ms   ← there it is
│  │  └─ span: SELECT Warehouses + StockItems                    305ms
│  └─ span: SaveChanges                                           18ms
```

You cannot get that from logs. This is the signal that answers "why is it slow" in one look.

`TraceId` is what ties logs to traces, which is why the Serilog sink also exports over OTLP —
one UI, correlated.

📂 `GlobalExceptionHandler` puts `traceId` in every error response. The user quotes it to
support; support pastes it into the dashboard; the whole request appears.

---

## 4. Health checks — and why there are two

📂 [`Program.cs`](../../src/LogiFlow.Api/Program.cs)

```csharp
app.MapHealthChecks("/health/live",  new() { Predicate = _ => false });                  // no dependencies
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }); // checks SQL
```

| | Checks | Failure means |
|---|---|---|
| **Liveness** | is the process up? **nothing else** | restart the pod |
| **Readiness** | can it serve traffic? DB, cache… | remove from the load balancer, do **not** restart |

**Getting this wrong causes outages.** If liveness checks the database, a brief database blip
makes Kubernetes restart every pod simultaneously — turning a 10-second degradation into a full
outage, because restarting cannot fix a database.

```
   /health/live     is the process alive?        fails ⇒ RESTART me
                    checks NOTHING else
                    └── check the database here and one 10-second blip restarts every pod
                        at once, turning a degradation into an outage

   /health/ready    can it serve a request?      fails ⇒ take me OUT of the load balancer,
                    checks SQL Server, cache             and do NOT restart me
```

---

## 5. What to actually alert on

Alert on **symptoms users feel**, not causes:

✅ error rate above 1% for 5 minutes · p99 latency above 2s · orders submitted dropped to zero ·
outbox backlog growing
❌ CPU above 80% · a single exception · memory above 70%

High CPU is not a problem if latency is fine. `OutboxMessage.ProcessedAtUtc` is nullable rather
than a boolean flag specifically so you can alert on **how far behind** the processor is — "when"
answers strictly more questions than "whether".

**The four golden signals** (from Google's SRE book) are the standard checklist: **latency,
traffic, errors, saturation**.

---

## 6. Try it

```bash
docker compose up -d                                   # the Aspire Dashboard lives here
cd src/LogiFlow.Api && dotnet run --launch-profile docker
```

This is the one part of the course that genuinely wants a container: the dashboard is distributed as
an image and nothing else. Without it the instrumentation still runs — `Otlp:Endpoint` is empty by
default, the exporters are skipped, and Serilog keeps writing to the console — you simply have
nowhere to look at the traces. The `docker` profile is what fills that endpoint in.

Then, at <http://localhost:18888>:

1. Submit an order via `requests/logiflow.http` and find the trace. Note how much time is SQL.
2. Trigger a validation failure (quantity 0) and see it logged at **Warning**, not Error.
3. Set `Microsoft.EntityFrameworkCore.Database.Command` to `Information` and watch every query
   appear as its own span.
4. Find a request by `traceId` from an error response.

---

## 7. Golden rules

> The card. Almost nobody learns this properly, which is exactly why it is a differentiator.

1. **Metrics to detect, traces to localise, logs to diagnose.** Using logs for metrics is slow and
   expensive; using metrics for diagnosis is impossible.
2. **Log a message template plus named properties, never an interpolated string.** The first
   arrives as indexed fields you can group by; the second is a million unique strings.
3. **Use scopes for correlation.** One id on every line written anywhere inside the request,
   including deep inside EF Core.
4. **A rejected order is a Warning, not an Error.** Logging business rejections at Error is how a
   team learns to ignore its own error log, and alert fatigue is an outage waiting to happen.
5. **Never log secrets, and guard `EnableSensitiveDataLogging` with an environment check** — not
   with an intention to remember. It logs parameter values, which under GDPR includes names,
   emails and addresses.
6. **Instrument with OpenTelemetry so the vendor becomes a configuration change**, not a rewrite.
7. **Liveness must not check dependencies. Readiness must.** Backwards, and a brief database
   problem becomes a restart storm.
8. **Alert on symptoms users feel** — error rate, p99 latency, a business metric flatlining — not
   on CPU or memory. Latency, traffic, errors and saturation are the four to start from.
9. **Every alert must be actionable.** One that fires and is routinely ignored should be deleted:
   it is training you to ignore the others.
10. **Store a timestamp, not a boolean.** A nullable `ProcessedAtUtc` lets you alert on *how far
    behind* the outbox is; an `IsProcessed` flag can only say whether.

---

## 8. Interview questions

**"Logs, metrics or traces — which do you reach for?"**
Metrics to detect (cheap, aggregated, alertable). Traces to localise (where did the time go).
Logs to diagnose (what exactly happened). Using logs for metrics is expensive and slow.

**"What is structured logging and why does it matter?"**
Logging a message template plus named properties rather than a formatted string, so the log
server indexes the fields and you can query and aggregate them. Interpolation produces unique
strings you cannot group.

**"Liveness vs readiness?"**
Liveness answers "should I be restarted" and must not check dependencies. Readiness answers
"should I receive traffic" and does. Checking the database in liveness turns a brief outage into
a restart storm.

**"How would you debug a slow endpoint in production?"**
Metrics to confirm it is real and scope it to a percentile. A distributed trace to see where the
time goes. Then logs filtered by that trace id for detail. Only then look at the query plan.

**"What would you alert on?"**
User-facing symptoms — error rate, p99 latency, a business metric flatlining — not resource
usage. Every alert should be actionable; anything that fires and is routinely ignored should be
deleted.

---

## Next

→ [Module 12 — Testing](../module-12-testing/)
