namespace Labs.Exercises.Exercises;

/// <summary>
/// LAB 03 — Async: timeouts, retries, bounded concurrency, and collecting every error.
/// </summary>
/// <remarks>
/// <para>
/// Implement each method below. Run <c>dotnet test labs/Labs.Exercises</c> until green.
/// Do not edit <c>Lab03_AsyncTests.cs</c> — it is the spec.
/// </para>
/// <para>
/// Read <c>course/module-04-async/</c> and <c>course/module-21-threading-and-memory-model/</c>
/// first, and <c>course/module-25-distributed-systems/</c> for exercise 2.
/// </para>
/// <para>
/// These four are not toys. They are the four pieces of resilience you write by hand before
/// you are allowed to reach for Polly, and every one of them has a trap that only shows up
/// under load.
/// </para>
/// </remarks>
public static class Lab03Async
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 1 — A timeout that does not leak
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Awaits <paramref name="task"/>, but gives up after <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>If the task completes in time, return its result.</description></item>
    ///   <item><description>If it does not, throw <see cref="TimeoutException"/>.</description></item>
    ///   <item><description>
    ///     If <paramref name="cancellationToken"/> is cancelled first, throw
    ///     <see cref="OperationCanceledException"/> — not <see cref="TimeoutException"/>.
    ///   </description></item>
    ///   <item><description>
    ///     If the task itself faults, let ITS exception out unchanged — do not wrap it in an
    ///     <c>AggregateException</c>.
    ///   </description></item>
    ///   <item><description>
    ///     Do not leave a timer running after the task completes. A per-call
    ///     <c>Task.Delay</c> that nobody cancels is a real leak in a hot path.
    ///   </description></item>
    /// </list>
    /// <para>
    /// HINT: <c>Task.WhenAny</c>, plus a <c>CancellationTokenSource</c> you cancel in a
    /// <c>finally</c>. <c>CancellationTokenSource.CreateLinkedTokenSource</c> combines the
    /// caller's token with your own — and the linked source must be disposed.
    /// </para>
    /// <para>
    /// THINK: after a timeout, what happens to the original task? You are not cancelling it —
    /// you are abandoning it. If it later throws, who observes that exception? (This is why
    /// real cancellation means passing the token INTO the work, not racing it from outside.)
    /// </para>
    /// </remarks>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="task">The work to wait for.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The task result.</returns>
    public static Task<T> WithTimeoutAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Lab 03, exercise 1");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 2 — Retry with backoff
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying on failure up to
    /// <paramref name="maxAttempts"/> times in total.
    /// </summary>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <paramref name="maxAttempts"/> counts the FIRST attempt. <c>maxAttempts: 3</c> means
    ///     at most three calls, not four.
    ///   </description></item>
    ///   <item><description>
    ///     Retry only when <paramref name="shouldRetry"/> returns true for the exception.
    ///     Anything else propagates immediately — a 400 will not become a 200 (module 25).
    ///   </description></item>
    ///   <item><description>
    ///     Wait <c>delay(attemptNumber)</c> between attempts, where the first attempt is 1.
    ///     The tests pass <c>_ =&gt; TimeSpan.Zero</c> so they stay fast; production passes
    ///     an exponential-backoff-with-jitter function.
    ///   </description></item>
    ///   <item><description>
    ///     Honour the cancellation token, both during the operation and during the wait.
    ///   </description></item>
    ///   <item><description>
    ///     If every attempt fails, throw the LAST exception — with its stack trace intact.
    ///   </description></item>
    /// </list>
    /// <para>
    /// HINT: for that last requirement, remember module 23 / the exceptions demo. Inside a
    /// <c>catch</c> you can use <c>throw;</c>. If you have stored the exception in a variable
    /// and want to rethrow it later, <c>throw ex;</c> destroys the trace —
    /// <c>ExceptionDispatchInfo.Capture(ex).Throw()</c> does not.
    /// </para>
    /// <para>
    /// THINK: should an <c>OperationCanceledException</c> caused by the caller's token be
    /// retried? (No. Work out why, and make sure your implementation agrees.)
    /// </para>
    /// </remarks>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="operation">The work. Receives the cancellation token.</param>
    /// <param name="maxAttempts">Total attempts, including the first. At least 1.</param>
    /// <param name="shouldRetry">Decides whether an exception is worth retrying.</param>
    /// <param name="delay">Wait before the next attempt, given the attempt number (1-based).</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The result of the first successful attempt.</returns>
    public static Task<T> RetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxAttempts,
        Func<Exception, bool> shouldRetry,
        Func<int, TimeSpan> delay,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Lab 03, exercise 2");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 3 — Bounded concurrency
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies <paramref name="operation"/> to every item, with at most
    /// <paramref name="maxConcurrency"/> running at once, and returns the results in the
    /// ORIGINAL order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape you need whenever you fan out to a downstream service:
    /// <c>Task.WhenAll</c> over 10,000 items opens 10,000 connections and gets you rate
    /// limited or blocked. A bulkhead is a concurrency cap (module 25).
    /// </para>
    /// <para>HINT: <c>SemaphoreSlim(maxConcurrency)</c>, <c>WaitAsync</c> before the work and
    /// <c>Release</c> in a <c>finally</c>. Keep the tasks in an array indexed by position and
    /// the ordering takes care of itself.</para>
    /// <para>
    /// THE TRAP: <c>Release()</c> must be in a <c>finally</c>. One faulting item that skips the
    /// release permanently reduces your concurrency limit, and after
    /// <paramref name="maxConcurrency"/> failures the whole thing deadlocks — in production,
    /// weeks later, under load.
    /// </para>
    /// <para>
    /// THINK: why not <c>Parallel.ForEachAsync</c>? (It is a fine answer in real code. Write it
    /// by hand once so you know what it is doing.)
    /// </para>
    /// </remarks>
    /// <typeparam name="TIn">Input type.</typeparam>
    /// <typeparam name="TOut">Output type.</typeparam>
    /// <param name="source">The items.</param>
    /// <param name="operation">The work per item.</param>
    /// <param name="maxConcurrency">Maximum simultaneous operations. At least 1.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The results, in the same order as the input.</returns>
    public static Task<IReadOnlyList<TOut>> MapWithConcurrencyLimitAsync<TIn, TOut>(
        IEnumerable<TIn> source,
        Func<TIn, CancellationToken, Task<TOut>> operation,
        int maxConcurrency,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Lab 03, exercise 3");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 4 — Collecting every error, not just the first
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Awaits every task and returns their results. If any faulted, throws an
    /// <see cref="AggregateException"/> containing EVERY failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>await Task.WhenAll(tasks)</c> throws only the FIRST exception; the others are on the
    /// combined task and are silently discarded if nobody looks. When you are fanning out to
    /// five services and three fail, "one of them failed" is not a useful diagnostic.
    /// </para>
    /// <para>
    /// REQUIREMENT: every task must be awaited to completion before you throw — do not
    /// short-circuit, or you leave unobserved faulted tasks behind.
    /// </para>
    /// <para>HINT: <c>Task.WhenAll</c> returns a Task you can inspect. Await it in a
    /// <c>try</c>, and in the <c>catch</c> read the combined task's <c>Exception</c> property
    /// rather than the exception you caught.</para>
    /// </remarks>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="tasks">The tasks.</param>
    /// <returns>Every result, in order.</returns>
    public static Task<IReadOnlyList<T>> WhenAllCollectingErrorsAsync<T>(
        IEnumerable<Task<T>> tasks) =>
        throw new NotImplementedException("Lab 03, exercise 4");
}
