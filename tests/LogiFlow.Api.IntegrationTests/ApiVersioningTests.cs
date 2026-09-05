using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LogiFlow.Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace LogiFlow.Api.IntegrationTests;

/// <summary>
/// Versioning: the versioned routes, the compatibility rewrite that keeps the old ones alive,
/// and the headers that tell a caller which is which.
/// </summary>
/// <remarks>
/// The rewrite is the part worth testing over real HTTP rather than in isolation, because its
/// failure mode is entirely about middleware ORDER — and order is exactly what a unit test of
/// the middleware in isolation cannot see. The first version of this feature 404'd every legacy
/// URL, because <c>WebApplication</c> inserts <c>UseRouting</c> at the top of the pipeline
/// unless you call it yourself, so the path was rewritten after the endpoint had already been
/// chosen. Nothing threw. Every one of these tests would have caught it.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ApiVersioningTests(LogiFlowApiFactory factory)
{
    // ── The rewrite decision, in isolation ───────────────────────────────────────────────
    // Cheap, exhaustive, and covers the cases nobody thinks to send through a server.

    [Theory]
    [InlineData("/api/orders", "/api/v1/orders")]
    [InlineData("/api/orders/", "/api/v1/orders/")]
    [InlineData("/api/reports/sales", "/api/v1/reports/sales")]
    [InlineData("/API/ORDERS", "/api/v1/ORDERS")]
    public void Unversioned_api_paths_are_rewritten_onto_v1(string path, string expected)
    {
        ApiVersioning.TryRewrite(new PathString(path), out PathString rewritten).ShouldBeTrue();
        rewritten.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("/api/v1/orders")]     // already versioned
    [InlineData("/api/v2/reports")]    // already versioned
    [InlineData("/api/dev/token")]     // not a resource
    [InlineData("/api")]               // nothing to rewrite
    [InlineData("/health/ready")]      // not the API at all
    [InlineData("/scalar/v1")]         // documentation, and note it CONTAINS a version segment
    public void Everything_else_is_left_alone(string path)
    {
        ApiVersioning.TryRewrite(new PathString(path), out _).ShouldBeFalse();
    }

    // ── Over real HTTP ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_versioned_route_serves_the_same_endpoint_as_the_legacy_one()
    {
        HttpClient client = await factory.CreateAuthenticatedClientAsync("Admin");

        HttpResponseMessage versioned = await client.GetAsync("/api/v1/orders");
        HttpResponseMessage legacy = await client.GetAsync("/api/orders");

        versioned.StatusCode.ShouldBe(HttpStatusCode.OK);
        legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_legacy_route_says_it_is_deprecated_and_when_it_stops_working()
    {
        HttpClient client = await factory.CreateAuthenticatedClientAsync("Admin");

        HttpResponseMessage response = await client.GetAsync("/api/orders");

        // RFC 8594. A removal announced only in a changelog is a removal nobody was told about.
        response.Headers.GetValues("Deprecation").ShouldContain("true");
        response.Headers.GetValues("Sunset").ShouldNotBeEmpty();
        response.Headers.GetValues("Link").First().ShouldContain("rel=\"successor-version\"");
    }

    [Fact]
    public async Task A_versioned_route_is_not_marked_deprecated()
    {
        HttpClient client = await factory.CreateAuthenticatedClientAsync("Admin");

        HttpResponseMessage response = await client.GetAsync("/api/v1/orders");

        response.Headers.Contains("Deprecation").ShouldBeFalse();
        response.Headers.GetValues("api-supported-versions").First().ShouldBe("v1, v2");
    }

    [Fact]
    public async Task V1_returns_a_bare_array_and_v2_returns_an_envelope()
    {
        HttpClient client = await factory.CreateAuthenticatedClientAsync("Analyst");

        const string query = "?from=2024-01-01T00:00:00Z&to=2025-01-01T00:00:00Z&period=Monthly";

        // This is the assertion the whole versioning feature exists to make possible: the same
        // data, two shapes, both live, neither breaking the other's callers.
        JsonDocument v1 = await GetJsonAsync(client, "/api/v1/reports/sales" + query);
        v1.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);

        JsonDocument v2 = await GetJsonAsync(client, "/api/v2/reports/sales" + query);
        v2.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        v2.RootElement.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Array);
        v2.RootElement.GetProperty("count").GetInt32()
            .ShouldBe(v2.RootElement.GetProperty("data").GetArrayLength());
        v2.RootElement.TryGetProperty("totalRevenue", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_version_is_a_404_rather_than_a_silent_fallback()
    {
        HttpClient client = await factory.CreateAuthenticatedClientAsync("Admin");

        // `v9` is not in the supported list, so it is treated as a resource name and rewritten
        // to /api/v1/v9/orders, which matches nothing. Failing loudly beats quietly serving v1
        // to a client that explicitly asked for something else.
        HttpResponseMessage response = await client.GetAsync("/api/v9/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
    }
}
