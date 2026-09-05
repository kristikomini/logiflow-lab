using System.Security.Claims;
using System.Threading.RateLimiting;
using LogiFlow.Api.Endpoints;
using LogiFlow.Api.Grpc;
using LogiFlow.Api.Infrastructure;
using LogiFlow.Api.RealTime;
using LogiFlow.Application;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Infrastructure;
using LogiFlow.Infrastructure.Persistence;
using LogiFlow.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;

// ═════════════════════════════════════════════════════════════════════════════════════════
//  THE COMPOSITION ROOT
//
//  The single place in the application where concrete types are chosen. Everything else
//  depends on interfaces and receives them through constructors. That is the whole point of
//  dependency inversion, and this file is where the arrow finally turns around.
//
//  Read it top to bottom - the ORDER of the middleware pipeline near the bottom is not
//  cosmetic, and getting it wrong causes bugs that look like anything but a wiring mistake.
//
//  Covered in: course/module-10-cross-cutting/01-dependency-injection.md
// ═════════════════════════════════════════════════════════════════════════════════════════

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── Is there anywhere to send telemetry? ─────────────────────────────────────────────────
// Empty or missing means "no collector running" - the case when you are working without the
// docker-compose stack (see the `no-docker` launch profile). Everything OTLP below is then
// skipped, rather than exporting into a socket nobody is listening on and filling the console
// with retry failures that look like real errors.
//
// Note this is a CAPABILITY check, not an environment check. Asking "is a collector configured?"
// keeps working when someone runs the dashboard locally or points Development at a shared one;
// asking "is this Development?" would not.
bool otlpEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["Otlp:Endpoint"]);

// ── Logging, configured FIRST ────────────────────────────────────────────────────────────
// Before anything else, so a failure during the rest of startup is actually logged. A
// misconfigured database that throws at line 60 is invisible if logging is set up at line 80.
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "LogiFlow.Api")
        .WriteTo.Console();

    if (!otlpEnabled)
    {
        return;
    }

    // Logs go out over OTLP to the same collector as traces and metrics, so a log line and the
    // trace it belongs to can be correlated by TraceId in one UI. A vendor-specific sink (Seq,
    // Datadog, Splunk) would work too - and would tie your application code to that vendor.
    configuration.WriteTo.OpenTelemetry(otlp =>
    {
        otlp.Endpoint = context.Configuration["Otlp:Endpoint"]!;
        otlp.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
        otlp.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "LogiFlow.Api",
        };
    });
});

// ── The three layers ─────────────────────────────────────────────────────────────────────
// Each owns its own registrations. This file knows the layers exist; it does not know what is
// inside them.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddLogiFlowAuth(builder.Configuration);

// ── Real-time ────────────────────────────────────────────────────────────────────────────
ISignalRServerBuilder signalR = builder.Services.AddSignalR(options =>
{
    // Development only. The default hides exception messages from clients, which is right in
    // production — an exception message is an information leak — and maddening while building
    // a client that is getting an error with no text in it.
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// THE BACKPLANE, and the reason it is a capability check rather than an environment check.
//
// A SignalR connection lives in ONE process's memory. Two instances behind a load balancer, and
// an event handled by instance A reaches nobody connected to instance B — silently, with a
// perfectly healthy log on both. Redis fixes it by fanning every message out to every instance.
//
// So: configured Redis, backplane. No Redis, single process, which is correct and complete for
// one instance and is exactly what `docker compose --profile app` lets you break on purpose by
// running two. Same reasoning as the cache and the OTLP exporter above — ask what is available,
// not what environment you are in.
string? redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
{
    signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix =
        StackExchange.Redis.RedisChannel.Literal("logiflow"));
}

// ── gRPC ─────────────────────────────────────────────────────────────────────────────────
// The internal, service-to-service surface. Same query handlers as REST, different wire.
// Covered in: course/module-25-distributed-systems/07-grpc.md
builder.Services.AddGrpc(options =>
{
    // Same reasoning as SignalR above: a detailed error is a leak in production and the only
    // useful thing on the screen while you are building a client.
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Registered AFTER AddInfrastructure, which registers a logging publisher for the same
// interface. The last registration wins for a single resolve, so the outbox now pushes to
// connected browsers — and Infrastructure still has no idea SignalR exists.
builder.Services.AddScoped<IIntegrationEventPublisher, SignalRIntegrationEventPublisher>();

// ── Error handling ───────────────────────────────────────────────────────────────────────
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Supplies the standard fields (type, title, status, traceId) so every error response - whether
// from our handler or from the framework's own 404s - has the same shape.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance =
            $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

        context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
    });

// ── OpenAPI ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Output caching ───────────────────────────────────────────────────────────────────────
// HTTP-level caching, distinct from the query-level Redis cache in CachingBehavior. This one
// stops a request reaching the application at all; that one stops it reaching the database.
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromSeconds(30)));
});

