using System.Net;
using System.Net.Http.Json;
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
/// A host with the auth rate limit turned down to three a minute.
/// </summary>
/// <remarks>
/// A separate factory rather than a setting on the shared one, because the shared one has the
/// limit raised so that the rest of the suite is not throttling itself. Raising a limit in
/// tests is fine; raising it and then never proving it works is how a defence quietly stops
/// being one — so it gets its own fixture.
/// </remarks>
public sealed class ThrottledAcademyFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Academy:Jwt:SigningKey"] = "a-test-signing-key-that-is-long-enough-32",
                ["Academy:AuthRequestsPerMinute"] = "3",
            }));

        // Its own in-memory database, so it cannot see the shared fixture's data — and so the
        // suite leaves no .db files behind. A file-backed connection string would have worked
        // and would have littered the working directory with one file per run.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AcademyDbContext>>();
            services.RemoveAll<AcademyDbContext>();

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<AcademyDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}

/// <summary>The per-address limit on sign-in attempts.</summary>
/// <param name="factory">A host configured with a very low limit.</param>
public sealed class RateLimitTests(ThrottledAcademyFactory factory) : IClassFixture<ThrottledAcademyFactory>
{
    [Fact]
    public async Task Repeated_sign_in_attempts_from_one_address_are_throttled()
    {
        // The per-account lockout in AuthEndpoints stops someone grinding ONE account. This
        // stops one address spraying one password across MANY accounts, which the lockout
        // cannot see at all — different attacks, different defences.
        using HttpClient client = factory.CreateClient();
        var url = new Uri("/api/auth/login", UriKind.Relative);

        var codes = new List<HttpStatusCode>();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                url,
                new { username = $"spray-{attempt}", password = "guessing-a-password" },
                TestClientExtensions.Json);
            codes.Add(response.StatusCode);
        }

        codes.ShouldContain(HttpStatusCode.TooManyRequests);
        codes.Take(3).ShouldAllBe(c => c == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_health_check_is_not_rate_limited()
    {
        // The limiter is a policy on the /api/auth group, not a global one. If it were global,
        // a burst of failed sign-ins would also take out the endpoint the load balancer polls —
        // turning an attack on one form into an outage of the whole service.
        using HttpClient client = factory.CreateClient();
        var url = new Uri("/api/health", UriKind.Relative);

        for (int i = 0; i < 8; i++)
        {
            using HttpResponseMessage response = await client.GetAsync(url);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
