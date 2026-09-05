using System.Collections;
using System.Collections.Frozen;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace Labs.Benchmarks;

/// <summary>
/// Entry point. Benchmarks must run in Release or BenchmarkDotNet refuses.
/// </summary>
/// <remarks>
/// <code>
/// cd labs/Labs.Benchmarks
/// dotnet run -c Release                 # pick from a menu
/// dotnet run -c Release --filter *Linq* # run one class
/// </code>
/// </remarks>
public static class Program
{
    /// <summary>Runs the benchmark switcher.</summary>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>
/// Why measuring beats guessing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every performance claim in this course is checkable here.</b> That matters because
/// intuition about .NET performance is frequently wrong, and "I read somewhere that X is
/// faster" is how codebases acquire unreadable micro-optimisations that make no difference.
/// </para>
/// <para>
/// <b>What BenchmarkDotNet does that a Stopwatch cannot:</b> it runs a warm-up phase so the JIT
/// has compiled and tiered-up your code, it runs enough iterations to get a statistically
/// meaningful result, it reports variance so you can tell signal from noise, it prevents the
/// JIT from optimising your benchmark away entirely (a real hazard — dead code elimination will
/// happily delete a loop whose result you never use), and with
/// <c>[MemoryDiagnoser]</c> it reports allocations exactly.
/// </para>
/// <para>
/// <b>Read the results properly:</b> the <c>Mean</c> column matters far less than
/// <c>Allocated</c> in most server code. A method that is 20ns slower but allocates nothing
/// beats one that is faster and produces garbage, because GC pauses hit every request, not
/// just this one.
/// </para>
/// Covered in: <c>course/module-14-performance/</c>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class LinqBenchmarks
{
    private int[] _numbers = [];
    private List<string> _words = [];

    /// <summary>How many items each benchmark works over.</summary>
    [Params(1_000, 100_000)]
    public int Size { get; set; }

    /// <summary>Builds the input. Excluded from the measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);   // fixed seed: every run measures the same data
        _numbers = [.. Enumerable.Range(0, Size).Select(_ => random.Next(0, 1000))];
        _words = [.. Enumerable.Range(0, Size).Select(i => $"item-{i}")];
    }

    // ── Sum: LINQ vs a hand-written loop ────────────────────────────────────────────────

    /// <summary>LINQ <c>Sum</c>.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Sum")]
    public long Linq_Sum() => _numbers.Sum(n => (long)n);

    /// <summary>A plain foreach.</summary>
    /// <remarks>
    /// Usually a little faster — no delegate invocation per element and no enumerator
    /// allocation. The gap is small enough that it almost never justifies giving up
    /// readability. Measure before you rewrite.
    /// </remarks>
    [Benchmark, BenchmarkCategory("Sum")]
    public long Loop_Sum()
    {
        long total = 0;
        foreach (int n in _numbers)
        {
            total += n;
        }

        return total;
    }

    /// <summary>Indexed for-loop over the array.</summary>
    /// <remarks>
    /// Iterating an array by index lets the JIT eliminate bounds checks entirely when the
    /// pattern is recognisable (<c>i &lt; array.Length</c>). Write <c>i &lt; _numbers.Length</c>,
    /// not a cached length variable — the cached version actually defeats the optimisation.
    /// </remarks>
    [Benchmark, BenchmarkCategory("Sum")]
    public long For_Sum()
    {
        long total = 0;
        for (int i = 0; i < _numbers.Length; i++)
        {
            total += _numbers[i];
        }

        return total;
    }

    // ── Lookup: the one that actually matters ───────────────────────────────────────────

    /// <summary>Linear scan with <c>Contains</c>.</summary>
    /// <remarks>
    /// O(n) per lookup. This is the shape of the accidental O(n²) that appears whenever someone
    /// writes <c>items.Where(i =&gt; otherList.Contains(i.Id))</c>. At Size=100,000 the
    /// difference here is not a micro-optimisation — it is the difference between a page that
    /// loads and one that times out.
    /// </remarks>
    [Benchmark(Baseline = true), BenchmarkCategory("Lookup")]
    public int List_Contains()
    {
        int found = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (_words.Contains($"item-{i}", StringComparer.Ordinal))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>Hash-set lookup.</summary>
    [Benchmark, BenchmarkCategory("Lookup")]
    public int HashSet_Contains()
    {
        var set = new HashSet<string>(_words, StringComparer.Ordinal);

        int found = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (set.Contains($"item-{i}"))
            {
                found++;
            }
        }

        return found;
    }

    // ── First vs Single ─────────────────────────────────────────────────────────────────

    /// <summary><c>First</c> stops at the first match.</summary>
    [Benchmark, BenchmarkCategory("FirstVsSingle")]
    public int Linq_First() => _numbers.First(n => n > 500);

    /// <summary>
    /// <c>Single</c> must keep scanning to prove there is no second match.
    /// </summary>
    /// <remarks>
    /// Worth internalising: <c>Single</c> is not a stricter <c>First</c>, it is a full scan.
    /// Use it when "exactly one" is a genuine invariant you want enforced; use <c>First</c>
    /// when you just want the first one.
    /// </remarks>
    [Benchmark, BenchmarkCategory("FirstVsSingle")]
    public int Linq_SingleOrDefault() => _numbers.Count(n => n > 990) == 1 ? 1 : 0;
}

