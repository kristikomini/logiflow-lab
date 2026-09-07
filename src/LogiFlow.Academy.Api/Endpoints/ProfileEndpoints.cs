using System.Security.Claims;
using System.Text.Json;
using LogiFlow.Academy.Api.Domain;
using LogiFlow.Academy.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Academy.Api.Endpoints;

/// <summary>Reading and writing a learner's progress, and the leaderboard.</summary>
public static class ProfileEndpoints
{
    /// <summary>
    /// Refuse a document larger than this.
    /// </summary>
    /// <remarks>
    /// A real profile is a few tens of kilobytes: 464 scheduled cards, a few hundred notes and
    /// a year of daily history. One megabyte is generous. Without a cap, an authenticated user
    /// can fill the disk one PUT at a time — the least glamorous denial of service there is,
    /// and one that costs a single length check to close.
    /// </remarks>
    private const int MaxDocumentBytes = 1_048_576;

    /// <summary>
    /// The mastery denominator used when <c>Academy:ChapterCount</c> is not configured.
    /// </summary>
    /// <remarks>
    /// <b>This must equal the number of chapters the site actually ships</b> — the length of
    /// <c>CHAPTERS</c> in <c>site/assets/chapters.js</c>, which is what <c>mastery()</c> in
    /// <c>site/assets/store.js</c> divides by. When the two disagree, the same profile scores
    /// one percentage on the learner's own dashboard and a different one on the leaderboard
    /// beside other people's names, with nothing anywhere to say which is wrong.
    ///
    /// That is not hypothetical: this constant and the manifest were 39 and 47 for a while,
    /// and the leaderboard was quietly the more generous of the two the whole time. It is now
    /// the <c>site/chapter-count</c> check in <c>tools/doctor.cs</c>, which compares this
    /// number, the value in <c>appsettings.json</c> and the manifest, and fails the build when
    /// any of the three drifts — because "remember to update the API when you add a chapter"
    /// is exactly the kind of instruction that works until the day it matters.
    /// </remarks>
    private const int DefaultChapterCount = 51;

    /// <summary>Registers the profile and leaderboard endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api")
            .WithTags("Progress")
            .RequireAuthorization();

        // ── Read ────────────────────────────────────────────────────────────────────────
        group.MapGet("/profile", async (
            ClaimsPrincipal principal,
            AcademyDbContext db,
            CancellationToken cancellationToken) =>
        {
            Guid userId = principal.UserId();

            // AsNoTracking: this is a pure read, so there is nothing to gain from building
            // change-tracking snapshots of a document that may be tens of kilobytes.
            LearnerProfile? profile = await db.Profiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

            if (profile is null)
            {
                // 204, not 404. "You have no stored progress yet" is a normal state for a new
                // account, and the client's sync logic reads it as "push whatever you have".
                return Results.NoContent();
            }

            using JsonDocument document = JsonDocument.Parse(profile.Document);
            return Results.Ok(new ProfileResponse(
                document.RootElement.Clone(), profile.UpdatedAt, profile.Revision));
        })
        .WithSummary("Returns the signed-in learner's stored progress document.");

