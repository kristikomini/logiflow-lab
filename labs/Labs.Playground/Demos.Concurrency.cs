using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;

namespace Labs.Playground;

/// <summary>
/// Demos for the layer underneath async/await — races, synchronization contexts,
/// the sync-over-async deadlock, thread-pool starvation, and ValueTask.
/// </summary>
public static partial class Program
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  race — a lost update, and the three ways to stop it
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static int _unsafeCounter;
    private static int _interlockedCounter;
    private static int _lockedCounter;
    private static readonly Lock CounterLock = new();

    private static void RaceCondition()
    {
        const int threads = 4;
        const int perThread = 200_000;
        const int expected = threads * perThread;

        Console.WriteLine($"{threads} threads, {perThread:N0} increments each. Expected: {expected:N0}\n");

        RunOnThreads(threads, () =>
        {
            for (int i = 0; i < perThread; i++)
            {
                _unsafeCounter++;                              // read, add, write — three steps
            }
        });

        RunOnThreads(threads, () =>
        {
            for (int i = 0; i < perThread; i++)
            {
                Interlocked.Increment(ref _interlockedCounter); // one atomic instruction
            }
        });

        RunOnThreads(threads, () =>
        {
            for (int i = 0; i < perThread; i++)
            {
                lock (CounterLock)
                {
                    _lockedCounter++;
                }
            }
        });

        Console.WriteLine($"   counter++                : {_unsafeCounter,10:N0}   {(_unsafeCounter == expected ? "" : "<- WRONG, and it lost " + (expected - _unsafeCounter).ToString("N0") + " updates")}");
        Console.WriteLine($"   Interlocked.Increment    : {_interlockedCounter,10:N0}");
        Console.WriteLine($"   lock {{ }} around it        : {_lockedCounter,10:N0}\n");

        Console.WriteLine("   `counter++` is not one operation. It is:");
        Console.WriteLine("      mov eax, [counter]      ; read");
        Console.WriteLine("      inc eax                 ; add");
        Console.WriteLine("      mov [counter], eax      ; write");
        Console.WriteLine("   Two threads can read the same value, both add one, and both write the");
        Console.WriteLine("   same result. One increment vanishes. That is a lost update, and it is");
        Console.WriteLine("   the same bug as two users editing one order row — see module 07.\n");

        Console.WriteLine("── Choosing between them ────────────────────────────────────\n");
        Console.WriteLine("   Interlocked  — one variable, one operation. Fastest, no blocking.");
        Console.WriteLine("   lock         — several variables, or an invariant across them.");
        Console.WriteLine("   Channel/queue— when you would rather not share the state at all.\n");

        Console.WriteLine("   Rules for `lock`: lock on a private readonly object (in .NET 9+, a");
        Console.WriteLine("   System.Threading.Lock), never on `this`, never on a string, never on a");
        Console.WriteLine("   Type. Anything a stranger can also lock on is a deadlock you cannot see.");
        Console.WriteLine("   And never `await` inside a lock — the compiler will not let you, because");
        Console.WriteLine("   a monitor is owned by a THREAD and the continuation may resume on");
        Console.WriteLine("   another one. Use SemaphoreSlim.WaitAsync when you need that.");

        static void RunOnThreads(int count, Action body)
        {
            Thread[] pool = new Thread[count];
            for (int i = 0; i < count; i++)
            {
                pool[i] = new Thread(() => body()) { IsBackground = true };
                pool[i].Start();
            }

            foreach (Thread t in pool)
            {
                t.Join();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  context — SynchronizationContext, and what ConfigureAwait(false) does
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A single-threaded context, like the one a UI framework or Blazor Server installs.
    /// Everything posted to it is run by one thread, one item at a time.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public SingleThreadContext(string name)
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = name };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void Dispose() => _queue.CompleteAdding();

        private void Loop()
        {
            SetSynchronizationContext(this);
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }

    private static void Contexts()
    {
        Console.WriteLine("A SynchronizationContext answers one question: WHERE does the code after");
        Console.WriteLine("an await resume? A console app has none, so the answer is 'the thread pool'.\n");

        Console.WriteLine($"   SynchronizationContext.Current in this console app : {SynchronizationContext.Current?.ToString() ?? "null"}\n");

        Console.WriteLine("   That is why sync-over-async never deadlocks in a console app and always");
        Console.WriteLine("   does in a UI. Let us install a UI-like context and watch.\n");

        using var context = new SingleThreadContext("ui-thread");
        Console.WriteLine($"   installed a single-threaded context, running on thread {context.ThreadId}\n");

        var done = new ManualResetEventSlim();

        context.Post(
            _ => RunOnContext().ContinueWith(_ => done.Set(), TaskScheduler.Default),
            null);

        done.Wait(TimeSpan.FromSeconds(5));

        Console.WriteLine("\n   WITHOUT ConfigureAwait(false): the continuation was POSTED back to the");
        Console.WriteLine("   context thread. Correct in a UI (you may touch the controls), and one");
        Console.WriteLine("   extra queue hop everywhere else.\n");
        Console.WriteLine("   WITH ConfigureAwait(false): the continuation ran on whichever pool");
        Console.WriteLine("   thread completed the task. Cheaper, and it cannot deadlock.\n");

        Console.WriteLine("   THE RULE: library code writes ConfigureAwait(false) on every await,");
        Console.WriteLine("   because a library does not know whose thread it is on. Application code");
        Console.WriteLine("   in ASP.NET Core does not need it — there is no context there since");
        Console.WriteLine("   .NET Core 1.0 — but Blazor Server and WPF very much do have one.");

        static async Task RunOnContext()
        {
            Console.WriteLine($"      [{Environment.CurrentManagedThreadId}] start (on the context thread)");

            await Task.Delay(20);
            Console.WriteLine($"      [{Environment.CurrentManagedThreadId}] after plain await          <- back on the context");

            await Task.Delay(20).ConfigureAwait(false);
            Console.WriteLine($"      [{Environment.CurrentManagedThreadId}] after ConfigureAwait(false) <- a thread-pool thread");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  deadlock — sync-over-async, reproduced safely
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Deadlock()
    {
        Console.WriteLine("The single most expensive bug in .NET, in four lines:\n");
        Console.WriteLine("      public string Get()                     // a synchronous caller");
        Console.WriteLine("      {");
        Console.WriteLine("          return GetAsync().Result;           // blocks THIS thread");
        Console.WriteLine("      }\n");
        Console.WriteLine("      private async Task<string> GetAsync()");
        Console.WriteLine("      {");
        Console.WriteLine("          await Task.Delay(10);               // wants to resume on THIS thread");
        Console.WriteLine("          return \"done\";");
        Console.WriteLine("      }\n");

        Console.WriteLine("   1. Get() blocks the context thread waiting for the Task.");
        Console.WriteLine("   2. The await captured that context and posts its continuation to it.");
        Console.WriteLine("   3. The context thread is busy blocking, so the continuation never runs.");
        Console.WriteLine("   4. The Task never completes, so the block never ends.\n");
        Console.WriteLine("   A cycle of two, and no exception ever gets thrown. The request just");
        Console.WriteLine("   hangs, one thread is gone, and it happens again on the next request.\n");

        Console.WriteLine("── Reproducing it, with a timeout so this demo can exit ─────\n");

        using var context = new SingleThreadContext("ui-thread");
        var finished = new ManualResetEventSlim();

        context.Post(
            _ =>
            {
                try
                {
                    // .Result on a task whose continuation needs THIS thread.
                    string result = GetAsync().Result;
                    Console.WriteLine($"      completed: {result}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      threw: {ex.GetType().Name}");
                }
                finally
                {
                    finished.Set();
                }
            },
            null);

        bool completed = finished.Wait(TimeSpan.FromSeconds(3));

        Console.WriteLine(completed
            ? "      it completed — no deadlock this time"
            : "      3 seconds later: STILL BLOCKED. That is the deadlock.\n");

        Console.WriteLine("      (the demo continues because that thread is a background thread —");
        Console.WriteLine("       in a real app it is a request thread, and it is gone for good)\n");

        Console.WriteLine("── The three fixes, in order of preference ──────────────────\n");
        Console.WriteLine("   1. Do not block. Make the caller async, all the way up to the");
        Console.WriteLine("      framework. `async` is not a feature you add locally.");
        Console.WriteLine("   2. If you are writing a LIBRARY, ConfigureAwait(false) on every await.");
        Console.WriteLine("      Then there is no context to post back to and the block succeeds.");
        Console.WriteLine("   3. Never `Task.Run(() => X()).Result` as a workaround. It works, and it");
        Console.WriteLine("      burns two threads per call to do the job of zero.\n");

        Console.WriteLine("   And know the corollary: .Result and .Wait() also wrap any exception in");
        Console.WriteLine("   an AggregateException, so your catch blocks stop matching. GetAwaiter()");
        Console.WriteLine("   .GetResult() at least rethrows the original — but you still blocked.");

        static async Task<string> GetAsync()
        {
            await Task.Delay(10);
            return "done";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  threadpool — starvation, watched live
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void ThreadPoolStarvation()
    {
        ThreadPool.GetMinThreads(out int minWorker, out _);
        ThreadPool.GetMaxThreads(out int maxWorker, out _);

        Console.WriteLine($"   processors : {Environment.ProcessorCount}");
        Console.WriteLine($"   min threads: {minWorker}   <- available instantly");
        Console.WriteLine($"   max threads: {maxWorker:N0}\n");

        Console.WriteLine("   Above the minimum, the pool injects roughly ONE new thread per second.");
        Console.WriteLine("   That number is the whole story of thread-pool starvation.\n");

        const int items = 32;

        Console.WriteLine($"── {items} work items that BLOCK for 100ms each ──────────────\n");

        var blockingWatch = Stopwatch.StartNew();
        Task[] blocking = new Task[items];
        for (int i = 0; i < items; i++)
        {
            blocking[i] = Task.Run(() => Thread.Sleep(100));   // holds a pool thread hostage
        }

        Task.WaitAll(blocking);
        blockingWatch.Stop();

        Console.WriteLine($"   Thread.Sleep(100)  x{items} : {blockingWatch.ElapsedMilliseconds,6:N0} ms");

        var awaitingWatch = Stopwatch.StartNew();
        Task[] awaiting = new Task[items];
        for (int i = 0; i < items; i++)
        {
            awaiting[i] = Task.Delay(100);                     // holds nothing at all
        }

        Task.WaitAll(awaiting);
        awaitingWatch.Stop();

        Console.WriteLine($"   await Task.Delay(100) x{items} : {awaitingWatch.ElapsedMilliseconds,6:N0} ms\n");

        Console.WriteLine("   Same 100ms of waiting. The blocking version needed a thread for each");
        Console.WriteLine("   wait and had to queue once it ran out; the awaiting version needed");
        Console.WriteLine("   none, because an incomplete Task is a callback, not a thread.\n");

        Console.WriteLine("   (On a machine with many cores the gap may be small here — scale `items`");
        Console.WriteLine("   up past the core count and it grows without limit. In production the");
        Console.WriteLine("   items are concurrent requests, and there are thousands.)\n");

        Console.WriteLine("── How it looks when it happens to you ──────────────────────\n");
        Console.WriteLine("   - p99 latency climbs while CPU sits near idle;");
        Console.WriteLine("   - it gets WORSE under load, then recovers a second at a time;");
        Console.WriteLine("   - ThreadPool.ThreadCount keeps rising;");
        Console.WriteLine("   - the culprit is always .Result, .Wait(), .GetAwaiter().GetResult(),");
        Console.WriteLine("     lock contention, or a synchronous file/database call in a handler.\n");

        Console.WriteLine("   The metric to alarm on: ThreadPool queue length, and the ratio of");
        Console.WriteLine("   busy threads to processor count. See module 11.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  valuetask — the consume-once rule
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A minimal pooled value-task source: exactly what the runtime does for a
    /// <c>ValueTask</c> that is not backed by a <c>Task</c>. Consuming it twice is
    /// undefined, and this implementation says so out loud rather than corrupting state.
    /// </summary>
    private sealed class OneShotSource : IValueTaskSource<int>
    {
        private bool _consumed;

        public ValueTask<int> AsValueTask() => new(this, token: 0);

        public int GetResult(short token)
        {
            if (_consumed)
            {
                throw new InvalidOperationException(
                    "This ValueTask has already been consumed. The backing object was returned to the pool.");
            }

            _consumed = true;
            return 42;
        }

        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => continuation(state);
    }

#pragma warning disable CA2012 // reading a ValueTask result directly is exactly what this demo is about
    private static void ValueTasks()
    {
        Console.WriteLine("Task is a CLASS. Every async method that actually suspends allocates one.");
        Console.WriteLine("ValueTask is a STRUCT, so the synchronous path allocates nothing.\n");

        Console.WriteLine("   RUN THIS ONE IN RELEASE:  dotnet run -c Release valuetask");
        Console.WriteLine("   A Debug build emits the async state machine as a CLASS so the debugger");
        Console.WriteLine("   can inspect it, which allocates on every call and hides the whole effect.");
        Console.WriteLine("   In Release it is a struct, and the two numbers separate completely.\n");

        Console.WriteLine("── Where it pays: a cache with a high hit rate ──────────────\n");

        var cache = new Dictionary<int, int> { [1] = 100 };

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = TaskLookup(cache, 1).GetAwaiter().GetResult();
        }

        long taskBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = ValueTaskLookup(cache, 1).GetAwaiter().GetResult();
        }

        long valueTaskBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"   10,000 cache hits returning Task<int>      : {taskBytes,8:N0} bytes");
        Console.WriteLine($"   10,000 cache hits returning ValueTask<int> : {valueTaskBytes,8:N0} bytes\n");

        Console.WriteLine("── The price: three rules that Task does not have ───────────\n");
        Console.WriteLine("   1. Await it ONCE. Never twice.");
        Console.WriteLine("   2. Never await it concurrently from two places.");
        Console.WriteLine("   3. Do not read .Result before it is known to be complete.\n");
        Console.WriteLine("   A Task is immutable and can be awaited a hundred times. A ValueTask may");
        Console.WriteLine("   be a rented object that goes back to a pool the instant you read it.\n");

        Console.WriteLine("── Rule 1, broken ───────────────────────────────────────────\n");

        var source = new OneShotSource();
        ValueTask<int> vt = source.AsValueTask();

        Console.WriteLine($"   first await  : {vt.GetAwaiter().GetResult()}");

        try
        {
            Console.WriteLine($"   second await : {vt.GetAwaiter().GetResult()}");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"   second await : {ex.Message}\n");
        }

        Console.WriteLine("   If you need the value twice, call .AsTask() once and await THAT.\n");

        Console.WriteLine("   WHEN TO USE IT: a hot path that usually completes synchronously —");
        Console.WriteLine("   a cache, a buffered stream read, a channel with items waiting. For");
        Console.WriteLine("   everything else return Task. The default is Task, and it is a good one.");

        static async Task<int> TaskLookup(Dictionary<int, int> cache, int key)
        {
            if (cache.TryGetValue(key, out int hit))
            {
                return hit;
            }

            await Task.Yield();
            return 0;
        }

        static async ValueTask<int> ValueTaskLookup(Dictionary<int, int> cache, int key)
        {
            if (cache.TryGetValue(key, out int hit))
            {
                return hit;
            }

            await Task.Yield();
            return 0;
        }
    }
#pragma warning restore CA2012

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  retry — why jitter is not optional
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void RetryStorm()
    {
        const int clients = 240;      // instances that were all mid-call when the blip happened
        const int attempts = 4;
        const int baseDelayMs = 100;  // 100, 200, 400, 800 — doubling each time
        const int columns = 64;
        const int columnMs = 25;      // deliberately FINER than the first delay: a synchronised
                                      // retry is a spike, and a coarse ruler hides spikes

        Console.WriteLine($"{clients} clients all fail at t=0, then retry {attempts} times with exponential");
        Console.WriteLine($"backoff from {baseDelayMs}ms. Each column below is {columnMs}ms of wall clock.\n");

        int[] fixedBackoff = Histogram(seed: 1, jitter: false);
        int[] jittered = Histogram(seed: 1, jitter: true);

        // ONE scale for both strips. Normalising each to its own maximum would make
        // them look identical, which is the opposite of the point.
        int scale = Math.Max(fixedBackoff.Max(), jittered.Max());

        Console.WriteLine($"── Fixed exponential backoff        peak {fixedBackoff.Max(),3} in one {columnMs}ms window ──\n");
        Draw(fixedBackoff, scale);

        Console.WriteLine($"── The same backoff, jittered       peak {jittered.Max(),3} in one {columnMs}ms window ──\n");
        Draw(jittered, scale);

        Console.WriteLine($"   Same {clients * attempts} requests, same delays, one scale. The only difference is");
        Console.WriteLine("   that each client picked a random point INSIDE its delay window.\n");

        Console.WriteLine("   Without jitter the clients stay in lockstep forever: they failed");
        Console.WriteLine($"   together, so they wait together and arrive together. The service that");
        Console.WriteLine($"   just fell over is hit by all {fixedBackoff.Max()} of them at once — four times.");
        Console.WriteLine("   Your retry policy has turned their one-second blip into their outage,");
        Console.WriteLine("   and then into yours, because every thread is parked waiting on it.\n");

        Console.WriteLine("   Jitter does not reduce the number of requests. It removes the");
        Console.WriteLine("   correlation between them, which is the thing that was harmful.\n");

        Console.WriteLine("   That is why Polly's UseJitter is not a tuning knob — it is the setting");
        Console.WriteLine("   that makes retry safe to switch on at all. Module 25 section 4, and");
        Console.WriteLine("   chapter 08 of that module for the outbound-HTTP version.\n");

        static int[] Histogram(int seed, bool jitter)
        {
            var random = new Random(seed);   // seeded: the shape is the same every run
            int[] slots = new int[columns];

            for (int client = 0; client < clients; client++)
            {
                double elapsed = 0;

                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    double window = baseDelayMs * Math.Pow(2, attempt);

                    // Full jitter: uniform inside the window, rather than always at its edge.
                    elapsed += jitter ? random.NextDouble() * window : window;

                    int slot = (int)(elapsed / columnMs);
                    if (slot < columns)
                    {
                        slots[slot]++;
                    }
                }
            }

            return slots;
        }

        static void Draw(int[] slots, int scale)
        {
            const string Ramp = " .,:;+*#%@";

            var strip = new char[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                int level = slots[i] == 0 ? 0 : 1 + (int)((slots[i] - 1) / (double)scale * (Ramp.Length - 1));
                strip[i] = Ramp[Math.Min(level, Ramp.Length - 1)];
            }

            Console.WriteLine("   |" + new string(strip) + "|");
            Console.WriteLine($"   0ms{new string(' ', columns - 6)}{columns * columnMs}ms\n");
        }
    }
}
