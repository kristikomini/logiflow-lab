using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LogiFlow.Academy.Api.Tests;

/// <summary>
/// The whole sign-in story, against the real application over real HTTP.
/// </summary>
/// <param name="factory">The in-process host.</param>
public sealed class AuthFlowTests(AcademyApiFactory factory) : IClassFixture<AcademyApiFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Register_then_sign_in_with_the_same_credentials()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("register-then-login");

        registered.Str("accessToken").ShouldNotBeNullOrWhiteSpace();
        registered.Str("refreshToken").ShouldNotBeNullOrWhiteSpace();

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            Url("/api/auth/login"),
            new { username = "register-then-login", password = "a-good-long-password" },
            TestClientExtensions.Json);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_username_is_matched_regardless_of_case_and_surrounding_space()
    {
        // Normalised on write and on read, so the index can be seeked rather than scanned
        // through LOWER(). The user-visible half of that decision is this: nobody has to
        // remember whether they capitalised their own name when they signed up.
        using HttpClient client = factory.CreateClient();
        await client.RegisterAsync("Case.Test");

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            Url("/api/auth/login"),
            new { username = "  CASE.TEST  ", password = "a-good-long-password" },
            TestClientExtensions.Json);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("ab")]                                  // too short
    [InlineData("this-username-is-far-too-long-to-be-accepted")]
    [InlineData("has space")]
    [InlineData("looks@like.email")]                    // the whole reason @ is excluded
    [InlineData(".leading-dot")]
    [InlineData("trailing-dash-")]
    [InlineData("")]
    public async Task A_username_that_breaks_the_rule_is_refused(string username)
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Url("/api/auth/register"),
            new { username, password = "a-good-long-password" },
            TestClientExtensions.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_account_give_the_same_answer()
    {
        // Account enumeration: if these differ, the login form tells anyone who asks which
        // email addresses are registered here.
        using HttpClient client = factory.CreateClient();
        await client.RegisterAsync("enumeration");

        using HttpResponseMessage wrongPassword = await client.PostAsJsonAsync(
            Url("/api/auth/login"),
            new { username = "enumeration", password = "not-the-password" },
            TestClientExtensions.Json);

        using HttpResponseMessage noSuchUser = await client.PostAsJsonAsync(
            Url("/api/auth/login"),
            new { username = "nobody-here", password = "not-the-password" },
            TestClientExtensions.Json);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        noSuchUser.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Compare the message, not the whole body: Problem Details carries a per-request
        // traceId, which differs by design and is not a disclosure.
        JsonElement a = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        JsonElement b = await noSuchUser.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        a.Str("detail").ShouldBe(b.Str("detail"));
        a.Str("title").ShouldBe(b.Str("title"));
        a.Str("detail").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_short_password_is_refused_at_registration()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Url("/api/auth/register"),
            new { username = "short", displayName = "Short", password = "abc123" },
            TestClientExtensions.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registering_the_same_username_twice_is_refused_and_says_so()
    {
        // Deliberately NOT the vague wording the sign-in form uses. A username is printed on
        // the leaderboard, so "that name is taken" discloses nothing an attacker could not
        // read off the board — and withholding it would leave people guessing why their
        // registration failed. The enumeration argument is real; it just does not apply here.
        using HttpClient client = factory.CreateClient();
        await client.RegisterAsync("duplicate");

        using HttpResponseMessage second = await client.PostAsJsonAsync(
            Url("/api/auth/register"),
            new { username = "duplicate", displayName = "Someone else", password = "another-long-password" },
            TestClientExtensions.Json);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        JsonElement problem = await second.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        problem.Str("detail").ShouldContain("taken");
    }

    [Fact]
    public async Task A_protected_endpoint_refuses_an_anonymous_caller()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Url("/api/profile"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_forged_token_is_refused()
    {
        // The header and payload are valid base64 JSON; only the signature is nonsense. This is
        // exactly what ValidateIssuerSigningKey exists to catch.
        using HttpClient client = factory.CreateClient();
        client.Bearer(
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJzdWIiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEifQ." +
            "this-signature-is-not-real");

        using HttpResponseMessage response = await client.GetAsync(Url("/api/profile"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refreshing_rotates_the_token_and_the_old_one_stops_working()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("rotation");
        string first = registered.Str("refreshToken");

        using HttpResponseMessage refreshed = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"), new { refreshToken = first }, TestClientExtensions.Json);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await refreshed.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        body.Str("refreshToken").ShouldNotBe(first);

        // Rotation means single use. Presenting the old one again must fail.
        using HttpResponseMessage replay = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"), new { refreshToken = first }, TestClientExtensions.Json);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Replaying_a_used_refresh_token_revokes_the_whole_family()
    {
        // The stolen-token case. If an old token is presented again, the server cannot tell a
        // thief from a client that lost a response, so it ends every session for that user.
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("replay");
        string first = registered.Str("refreshToken");

        using HttpResponseMessage rotated = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"), new { refreshToken = first }, TestClientExtensions.Json);
        JsonElement body = await rotated.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        string second = body.Str("refreshToken");

        // Replay the first, which revokes everything...
        using HttpResponseMessage replay = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"), new { refreshToken = first }, TestClientExtensions.Json);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...including the token the legitimate client is now holding.
        using HttpResponseMessage afterRevocation = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"), new { refreshToken = second }, TestClientExtensions.Json);
        afterRevocation.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_revokes_the_refresh_token()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("signout");
        string token = registered.Str("refreshToken");

        using HttpResponseMessage logout = await client.PostAsJsonAsync(
            Url("/api/auth/logout"), new { refreshToken = token }, TestClientExtensions.Json);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using HttpResponseMessage afterwards = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"), new { refreshToken = token }, TestClientExtensions.Json);
        afterwards.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_with_an_unknown_token_still_returns_no_content()
    {
        // "That token did not exist" is information an unauthenticated caller does not need.
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Url("/api/auth/logout"), new { refreshToken = "never-issued" }, TestClientExtensions.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/dashboard.html")]
    // The three account pages, and signin.html above all: a sign-in page that the fallback
    // authorization policy has closed asks you to sign in before you can sign in.
    [InlineData("/signin.html")]
    [InlineData("/register.html")]
    [InlineData("/reset.html")]
    [InlineData("/account.html")]
    [InlineData("/chapters/06-collections-and-linq.html")]
    [InlineData("/viva.html")]
    [InlineData("/assets/quizzes-1.js")]
    // Generated by tools/viva-deck.cs. In the list because a deck that 404s
    // leaves viva.html rendering an empty page rather than failing loudly.
    [InlineData("/assets/rules.js")]
    [InlineData("/assets/auth-page.js")]
    [InlineData("/favicon.svg")]
    public async Task Every_public_page_and_asset_is_served_anonymously(string path)
    {
        // "/" is in this list because it broke. UseDefaultFiles rewrites it to /index.html, but
        // static-file middleware steps aside when an endpoint has already been selected — and
        // with UseRouting left implicit at the top of the pipeline, MapFallback was matching "/"
        // first and answering 404 while every other page worked. The fix was an explicit
        // UseRouting placed BELOW the static files; this is the test that keeps it there.
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Url(path));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/favicon.ico")]
    [InlineData("/chapters/does-not-exist.html")]
    [InlineData("/nonsense")]
    public async Task An_unknown_path_is_a_404_and_not_a_401(string path)
    {
        // The fallback authorization policy closes every endpoint by default, which would
        // otherwise turn a mistyped URL — and the browser's automatic /favicon.ico probe —
        // into "sign in" rather than "not found".
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Url(path));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_site_itself_is_served_without_a_token()
    {
        // The fallback authorization policy closes every endpoint by default. The tutorial must
        // still be readable by someone who has never signed in — this is the test that would
        // fail if the static-file middleware drifted below UseAuthorization.
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Url("/index.html"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync())
            .ShouldContain("LogiFlow Academy");
    }
}
