using System.Net.Http.Headers;
using System.Net.Http.Json;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LogiFlow.Api.IntegrationTests;

/// <summary>
/// Boots the real application in-process against a throwaway database on a locally installed
/// SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a real database instead of the in-memory provider?</b> Because
/// <c>UseInMemoryDatabase</c> is not a database — it is a LINQ-to-Objects shim wearing a
/// DbContext costume. It has no schema, so it happily accepts a 500-character string in an
/// <c>nvarchar(20)</c> column. It has no constraints, so unique indexes and CHECK constraints
/// silently do nothing. It cannot do transactions, raw SQL, or <c>rowversion</c> concurrency.
/// </para>
/// <para>
/// Every one of those gaps is a place where your tests pass and production fails. Microsoft's
/// own documentation now recommends against it for exactly this reason.
/// </para>
/// <para>
/// <b>Why not Testcontainers?</b> It is an excellent library and this suite used it until the
/// repository moved off Docker. The trade is explicit: a container gives you a guaranteed-clean
/// SQL Server pinned to an exact build, at the cost of requiring a running Docker daemon on
/// every machine that runs the tests. Pointing at the SQL Server you already have installed
/// removes that requirement and takes seconds off each run — and gives up the version pinning,
/// so a test that fails only on your machine now genuinely might be your SQL Server build.
/// The isolation is preserved the other way instead: every run creates its OWN database
/// (<c>LogiFlow_Test_{guid}</c>) and drops it afterwards, so runs cannot see each other and a
/// crashed run leaves at most one stray database behind.
/// </para>
/// <para>
/// <b>Pointing it somewhere else:</b> set the <c>LOGIFLOW_TEST_SQL</c> environment variable to
/// any SQL Server connection string — a named instance, LocalDB, a build agent's server, or a
/// container you started yourself. Whatever <c>Initial Catalog</c> it carries is ignored; the
/// per-run database name always wins.
/// </para>
/// <para>
/// <b>How the wiring works:</b> <see cref="WebApplicationFactory{TEntryPoint}"/> runs the real
/// <c>Program.cs</c> — the same DI registrations, middleware pipeline and endpoints as
/// production — then <see cref="ConfigureWebHost"/> swaps only the connection string. Nothing
/// else is stubbed, so an integration test failing means the application is genuinely broken.
/// </para>
/// Covered in: <c>course/module-12-testing/README.md</c> section 4, "Integration tests - and why not the in-memory provider"
/// </remarks>
public sealed class LogiFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // A fresh database per run, named with a GUID so two runs - or a run and a colleague's run
    // against a shared server - cannot collide. Created by MigrateAsync below and dropped in
    // DisposeAsync.
    private readonly string _database = $"LogiFlow_Test_{Guid.NewGuid():N}";

    /// <summary>
    /// Where to find SQL Server. Defaults to the local default instance using Windows
    /// Authentication, which is what a stock installation gives you and requires no secret.
    /// </summary>
    private const string DefaultServer =
        "Server=localhost;Integrated Security=True;TrustServerCertificate=True;Encrypt=True";

    private string ConnectionString => new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("LOGIFLOW_TEST_SQL") is { Length: > 0 } configured
            ? configured
            : DefaultServer)
    {
        // Overwrites whatever database the base string named. The per-run name is not optional:
        // it is the isolation.
        InitialCatalog = _database,
    }.ConnectionString;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development so the dev-token endpoint is mapped and we can authenticate.
        builder.UseEnvironment(Environments.Development);

        // UseSetting, not just the in-memory source below. Under minimal hosting the app reads
        // ConnectionStrings:SqlServer while Program.cs is still running, before the test's
        // AddInMemoryCollection is layered on - so without this the override loses to
        // appsettings.Development.json and the tests quietly talk to the REAL LogiFlow
        // database instead of the throwaway one. That is the difference between a test suite
        // and a data-loss incident, so it is not a subtle detail.
        builder.UseSetting("ConnectionStrings:SqlServer", ConnectionString);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = ConnectionString,

                // Blank: fall back to the in-memory distributed cache. Redis would add startup
                // time and test nothing this suite is about.
                ["ConnectionStrings:Redis"] = string.Empty,

                ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-bytes-long",
                ["Jwt:Issuer"] = "logiflow",
                ["Jwt:Audience"] = "logiflow-api",
            });
        });

        builder.ConfigureServices(services =>
        {
            // The outbox processor polls every 10 seconds forever, and the email delivery worker
            // every 5. In a test host both are noise and a source of flakiness - they hold a
            // DbContext while assertions run, and the delivery worker will happily CLAIM the
            // very rows EmailQueueTests is about to claim, which is a race the test would lose
            // intermittently and for reasons that look like a bug in the queue.
            //
            // Removing them is not "testing something other than production": both are covered
            // by targeted tests that drive them directly, which is also the only way to assert
            // on a background loop without sleeping.
            string[] backgroundWorkers = ["OutboxProcessor", "EmailDeliveryService"];

            foreach (ServiceDescriptor worker in services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType is not null
                    && backgroundWorkers.Contains(d.ImplementationType.Name, StringComparer.Ordinal))
                .ToList())
            {
                services.Remove(worker);
            }
        });
    }

    // ── xUnit lifecycle, implemented EXPLICITLY ─────────────────────────────────────────
    // xUnit v2's IAsyncLifetime declares Task-returning methods, while
    // WebApplicationFactory<T> already has `public override ValueTask DisposeAsync()` from
    // IAsyncDisposable. Two members with the same name and different return types cannot both
    // be implicit - the compiler reports CS0738.
    //
    // Explicit interface implementation gives each contract its own method. This is exactly
    // what explicit implementation is for, and it comes up whenever a class has to satisfy two
    // interfaces that disagree on a signature.

    /// <summary>Creates the throwaway database and applies migrations. Called once by xUnit.</summary>
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Touching Services forces the host to build. Program.cs also migrates in Development,
        // but doing it explicitly here means a schema failure surfaces as a clear startup error
        // rather than a confusing failure inside whichever test happens to run first.
        //
        // MigrateAsync CREATES the database when it does not exist - it connects to master to
        // do so - which is why nothing here issues a CREATE DATABASE of its own.
        using IServiceScope scope = Services.CreateScope();
        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        await context.Database.MigrateAsync();
    }

    /// <summary>Drops the throwaway database and disposes the host. Called once by xUnit.</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        // Order matters. Drop the database FIRST, while the host - and therefore the DbContext
        // that knows how to reach it - is still alive.
        //
        // EnsureDeletedAsync is doing more than DROP DATABASE here: the SQL Server provider
        // clears the connection pool and puts the database into SINGLE_USER WITH ROLLBACK
        // IMMEDIATE first. Without that, a pooled connection still parked on the database is
        // enough to make the drop fail with "database is currently in use" - the classic
        // half-hour of confusion when people hand-roll this teardown.
        try
        {
            using (IServiceScope scope = Services.CreateScope())
            {
                LogiFlowDbContext context =
                    scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();

                await context.Database.EnsureDeletedAsync();
            }
        }
        catch (SqlException)
        {
            // A failed cleanup must not turn a green run red. The cost of swallowing this is
            // one stray LogiFlow_Test_* database on the server, which is why the name carries
            // that prefix: they are trivial to find and drop in bulk.
        }

        await DisposeAsync();
    }

    /// <summary>Creates a client carrying a bearer token with the given roles.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(params string[] roles)
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dev/token",
            new { email = "test@logiflow.dev", roles = roles.Length == 0 ? ["Admin"] : roles });

        response.EnsureSuccessStatusCode();

        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        return client;
    }

    /// <summary>Runs an action against a scoped <see cref="LogiFlowDbContext"/>.</summary>
    /// <remarks>
    /// Lets a test assert against the database directly — "did the row actually change?" —
    /// rather than trusting the API's own read endpoint to tell the truth about its own write.
    /// </remarks>
    public async Task WithDbContextAsync(Func<LogiFlowDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using IServiceScope scope = Services.CreateScope();
        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();

        await action(context);
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresInSeconds);
}

/// <summary>
/// Shares one database across every test class in the collection.
/// </summary>
/// <remarks>
/// Without this, xUnit creates a fresh fixture per test class — and a fresh database, with a
/// full migration run, with it. Three test classes would mean three schema builds and a lot of
/// dead time. The trade-off is that classes in the collection share state and cannot run in
/// parallel, which is why each test below creates its own data rather than relying on a shared
/// fixture.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LogiFlowApiFactory>
{
    /// <summary>The collection name referenced by <c>[Collection]</c>.</summary>
    public const string Name = "LogiFlow API";
}
