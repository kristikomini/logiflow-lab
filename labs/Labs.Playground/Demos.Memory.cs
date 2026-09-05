using System.Buffers;

namespace Labs.Playground;

/// <summary>
/// Demos for memory and the garbage collector — generations, finalizers, disposal,
/// pooling, and the leaks a managed language still allows.
/// </summary>
public static partial class Program
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  gc — generations, promotion, and what a collection costs
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void GarbageCollection()
    {
        Console.WriteLine("The heap is generational because of one empirical fact:");
        Console.WriteLine("MOST OBJECTS DIE YOUNG. The GC is built to exploit that.\n");

        Console.WriteLine("   gen 0  — the nursery. Collected constantly, in under a millisecond.");
        Console.WriteLine("   gen 1  — survived one collection. A buffer between 0 and 2.");
        Console.WriteLine("   gen 2  — survived twice. Collected rarely, and expensively.");
        Console.WriteLine("   LOH    — >= 85,000 bytes. Allocated straight into gen 2, and by");
        Console.WriteLine("            default never compacted.\n");

        Console.WriteLine($"   Server GC     : {System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine($"   Latency mode  : {System.Runtime.GCSettings.LatencyMode}");
        Console.WriteLine($"   Logical cores : {Environment.ProcessorCount}\n");

        Console.WriteLine("── Where does a new object live? ────────────────────────────\n");

        var small = new byte[1000];
        var large = new byte[100_000];

        Console.WriteLine($"   new byte[1000]     -> generation {GC.GetGeneration(small)}");
        Console.WriteLine($"   new byte[100_000]  -> generation {GC.GetGeneration(large)}   <- the LOH, straight to gen 2");
        Console.WriteLine("   85,000 bytes is roughly a 21,250-element int[], or a 42,500-char string.");
        Console.WriteLine("   Cross that line in a hot path and every one of those buffers is a gen-2");
        Console.WriteLine("   object you will pay to collect.\n");

        Console.WriteLine("── Promotion, watched live ──────────────────────────────────\n");

        var survivor = new byte[100];
        Console.WriteLine($"   fresh object                : gen {GC.GetGeneration(survivor)}");

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"   after one gen-0 collection  : gen {GC.GetGeneration(survivor)}   <- promoted, because it was still reachable");

        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"   after one gen-1 collection  : gen {GC.GetGeneration(survivor)}   <- promoted again\n");

        Console.WriteLine("   THAT is what a memory leak looks like to the GC: not a failure to free,");
        Console.WriteLine("   but an object that is still referenced, promoted to gen 2, and now only");
        Console.WriteLine("   examined during the expensive collections.\n");

        Console.WriteLine("── The counters worth knowing ───────────────────────────────\n");

        long before0 = GC.CollectionCount(0);
        long beforeBytes = GC.GetTotalAllocatedBytes(precise: false);

        for (int i = 0; i < 200_000; i++)
        {
            _ = new byte[100];      // garbage, immediately unreachable
        }

        long after0 = GC.CollectionCount(0);
        long afterBytes = GC.GetTotalAllocatedBytes(precise: false);

        Console.WriteLine($"   allocated              : {afterBytes - beforeBytes,12:N0} bytes");
        Console.WriteLine($"   gen-0 collections      : {after0 - before0,12:N0}");
        Console.WriteLine($"   gen-1 collections      : {GC.CollectionCount(1),12:N0}  (total for the process)");
        Console.WriteLine($"   gen-2 collections      : {GC.CollectionCount(2),12:N0}  (total for the process)");
        Console.WriteLine($"   heap size now          : {GC.GetTotalMemory(forceFullCollection: false),12:N0} bytes\n");

        Console.WriteLine("   20 MB of garbage, a handful of cheap collections, and the heap did not");
        Console.WriteLine("   grow. Allocation in .NET is a pointer bump; DEALLOCATION of short-lived");
        Console.WriteLine("   objects is very nearly free, because the collector copies out the few");
        Console.WriteLine("   survivors and resets the pointer.\n");

        Console.WriteLine("── The rule this gives you ──────────────────────────────────\n");
        Console.WriteLine("   Allocating in a hot loop is not automatically wrong. Allocating objects");
        Console.WriteLine("   that SURVIVE is what costs — a cache, a static list, a captured closure");
        Console.WriteLine("   held by an event. Short-lived garbage is the case the GC was designed");
        Console.WriteLine("   for. Long-lived garbage is the case that pages your on-call engineer.\n");

        Console.WriteLine("   And never call GC.Collect() in production. Every call in this demo is");
        Console.WriteLine("   here to make the machine observable, which is the only good reason.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  finalizers — not destructors, and they run when they like
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class HasFinalizer
    {
        private readonly string _name;

        public HasFinalizer(string name) => _name = name;

        ~HasFinalizer() => Console.WriteLine($"      [finalizer] {_name} finalized — on the finalizer thread, whenever the runtime chose to");
    }

    private sealed class Proper : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;         // Dispose must be idempotent — it can legally be called twice
            }

            _disposed = true;
            Console.WriteLine("      [Dispose] released, deterministically, right now");
            GC.SuppressFinalize(this);
        }
    }

    private static void Finalizers()
    {
        Console.WriteLine("C# has no destructors. `~Foo()` declares a FINALIZER, which is different:\n");
        Console.WriteLine("   - it does not run at end of scope;");
        Console.WriteLine("   - it runs on a dedicated finalizer thread, later, maybe;");
        Console.WriteLine("   - it makes the object survive at least one extra collection;");
        Console.WriteLine("   - it may not run at all before the process exits.\n");

        Console.WriteLine("── Watch the non-determinism ────────────────────────────────\n");

        Console.WriteLine("   creating an object with a finalizer, then dropping the reference...");
        MakeGarbage();
        Console.WriteLine("   ...reference dropped. Nothing has been finalized.\n");

        Console.WriteLine("   forcing a collection:");
        GC.Collect();
        Console.WriteLine("   ...collected. STILL nothing printed — the object was only QUEUED.\n");

        Console.WriteLine("   waiting for the finalizer thread:");
        GC.WaitForPendingFinalizers();
        Console.WriteLine();

        Console.WriteLine("   That took a collection, a queue, a second thread, and an explicit wait.");
        Console.WriteLine("   In production none of those are things you control.\n");

        Console.WriteLine("── The deterministic alternative ────────────────────────────\n");

        using (var p = new Proper())
        {
            Console.WriteLine("   inside the using block");
        }

        Console.WriteLine("\n   THE RULE: a finalizer is a safety net for UNMANAGED memory only — a");
        Console.WriteLine("   file handle, a socket, a native pointer. Every other cleanup belongs in");
        Console.WriteLine("   Dispose. If you write a finalizer you also write Dispose, and Dispose");
        Console.WriteLine("   calls GC.SuppressFinalize(this) so the object stops paying for a net it");
        Console.WriteLine("   no longer needs.\n");

        Console.WriteLine("   And never touch another managed object from a finalizer: at that point");
        Console.WriteLine("   it may already have been finalized itself. Order is not guaranteed.");

        static void MakeGarbage() => _ = new HasFinalizer("scanner-connection");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  disposal — using, order, and IAsyncDisposable
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class Noisy(string name) : IDisposable
    {
        public void Dispose() => Console.WriteLine($"      disposed {name}");
    }

    private sealed class NoisyAsync(string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Task.Delay(10).ConfigureAwait(false);
            Console.WriteLine($"      async-disposed {name} (after actually flushing)");
        }
    }

    private static void Disposal()
    {
        Console.WriteLine("1. Nested usings dispose in REVERSE order — like a stack.\n");

        using (var outer = new Noisy("outer"))
        using (var inner = new Noisy("inner"))
        {
            Console.WriteLine("      body runs");
        }

        Console.WriteLine("\n   Which is what you want: the inner thing was built on the outer one, so");
        Console.WriteLine("   it must be torn down first. A transaction inside a connection, always.\n");

        Console.WriteLine("2. An exception does not skip Dispose.\n");

        try
        {
            using var d = new Noisy("held-during-a-throw");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("      caught the exception — but note the dispose printed FIRST\n");
        }

        Console.WriteLine("3. A `using` declaration disposes at the end of the enclosing SCOPE.\n");

        DeclarationScope();

        Console.WriteLine("\n4. IAsyncDisposable exists because Dispose cannot await.\n");

        DisposeAsyncDemo().GetAwaiter().GetResult();

        Console.WriteLine("\n   A synchronous Dispose that needs to flush over a network has two bad");
        Console.WriteLine("   options: block a thread, or lose the data. `await using` is the third.\n");

        Console.WriteLine("   THE TRAP: `using` on a type that implements only IAsyncDisposable does");
        Console.WriteLine("   not compile, and `using` on a type that implements BOTH silently calls");
        Console.WriteLine("   the synchronous one. Write `await using` whenever the type offers it.");

        static void DeclarationScope()
        {
            using var d = new Noisy("scoped-to-the-method");
            Console.WriteLine("      still inside the method");
        }

        static async Task DisposeAsyncDemo()
        {
            await using var d = new NoisyAsync("stream");
            Console.WriteLine("      body of the await using block");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  pooling — ArrayPool<T>, and the dirty-buffer trap
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Pooling()
    {
        Console.WriteLine("An 80 KB buffer per request is 80 KB of gen-0 garbage per request.");
        Console.WriteLine("ArrayPool<T> lets you borrow one instead.\n");

        const int size = 64 * 1024;
        const int iterations = 200;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            byte[] buffer = new byte[size];
            buffer[0] = 1;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                buffer[0] = 1;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        long pooled = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"   {iterations} x new byte[{size}]        : {allocated,12:N0} bytes");
        Console.WriteLine($"   {iterations} x ArrayPool Rent/Return   : {pooled,12:N0} bytes\n");

        Console.WriteLine("── Trap 1: Rent returns a buffer AT LEAST that big ──────────\n");

        byte[] rented = ArrayPool<byte>.Shared.Rent(1000);
        Console.WriteLine($"   Rent(1000).Length = {rented.Length}   <- not 1000");
        Console.WriteLine("   So you must carry the length yourself, and slice:");
        Console.WriteLine("      var span = rented.AsSpan(0, 1000);");
        Console.WriteLine("   Every bug in pooled code starts with somebody using buffer.Length.\n");

        Console.WriteLine("── Trap 2: a rented buffer is DIRTY ─────────────────────────\n");

        rented[0] = 42;
        rented[1] = 43;
        ArrayPool<byte>.Shared.Return(rented);

        byte[] again = ArrayPool<byte>.Shared.Rent(1000);
        Console.WriteLine($"   returned a buffer holding [42, 43], rented again: [{again[0]}, {again[1]}]");
        Console.WriteLine("   If that had been someone else's password, you would just have handed");
        Console.WriteLine("   it to the next request. Return(clearArray: true) for anything sensitive.\n");

        ArrayPool<byte>.Shared.Return(again, clearArray: true);

        Console.WriteLine("── Trap 3: use-after-return ─────────────────────────────────\n");
        Console.WriteLine("   Once you Return it, another thread may already own it. Touching it");
        Console.WriteLine("   afterwards is the managed equivalent of use-after-free, and it fails");
        Console.WriteLine("   silently under load and never on your machine.\n");

        Console.WriteLine("   Rent in a try, Return in the finally. Never anywhere else.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  leaks — how a garbage-collected language still leaks
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class Publisher
    {
        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Subscriber
    {
        private readonly byte[] _ballast = new byte[10_000];

        public void Subscribe(Publisher p) => p.Changed += OnChanged;

        public void Unsubscribe(Publisher p) => p.Changed -= OnChanged;

        private void OnChanged(object? sender, EventArgs e) => _ = _ballast.Length;
    }

    private static void Leaks()
    {
        Console.WriteLine("The GC frees UNREACHABLE objects. A leak in .NET is not a missing free —");
        Console.WriteLine("it is an object you forgot you were still pointing at.\n");

        Console.WriteLine("── The classic: an event handler ────────────────────────────\n");

        var publisher = new Publisher();
        WeakReference leaked = SubscribeAndForget(publisher);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Console.WriteLine($"   subscriber went out of scope, two full GCs later: alive = {leaked.IsAlive}");
        Console.WriteLine("   The subscriber is unreachable from YOUR code — but the publisher holds");
        Console.WriteLine("   the delegate, the delegate holds `this`, and the publisher is alive.");
        Console.WriteLine("   The arrow points from publisher to subscriber, which is backwards from");
        Console.WriteLine("   how you think about it, and that is the whole bug.\n");

        WeakReference notLeaked = SubscribeAndUnsubscribe(publisher);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Console.WriteLine($"   same thing, with -= in Dispose:                 alive = {notLeaked.IsAlive}\n");

        Console.WriteLine("   In a Blazor Server app this is THE leak: a component subscribes to a");
        Console.WriteLine("   singleton service in OnInitialized, the user navigates away, and the");
        Console.WriteLine("   circuit keeps every component it ever rendered. Implement IDisposable");
        Console.WriteLine("   on the component and unsubscribe there.\n");

        Console.WriteLine("── The other three ways ─────────────────────────────────────\n");
        Console.WriteLine("   1. A static collection nobody ever removes from. A `static` field is a");
        Console.WriteLine("      GC root, which means it is the definition of a leak.");
        Console.WriteLine("   2. A cache with no eviction. MemoryCache without a size limit is an");
        Console.WriteLine("      unbounded dictionary with good manners.");
        Console.WriteLine("   3. A captured closure held by a long-lived timer or Task. The lambda");
        Console.WriteLine("      captured `this`, so the whole object graph stays alive with it.\n");

        Console.WriteLine("   How you find it: memory grows, gen-2 collections rise, and the heap");
        Console.WriteLine("   after a forced full GC does not come back down. Take two dumps ten");
        Console.WriteLine("   minutes apart and diff the object counts — the type whose count only");
        Console.WriteLine("   ever grows is the answer.");

        static WeakReference SubscribeAndForget(Publisher p)
        {
            var s = new Subscriber();
            s.Subscribe(p);
            return new WeakReference(s);
        }

        static WeakReference SubscribeAndUnsubscribe(Publisher p)
        {
            var s = new Subscriber();
            s.Subscribe(p);
            s.Unsubscribe(p);
            return new WeakReference(s);
        }
    }
}
