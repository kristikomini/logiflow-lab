using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogiFlow.Academy.Api.Endpoints;

/// <summary>Sign-up request.</summary>
/// <param name="Username">The login. Normalised to lower case before storage, and unique.</param>
/// <param name="DisplayName">Shown on the leaderboard. Optional; defaults to the username.</param>
/// <param name="Password">Plaintext, over TLS, never stored and never logged.</param>
public sealed record RegisterRequest(string Username, string? DisplayName, string Password);

/// <summary>Sign-in request.</summary>
/// <param name="Username">The login.</param>
/// <param name="Password">Plaintext, over TLS.</param>
public sealed record LoginRequest(string Username, string Password);

/// <summary>Exchange a refresh token for a new pair.</summary>
/// <param name="RefreshToken">The refresh token issued by the previous call.</param>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Step one of a reset: prove the account is yours.</summary>
/// <param name="Username">The account to recover.</param>
/// <param name="RecoveryCode">The code issued at registration. Casing and dashes do not matter.</param>
public sealed record ForgotPasswordRequest(string Username, string RecoveryCode);

/// <summary>What step one hands back: a ticket, and how long it is good for.</summary>
/// <param name="ResetToken">Single-use, short-lived, and stored server-side only as a hash.</param>
/// <param name="ExpiresInSeconds">How long the ticket lasts.</param>
public sealed record ForgotPasswordResponse(string ResetToken, int ExpiresInSeconds);

/// <summary>Step two of a reset: spend the ticket on a new password.</summary>
/// <param name="ResetToken">The ticket from step one.</param>
/// <param name="NewPassword">Plaintext, over TLS. Same length rule as registration.</param>
public sealed record ResetPasswordRequest(string ResetToken, string NewPassword);

/// <summary>Who is signed in.</summary>
/// <param name="Id">Stable user id.</param>
/// <param name="Username">Their login.</param>
/// <param name="DisplayName">Their leaderboard name.</param>
public sealed record UserDto(Guid Id, string Username, string DisplayName);

/// <summary>What a successful sign-in returns.</summary>
/// <param name="AccessToken">Short-lived JWT for the Authorization header.</param>
/// <param name="RefreshToken">Long-lived, single-use, rotated on every exchange.</param>
/// <param name="ExpiresInSeconds">Access-token lifetime, so the client can refresh before it expires.</param>
/// <param name="User">The signed-in user.</param>
/// <param name="RecoveryCode">
/// Set ONLY on the two responses that mint one — registration and a completed reset — and
/// <see langword="null"/> on every ordinary sign-in. This is the single moment the code exists
/// in a form anyone can read; the server keeps only its hash, so it cannot be shown again.
/// </param>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    UserDto User,
    string? RecoveryCode = null);

/// <summary>A freshly minted recovery code, replacing whatever the account had before.</summary>
/// <param name="RecoveryCode">Shown once. Write it down.</param>
public sealed record RecoveryCodeResponse(string RecoveryCode);

/// <summary>A stored progress document as it goes back to the browser.</summary>
/// <param name="Data">The document, verbatim.</param>
/// <param name="UpdatedAt">When the server last stored it.</param>
/// <param name="Revision">Server revision; send it back as <c>baseRevision</c> on the next write.</param>
public sealed record ProfileResponse(JsonElement Data, DateTimeOffset UpdatedAt, int Revision);

/// <summary>A write of the whole progress document.</summary>
/// <param name="Data">The document as the browser has it.</param>
/// <param name="UpdatedAt">The client's own modification stamp.</param>
/// <param name="BaseRevision">The revision this edit was based on; 0 for a first write.</param>
public sealed record ProfileWriteRequest(JsonElement Data, DateTimeOffset? UpdatedAt, int BaseRevision);

/// <summary>One row of the leaderboard.</summary>
/// <param name="DisplayName">The learner's chosen name.</param>
/// <param name="Xp">Experience points.</param>
/// <param name="MasteryPercent">Course mastery, 0–100.</param>
/// <param name="StreakDays">Current study streak.</param>
/// <param name="ChaptersPassed">Chapters whose test is passed at 80% or better.</param>
/// <param name="IsYou">True for the caller's own row.</param>
public sealed record LeaderboardRow(
    string DisplayName, int Xp, double MasteryPercent, int StreakDays, int ChaptersPassed, bool IsYou);

