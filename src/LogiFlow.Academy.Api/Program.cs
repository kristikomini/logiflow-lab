using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using LogiFlow.Academy.Api.Endpoints;
using LogiFlow.Academy.Api.Persistence;
using LogiFlow.Academy.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

// ═════════════════════════════════════════════════════════════════════════════════════════
//  LOGIFLOW ACADEMY — accounts and progress for the tutorial site in `site/`
//
//  Run it, open the address it prints, and the whole site is served from here with sign-in
//  working. No database to install, no Docker, no configuration:
//
//      dotnet run --project src/LogiFlow.Academy.Api
//
//  It is a small service on purpose, and it is meant to be READ. Everything an Italian junior
//  .NET advert asks about — minimal APIs, DI, EF Core, JWT, middleware order, CORS, rate
//  limiting, password storage — is here in about four files, at a size where the whole thing
//  fits in your head.
// ═════════════════════════════════════════════════════════════════════════════════════════

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── The clock, as a dependency ───────────────────────────────────────────────────────────
// Not DateTimeOffset.UtcNow scattered through the code. Token expiry, lockout windows and
// streaks are all time-dependent, and a test that has to wait fifteen real minutes to prove a
// token expired is a test nobody runs. TimeProvider is the .NET 8+ way to say so.
builder.Services.TryAddSingletonTimeProvider();

// ── Database ─────────────────────────────────────────────────────────────────────────────
//
//  SQLite by default so a fresh clone runs; SQL Server with one setting, because that is what
//  the region's shops actually run and because a provider you cannot switch is not really an
//  abstraction. The switch is genuine: nothing else in this project mentions either provider.
//
//      "Academy": { "Provider": "SqlServer" },
//      "ConnectionStrings": { "Academy": "Server=...;Database=LogiFlowAcademy;..." }
//
string provider = builder.Configuration["Academy:Provider"] ?? "Sqlite";
string? connectionString = builder.Configuration.GetConnectionString("Academy");

builder.Services.AddDbContext<AcademyDbContext>(options =>
{
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(
            connectionString ?? throw new InvalidOperationException(
                "Academy:Provider is SqlServer but ConnectionStrings:Academy is not set."),
            sql => sql.EnableRetryOnFailure());
    }
    else
    {
        // A file beside the application, not an in-memory database: progress has to survive a
        // restart, which is the entire point of having accounts at all.
        string dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        options.UseSqlite(connectionString ?? $"Data Source={Path.Combine(dataDirectory, "academy.db")}");
    }
});

// ── Authentication ───────────────────────────────────────────────────────────────────────
builder.Services
    .AddOptions<AcademyJwtOptions>()
    .Bind(builder.Configuration.GetSection(AcademyJwtOptions.SectionName))
    .PostConfigure(options =>
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return;
        }

        // No key configured. In Development, generate one and keep it in App_Data so that
        // restarting does not invalidate everybody's session — but never ship a key in the
        // repository, because a committed signing key lets anyone mint a token for any user.
        // Outside Development the Validate below stops the process instead.
        if (!builder.Environment.IsDevelopment())
        {
            return;
        }

        string keyPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "signing.key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        if (!File.Exists(keyPath))
        {
            File.WriteAllText(keyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        }

        options.SigningKey = File.ReadAllText(keyPath).Trim();
    })
    .Validate(
        o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
        "Academy:Jwt:SigningKey must be set to at least 32 bytes. Use an environment variable, " +
        "user-secrets or a key vault — never appsettings.json.")
    // At STARTUP, not at the first sign-in. A service that boots green and then 500s an hour
    // later on the first login is strictly worse than one that refuses to start.
    .ValidateOnStart();

builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

// Configured through the options system rather than in the AddJwtBearer callback, because the
// settings depend on ANOTHER options object. The shortcut - calling BuildServiceProvider()
// inside the callback to fetch it - is flagged by analyser ASP0000 and deserves to be: it
// builds a second container, so every singleton resolved through it is a duplicate of the one
// the application actually uses. Configure<TDep> is the supported way to say "configure this
// using that", and it resolves lazily, so the PostConfigure above has already run.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AcademyJwtOptions>>((options, academyOptions) =>
    {
        AcademyJwtOptions jwt = academyOptions.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,             // else a token from any issuer is accepted
            ValidateAudience = true,           // else a token minted for another service works here
            ValidateLifetime = true,           // else expired tokens work forever
            ValidateIssuerSigningKey = true,   // else a forged token is accepted — the important one
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // The default is five minutes of leeway, which means a fifteen-minute token is
            // really a twenty-minute token. Say what you mean.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.IncludeErrorDetails = builder.Environment.IsDevelopment();
    });

