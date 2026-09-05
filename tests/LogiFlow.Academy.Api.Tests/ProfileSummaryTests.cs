using System.Text.Json;
using LogiFlow.Academy.Api.Endpoints;

namespace LogiFlow.Academy.Api.Tests;

/// <summary>
/// The mastery formula is written twice — here in C# and in <c>site/assets/store.js</c> — so
/// these tests are the thing that catches the two drifting apart.
/// </summary>
public sealed class ProfileSummaryTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_read_chapter_with_no_test_is_worth_a_quarter()
    {
        JsonElement document = Parse("""
            { "xp": 10, "chapters": { "01-dotnet-platform": { "read": true, "best": 0 } } }
            """);

        ProfileSummary.Summary summary = ProfileSummary.From(document, chapterCount: 4);

        // One chapter of four, worth 0.25 of a chapter: 0.25 / 4 = 6.25% -> 6.3 rounded.
        summary.MasteryPercent.ShouldBe(6.3);
        summary.ChaptersPassed.ShouldBe(0);
        summary.Xp.ShouldBe(10);
    }

    [Fact]
    public void A_perfect_chapter_is_worth_all_of_it()
    {
        JsonElement document = Parse("""
            { "chapters": { "a": { "read": true, "best": 1 } } }
            """);

        ProfileSummary.From(document, chapterCount: 4).MasteryPercent.ShouldBe(25);
    }

    [Fact]
    public void Passing_means_eighty_percent_or_better()
    {
        JsonElement document = Parse("""
            {
              "chapters": {
                "a": { "read": true, "best": 0.79 },
                "b": { "read": true, "best": 0.80 },
                "c": { "read": true, "best": 1.0 }
              }
            }
            """);

        ProfileSummary.From(document, chapterCount: 3).ChaptersPassed.ShouldBe(2);
    }

    [Fact]
    public void The_streak_is_read_from_the_nested_object()
    {
        JsonElement document = Parse("""{ "streak": { "count": 12, "best": 30 } }""");

        ProfileSummary.From(document, chapterCount: 39).StreakDays.ShouldBe(12);
    }

    [Fact]
    public void A_score_above_one_cannot_inflate_the_percentage()
    {
        // The document comes from a browser, so it can say anything. Clamping is what stops a
        // hand-edited profile from putting 4000% on a shared leaderboard.
        JsonElement document = Parse("""
            { "xp": -50, "chapters": { "a": { "read": true, "best": 99 } } }
            """);

        ProfileSummary.Summary summary = ProfileSummary.From(document, chapterCount: 1);

        summary.MasteryPercent.ShouldBe(100);
        summary.Xp.ShouldBe(0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "chapters": "not an object" }""")]
    [InlineData("""{ "chapters": { "a": 42 } }""")]
    [InlineData("""{ "xp": "lots", "streak": null }""")]
    [InlineData("[]")]
    public void A_malformed_document_produces_zeroes_rather_than_an_exception(string json)
    {
        // An older version of the site, a partially-written file, or somebody poking at the
        // API: none of them should be able to make a sync fail with a 500.
        ProfileSummary.Summary summary = ProfileSummary.From(Parse(json), chapterCount: 39);

        summary.Xp.ShouldBe(0);
        summary.MasteryPercent.ShouldBe(0);
        summary.StreakDays.ShouldBe(0);
        summary.ChaptersPassed.ShouldBe(0);
    }

    [Fact]
    public void A_chapter_count_of_zero_cannot_divide_by_zero()
    {
        ProfileSummary.From(Parse("""{ "xp": 5 }"""), chapterCount: 0).MasteryPercent.ShouldBe(0);
    }
}
