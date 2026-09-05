using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LogiFlow.Academy.Api.Tests;

/// <summary>
/// Recovering an account with no email server.
/// </summary>
/// <remarks>
/// The interesting tests here are the negative ones. A reset flow that lets the right person
/// back in is easy; the reason this is worth thirteen tests is everything it must refuse — a
/// spent ticket, a superseded ticket, a code that already recovered the account once, and a
/// session belonging to whoever the reset was protecting against.
/// </remarks>
/// <param name="factory">The in-process host.</param>
public sealed class PasswordResetTests(AcademyApiFactory factory) : IClassFixture<AcademyApiFactory>
{
    private const string OldPassword = "a-good-long-password";
    private const string NewPassword = "an-even-better-long-password";

    private static Uri Url(string path) => new(path, UriKind.Relative);

    /// <summary>Registers a learner and hands back the recovery code they were shown once.</summary>
    private static async Task<string> RegisterAndKeepCodeAsync(HttpClient client, string username)
    {
        JsonElement registered = await client.RegisterAsync(username, OldPassword);
        string code = registered.Str("recoveryCode");
        code.ShouldNotBeNullOrWhiteSpace();
        return code;
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string username, string code) =>
        client.PostAsJsonAsync(
            Url("/api/auth/forgot-password"),
            new { username, recoveryCode = code },
            TestClientExtensions.Json);

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string ticket, string password) =>
        client.PostAsJsonAsync(
            Url("/api/auth/reset-password"),
            new { resetToken = ticket, newPassword = password },
            TestClientExtensions.Json);

    [Fact]
    public async Task Registration_shows_the_recovery_code_exactly_once()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("code-once");

        registered.Str("recoveryCode").ShouldNotBeNullOrWhiteSpace();

        // Signing in afterwards must NOT repeat it. The server keeps only a hash, so it could
        // not do so even if it wanted to — this test is what stops somebody "helpfully"
        // storing the plaintext to make the UI easier.
        using HttpResponseMessage login = await client.LoginAsync("code-once");
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        bool present = body.TryGetProperty("recoveryCode", out JsonElement code);
        (!present || code.ValueKind == JsonValueKind.Null).ShouldBeTrue(
            "an ordinary sign-in must not carry a recovery code");
    }

    [Fact]
    public async Task The_whole_reset_replaces_the_password()
    {
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, "full-reset");

        using HttpResponseMessage forgot = await ForgotAsync(client, "full-reset", code);
        forgot.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement ticket = await forgot.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        using HttpResponseMessage reset = await ResetAsync(client, ticket.Str("resetToken"), NewPassword);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage withOld = await client.LoginAsync("full-reset", OldPassword);
        withOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using HttpResponseMessage withNew = await client.LoginAsync("full-reset", NewPassword);
        withNew.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_completed_reset_signs_you_in_and_issues_a_new_code()
    {
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, "reset-issues-code");

        using HttpResponseMessage forgot = await ForgotAsync(client, "reset-issues-code", code);
        JsonElement ticket = await forgot.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        using HttpResponseMessage reset = await ResetAsync(client, ticket.Str("resetToken"), NewPassword);
        JsonElement body = await reset.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        body.Str("accessToken").ShouldNotBeNullOrWhiteSpace();
        body.Str("recoveryCode").ShouldNotBeNullOrWhiteSpace();
        body.Str("recoveryCode").ShouldNotBe(code);
    }

    [Fact]
    public async Task The_code_that_recovered_the_account_stops_working()
    {
        // A recovery code that survives its own use is a permanent second password. Anyone who
        // saw it once — over a shoulder, in a screenshot, in a chat log — keeps the account
        // forever, and the owner has no way to find out.
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, "code-is-spent");

        using HttpResponseMessage forgot = await ForgotAsync(client, "code-is-spent", code);
        JsonElement ticket = await forgot.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        using HttpResponseMessage reset = await ResetAsync(client, ticket.Str("resetToken"), NewPassword);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage again = await ForgotAsync(client, "code-is-spent", code);
        again.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_reset_ticket_cannot_be_spent_twice()
    {
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, "single-use");

        using HttpResponseMessage forgot = await ForgotAsync(client, "single-use", code);
        JsonElement ticket = await forgot.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        string token = ticket.Str("resetToken");

        using HttpResponseMessage first = await ResetAsync(client, token, NewPassword);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage second = await ResetAsync(client, token, "a-third-long-password");
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the password is still the one the first reset set, not the second attempt's.
        using HttpResponseMessage login = await client.LoginAsync("single-use", NewPassword);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Asking_for_a_second_ticket_kills_the_first()
    {
        // Otherwise every abandoned attempt leaves a live password-change credential behind
        // for its full lifetime, and clicking the button repeatedly widens the window instead
        // of restarting it.
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, "superseded");

        using HttpResponseMessage first = await ForgotAsync(client, "superseded", code);
        JsonElement one = await first.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        using HttpResponseMessage second = await ForgotAsync(client, "superseded", code);
        JsonElement two = await second.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        using HttpResponseMessage stale = await ResetAsync(client, one.Str("resetToken"), NewPassword);
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using HttpResponseMessage fresh = await ResetAsync(client, two.Str("resetToken"), NewPassword);
        fresh.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reset_revokes_every_existing_session()
    {
        // The reason someone resets is often that somebody else got in. Leaving the intruder's
        // refresh token alive would make the whole exercise decorative.
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("kick-everyone", OldPassword);
        string code = registered.Str("recoveryCode");
        string sessionElsewhere = registered.Str("refreshToken");

        using HttpResponseMessage forgot = await ForgotAsync(client, "kick-everyone", code);
        JsonElement ticket = await forgot.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        using HttpResponseMessage reset = await ResetAsync(client, ticket.Str("resetToken"), NewPassword);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);

        using HttpResponseMessage refresh = await client.PostAsJsonAsync(
            Url("/api/auth/refresh"),
            new { refreshToken = sessionElsewhere },
            TestClientExtensions.Json);

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_code_give_the_same_answer()
    {
        // The reset form is the one place an attacker can probe with no password at all. If
        // these two differ, it becomes a free membership list.
        using HttpClient client = factory.CreateClient();
        await RegisterAndKeepCodeAsync(client, "reset-enumeration");

        using HttpResponseMessage wrongCode =
            await ForgotAsync(client, "reset-enumeration", "K7M2Q-3XZ9F-P4WRT-8NBHV");
        using HttpResponseMessage noSuchUser =
            await ForgotAsync(client, "no-such-learner", "K7M2Q-3XZ9F-P4WRT-8NBHV");

        wrongCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        noSuchUser.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        JsonElement a = await wrongCode.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        JsonElement b = await noSuchUser.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        a.Str("detail").ShouldBe(b.Str("detail"));
        a.Str("detail").ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>How a code might come back from a human, none of which changes its meaning.</summary>
    public enum Typing
    {
        /// <summary>Exactly as it was shown.</summary>
        AsIssued,

        /// <summary>Pasted along with surrounding whitespace.</summary>
        Padded,

        /// <summary>Typed in lower case, with spaces instead of dashes.</summary>
        Sloppy,

        /// <summary>Read aloud and mistyped: I for 1, O for 0 — the confusions the alphabet expects.</summary>
        Confused,
    }

    [Theory]
    [InlineData(Typing.AsIssued, "typed-plain")]
    [InlineData(Typing.Padded, "typed-padded")]
    [InlineData(Typing.Sloppy, "typed-sloppy")]
    [InlineData(Typing.Confused, "typed-confused")]
    public async Task A_code_is_accepted_however_it_was_typed(Typing typing, string username)
    {
        // Casing, spacing and dashes carry no information, so rejecting a code over them only
        // ever punishes the honest user. Content is a different matter and is unforgiving.
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, username);

        string typed = typing switch
        {
            Typing.Padded => "  " + code + "  ",
            Typing.Sloppy => code.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant(),
            Typing.Confused => code
                .Replace("1", "I", StringComparison.Ordinal)
                .Replace("0", "O", StringComparison.Ordinal),
            _ => code,
        };

        using HttpResponseMessage forgot = await ForgotAsync(client, username, typed);

        forgot.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_short_new_password_is_refused()
    {
        using HttpClient client = factory.CreateClient();
        string code = await RegisterAndKeepCodeAsync(client, "short-new");

        using HttpResponseMessage forgot = await ForgotAsync(client, "short-new", code);
        JsonElement ticket = await forgot.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);

        using HttpResponseMessage reset = await ResetAsync(client, ticket.Str("resetToken"), "abc123");
        reset.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // And the ticket survives, so a typo in the new password does not cost you the reset.
        using HttpResponseMessage retry = await ResetAsync(client, ticket.Str("resetToken"), NewPassword);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_invented_reset_token_is_refused()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage reset = await ResetAsync(client, "not-a-ticket-anyone-issued", NewPassword);

        reset.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_learner_can_replace_a_lost_code()
    {
        using HttpClient client = factory.CreateClient();
        JsonElement registered = await client.RegisterAsync("lost-my-code", OldPassword);
        string original = registered.Str("recoveryCode");
        client.Bearer(registered.Str("accessToken"));

        using HttpResponseMessage issued = await client.PostAsync(Url("/api/auth/recovery-code"), null);
        issued.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await issued.Content.ReadFromJsonAsync<JsonElement>(TestClientExtensions.Json);
        string replacement = body.Str("recoveryCode");
        replacement.ShouldNotBeNullOrWhiteSpace();
        replacement.ShouldNotBe(original);

        // Replaces, never adds: two live codes would double the number of secrets that can
        // take the account over, and the learner could not tell you which one leaked.
        using HttpClient anonymous = factory.CreateClient();
        using HttpResponseMessage withOld = await ForgotAsync(anonymous, "lost-my-code", original);
        withOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using HttpResponseMessage withNew = await ForgotAsync(anonymous, "lost-my-code", replacement);
        withNew.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Issuing_a_recovery_code_needs_a_token()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync(Url("/api/auth/recovery-code"), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
