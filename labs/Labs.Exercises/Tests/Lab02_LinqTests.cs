using Labs.Exercises.Exercises;
using static Labs.Exercises.Exercises.Lab02Linq;

namespace Labs.Exercises.Tests;

/// <summary>
/// The spec for Lab 02. DO NOT EDIT — make the implementations satisfy these.
/// </summary>
public sealed class Lab02LinqTests
{
    private static readonly DateOnly Day1 = new(2026, 3, 1);
    private static readonly DateOnly Day2 = new(2026, 3, 2);
    private static readonly DateOnly Day3 = new(2026, 3, 3);

    private static List<Line> Sample() =>
    [
        new(1, "ELE-100001", "ELE", 2, 100m, Day1),   // 200
        new(1, "PAC-200001", "PAC", 1, 50m, Day1),    //  50
        new(2, "ELE-100001", "ELE", 5, 100m, Day2),   // 500
        new(2, "SAF-300001", "SAF", 10, 12m, Day2),   // 120
        new(3, "PAC-200001", "PAC", 3, 50m, Day3),    // 150
        new(3, "ELE-100002", "ELE", 1, 900m, Day3),   // 900
    ];

    // ── 1 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HighValueSkus_returns_distinct_sorted_skus_above_the_threshold()
    {
        // Lines worth > 150: ELE-100001 (200), ELE-100001 (500), ELE-100002 (900)
        string[] result = [.. HighValueSkus(Sample(), 150m)];

        result.ShouldBe(["ELE-100001", "ELE-100002"]);
    }

    [Fact]
    public void HighValueSkus_returns_empty_when_nothing_qualifies()
    {
        HighValueSkus(Sample(), 100_000m).ShouldBeEmpty();
    }

    // ── 2 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RevenueByCategory_aggregates_and_sorts_by_revenue_descending()
    {
        CategoryRevenue[] result = [.. RevenueByCategory(Sample())];

        result.Length.ShouldBe(3);

        result[0].Category.ShouldBe("ELE");
        result[0].Revenue.ShouldBe(1600m);   // 200 + 500 + 900
        result[0].UnitsSold.ShouldBe(8);     // 2 + 5 + 1

        result[1].Category.ShouldBe("PAC");
        result[1].Revenue.ShouldBe(200m);

        result[2].Category.ShouldBe("SAF");
        result[2].Revenue.ShouldBe(120m);
    }

    // ── 3 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LargestOrderId_finds_the_highest_value_order()
    {
        // Order 1 = 250, order 2 = 620, order 3 = 1050
        LargestOrderId(Sample()).ShouldBe(3);
    }

    [Fact]
    public void LargestOrderId_returns_null_for_an_empty_sequence()
    {
        // This is the assertion that catches First()/Max() on an empty sequence.
        LargestOrderId([]).ShouldBeNull();
    }

    // ── 4 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LazySkuQuery_does_not_evaluate_anything_until_enumerated()
    {
        List<string> evaluated = [];

        IEnumerable<string> query = LazySkuQuery(Sample(), 5, evaluated.Add);

        // Building the query must touch nothing. If this fails you called ToList() somewhere.
        evaluated.ShouldBeEmpty();

        _ = query.ToList();

        evaluated.ShouldNotBeEmpty();
    }

    [Fact]
    public void LazySkuQuery_re_evaluates_on_every_enumeration()
    {
        List<string> evaluated = [];

        IEnumerable<string> query = LazySkuQuery(Sample(), 5, evaluated.Add);

        _ = query.ToList();
        int afterFirst = evaluated.Count;

        _ = query.ToList();

        // Deferred execution means the query runs AGAIN. This is the behaviour that causes
        // duplicate database round trips when people forget to materialise a query once.
        evaluated.Count.ShouldBe(afterFirst * 2);
    }

    // ── 5 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChunkBy_splits_into_chunks_of_the_requested_size()
    {
        IReadOnlyList<int>[] chunks = [.. ChunkBy(Enumerable.Range(1, 7), 3)];

        chunks.Length.ShouldBe(3);
        chunks[0].ShouldBe([1, 2, 3]);
        chunks[1].ShouldBe([4, 5, 6]);
        chunks[2].ShouldBe([7]);          // the final partial chunk must be returned
    }

    [Fact]
    public void ChunkBy_returns_nothing_for_an_empty_source()
    {
        ChunkBy(Array.Empty<int>(), 3).ShouldBeEmpty();
    }

    [Fact]
    public void ChunkBy_validates_size_eagerly_not_on_enumeration()
    {
        // THE POINT OF THIS TEST: no enumeration happens here at all. If the guard clause lives
        // inside an iterator method, the exception is not thrown until someone enumerates -
        // and this assertion fails. That is the two-method iterator pattern.
        Should.Throw<ArgumentOutOfRangeException>(() => ChunkBy(Enumerable.Range(1, 5), 0));
    }

    [Fact]
    public void ChunkBy_enumerates_the_source_only_once()
    {
        int enumerations = 0;

        IEnumerable<int> CountingSource()
        {
            enumerations++;
            for (int i = 1; i <= 5; i++)
            {
                yield return i;
            }
        }

        _ = ChunkBy(CountingSource(), 2).ToList();

        enumerations.ShouldBe(1);
    }

    // ── 6 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OrphanedSkus_finds_skus_missing_from_the_catalogue()
    {
        string[] catalogue = ["ELE-100001", "PAC-200001"];

        string[] result = [.. OrphanedSkus(Sample(), catalogue).OrderBy(s => s, StringComparer.Ordinal)];

        result.ShouldBe(["ELE-100002", "SAF-300001"]);
    }

    [Fact]
    public void OrphanedSkus_returns_empty_when_the_catalogue_covers_everything()
    {
        string[] catalogue = ["ELE-100001", "ELE-100002", "PAC-200001", "SAF-300001"];

        OrphanedSkus(Sample(), catalogue).ShouldBeEmpty();
    }

    // ── 7 ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CumulativeDailyRevenue_accumulates_in_date_order()
    {
        (DateOnly Date, decimal RunningTotal)[] result = [.. CumulativeDailyRevenue(Sample())];

        result.Length.ShouldBe(3);

        result[0].ShouldBe((Day1, 250m));    // 200 + 50
        result[1].ShouldBe((Day2, 870m));    // + 500 + 120
        result[2].ShouldBe((Day3, 1920m));   // + 150 + 900
    }
}