/// <summary>
/// Pulls the handful of figures the leaderboard needs out of a progress document.
/// </summary>
/// <remarks>
/// <b>Why the server re-derives these instead of trusting a summary from the client.</b> The
/// document itself is the learner's own data and there is no point policing it — but the
/// leaderboard is shared, and a number that appears next to other people's names should not
/// be one the browser simply asserted. Reading them out of the document costs one parse and
/// removes the whole question.
///
/// It is still not a ranking of record: anyone can edit their own local profile and sync it.
/// The leaderboard is a nudge, not an audit, and the honest thing is to say so rather than to
/// build defences that do not actually hold.
/// </remarks>
internal static class ProfileSummary
{
    /// <summary>The figures promoted out of the document into indexed columns.</summary>
    /// <param name="Xp">Total experience points.</param>
    /// <param name="MasteryPercent">Course mastery, 0–100.</param>
    /// <param name="StreakDays">Current consecutive-day streak.</param>
    /// <param name="ChaptersPassed">Chapters passed at 80% or better.</param>
    internal readonly record struct Summary(int Xp, double MasteryPercent, int StreakDays, int ChaptersPassed);

    /// <summary>Reads the summary figures from a progress document.</summary>
    /// <param name="document">The document sent by the browser.</param>
    /// <param name="chapterCount">How many chapters the site has, for the mastery denominator.</param>
    /// <remarks>
    /// Every read is defensive. This document is produced by a browser that may be running an
    /// older version of the site, so a missing or wrongly-typed property is expected input, not
    /// an error — and a malformed profile must never be able to fail a sync.
    /// </remarks>
    internal static Summary From(JsonElement document, int chapterCount)
    {
        if (document.ValueKind != JsonValueKind.Object || chapterCount <= 0)
        {
            return default;
        }

        int xp = ReadInt(document, "xp");
        int streak = 0;
        if (document.TryGetProperty("streak", out JsonElement streakElement) &&
            streakElement.ValueKind == JsonValueKind.Object)
        {
            streak = ReadInt(streakElement, "count");
        }

        double points = 0;
        int passed = 0;

        if (document.TryGetProperty("chapters", out JsonElement chapters) &&
            chapters.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty chapter in chapters.EnumerateObject())
            {
                if (chapter.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Must match mastery() in site/assets/store.js: a quarter of a chapter for
                // having read it, three quarters for the best test score. Two copies of one
                // rule is a real risk of drift, and the comment at both ends is the mitigation.
                bool read = chapter.Value.TryGetProperty("read", out JsonElement r) &&
                            r.ValueKind == JsonValueKind.True;
                double best = Math.Clamp(ReadDouble(chapter.Value, "best"), 0, 1);

                points += (read ? 0.25 : 0) + (0.75 * best);
                if (best >= 0.8)
                {
                    passed++;
                }
            }
        }

        // AwayFromZero, not the default. .NET rounds midpoints to even ("banker's
        // rounding"), so 6.25 becomes 6.2 — while the browser's Math.round in store.js
        // rounds half up and shows 6.3. Two different numbers for the same profile, one
        // on the dashboard and one on the leaderboard, is the kind of discrepancy that
        // costs an afternoon to track down.
        double mastery = Math.Round(
            Math.Clamp(points / chapterCount * 100, 0, 100), 1, MidpointRounding.AwayFromZero);

        return new Summary(Math.Max(0, xp), mastery, Math.Max(0, streak), passed);
    }

    private static int ReadInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int result)
            ? result
            : 0;

    private static double ReadDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double result)
            ? result
            : 0;
}

/// <summary>
/// Source-generated JSON context.
/// </summary>
/// <remarks>
/// Reflection-based serialisation still works, but a generated context is what lets this
/// service run trimmed or ahead-of-time compiled later without the serialiser silently losing
/// properties. It costs one attribute per contract type.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(ForgotPasswordRequest))]
[JsonSerializable(typeof(ForgotPasswordResponse))]
[JsonSerializable(typeof(ResetPasswordRequest))]
[JsonSerializable(typeof(RecoveryCodeResponse))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(ProfileWriteRequest))]
[JsonSerializable(typeof(LeaderboardRow[]))]
internal sealed partial class AcademyJsonContext : JsonSerializerContext;