/// <summary>String building strategies.</summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class StringBenchmarks
{
    private string[] _parts = [];

    /// <summary>How many fragments to join.</summary>
    [Params(10, 1_000)]
    public int Count { get; set; }

    /// <summary>Builds the input.</summary>
    [GlobalSetup]
    public void Setup() => _parts = [.. Enumerable.Range(0, Count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>
    /// Naive concatenation. O(n²) in bytes copied.
    /// </summary>
    /// <remarks>
    /// Note how the <c>Allocated</c> column explodes with <c>Count</c> while the others stay
    /// flat. That column, not the timing, is the thing to look at.
    /// </remarks>
    [Benchmark(Baseline = true)]
    public string Concatenation()
    {
        string result = string.Empty;
        foreach (string part in _parts)
        {
            result += part;
        }

        return result;
    }

    /// <summary><see cref="StringBuilder"/>: one growing buffer.</summary>
    [Benchmark]
    public string Builder()
    {
        var sb = new StringBuilder();
        foreach (string part in _parts)
        {
            sb.Append(part);
        }

        return sb.ToString();
    }

    /// <summary>
    /// <see cref="string.Concat(string[])"/>: the framework knows the total length up front.
    /// </summary>
    /// <remarks>
    /// The fastest option when you already have the pieces in a collection, because it sizes
    /// the destination buffer exactly once. Reach for it before StringBuilder when the input
    /// is already materialised.
    /// </remarks>
    [Benchmark]
    public string Concat() => string.Concat(_parts);

    /// <summary><see cref="string.Join(string, string[])"/> with no separator.</summary>
    [Benchmark]
    public string Join() => string.Join(string.Empty, _parts);
}

/// <summary>Dictionary variants for lookup-heavy, write-once data.</summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class DictionaryBenchmarks
{
    private Dictionary<string, int> _dictionary = [];
    private FrozenDictionary<string, int> _frozen = FrozenDictionary<string, int>.Empty;
    private Hashtable _hashtable = [];
    private string[] _keys = [];

    /// <summary>How many entries.</summary>
    [Params(100, 10_000)]
    public int Size { get; set; }

    /// <summary>Builds the inputs.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _keys = [.. Enumerable.Range(0, Size).Select(i => $"key-{i}")];
        _dictionary = _keys.ToDictionary(k => k, k => k.Length, StringComparer.Ordinal);
        _frozen = _dictionary.ToFrozenDictionary(StringComparer.Ordinal);

        _hashtable = [];
        foreach (string key in _keys)
        {
            _hashtable[key] = key.Length;
        }
    }

    /// <summary>Standard dictionary lookup.</summary>
    [Benchmark(Baseline = true)]
    public int Dictionary_Lookup()
    {
        int total = 0;
        foreach (string key in _keys)
        {
            total += _dictionary[key];
        }

        return total;
    }

    /// <summary>
    /// <see cref="FrozenDictionary{TKey,TValue}"/>: slower to build, faster to read.
    /// </summary>
    /// <remarks>
    /// Added in .NET 8. It analyses the keys at construction time to pick an optimal lookup
    /// strategy, so reads get measurably faster in exchange for a much more expensive build.
    /// Exactly the right trade for static configuration — which is why
    /// <c>OrderStateMachine</c> and the outbox type allow-list both use one — and exactly the
    /// wrong trade for anything you mutate.
    /// </remarks>
    [Benchmark]
    public int Frozen_Lookup()
    {
        int total = 0;
        foreach (string key in _keys)
        {
            total += _frozen[key];
        }

        return total;
    }

    /// <summary>
    /// Non-generic <see cref="Hashtable"/>: boxes every value.
    /// </summary>
    /// <remarks>
    /// Included to make the cost of boxing visible in the <c>Allocated</c> column. Pre-generics
    /// code is full of these; you will meet them in old codebases.
    /// </remarks>
    [Benchmark]
    public int Hashtable_Lookup()
    {
        int total = 0;
        foreach (string key in _keys)
        {
            total += (int)_hashtable[key]!;   // unboxing on every read
        }

        return total;
    }
}

/// <summary>The cost of exceptions versus a Result type.</summary>
/// <remarks>
/// <para>
/// This is the measurement behind <c>LogiFlow.Domain.Results.Result</c>. Run it before arguing
/// either side: the ratio is usually a shock the first time.
/// </para>
/// <para>
/// Note what is being measured: <b>throwing</b>, not <c>try/catch</c> itself. A try block that
/// never throws costs essentially nothing. The expense is in capturing a stack trace and
/// unwinding — which is precisely why exceptions are fine for exceptional cases and ruinous
/// in a validation loop.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ErrorHandlingBenchmarks
{
    /// <summary>How many failures to process.</summary>
    [Params(1_000)]
    public int Failures { get; set; }

    /// <summary>Signalling failure by throwing.</summary>
    [Benchmark(Baseline = true)]
    public int WithExceptions()
    {
        int handled = 0;
        for (int i = 0; i < Failures; i++)
        {
            try
            {
                throw new InvalidOperationException("Business rule violated");
            }
            catch (InvalidOperationException)
            {
                handled++;
            }
        }

        return handled;
    }

    /// <summary>Signalling failure by returning a value.</summary>
    [Benchmark]
    public int WithResults()
    {
        int handled = 0;
        for (int i = 0; i < Failures; i++)
        {
            (bool ok, string _) = Attempt();
            if (!ok)
            {
                handled++;
            }
        }

        return handled;

        static (bool Ok, string Error) Attempt() => (false, "Business rule violated");
    }
}