        // ── Write ───────────────────────────────────────────────────────────────────────
        group.MapPut("/profile", async (
            ProfileWriteRequest request,
            ClaimsPrincipal principal,
            AcademyDbContext db,
            TimeProvider clock,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            Guid userId = principal.UserId();

            if (request.Data.ValueKind != JsonValueKind.Object)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["data"] = ["A profile document must be a JSON object."],
                });
            }

            string json = request.Data.GetRawText();
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
            {
                return Results.Problem(
                    detail: "That profile is too large to store.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            LearnerProfile? profile = await db.Profiles
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

            DateTimeOffset now = clock.GetUtcNow();
            int chapterCount = configuration.GetValue("Academy:ChapterCount", DefaultChapterCount);
            ProfileSummary.Summary summary = ProfileSummary.From(request.Data, chapterCount);

            if (profile is null)
            {
                profile = new LearnerProfile
                {
                    UserId = userId,
                    Document = json,
                    Revision = 1,
                    UpdatedAt = now,
                    ClientUpdatedAt = request.UpdatedAt ?? now,
                    Xp = summary.Xp,
                    MasteryPercent = summary.MasteryPercent,
                    StreakDays = summary.StreakDays,
                    ChaptersPassed = summary.ChaptersPassed,
                };
                db.Profiles.Add(profile);
            }
            else
            {
                // Optimistic concurrency, by hand and on purpose.
                //
                // The client tells us which revision it read before editing. If the stored
                // revision has moved on, another device wrote in between and this PUT would
                // destroy that work — so it is refused with the current document attached, and
                // the client merges (see merge() in site/assets/account.js) and tries again.
                //
                // BaseRevision 0 means "I have never synced", which is the first push from a
                // browser that already has local progress; that one is allowed through and the
                // client's own merge has already run against what it pulled a moment earlier.
                if (request.BaseRevision > 0 && request.BaseRevision != profile.Revision)
                {
                    using JsonDocument current = JsonDocument.Parse(profile.Document);
                    return Results.Json(
                        new ProfileResponse(current.RootElement.Clone(), profile.UpdatedAt, profile.Revision),
                        statusCode: StatusCodes.Status409Conflict);
                }

                profile.Document = json;
                profile.Revision += 1;
                profile.UpdatedAt = now;
                profile.ClientUpdatedAt = request.UpdatedAt ?? now;
                profile.Xp = summary.Xp;
                profile.MasteryPercent = summary.MasteryPercent;
                profile.StreakDays = summary.StreakDays;
                profile.ChaptersPassed = summary.ChaptersPassed;
            }

            await db.SaveChangesAsync(cancellationToken);

            using JsonDocument saved = JsonDocument.Parse(profile.Document);
            return Results.Ok(new ProfileResponse(saved.RootElement.Clone(), profile.UpdatedAt, profile.Revision));
        })
        .WithSummary("Stores the learner's progress document. Rejects a write based on a stale revision.");

        // ── Leaderboard ─────────────────────────────────────────────────────────────────
        group.MapGet("/leaderboard", async (
            ClaimsPrincipal principal,
            AcademyDbContext db,
            CancellationToken cancellationToken) =>
        {
            Guid me = principal.UserId();

            // Projected straight into the DTO, so the query asks for five columns rather than
            // loading two entity graphs and throwing most of them away.
            List<LeaderboardRow> rows = await db.Profiles
                .AsNoTracking()
                .Where(p => p.User!.ShowOnLeaderboard)
                .OrderByDescending(p => p.Xp)
                .ThenByDescending(p => p.MasteryPercent)
                .Take(50)
                .Select(p => new LeaderboardRow(
                    p.User!.DisplayName,
                    p.Xp,
                    p.MasteryPercent,
                    p.StreakDays,
                    p.ChaptersPassed,
                    p.UserId == me))
                .ToListAsync(cancellationToken);

            return Results.Ok(rows);
        })
        .WithSummary("The top fifty learners who have not opted out.");

        // ── Leaderboard opt-out ─────────────────────────────────────────────────────────
        group.MapPut("/me/leaderboard", async (
            bool visible,
            ClaimsPrincipal principal,
            AcademyDbContext db,
            CancellationToken cancellationToken) =>
        {
            Guid userId = principal.UserId();
            AcademyUser? user = await db.Users.FindAsync([userId], cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            user.ShowOnLeaderboard = visible;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        })
        .WithSummary("Shows or hides the signed-in learner on the leaderboard.");

        // ── Delete everything ───────────────────────────────────────────────────────────
        group.MapDelete("/me", async (
            ClaimsPrincipal principal,
            AcademyDbContext db,
            CancellationToken cancellationToken) =>
        {
            Guid userId = principal.UserId();
            AcademyUser? user = await db.Users.FindAsync([userId], cancellationToken);
            if (user is null)
            {
                return Results.NoContent();
            }

            // A real delete, not a flag. The profile and the refresh tokens go with it through
            // the cascade configured in AcademyDbContext. An account you cannot delete is not
            // an account you should have been asked to create — and under GDPR it is not
            // optional either.
            db.Users.Remove(user);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        })
        .WithSummary("Permanently deletes the account, its progress and its sessions.");

        return app;
    }
}
