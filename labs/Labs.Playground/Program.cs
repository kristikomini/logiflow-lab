using System.Diagnostics;

namespace Labs.Playground;

/// <summary>
/// Small runnable demos that print what they are doing.
/// </summary>
/// <remarks>
/// Reading that "LINQ is lazy" is not the same as watching the evaluation order print out of
/// order in your own terminal. Run these:
/// <code>
/// cd labs/Labs.Playground
/// dotnet run                    # lists every demo
/// dotnet run deferred           # runs one
/// </code>
/// </remarks>
public static partial class Program
{
    /// <summary>One runnable demo.</summary>
    /// <param name="Name">The argument you type after <c>dotnet run</c>.</param>
    /// <param name="Category">Which part of the course it belongs to.</param>
    /// <param name="Description">One line, printed by <c>dotnet run list</c>.</param>
    /// <param name="Run">The demo itself.</param>
    private sealed record Demo(string Name, string Category, string Description, Action Run);

    /// <summary>Adapts an async demo to the registry's one shape.</summary>
    /// <remarks>
    /// <para>
    /// Yes, this blocks on a <see cref="Task"/> — the thing the <c>deadlock</c> demo exists to
    /// warn you about. It is safe *here* and nowhere else in this repository, for one reason:
    /// a console application has no <see cref="SynchronizationContext"/>, so the continuation
    /// resumes on a thread-pool thread rather than queueing behind the thread that is waiting.
    /// </para>
    /// <para>
    /// The rule that survives outside this file is the one the demo teaches: block only at the
    /// very top of the stack, where you own the thread and nobody is waiting behind you. In a
    /// UI handler or a classic ASP.NET request this same line deadlocks.
    /// </para>
    /// </remarks>
    private static Action Async(Func<Task> run) => () => run().GetAwaiter().GetResult();

    private static readonly Demo[] Demos =
    [
        // ── The language ───────────────────────────────────────────────────────────────
        new("deferred",   "language",    "A query is a recipe, not a result", DeferredExecution),
        new("closures",   "language",    "Closure capture: the classic loop-variable trap", ClosureCapture),
        new("structs",    "language",    "Value vs reference semantics, and the mutable-struct trap", ValueVsReference),
        new("strings",    "language",    "Interning, == vs ReferenceEquals, and StringBuilder", Strings),
        new("boxing",     "language",    "Boxing: where hidden allocations come from", Boxing),
        new("equality",   "language",    "The five kinds of equality, and when each one fires", Equality),
        new("hashcode",   "language",    "The GetHashCode contract, and the mutable-key trap", HashCodes),
        new("numbers",    "language",    "float, double, decimal, overflow, and money", Numbers),
        new("variance",   "language",    "Array covariance is a runtime bug; generic variance is not", Variance),
        new("generics",   "language",    "What the JIT does with T, and where the boxing hides", Generics),
        new("iterators",  "language",    "yield return: the state machine, and the deferred throw", Iterators),
        new("expressions","language",    "Func<T> vs Expression<Func<T>>: code vs data", Expressions),

        // ── Memory and the GC ──────────────────────────────────────────────────────────
        new("gc",         "memory",      "Generations, promotion, and what a collection costs", GarbageCollection),
        new("finalizers", "memory",      "Finalizers are not destructors, and run when they like", Finalizers),
        new("disposal",   "memory",      "using, dispose order, and IAsyncDisposable", Disposal),
        new("pooling",    "memory",      "ArrayPool<T>, and the dirty-buffer trap", Pooling),
        new("leaks",      "memory",      "How a managed language still leaks: events and statics", Leaks),
        new("spans",      "memory",      "Span<T>: slicing without allocating", Spans),

        // ── Concurrency ────────────────────────────────────────────────────────────────
        new("async",      "concurrency", "Async is not threads: what await actually does", AsyncBasics),
        new("race",       "concurrency", "A lost update, and the three ways to stop it", RaceCondition),
        new("context",    "concurrency", "SynchronizationContext, and what ConfigureAwait(false) does", Contexts),
        new("deadlock",   "concurrency", "Sync-over-async: the deadlock, reproduced safely", Deadlock),
        new("threadpool", "concurrency", "Thread-pool starvation, watched live", ThreadPoolStarvation),
        new("valuetask",  "concurrency", "ValueTask: the consume-once rule", ValueTasks),
        new("retry",      "concurrency", "Retry storms: why jitter is not optional", RetryStorm),

        // ── Runtime behaviour that bites in production ─────────────────────────────────
        new("culture",    "runtime",     "The Turkish I, and why an Italian machine parses 1.5 as 15", Culture),
        new("datetime",   "runtime",     "DateTime, DateTimeOffset, DST, and TimeProvider", DateAndTime),
        new("json",       "runtime",     "System.Text.Json: the four traps", Json),
        new("exceptions", "runtime",     "throw vs throw ex, filters, and what an exception costs", Exceptions),
        new("reflection", "runtime",     "Reflection: the metadata lookup, the boxing, and the cache that kills both", Reflection),

        // ── Industrial: where the software meets the machines ──────────────────────────
        new("modbus",     "industrial",  "Modbus TCP by hand: the wire has no types", Async(ModbusAsync)),
        new("tags",       "industrial",  "Polling loses events, and the machine will not wait for you", Async(TagsAsync)),
        new("oee",        "industrial",  "OEE from the event stream, and the argument underneath it", Oee),
        new("traffic",    "industrial",  "Two AGVs, one aisle: the deadlock and the one-line fix", Async(TrafficAsync)),
        new("serial",     "industrial",  "A byte stream has no messages: framing, and the RS-485 party line", Async(SerialAsync)),
        new("can",        "industrial",  "CAN: arbitration by identifier, and bits packed at an offset", Can),
    ];