// ── Rate limiting ────────────────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned per authenticated user, falling back to IP for anonymous callers. A single
    // global limiter would let one noisy client exhaust the budget for everyone - which is a
    // denial of service you built yourself.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // ── The claim to partition on, and a bug this used to have ───────────────────────
        //
        // This read `User.Identity?.Name` and fell back to the IP. That looks right and was
        // wrong: `Identity.Name` is populated from ClaimTypes.Name, and the tokens this API
        // issues carry `sub`, `nameidentifier`, `email` and roles — no `name`. So the first
        // expression was ALWAYS null, every authenticated caller fell through to the IP, and
        // behind a load balancer or a corporate NAT that is one partition for everybody. The
        // comment above promises this cannot happen; for a year the code did exactly it.
        //
        // Nothing failed. Every test passed. It surfaced when labs/Labs.LoadTests pointed 60
        // distinct identities at the API and 96% of the requests came back 429 — which is the
        // argument for owning a load test in one paragraph.
        //
        // NameIdentifier (`sub`) is the claim that actually identifies a caller and is present
        // on every token this API accepts. Name is kept as a fallback for tokens from an
        // identity provider that sets it instead.
        string partitionKey =
            httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.Identity?.Name
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,   // reject immediately rather than queue; a queued request still holds a thread
        });
    });

    // Tell the client WHEN to retry. Without Retry-After a well-behaved client has to guess,
    // and most guess "immediately".
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Too many requests. Retry in 60 seconds.", code = "RateLimit.Exceeded" },
            cancellationToken);
    };
});

// ── Health checks ────────────────────────────────────────────────────────────────────────
builder.Services
    .AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("SqlServer")!,
        name: "sql-server",
        tags: ["ready"]);

// ── OpenTelemetry ────────────────────────────────────────────────────────────────────────
// The instrumentation is registered either way - Activity and Meter objects are created, and
// anything that reads them in-process still works. Only the EXPORTER is conditional, because
// exporting is the part that needs somewhere to export to.
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("LogiFlow.Api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(o => o.RecordException = true)
            .AddHttpClientInstrumentation();

        if (otlpEnabled)
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();

        if (otlpEnabled)
        {
            metrics.AddOtlpExporter();
        }
    });

WebApplication app = builder.Build();

// ═════════════════════════════════════════════════════════════════════════════════════════
//  THE MIDDLEWARE PIPELINE
//
//  Order is behaviour. Each piece wraps everything registered after it, so the request travels
//  DOWN the list and the response travels back UP. Common ordering bugs:
//
//    * UseAuthorization before UseAuthentication -> nobody is ever authenticated, so every
//      authorized endpoint returns 401 no matter what token is sent.
//    * Exception handling registered late -> exceptions thrown by earlier middleware escape
//      it entirely and surface as an unformatted 500.
//    * Rate limiting BEFORE authentication -> the limiter cannot see who the caller is, so
//      every partition key collapses to the IP and one NAT is one budget. Putting it after
//      costs a token validation on a flood, which is the cheaper of the two mistakes. See the
//      long note at UseRateLimiter below; a load test is what settled it.
// ═════════════════════════════════════════════════════════════════════════════════════════

// FIRST: catches everything downstream.
app.UseExceptionHandler();