builder.Services.AddAuthorization(options =>
{
    // Secure by default: an endpoint that forgets to say anything is closed, not open.
    // Everything public — the site's static files, sign-in, the health check — opts out
    // explicitly with AllowAnonymous, which is a decision you can see in a code review.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ── CORS ─────────────────────────────────────────────────────────────────────────────────
//
//  The site is normally served BY this process, so same-origin, and CORS does not apply. It
//  matters for the other supported case: the site opened straight from disk (file://), which
//  browsers send as the literal origin "null".
//
//  AllowAnyOrigin is not used, and could not be: it is incompatible with credentials, and the
//  usual "fix" — reflecting whatever Origin arrived — is the same hole with extra steps.
string[] allowedOrigins = builder.Configuration
    .GetSection("Academy:AllowedOrigins").Get<string[]>()
    ?? ["null", "http://localhost:8791", "http://localhost:8777", "http://localhost:8803", "http://127.0.0.1:8791"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// ── Rate limiting ────────────────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-account lockout (in AuthEndpoints) stops someone grinding one account. This stops
    // one address grinding many accounts. They are different attacks and both need answering.
    //
    // Configurable because the right number depends on where this runs: ten a minute per IP is
    // sensible on the open internet and wrong behind a reverse proxy that presents every user
    // as one address — and wrong again for a test suite, where forty requests arrive from
    // "unknown" in five seconds.
    int authPermits = builder.Configuration.GetValue("Academy:AuthRequestsPerMinute", 10);

    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { detail = "Too many attempts. Try again in a minute." }, cancellationToken);
    };
});

// ── Errors, in one shape ─────────────────────────────────────────────────────────────────
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier));

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// ═════════════════════════════════════════════════════════════════════════════════════════
//  THE PIPELINE. Order is behaviour, not style.
// ═════════════════════════════════════════════════════════════════════════════════════════

app.UseExceptionHandler();      // first, so it wraps everything after it
app.UseStatusCodePages();       // framework 404s come out shaped like our own errors

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("LogiFlow Academy API")
        .WithTheme(ScalarTheme.BluePlanet));
}

// ── The site itself, before ANYTHING else ────────────────────────────────────────────────
//
//  Serving `site/` from here is what makes "several people can use it" true rather than
//  theoretical: one process, one URL, and the account button in the top bar already points at
//  the right origin.
//
//  In Development the folder is found by walking up from the content root, so the files are
//  served live from the repository and an edit is one refresh away. A published build copies
//  them into wwwroot instead.
//
//  THE POSITION MATTERS, and it took a broken "/" to prove it. Static-file middleware checks
//  whether an endpoint has already been selected for the request, and if one has, it steps
//  aside and lets the endpoint answer. With `app.UseRouting()` left implicit, WebApplication
//  inserts it at the TOP of the pipeline — so the MapFallback below was matching "/" before
//  UseDefaultFiles ever ran, the static-file middleware politely declined, and the home page
//  404ed while every other page worked.
//
//  Placing the files above an EXPLICIT UseRouting fixes it and is better anyway: the tutorial
//  is public, so a stylesheet should never pay for route matching, token validation or a
//  rate-limit permit.
string? sitePath = FindSiteFolder(builder.Environment.ContentRootPath);
if (sitePath is not null)
{
    var files = new PhysicalFileProvider(sitePath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        // The learning assets change often while the course is being written; a stale
        // quizzes-1.js served from cache would look like a bug in the site.
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate",
    });
    app.Logger.LogInformation("Serving the tutorial site from {SitePath}", sitePath);
}
else
{
    app.Logger.LogWarning(
        "No site/ folder found from {Root}; the API will run but will not serve the tutorial.",
        builder.Environment.ContentRootPath);
}

// Explicit, so that everything above runs before route matching and everything below runs
// after it. Left implicit, WebApplication decides for you — and decides differently from what
// the code above needs.
app.UseRouting();

app.UseCors();

app.UseRateLimiter();           // before authentication: rejecting a flood should be cheap

// Authentication (who are you?) strictly before authorization (may you?). Reversed, every
// protected endpoint returns 401 no matter what token is sent — and the symptom looks nothing
// like the cause, which is why it is worth being able to recite.
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ────────────────────────────────────────────────────────────────────────────
app.MapAuthEndpoints();
app.MapProfileEndpoints();