    /// <summary>Entry point.</summary>
    /// <param name="args">The demo name, or nothing to list them.</param>
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "list" or "--help" or "-h")
        {
            Console.WriteLine("LogiFlow playground — runnable demos\n");

            foreach (IGrouping<string, Demo> group in Demos.GroupBy(d => d.Category))
            {
                Console.WriteLine($"  ── {group.Key} ──────────────────────────────────────────");
                foreach (Demo d in group)
                {
                    Console.WriteLine($"     {d.Name,-12} {d.Description}");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"Usage: dotnet run <name>          ({Demos.Length} demos)");
            Console.WriteLine("       dotnet run all              (run every one, in order)");
            return;
        }

        if (args[0] is "all")
        {
            foreach (Demo d in Demos)
            {
                Console.WriteLine($"\n╔═══ {d.Name} — {d.Description}\n");
                d.Run();
            }

            return;
        }

        Demo? demo = Array.Find(Demos, d => string.Equals(d.Name, args[0], StringComparison.OrdinalIgnoreCase));

        if (demo is null)
        {
            Console.WriteLine($"Unknown demo '{args[0]}'. Run with no arguments to list them.");
            return;
        }

        Console.WriteLine($"── {demo.Description} ──\n");
        demo.Run();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void DeferredExecution()
    {
        int[] numbers = [1, 2, 3, 4, 5];

        Console.WriteLine("Building the query (nothing should print yet)...");

        IEnumerable<int> query = numbers.Where(n =>
        {
            Console.WriteLine($"  ...evaluating {n}");
            return n % 2 == 0;
        });

        Console.WriteLine("Query built. Notice: NOTHING was evaluated.\n");

        Console.WriteLine("First enumeration:");
        Console.WriteLine($"  result: [{string.Join(", ", query)}]\n");

        Console.WriteLine("Second enumeration — it runs AGAIN:");
        Console.WriteLine($"  result: [{string.Join(", ", query)}]\n");

        Console.WriteLine("This is why you materialise once with ToList() when you enumerate twice.");
        Console.WriteLine("Against a database, that second pass is a second round trip.\n");

        // The other half of the trap: the query sees changes made AFTER it was defined.
        var list = new List<int> { 1, 2, 3 };
        IEnumerable<int> live = list.Where(n => n > 1);

        Console.WriteLine($"query over [1,2,3] gives: [{string.Join(", ", live)}]");
        list.Add(99);
        Console.WriteLine($"after list.Add(99):       [{string.Join(", ", live)}]  <- the query re-ran");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void ClosureCapture()
    {
        Console.WriteLine("A `for` loop captures ONE shared variable:\n");

        List<Func<int>> forLoop = [];
        for (int i = 0; i < 3; i++)
        {
            forLoop.Add(() => i);
        }

        Console.WriteLine($"  results: [{string.Join(", ", forLoop.Select(f => f()))}]");
        Console.WriteLine("  Every lambda closed over the SAME `i`, which is now 3.\n");

        Console.WriteLine("The fix — copy into a variable scoped INSIDE the loop:\n");

        List<Func<int>> fixedLoop = [];
        for (int i = 0; i < 3; i++)
        {
            int copy = i;
            fixedLoop.Add(() => copy);
        }

        Console.WriteLine($"  results: [{string.Join(", ", fixedLoop.Select(f => f()))}]\n");

        Console.WriteLine("`foreach` was CHANGED in C# 5 to declare a fresh variable per iteration:\n");

        List<Func<int>> foreachLoop = [];
        foreach (int i in Enumerable.Range(0, 3))
        {
            foreachLoop.Add(() => i);
        }

        Console.WriteLine($"  results: [{string.Join(", ", foreachLoop.Select(f => f()))}]");
        Console.WriteLine("  ...so foreach is safe, and `for` still is not. See Dispatcher.cs,");
        Console.WriteLine("  where the pipeline is built with a `for` loop and copies both locals.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private struct MutablePoint
    {
        public int X;

        public void Increment() => X++;
    }

    private sealed class MutableBox
    {
        public int X;
    }

    private static void ValueVsReference()
    {
        var s1 = new MutablePoint { X = 1 };
        MutablePoint s2 = s1;
        s2.X = 99;
        Console.WriteLine($"struct:  s1.X={s1.X}, s2.X={s2.X}   <- assignment COPIED");

        var c1 = new MutableBox { X = 1 };
        MutableBox c2 = c1;
        c2.X = 99;
        Console.WriteLine($"class:   c1.X={c1.X}, c2.X={c2.X}  <- both point at one object\n");

        Console.WriteLine("The mutable-struct trap — a struct in a List:");
        var list = new List<MutablePoint> { new() { X = 1 } };

        // list[0] returns a COPY. Incrementing it changes the copy and throws the result away.
        MutablePoint copy = list[0];
        copy.Increment();

        Console.WriteLine($"  after copy.Increment(): list[0].X = {list[0].X}  <- unchanged!");
        Console.WriteLine("  This is why mutable structs are discouraged. Make them `readonly`");
        Console.WriteLine("  and the compiler stops you writing this bug at all.\n");

        Console.WriteLine("An array behaves differently, which makes it worse:");
        MutablePoint[] array = [new() { X = 1 }];
        array[0].Increment();   // arrays give a REFERENCE to the slot, so this works
        Console.WriteLine($"  after array[0].Increment(): array[0].X = {array[0].X}  <- changed!");
        Console.WriteLine("  Same syntax, opposite behaviour, depending on the collection type.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Strings()
    {
        const string a = "hello";
        const string b = "hello";
        string c = new(['h', 'e', 'l', 'l', 'o']);

        Console.WriteLine($"a == b                      : {a == b}    (string == compares VALUE)");
        Console.WriteLine($"ReferenceEquals(a, b)       : {ReferenceEquals(a, b)}    (literals are interned - same object)");
        Console.WriteLine($"c == a                      : {c == a}    (equal value...)");
        Console.WriteLine($"ReferenceEquals(c, a)       : {ReferenceEquals(c, a)}   (...different object)\n");

        Console.WriteLine("Strings are IMMUTABLE. Every 'modification' allocates a new one:\n");

        string s = "";
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            s += "x";   // allocates a new string EVERY iteration: O(n^2) bytes
        }

        long concatBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append('x');
        }

        _ = sb.ToString();
        long builderBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"  1000x  s += \"x\"        : {concatBytes,10:N0} bytes");
        Console.WriteLine($"  1000x  sb.Append('x')  : {builderBytes,10:N0} bytes");
        Console.WriteLine($"  ratio                  : {(double)concatBytes / builderBytes:N0}x more\n");
        Console.WriteLine("  For 3 concatenations, += is fine and clearer. In a loop, it is a bug.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void AsyncBasics()
    {
        Console.WriteLine("async does NOT mean 'on another thread'.\n");

        Console.WriteLine($"[{Environment.CurrentManagedThreadId}] before");

        Task task = DemoAsync();
        Console.WriteLine($"[{Environment.CurrentManagedThreadId}] after calling DemoAsync — it already returned");

        task.GetAwaiter().GetResult();

        Console.WriteLine("\nWhat happened: DemoAsync ran SYNCHRONOUSLY until the first await.");
        Console.WriteLine("At the await it returned an incomplete Task to us. The rest of the");
        Console.WriteLine("method was scheduled as a continuation and ran later — possibly on a");
        Console.WriteLine("different thread, possibly the same one. No thread was blocked waiting.\n");

        Console.WriteLine("That is the whole point: async frees threads, it does not create them.");
        Console.WriteLine("A server handling 10,000 idle connections needs ~0 threads, not 10,000.");

        static async Task DemoAsync()
        {
            Console.WriteLine($"[{Environment.CurrentManagedThreadId}]   inside, before await (synchronous)");
            await Task.Delay(50);
            Console.WriteLine($"[{Environment.CurrentManagedThreadId}]   inside, after await (continuation)");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Spans()
    {
        const string csv = "ELE-100001,Wireless Scanner,189.00,320";

        Console.WriteLine($"Parsing: {csv}\n");

        long before = GC.GetAllocatedBytesForCurrentThread();
        string[] parts = csv.Split(',');
        long splitBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"  string.Split      : {splitBytes,6:N0} bytes  (an array + {parts.Length} new strings)");

        before = GC.GetAllocatedBytesForCurrentThread();
        ReadOnlySpan<char> span = csv.AsSpan();
        int fields = 0;
        while (!span.IsEmpty)
        {
            int comma = span.IndexOf(',');
            ReadOnlySpan<char> field = comma < 0 ? span : span[..comma];
            fields++;
            _ = field.Length;
            span = comma < 0 ? [] : span[(comma + 1)..];
        }

        long spanBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"  ReadOnlySpan slice: {spanBytes,6:N0} bytes  ({fields} fields, zero copies)\n");
        Console.WriteLine("  A span is a pointer plus a length. Slicing it copies nothing.");
        Console.WriteLine("  Irrelevant once. Decisive when parsing a million rows.");
        Console.WriteLine("\n  The catch: a Span<T> lives on the stack, so it cannot be a field of a");
        Console.WriteLine("  class, cannot be captured by a lambda, and cannot cross an `await`.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Boxing()
    {
        Console.WriteLine("Boxing = wrapping a value type in a heap object to treat it as `object`.\n");

        long before = GC.GetAllocatedBytesForCurrentThread();
        object boxed = 42;              // allocates
        int unboxed = (int)boxed;       // copies back out
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"  boxing one int: {bytes} bytes (value {unboxed})\n");

        Console.WriteLine("Where it hides in real code:\n");

        long b1 = GC.GetAllocatedBytesForCurrentThread();
        System.Collections.ArrayList untyped = [];
        for (int i = 0; i < 100_000; i++)
        {
            untyped.Add(i);             // every int is boxed into its own heap object
        }

        long untypedBytes = GC.GetAllocatedBytesForCurrentThread() - b1;

        long b2 = GC.GetAllocatedBytesForCurrentThread();
        List<int> typed = [];
        for (int i = 0; i < 100_000; i++)
        {
            typed.Add(i);               // stored inline in an int[]
        }

        long typedBytes = GC.GetAllocatedBytesForCurrentThread() - b2;

        Console.WriteLine($"  ArrayList  (boxes) : {untypedBytes,10:N0} bytes");
        Console.WriteLine($"  List<int>  (no box): {typedBytes,10:N0} bytes");
        Console.WriteLine($"  ratio              : {(double)untypedBytes / typedBytes,10:N1}x\n");

        Console.WriteLine("  This is why generics were added in C# 2.0. Not for the syntax — for this.\n");

        Console.WriteLine("  NOTE: no timings here, on purpose. Measuring elapsed time in a plain");
        Console.WriteLine("  console app gives you the JIT warming up, not the code — whichever loop");
        Console.WriteLine("  runs first looks slower. Allocation counts are reliable because they are");
        Console.WriteLine("  exact; timings need BenchmarkDotNet, which warms up and runs many");
        Console.WriteLine("  iterations. See labs/Labs.Benchmarks for the timed version.");
    }
}
