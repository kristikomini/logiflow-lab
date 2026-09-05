using System.Reflection;
using Labs.Exercises.Exercises;

namespace Labs.Exercises.Tests;

/// <summary>
/// The spec for Lab 05. Do not edit — edit
/// <c>Exercises/Lab05_Delegates.cs</c> until these pass.
/// </summary>
public sealed class Lab05DelegatesTests
{
    // ── Exercise 1 — MyWhere ──────────────────────────────────────────────────────────

    [Fact]
    public void MyWhere_filters()
    {
        int[] source = [1, 2, 3, 4, 5, 6];

        Lab05Delegates.MyWhere(source, n => n % 2 == 0).ShouldBe([2, 4, 6]);
    }

    [Fact]
    public void MyWhere_preserves_order()
    {
        string[] source = ["delta", "alfa", "charlie", "bravo"];

        Lab05Delegates.MyWhere(source, s => s.Length == 5)
            .ShouldBe(["delta", "bravo"]);
    }

    [Fact]
    public void MyWhere_does_not_run_until_enumerated()
    {
        var evaluated = 0;
        IEnumerable<int> Source()
        {
            foreach (int n in new[] { 1, 2, 3 })
            {
                evaluated++;
                yield return n;
            }
        }

        IEnumerable<int> query = Lab05Delegates.MyWhere(Source(), n => n > 0);

        evaluated.ShouldBe(0, "building the query must not enumerate anything");

        _ = query.ToList();

        evaluated.ShouldBe(3);
    }

    [Fact]
    public void MyWhere_streams_and_can_handle_an_infinite_sequence()
    {
        static IEnumerable<int> Naturals()
        {
            int n = 0;
            while (true)
            {
                yield return n++;
            }
        }

        // If this materialises, the test hangs rather than fails — which is itself the lesson.
        Lab05Delegates.MyWhere(Naturals(), n => n % 10 == 0).Take(3).ShouldBe([0, 10, 20]);
    }

    [Fact]
    public void MyWhere_validates_arguments_eagerly()
    {
        // The two-method pattern: this must throw NOW, not on first enumeration.
        Should.Throw<ArgumentNullException>(() => Lab05Delegates.MyWhere<int>(null!, _ => true));
        Should.Throw<ArgumentNullException>(() => Lab05Delegates.MyWhere([1, 2], null!));
    }

    // ── Exercise 2 — Memoize ──────────────────────────────────────────────────────────

    [Fact]
    public void Memoize_computes_once_per_distinct_argument()
    {
        var calls = 0;
        Func<int, int> square = Lab05Delegates.Memoize<int, int>(n => { calls++; return n * n; });

        square(4).ShouldBe(16);
        square(4).ShouldBe(16);
        square(4).ShouldBe(16);

        calls.ShouldBe(1);
    }

    [Fact]
    public void Memoize_computes_each_distinct_argument()
    {
        var calls = 0;
        Func<int, int> square = Lab05Delegates.Memoize<int, int>(n => { calls++; return n * n; });

        square(2).ShouldBe(4);
        square(3).ShouldBe(9);
        square(2).ShouldBe(4);

        calls.ShouldBe(2);
    }

    [Fact]
    public void Memoize_caches_null_results()
    {
        var calls = 0;
        Func<int, string?> lookup = Lab05Delegates.Memoize<int, string?>(_ => { calls++; return null; });

        lookup(1).ShouldBeNull();
        lookup(1).ShouldBeNull();

        calls.ShouldBe(1, "a cached null is a result, not a miss");
    }

    [Fact]
    public void Memoize_gives_each_wrapper_its_own_cache()
    {
        var calls = 0;
        Func<int, int> Make() => Lab05Delegates.Memoize<int, int>(n => { calls++; return n; });

        Func<int, int> first = Make();
        Func<int, int> second = Make();

        first(1);
        second(1);

        calls.ShouldBe(2, "two separately memoised functions must not share a cache");
    }

    // ── Exercise 3 — the capture bug ──────────────────────────────────────────────────

    [Fact]
    public void MakeCounters_each_function_returns_its_own_index()
    {
        IReadOnlyList<Func<int>> counters = Lab05Delegates.MakeCounters(3);

        counters.Count.ShouldBe(3);
        counters.Select(f => f()).ShouldBe([0, 1, 2], "not [3, 3, 3]");
    }