// Turns a bare 404 or 405 from the routing layer into a Problem Details document, so clients
// get one error shape from the entire API rather than two.
app.UseStatusCodePages();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
    };
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Scalar rather than Swagger UI: it is what the .NET 9+ templates moved to, it renders the
    // same OpenAPI document, and it generates working client snippets.
    app.MapScalarApiReference(options => options
        .WithTitle("LogiFlow API")
        .WithTheme(ScalarTheme.BluePlanet));
}
else
{
    // HSTS tells browsers to refuse plain HTTP for this host for the next N days. Development
    // is excluded because it would poison localhost for every other project you run.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Rewrites the pre-versioning paths (/api/orders) onto /api/v1 and stamps the response with
// Deprecation and Sunset.
app.UseUnversionedApiCompatibility();

// EXPLICIT UseRouting, and the position of this line is the whole point.
//
// WebApplication inserts UseRouting at the TOP of the pipeline automatically — before the
// first middleware you register — unless you call it yourself, in which case yours is used
// where you put it. Automatic placement is right almost always and wrong here: routing
// selects the endpoint by matching Request.Path, so a rewrite that runs after it changes a
// path nobody will look at again. The symptom is not an error. It is a 404 on every legacy
// URL, from middleware that ran and did exactly what it was told.
//
// The Academy service has the same line for the same reason, guarding static files instead
// (see its Program.cs, and AuthFlowTests.Every_public_page_and_asset_is_served_anonymously).
// Two unrelated services, one trap.
app.UseRouting();

// Authentication (who are you?) strictly before authorization (may you?).
app.UseAuthentication();
app.UseAuthorization();

// ── Rate limiting, AFTER authentication, and this line moved on purpose ──────────────────
//
// It used to sit above UseAuthentication, with the comment "a flood of unauthenticated
// requests should be cheap to reject". That reasoning is sound and the placement made the
// limiter's own partitioning impossible: `httpContext.User` is populated BY UseAuthentication,
// so a limiter running before it sees an empty ClaimsPrincipal on every request, always falls
// through to the IP, and gives every caller behind one NAT or load balancer a single shared
// budget. The comment above the limiter promised per-user partitioning; the position
// guaranteed per-IP. Both were written by someone who had read the docs.
//
// It took a load test to see it. Every test passed either way, because a test that makes ten
// requests never reaches a limit — labs/Labs.LoadTests pointed 60 identities at the API and
// 96% came back 429, which is the shape of one partition, not sixty.
//
// The cost of the fix is exactly what the old comment warned about: a flood of garbage tokens
// now pays JWT validation before it is rejected. That is a real cost and it is the smaller
// one — a signature check on a malformed token fails in microseconds, while a rate limiter
// that cannot tell two users apart is not doing the job it was added for. Volumetric flood
// protection belongs at the edge (nginx, Cloudflare, an API gateway) where it can drop packets
// without a process at all; this limiter's job is fairness between authenticated callers.
//
// Covered in: course/module-15-aspnetcore-in-depth/ — "order is behaviour", with a real one.
app.UseRateLimiter();

// After authentication, so context.User is populated by the time it is copied across.
app.UseMiddleware<CurrentUserMiddleware>();

app.UseOutputCache();

// ── Endpoints ────────────────────────────────────────────────────────────────────────────
//
// Every resource is mapped onto a VERSION GROUP rather than straight onto the app, so the
// version is a property of the group and not something 22 route strings have to agree about.
//
// The original unversioned paths (/api/orders) still work: UseUnversionedApiCompatibility
// above rewrites them onto v1 before routing runs, and marks the response deprecated with a
// Sunset date. One set of endpoints, two sets of URLs.
//
// Covered in: course/module-15-aspnetcore-in-depth/02-api-versioning.md
RouteGroupBuilder v1 = app.MapApiVersion("v1");
v1.MapOrderEndpoints();
v1.MapProductEndpoints();
v1.MapInventoryEndpoints();
v1.MapShipmentEndpoints();
v1.MapReportingEndpoints();

// v2 exists to carry ONE changed shape, not a second copy of the API. Anything a caller does
// not find here it finds on v1, which is what makes a version bump cheap enough to actually
// do — the alternative, duplicating 22 endpoints to change one, is why teams put it off until
// the change is a rewrite.
RouteGroupBuilder v2 = app.MapApiVersion("v2");
v2.MapReportingEndpointsV2();

// The live order-tracking hub. Deliberately NOT under /api: it is not REST, it has no verbs and
// no resources, and putting it there would drag it into the versioning rewrite above. It is also
// the path the JWT handler special-cases to read the token from the query string, because a
// browser cannot set a header on a WebSocket handshake — see AuthenticationSetup.
app.MapHub<OrderTrackingHub>("/hubs/orders");

// gRPC endpoints. Note there is no route template: the path comes from the .proto - it is
// /logiflow.orders.v1.OrderLookup/GetOrder, built from the package and service names. That is
// also why gRPC does not appear in the OpenAPI document or in Scalar: it is not HTTP-shaped, and
// no amount of tooling makes it browsable.
//
// It needs HTTP/2 END TO END. Kestrel negotiates it over TLS automatically, and over plain HTTP
// it must be told - see Kestrel:EndpointDefaults:Protocols in appsettings.json. A proxy that
// downgrades to HTTP/1.1 breaks this with an error that names neither HTTP/2 nor the proxy.
app.MapGrpcService<OrderLookupService>();

// Liveness: is the process up? Deliberately has NO dependency checks - if this fails,
// the orchestrator restarts the pod, and restarting will not fix a broken database.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

// Readiness: can it serve traffic? Checks dependencies. A failure here removes the instance
// from the load balancer WITHOUT restarting it - exactly right while the database recovers.
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapDevTokenEndpoint();

    await ApplyMigrationsAndSeedAsync(app);
}

await app.RunAsync();

// ═════════════════════════════════════════════════════════════════════════════════════════

// Applies pending migrations and seeds sample data. DEVELOPMENT ONLY.
//
// Never do this in production. It is convenient locally and dangerous in a real deployment:
// with several instances starting at once they race to migrate the same database, and the
// application ends up needing schema-altering permissions it should not hold at runtime.
// Real deployments run migrations as a separate, single-instance pipeline step - see
// course/module-13-deployment/04-migrations-in-ci.md.
//
// (A plain comment, not an XML doc comment: this is a local function, and XML documentation
// comments are only valid on type and member declarations. CS1587 is the compiler saying so.)
static async Task ApplyMigrationsAndSeedAsync(WebApplication app)
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

    LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
    ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Applying pending migrations");
        await context.Database.MigrateAsync();

        DatabaseSeeder seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database initialisation failed; the API cannot serve requests");
        throw;
    }
}

/// <summary>
/// Exposes the implicit <c>Program</c> class to the integration test project.
/// </summary>
/// <remarks>
/// Top-level statements compile to an <c>internal</c> class named <c>Program</c>, which
/// <c>WebApplicationFactory&lt;T&gt;</c> cannot see from another assembly. Declaring it
/// <c>public partial</c> here is the documented workaround, and it is why
/// <c>LogiFlow.Api.IntegrationTests</c> can boot the real application in-process.
/// </remarks>
public partial class Program;
