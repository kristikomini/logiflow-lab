using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LogiFlow.Academy.Api.Tests;

/// <summary>
/// Storing and retrieving progress, and the concurrency rule that makes two devices safe.
/// </summary>
/// <param name="factory">The in-process host.</param>
public sealed class ProfileSyncTests(AcademyApiFactory factory) : IClassFixture<AcademyApiFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static object Document(int xp, double best = 0.9) => new
    {
        v = 1,
        xp,
        updatedAt = DateTimeOffset.UtcNow,
        streak = new { count = 3, best = 5, lastDay = "2026-09-04" },
        chapters = new Dictionary<string, object>
        {
            ["06-collections-and-linq"] = new { read = true, best, attempts = 1 },
        },
        cards = new Dictionary<string, object>(),
        notes = Array.Empty<object>(),
        badges = new Dictionary<string, string>(),
    };

    private async Task<HttpClient> SignedInAsync(string username)
    {
        HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync(username);
        client.Bearer(registered.Str("accessToken"));
        return client;
    }

    [Fact]
    public async Task A_new_account_has_no_stored_profile()
    {
        using HttpClient client = await SignedInAsync("fresh");

        using HttpResponseMessage response = await client.GetAsync(Url("/api/profile"));

        // 204, not 404: having nothing stored yet is a normal state, and the client reads it as
        // "push whatever you have locally".
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_stored_profile_comes_back_verbatim()
    {
        using HttpClient client = await SignedInAsync("roundtrip");

        using HttpResponseMessage put = await client.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(430), updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage get = await client.GetAsync(Url("/api/profile"));
        JsonElement body = await get.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        body.GetProperty("data").GetProperty("xp").GetInt32().ShouldBe(430);
        body.GetProperty("revision").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Each_write_moves_the_revision_on()
    {
        using HttpClient client = await SignedInAsync("revisions");

        int revision = 0;
        for (int i = 1; i <= 3; i++)
        {
            using HttpResponseMessage put = await client.PutAsJsonAsync(
                Url("/api/profile"),
                new { data = Document(i * 100), updatedAt = DateTimeOffset.UtcNow, baseRevision = revision },
                TestClientExtensions.Json);

            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            JsonElement body = await put.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
            revision = body.GetProperty("revision").GetInt32();
            revision.ShouldBe(i);
        }
    }

    [Fact]
    public async Task A_write_based_on_a_stale_revision_is_refused_and_hands_back_the_current_one()
    {
        // The two-device case, which is the normal case: answer questions on a laptop, review
        // cards on a phone. Without this the second device's push silently destroys the first
        // device's work and nobody finds out until the XP goes backwards.
        using HttpClient laptop = await SignedInAsync("two-devices");

        using HttpResponseMessage first = await laptop.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(100), updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage second = await laptop.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(250), updatedAt = DateTimeOffset.UtcNow, baseRevision = 1 },
            TestClientExtensions.Json);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The phone still thinks the world is at revision 1.
        using HttpResponseMessage stale = await laptop.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(120), updatedAt = DateTimeOffset.UtcNow, baseRevision = 1 },
            TestClientExtensions.Json);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // And the 409 carries the current document, so the client can merge without a round trip.
        JsonElement conflict = await stale.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        conflict.GetProperty("data").GetProperty("xp").GetInt32().ShouldBe(250);
        conflict.GetProperty("revision").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task One_learner_cannot_read_another_learner_profile()
    {
        using HttpClient alice = await SignedInAsync("alice");
        await alice.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(999), updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);

        using HttpClient bob = await SignedInAsync("bob");
        using HttpResponseMessage bobsProfile = await bob.GetAsync(Url("/api/profile"));

        // There is no id in the route to tamper with — the user comes from the token — so the
        // only way to get Alice's document is to be Alice.
        bobsProfile.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_document_that_is_not_an_object_is_refused()
    {
        using HttpClient client = await SignedInAsync("not-an-object");

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = "just a string", updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_leaderboard_derives_its_figures_from_the_document()
    {
        using HttpClient client = await SignedInAsync("leaderboard");
        await client.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(1234), updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);

        using HttpResponseMessage response = await client.GetAsync(Url("/api/leaderboard"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement rows = await response.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        JsonElement mine = rows.EnumerateArray()
            .First(r => r.GetProperty("displayName").GetString() == "leaderboard");

        mine.GetProperty("xp").GetInt32().ShouldBe(1234);
        mine.GetProperty("streakDays").GetInt32().ShouldBe(3);
        mine.GetProperty("chaptersPassed").GetInt32().ShouldBe(1);
        mine.GetProperty("isYou").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Opting_out_removes_the_learner_from_the_leaderboard()
    {
        using HttpClient client = await SignedInAsync("private");
        await client.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(77), updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);

        using HttpResponseMessage opt = await client.PutAsync(Url("/api/me/leaderboard?visible=false"), content: null);
        opt.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpResponseMessage board = await client.GetAsync(Url("/api/leaderboard"));
        JsonElement rows = await board.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        rows.EnumerateArray()
            .Any(r => r.GetProperty("displayName").GetString() == "private")
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_the_account_removes_the_profile_and_the_sessions()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("delete-me");
        client.Bearer(registered.Str("accessToken"));

        await client.PutAsJsonAsync(
            Url("/api/profile"),
            new { data = Document(50), updatedAt = DateTimeOffset.UtcNow, baseRevision = 0 },
            TestClientExtensions.Json);

        using HttpResponseMessage deleted = await client.DeleteAsync(Url("/api/me"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The refresh token went with it, through the cascade.
        using HttpResponseMessage refresh = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"),
            new { refreshToken = registered.Str("refreshToken") },
            TestClientExtensions.Json);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Signing in again is refused too — the account is genuinely gone, not flagged.
        using HttpResponseMessage login = await client.PostAsJsonAsync(
            Url("/api/auth/login"),
            new { username = "delete-me", password = "a-good-long-password" },
            TestClientExtensions.Json);
        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