// Anything that is neither a static file nor a known route is a 404, not a 401.
//
// Without this, the fallback authorization policy challenges every request that matched no
// endpoint — so a mistyped chapter URL, or the browser's automatic /favicon.ico probe, comes
// back as "sign in" rather than "not found". Confusing for a public tutorial, and it hides
// nothing worth hiding: the whole site is served anonymously above.
//
// The pattern is spelled out rather than left to the MapFallback() overload, whose default is
// "{*path:nonfile}" — that constraint skips any path that looks like a file, which is exactly
// /favicon.ico and /chapters/typo.html, the two cases this exists for. Static files have
// already had their chance further up the pipeline, so there is nothing left to defer to.
app.MapFallback("{*path}", () => Results.NotFound()).AllowAnonymous();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    provider,
    utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
}))
.AllowAnonymous()
.WithTags("Diagnostics")
.WithSummary("Liveness. Deliberately does not touch the database.");

// ── Schema ───────────────────────────────────────────────────────────────────────────────
//
//  EnsureCreated, NOT Migrate, and NOT because migrations are hard.
//
//  This service is a single instance with one small schema and no upgrade history to preserve;
//  EnsureCreated gives a running database on first launch with no `dotnet ef` install and no
//  Migrations folder to keep in step. That trade is right HERE and wrong almost everywhere
//  else — see course/module-13-deployment/README.md section 3, where the same shortcut in a
//  multi-instance deployment is exactly what causes a startup race and forces the runtime
//  account to hold schema-altering permissions.
//
//  If this ever grows a second instance or a schema that has to evolve without losing data,
//  the fix is real migrations run as a separate pipeline step, not a bigger EnsureCreated.
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    AcademyDbContext db = scope.ServiceProvider.GetRequiredService<AcademyDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureSchemaIsCurrentAsync(db);
}

await app.RunAsync();

// ═════════════════════════════════════════════════════════════════════════════════════════

// EnsureCreated builds the schema only when there is no database at all. An existing file from
// an older version is left exactly as it was — so the process starts green and then throws
// "no such column: Username" on the first sign-in, which names neither the cause nor the fix.
//
// One cheap query closes that gap: touch a column the current model has and the old one did not,
// at STARTUP, and turn the provider's error into an instruction. This is not migrations by the
// back door — it cannot repair anything, and deliberately does not try. It is the honest cost of
// the EnsureCreated trade written down where somebody hits it, and the moment this service needs
// to evolve a schema without losing data is the moment that trade stops being the right one.
static async Task EnsureSchemaIsCurrentAsync(AcademyDbContext db)
{
    try
    {
        _ = await db.Users.Select(u => u.Username).FirstOrDefaultAsync();
    }
    catch (DbException ex)
    {
        throw new InvalidOperationException(
            "The Academy database exists but predates the username and recovery-code schema. " +
            "EnsureCreated cannot alter a database it did not just build, so this cannot be " +
            "repaired automatically. Delete it and start again — for the default SQLite setup " +
            "that is App_Data/academy.db (plus its -wal and -shm files). Every account and every " +
            "synced progress document in it will be lost, so export a backup from the site's " +
            "area riservata first if there is anything worth keeping.",
            ex);
    }
}

// Walks up from the content root looking for the tutorial's site/ folder, and returns null
// when it is not there.
//
// A plain comment rather than an XML one: this is a local function, and XML documentation
// comments are only legal on type and member declarations. CS1587 is the compiler saying so.
static string? FindSiteFolder(string start)
{
    DirectoryInfo? directory = new(start);
    for (int depth = 0; depth < 6 && directory is not null; depth++)
    {
        string candidate = Path.Combine(directory.FullName, "site");
        if (File.Exists(Path.Combine(candidate, "index.html")))
        {
            return candidate;
        }
        directory = directory.Parent;
    }
    return null;
}

/// <summary>Small registration helpers kept out of the pipeline above.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>Registers the system clock, unless a test has already substituted one.</summary>
    /// <param name="services">The container.</param>
    internal static IServiceCollection TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
        return services;
    }
}

/// <summary>Exposes the generated entry point to the test project.</summary>
/// <remarks>
/// Top-level statements compile to an <c>internal</c> class called <c>Program</c>, which
/// <c>WebApplicationFactory&lt;T&gt;</c> in another assembly cannot see. Declaring it
/// <c>public partial</c> is the documented workaround.
/// </remarks>
public partial class Program;
