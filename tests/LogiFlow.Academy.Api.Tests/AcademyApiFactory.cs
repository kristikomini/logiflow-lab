using System.Net.Http.Json;
using System.Text.Json;
using LogiFlow.Academy.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LogiFlow.Academy.Api.Tests;

/// <summary>
/// Boots the real application in-process, against a private SQLite database.
/// </summary>
/// <remarks>
/// <b>Not the EF Core in-memory provider.</b> That provider is not a relational database — no
/// real SQL, no constraints, no transactions — so it happily passes tests that fail against
/// anything you would deploy. SQLite in <c>:memory:</c> mode IS a relational engine, runs with
/// no Docker and no server, and is the right tool for a service whose production provider is
/// pluggable anyway.
///
/// The connection is held open for the fixture's lifetime on purpose: a SQLite in-memory
/// database exists only while at least one connection to it is open, so closing it between
/// commands would silently reset the schema.
/// </remarks>
public sealed class AcademyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection? _connection;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // A fixed key, because these tests are not Development and the application
                // deliberately refuses to invent one outside it. Being forced to supply it here
                // is the ValidateOnStart rule doing its job.
                ["Academy:Jwt:SigningKey"] = "a-test-signing-key-that-is-long-enough-32",
                ["Academy:Jwt:AccessMinutes"] = "15",
                ["Academy:ChapterCount"] = "39",
                // The whole suite arrives from one "address", so the production limit
                // of ten a minute would throttle the tests rather than an attacker.
                // RateLimitTests uses its own factory to prove the limit still works.
                ["Academy:AuthRequestsPerMinute"] = "10000",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AcademyDbContext>>();
            services.RemoveAll<AcademyDbContext>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<AcademyDbContext>(options => options.UseSqlite(_connection));
        });
    }

    // ── xUnit lifecycle, implemented EXPLICITLY ─────────────────────────────────────────
    // xUnit v2's IAsyncLifetime declares Task-returning methods, and WebApplicationFactory<T>
    // already has `public override ValueTask DisposeAsync()` from IAsyncDisposable. Two members
    // with the same name and different return types cannot both be implicit — CS0738. Explicit
    // interface implementation gives each contract its own method, which is precisely what
    // explicit implementation is for. (LogiFlowApiFactory in the other test project hits the
    // same wall for the same reason.)

    /// <summary>Builds the host and creates the schema. Called once by xUnit.</summary>
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Touching the client forces the host to build, which runs EnsureCreated in Program.cs.
        // Doing it here means a schema failure surfaces as a clear fixture error rather than as
        // a confusing failure inside whichever test happened to run first.
        using HttpClient client = CreateClient();
        using HttpResponseMessage health = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        health.EnsureSuccessStatusCode();
    }

    /// <summary>Tears the host down and closes the in-memory database. Called once by xUnit.</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}

/// <summary>Small helpers so the tests read as a story rather than as HTTP plumbing.</summary>
internal static class TestClientExtensions
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a new learner and returns the parsed auth response.</summary>
    internal static async Task<JsonElement> RegisterAsync(
        this HttpClient client, string username, string password = "a-good-long-password")
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri("/api/auth/register", UriKind.Relative),
            new { username, displayName = username, password },
            Json);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Signs in, returning the raw response so a test can assert on the status.</summary>
    internal static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string username, string password = "a-good-long-password") =>
        client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new { username, password }, Json);

    /// <summary>Puts a bearer token on the client for subsequent calls.</summary>
    internal static void Bearer(this HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Reads a string property from a JSON object.</summary>
    internal static string Str(this JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? string.Empty;
}
