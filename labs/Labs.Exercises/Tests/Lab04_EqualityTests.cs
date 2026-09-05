using System.Diagnostics;
using Labs.Exercises.Exercises;

namespace Labs.Exercises.Tests;

/// <summary>
/// The spec for Lab 04. Do not edit — edit
/// <c>Exercises/Lab04_Equality.cs</c> until these pass.
/// </summary>
public sealed class Lab04EqualityTests
{
    // ── Exercise 1 — the equality contract ────────────────────────────────────────────

    [Fact]
    public void StockKey_equal_keys_are_equal()
    {
        var a = new Lab04Equality.StockKey("ELE-100001", 1);
        var b = new Lab04Equality.StockKey("ELE-100001", 1);

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void StockKey_sku_comparison_ignores_case()
    {
        var upper = new Lab04Equality.StockKey("ELE-100001", 1);
        var lower = new Lab04Equality.StockKey("ele-100001", 1);

        upper.Equals(lower).ShouldBeTrue();
    }

    [Fact]
    public void StockKey_warehouse_is_part_of_the_key()
    {
        var one = new Lab04Equality.StockKey("ELE-100001", 1);
        var two = new Lab04Equality.StockKey("ELE-100001", 2);

        one.Equals(two).ShouldBeFalse();
        (one != two).ShouldBeTrue();
    }

    [Fact]
    public void StockKey_equal_keys_hash_equally()
    {
        // THE CONTRACT. Break this and every hash-based collection loses your data silently.
        var upper = new Lab04Equality.StockKey("ELE-100001", 1);
        var lower = new Lab04Equality.StockKey("ele-100001", 1);

        upper.GetHashCode().ShouldBe(lower.GetHashCode());
    }

    [Fact]
    public void StockKey_does_not_collide_on_transposed_fields()
    {
        // If GetHashCode XORs or sums its parts, these two collide — which is the bug
        // module 20 warns about. A collision is not *incorrect*, so this test only
        // demands that a naive XOR is not what you wrote.
        var a = new Lab04Equality.StockKey("A", 66);   // 'B' == 66
        var b = new Lab04Equality.StockKey("B", 65);   // 'A' == 65

        a.Equals(b).ShouldBeFalse();
    }

    [Fact]
    public void StockKey_works_as_a_dictionary_key()
    {
        var dictionary = new Dictionary<Lab04Equality.StockKey, int>
        {
            [new Lab04Equality.StockKey("ELE-100001", 1)] = 42,
        };

        dictionary[new Lab04Equality.StockKey("ele-100001", 1)].ShouldBe(42);
        dictionary.ContainsKey(new Lab04Equality.StockKey("ELE-100001", 2)).ShouldBeFalse();
    }

    [Fact]
    public void StockKey_comparison_does_not_allocate()
    {
        // Implementing IEquatable<StockKey> is what makes this true: without it, the
        // comparison goes through Equals(object) and boxes both sides.
        var a = new Lab04Equality.StockKey("ELE-100001", 1);
        var b = new Lab04Equality.StockKey("ELE-100001", 1);

        _ = a.Equals(b);   // warm up anything lazy

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            _ = a.Equals(b);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    // ── Exercise 2 — a comparison that does not overflow ──────────────────────────────

    [Fact]
    public void Comparer_orders_by_units_descending()
    {
        List<Lab04Equality.Holding> holdings =
        [
            new("Modena", 10),
            new("Bologna", 90),
            new("Parma", 50),
        ];

        holdings.Sort(Lab04Equality.ByUnitsDescendingThenName());

        holdings.Select(h => h.Name).ShouldBe(["Bologna", "Parma", "Modena"]);
    }

    [Fact]
    public void Comparer_breaks_ties_by_name()
    {
        List<Lab04Equality.Holding> holdings =
        [
            new("Reggio", 50),
            new("Bologna", 50),
            new("Modena", 50),
        ];

        holdings.Sort(Lab04Equality.ByUnitsDescendingThenName());

        holdings.Select(h => h.Name).ShouldBe(["Bologna", "Modena", "Reggio"]);
    }

    [Fact]
    public void Comparer_does_not_overflow_on_extreme_values()
    {
        // `y.Units - x.Units` overflows here and reverses the sign, putting the SMALLEST
        // holding first. This is the test the obvious implementation fails.
        var largest = new Lab04Equality.Holding("Largest", int.MaxValue);
        var smallest = new Lab04Equality.Holding("Smallest", int.MinValue);

        IComparer<Lab04Equality.Holding> comparer = Lab04Equality.ByUnitsDescendingThenName();

        comparer.Compare(largest, smallest).ShouldBeLessThan(0);
        comparer.Compare(smallest, largest).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Comparer_says_an_item_equals_itself()
    {
        var one = new Lab04Equality.Holding("Modena", 10);

        Lab04Equality.ByUnitsDescendingThenName().Compare(one, one).ShouldBe(0);
    }

    // ── Exercise 3 — choosing the right collection ────────────────────────────────────

    [Fact]
    public void MissingSkus_returns_only_what_is_not_in_stock()
    {
        IReadOnlyList<string> missing = Lab04Equality.MissingSkus(
            ["ELE-1", "ELE-2", "ELE-3"],
            ["ELE-2"]);

        missing.ShouldBe(["ELE-1", "ELE-3"]);
    }

    [Fact]
    public void MissingSkus_ignores_case()
    {
        IReadOnlyList<string> missing = Lab04Equality.MissingSkus(
            ["ELE-1", "ELE-2"],
            ["ele-1", "ele-2"]);

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void MissingSkus_preserves_order_and_removes_duplicates()
    {
        IReadOnlyList<string> missing = Lab04Equality.MissingSkus(
            ["ELE-9", "ELE-1", "ELE-9", "ele-9"],
            ["ELE-5"]);

        missing.ShouldBe(["ELE-9", "ELE-1"]);
    }

    [Fact]
    public void MissingSkus_is_not_quadratic()
    {
        // 20,000 x 20,000 with List.Contains is 400 million comparisons and takes many
        // seconds. With a HashSet it is 40,000 and takes milliseconds.
        string[] ordered = [.. Enumerable.Range(0, 20_000).Select(i => $"ELE-{i}")];
        string[] inStock = [.. Enumerable.Range(0, 20_000).Select(i => $"ELE-{i}")];

        var watch = Stopwatch.StartNew();
        IReadOnlyList<string> missing = Lab04Equality.MissingSkus(ordered, inStock);
        watch.Stop();

        missing.ShouldBeEmpty();
        watch.ElapsedMilliseconds.ShouldBeLessThan(2000);
    }

    // ── Exercise 4 — the mutable-key bug ──────────────────────────────────────────────

    [Fact]
    public void Mutating_a_key_in_place_makes_the_entry_unreachable()
    {
        // Predict these two values BEFORE you run the test. If you predicted them
        // correctly and can say why, you understand rule 3 of the hashing contract.
        var key = new Lab04Equality.MutableStockKey { Sku = "ELE-100001" };

        (bool found, int count) = Lab04Equality.MutateKeyInPlace(key, "ELE-999999");

        found.ShouldBeFalse();   // the SAME object, and the dictionary cannot find it
        count.ShouldBe(1);       // and it is still in there, taking up space, forever
    }
}