    [Fact]
    public void MakeCounters_is_stable_across_calls()
    {
        IReadOnlyList<Func<int>> counters = Lab05Delegates.MakeCounters(2);

        counters[1]().ShouldBe(1);
        counters[1]().ShouldBe(1);
        counters[0]().ShouldBe(0);
    }

    [Fact]
    public void MakeCounters_zero_is_empty()
        => Lab05Delegates.MakeCounters(0).ShouldBeEmpty();

    // ── Exercise 4 — Largest ──────────────────────────────────────────────────────────

    [Fact]
    public void Largest_of_ints()
        => Lab05Delegates.Largest([3, 17, 4, 1]).ShouldBe(17);

    [Fact]
    public void Largest_of_strings_uses_the_types_own_ordering()
        => Lab05Delegates.Largest(["alfa", "zulu", "mike"]).ShouldBe("zulu");

    [Fact]
    public void Largest_of_one()
        => Lab05Delegates.Largest([42]).ShouldBe(42);

    [Fact]
    public void Largest_of_empty_throws_rather_than_returning_default()
        => Should.Throw<InvalidOperationException>(() => Lab05Delegates.Largest(Array.Empty<int>()));

    [Fact]
    public void Largest_enumerates_the_source_exactly_once()
    {
        var enumerations = 0;
        IEnumerable<int> Source()
        {
            enumerations++;
            yield return 5;
            yield return 9;
            yield return 2;
        }

        Lab05Delegates.Largest(Source()).ShouldBe(9);
        enumerations.ShouldBe(1, "the source may be a query or a stream");
    }

    // ── Exercise 5 — the event ────────────────────────────────────────────────────────

    [Fact]
    public void StockLow_is_declared_as_an_event_not_a_public_delegate_field()
    {
        Type type = typeof(Lab05Delegates.StockWatcher);

        type.GetEvent("StockLow").ShouldNotBeNull("callers must only be able to += and -=");
        type.GetField("StockLow", BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeNull("a public delegate field would let any caller raise or wipe it");
    }

    [Fact]
    public void Report_raises_when_below_the_threshold()
    {
        var watcher = new Lab05Delegates.StockWatcher();
        Lab05Delegates.StockLowEventArgs? received = null;
        object? sender = null;

        watcher.StockLow += (s, e) => { sender = s; received = e; };
        watcher.Report("ELE-100001", remaining: 2, threshold: 5);

        received.ShouldNotBeNull();
        received!.Sku.ShouldBe("ELE-100001");
        received.Remaining.ShouldBe(2);
        sender.ShouldBeSameAs(watcher);
    }

    [Fact]
    public void Report_is_silent_at_or_above_the_threshold()
    {
        var watcher = new Lab05Delegates.StockWatcher();
        var raised = 0;
        watcher.StockLow += (_, _) => raised++;

        watcher.Report("ELE-100001", remaining: 5, threshold: 5);
        watcher.Report("ELE-100001", remaining: 9, threshold: 5);

        raised.ShouldBe(0, "strictly below, not at or below");
    }

    [Fact]
    public void Report_with_no_subscribers_does_not_throw()
    {
        var watcher = new Lab05Delegates.StockWatcher();

        Should.NotThrow(() => watcher.Report("ELE-100001", remaining: 0, threshold: 5));
    }

    [Fact]
    public void Every_subscriber_is_called_in_subscription_order()
    {
        var watcher = new Lab05Delegates.StockWatcher();
        var order = new List<string>();

        watcher.StockLow += (_, _) => order.Add("first");
        watcher.StockLow += (_, _) => order.Add("second");
        watcher.StockLow += (_, _) => order.Add("third");

        watcher.Report("ELE-100001", remaining: 1, threshold: 5);

        order.ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public void Unsubscribing_stops_that_handler_and_leaves_the_others()
    {
        var watcher = new Lab05Delegates.StockWatcher();
        var kept = 0;
        var removed = 0;

        void Removed(object? s, Lab05Delegates.StockLowEventArgs e) => removed++;

        watcher.StockLow += (_, _) => kept++;
        watcher.StockLow += Removed;

        watcher.Report("A", 1, 5);
        watcher.StockLow -= Removed;
        watcher.Report("A", 1, 5);

        kept.ShouldBe(2);
        removed.ShouldBe(1, "the unsubscribed handler must not run again");
    }
}
