namespace Labs.Exercises.Exercises;

/// <summary>
/// LAB 06 — Disposal, lifetime and shared state.
/// </summary>
/// <remarks>
/// <para>
/// Implement each member below. Run <c>dotnet test labs/Labs.Exercises</c> until green.
/// Do not edit <c>Lab06_DisposalTests.cs</c> — it is the spec.
/// </para>
/// <para>
/// Read <c>course/module-19-memory-and-gc/</c> and
/// <c>course/module-21-threading-and-memory-model/</c> first, or
/// <c>site/chapters/08-disposal-and-thread-safety.html</c> for the shorter version.
/// </para>
/// <para>
/// Exercise 3 is the one worth doing twice. Write it wrong first, watch the assertion fail
/// with a different number every run, and only then fix it. A race condition you have
/// <i>seen</i> is a different kind of knowledge from one you have read about.
/// </para>
/// </remarks>
public static class Lab06Disposal
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 1 — Dispose exactly once, however many times it is called
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hands out leases from a fixed pool and takes them back when they are disposed.
    /// </summary>
    /// <param name="capacity">How many leases exist.</param>
    /// <remarks>
    /// <para>
    /// Stands in for a connection pool, which is where you will meet this for real.
    /// </para>
    /// </remarks>
    public sealed class LeasePool(int capacity)
    {
        /// <summary>How many leases are currently available.</summary>
        public int Available { get; private set; } = capacity;

        /// <summary>
        /// Takes a lease. Dispose it to give it back.
        /// </summary>
        /// <returns>The lease.</returns>
        /// <exception cref="InvalidOperationException">The pool is exhausted.</exception>
        public Lease Take() => throw new NotImplementedException("Lab 06, exercise 1");

        /// <summary>Returns a lease to the pool. Called by <see cref="Lease.Dispose"/>.</summary>
        internal void Return() => Available++;
    }

    /// <summary>
    /// A borrowed slot in a <see cref="LeasePool"/>.
    /// </summary>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Dispose is idempotent.</b> Calling it twice returns the lease to the pool
    ///     <i>once</i>. This is not a nicety — a double return corrupts the pool count, and
    ///     the guidance for <c>IDisposable</c> is explicit that Dispose must be safe to call
    ///     more than once.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Use after dispose throws <c>ObjectDisposedException</c>.</b> Failing loudly beats
    ///     working on a resource somebody else now owns.
    ///   </description></item>
    /// </list>
    /// <para>
    /// HINT: one <c>bool _disposed</c> field, checked at the top of both members.
    /// <c>ObjectDisposedException.ThrowIf</c> exists and is the tidy way to do the second one.
    /// </para>
    /// <para>
    /// THINK: why is there no finalizer here? Because there is no <i>unmanaged</i> resource —
    /// the pool is a managed object and the GC handles it. A finalizer you do not need makes
    /// every instance more expensive to allocate and delays its collection by a whole
    /// generation. Module 19 has the numbers.
    /// </para>
    /// </remarks>
    public sealed class Lease : IDisposable
    {
        private readonly LeasePool _pool;

        /// <summary>Creates a lease against a pool.</summary>
        /// <param name="pool">The pool to return to.</param>
        internal Lease(LeasePool pool) => _pool = pool;

        /// <summary>Does some work with the leased resource.</summary>
        /// <returns>A value, in a real implementation.</returns>
        /// <exception cref="ObjectDisposedException">The lease has been returned.</exception>
        public int Use() => throw new NotImplementedException("Lab 06, exercise 1");

        /// <inheritdoc />
        public void Dispose() => throw new NotImplementedException("Lab 06, exercise 1");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 2 — Flush on the way out, without blocking a thread
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Buffers items and writes them out in one batch when disposed.
    /// </summary>
    /// <param name="flush">Receives the batch. Stands in for an I/O call.</param>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>Nothing is flushed until disposal.</description></item>
    ///   <item><description><c>DisposeAsync</c> flushes exactly once, in insertion order.</description></item>
    ///   <item><description>Disposing twice flushes once.</description></item>
    ///   <item><description>Disposing with nothing buffered does not call <paramref name="flush"/> at all.</description></item>
    ///   <item><description><c>Add</c> after disposal throws <c>ObjectDisposedException</c>.</description></item>
    /// </list>
    /// <para>
    /// HINT: implement <c>IAsyncDisposable</c>, not <c>IDisposable</c>. The release does I/O, and
    /// a synchronous <c>Dispose</c> that blocked on it would tie up a thread pool thread for the
    /// duration — the sin from module 04.
    /// </para>
    /// <para>
    /// THINK: callers write <c>await using var buffer = new AsyncBatchWriter(...)</c>. What does
    /// that compile to, and why does it guarantee the flush even when the body throws?
    /// </para>
    /// </remarks>
    public sealed class AsyncBatchWriter(Func<IReadOnlyList<string>, Task> flush) : IAsyncDisposable
    {
        /// <summary>Buffers an item.</summary>
        /// <param name="item">The item.</param>
        /// <exception cref="ObjectDisposedException">Already flushed.</exception>
        public void Add(string item) => throw new NotImplementedException("Lab 06, exercise 2");

        /// <inheritdoc />
        public ValueTask DisposeAsync() => throw new NotImplementedException("Lab 06, exercise 2");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 3 — count++ is not one operation
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A counter that gives the right answer when several threads use it at once.
    /// </summary>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     After N increments from any number of threads, <c>Value</c> is exactly N.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Value</c> must be readable safely while other threads are incrementing.
    ///   </description></item>
    /// </list>
    /// <para>
    /// HINT: <c>Interlocked.Increment</c> is the right tool for a single counter — it is one
    /// atomic instruction and far cheaper than taking a lock. Use <c>Interlocked.Read</c> or a
    /// <c>volatile</c> read for the getter, and think about why a plain read of a
    /// <c>long</c> is not guaranteed atomic on a 32-bit runtime.
    /// </para>
    /// <para>
    /// DO THIS FIRST: implement it with a bare <c>_count++</c> and run the test. It fails, and it
    /// fails with a <i>different</i> number every time. That irreproducibility is the whole
    /// reason race conditions survive testing and get closed as "cannot reproduce".
    /// </para>
    /// <para>
    /// THINK: <c>lock</c> would also pass this test. Why is <c>Interlocked</c> the better answer
    /// <i>here</i>, and what would have to change for <c>lock</c> to become the right one?
    /// </para>
    /// </remarks>
    public sealed class SafeCounter
    {
        /// <summary>The current count.</summary>
        public long Value => throw new NotImplementedException("Lab 06, exercise 3");

        /// <summary>Adds one, atomically.</summary>
        public void Increment() => throw new NotImplementedException("Lab 06, exercise 3");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 4 — one loader, everyone else waits
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An async cache where concurrent misses for the same key load it once.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The cached value.</typeparam>
    /// <param name="load">Loads a value. Expensive; must not be called twice for one key.</param>
    /// <remarks>
    /// <para>
    /// This is the cache stampede from module 10, in miniature: fifty requests arrive for a key
    /// that is not cached, all fifty miss, and all fifty hit the database.
    /// </para>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Fifty concurrent <c>GetAsync</c> calls for the same key invoke <paramref name="load"/>
    ///     <b>exactly once</b>, and all fifty get the value.
    ///   </description></item>
    ///   <item><description>Different keys load independently and must not block each other.</description></item>
    ///   <item><description>A later call for a loaded key does not load again.</description></item>
    ///   <item><description>
    ///     If <paramref name="load"/> throws, the failure is not cached — a later call retries.
    ///   </description></item>
    /// </list>
    /// <para>
    /// HINT: you cannot <c>await</c> inside a <c>lock</c>, and it will not compile if you try —
    /// a monitor is owned by a thread and the continuation may resume on another.
    /// <c>SemaphoreSlim(1, 1)</c> with <c>WaitAsync</c> is the async equivalent. Release it in a
    /// <c>finally</c>, or one exception holds the gate shut for the life of the process.
    /// </para>
    /// <para>
    /// THINK: there is a much shorter solution that caches the <c>Task&lt;TValue&gt;</c> rather
    /// than the value, using <c>ConcurrentDictionary.GetOrAdd</c>. It is elegant and it is what
    /// production code often does — but read the requirement about failures not being cached
    /// again before you reach for it, because that is exactly where it goes wrong.
    /// </para>
    /// </remarks>
    public sealed class SingleFlightCache<TKey, TValue>(Func<TKey, Task<TValue>> load)
        where TKey : notnull
    {
        /// <summary>Gets a value, loading it at most once across concurrent callers.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The cached or freshly loaded value.</returns>
        public Task<TValue> GetAsync(TKey key) =>
            throw new NotImplementedException("Lab 06, exercise 4");
    }
}
