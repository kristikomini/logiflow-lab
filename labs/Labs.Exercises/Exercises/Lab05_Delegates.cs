namespace Labs.Exercises.Exercises;

/// <summary>
/// LAB 05 — Delegates, generics, closures and events.
/// </summary>
/// <remarks>
/// <para>
/// Implement each member below. Run <c>dotnet test labs/Labs.Exercises</c> until green.
/// Do not edit <c>Lab05_DelegatesTests.cs</c> — it is the spec.
/// </para>
/// <para>
/// Read <c>course/module-02-delegates-and-closures/</c> first, and
/// <c>site/chapters/04-generics-delegates-events.html</c> for the shorter version.
/// </para>
/// <para>
/// These five are the machinery under LINQ, dependency injection and every event handler
/// you will ever write. Four of them are also standard interview questions.
/// </para>
/// </remarks>
public static class Lab05Delegates
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 1 — Write Where yourself
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Filters a sequence, lazily.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <param name="predicate">The test each element must pass.</param>
    /// <returns>The elements that pass, in order.</returns>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Deferred.</b> Calling this method must not touch <paramref name="source"/> at all.
    ///     Nothing runs until somebody enumerates the result.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Streaming.</b> It must never materialise the whole sequence. A caller taking two
    ///     elements from an infinite sequence must get two elements and terminate.
    ///   </description></item>
    ///   <item><description>Argument nulls throw <c>ArgumentNullException</c> — but see the trap below.</description></item>
    /// </list>
    /// <para>
    /// HINT: <c>yield return</c> inside a <c>foreach</c>. That is the whole implementation.
    /// </para>
    /// <para>
    /// THE TRAP: a method containing <c>yield return</c> has its <i>entire</i> body deferred,
    /// including your argument validation — so the <c>ArgumentNullException</c> would not be
    /// thrown until first enumeration, which is far away from the bug. The fix is the
    /// two-method pattern: a normal method that validates and returns a call to a private
    /// iterator method. This is exactly what the real <c>Enumerable.Where</c> does, and it is
    /// a good interview answer.
    /// </para>
    /// </remarks>
    public static IEnumerable<T> MyWhere<T>(IEnumerable<T> source, Func<T, bool> predicate) =>
        throw new NotImplementedException("Lab 05, exercise 1");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 2 — A closure that remembers
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps a function so each distinct argument is computed at most once.
    /// </summary>
    /// <typeparam name="TIn">The argument type. Used as a dictionary key.</typeparam>
    /// <typeparam name="TOut">The result type.</typeparam>
    /// <param name="function">The expensive function to wrap.</param>
    /// <returns>A function with the same signature that caches results.</returns>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>Calling the returned function twice with the same argument invokes
    ///     <paramref name="function"/> exactly once.</description></item>
    ///   <item><description>Different arguments each invoke it once.</description></item>
    ///   <item><description>Two separately memoised functions must not share a cache.</description></item>
    ///   <item><description><c>null</c> results are cached too — a cached <c>null</c> is a result,
    ///     not a cache miss.</description></item>
    /// </list>
    /// <para>
    /// HINT: declare a <c>Dictionary</c> inside the method and use it from the lambda. The lambda
    /// <i>captures</i> that dictionary, which is why each call to <c>Memoize</c> gets its own.
    /// </para>
    /// <para>
    /// THINK: what does the compiler actually generate here? The dictionary is a local, but it
    /// outlives the method call — so it cannot be on the stack. Module 02 shows the display
    /// class the compiler writes for you.
    /// </para>
    /// <para>
    /// AND THINK: this is not thread-safe, and the test does not check that. What would break
    /// first if two threads called it at once? Lab 06 exercise 4 is that problem, properly.
    /// </para>
    /// </remarks>
    public static Func<TIn, TOut> Memoize<TIn, TOut>(Func<TIn, TOut> function)
        where TIn : notnull =>
        throw new NotImplementedException("Lab 05, exercise 2");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 3 — The capture bug, fixed
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds <paramref name="count"/> functions, where the function at index i returns i.
    /// </summary>
    /// <param name="count">How many functions to build. Zero is legal.</param>
    /// <returns>The functions, in order.</returns>
    /// <remarks>
    /// <para>
    /// This is the classic interview puzzle. The obvious implementation is wrong:
    /// </para>
    /// <code>
    /// var result = new List&lt;Func&lt;int&gt;&gt;();
    /// for (int i = 0; i &lt; count; i++)
    ///     result.Add(() =&gt; i);      // every function returns `count`
    /// return result;
    /// </code>
    /// <para>
    /// All the lambdas captured the same variable, and by the time anybody calls them the loop
    /// has finished. Fix it so <c>MakeCounters(3)</c> produces functions returning 0, 1 and 2.
    /// </para>
    /// <para>
    /// HINT: give each iteration its own variable to capture.
    /// </para>
    /// <para>
    /// THINK: a <c>foreach</c> loop does not have this problem, and has not since C# 5. A
    /// <c>for</c> loop still does, and always will. Why is that difference correct rather than
    /// an inconsistency? (What would it mean to "reset" the <c>for</c> loop variable?)
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Func<int>> MakeCounters(int count) =>
        throw new NotImplementedException("Lab 05, exercise 3");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 4 — A generic method with a constraint
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the largest element of a sequence.
    /// </summary>
    /// <typeparam name="T">Anything that can be compared with itself.</typeparam>
    /// <param name="source">The sequence. Must not be empty.</param>
    /// <returns>The largest element.</returns>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>One pass. Do not sort — that is O(n log n) for an O(n) job.</description></item>
    ///   <item><description>Works for <c>int</c>, <c>string</c>, <c>DateTime</c> and anything else
    ///     implementing <c>IComparable&lt;T&gt;</c>, with no overloads.</description></item>
    ///   <item><description>An empty sequence throws <c>InvalidOperationException</c>. Do not
    ///     return <c>default</c> — for <c>int</c> that silently answers 0, which is the
    ///     <c>Max</c>-of-an-empty-sequence bug from Lab 02.</description></item>
    ///   <item><description>Enumerate <paramref name="source"/> exactly once. It might be a
    ///     database query or a stream.</description></item>
    /// </list>
    /// <para>
    /// HINT: the constraint is already written for you in the signature. Without it,
    /// <c>CompareTo</c> would not compile — which is the entire point of constraints.
    /// </para>
    /// </remarks>
    public static T Largest<T>(IEnumerable<T> source)
        where T : IComparable<T> =>
        throw new NotImplementedException("Lab 05, exercise 4");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 5 — An event that behaves
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>Reports that stock fell below its reorder level.</summary>
    /// <param name="sku">The product.</param>
    /// <param name="remaining">Units left.</param>
    public sealed class StockLowEventArgs(string sku, int remaining) : EventArgs
    {
        /// <summary>The product that ran low.</summary>
        public string Sku { get; } = sku;

        /// <summary>How many units remain.</summary>
        public int Remaining { get; } = remaining;
    }

    /// <summary>
    /// Watches stock levels and raises an event when one falls below its threshold.
    /// </summary>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>StockLow</c> is an <c>event</c>, not a public delegate field. Callers must be able
    ///     to <c>+=</c> and <c>-=</c> and nothing else — the test proves this by reflection.
    ///   </description></item>
    ///   <item><description>
    ///     <c>Report</c> raises the event only when <c>remaining</c> is strictly below
    ///     <c>threshold</c>.
    ///   </description></item>
    ///   <item><description>
    ///     Raising with no subscribers must not throw. An event with nobody listening is
    ///     <c>null</c>.
    ///   </description></item>
    ///   <item><description>
    ///     Every subscriber is called, in subscription order, and <c>sender</c> is this watcher.
    ///   </description></item>
    ///   <item><description>
    ///     After <c>-=</c>, that handler is not called again.
    ///   </description></item>
    /// </list>
    /// <para>
    /// HINT: <c>StockLow?.Invoke(this, new StockLowEventArgs(...))</c>. The <c>?.</c> is the
    /// requirement about no subscribers, and it is also thread-safe in a way that
    /// <c>if (StockLow != null) StockLow(...)</c> is not.
    /// </para>
    /// <para>
    /// THINK: subscribing makes <i>this watcher</i> hold a reference to the subscriber. If the
    /// watcher is a long-lived singleton and the subscriber is a screen the user closed, what
    /// happens to that screen? That is the number-one memory leak in .NET, and it is why the
    /// last test in this exercise exists.
    /// </para>
    /// </remarks>
    public sealed class StockWatcher
    {
        /// <summary>
        /// Raised when a reported level is below the threshold.
        /// </summary>
        /// <remarks>
        /// <b>This is deliberately wrong.</b> It is a public delegate <i>field</i>, so any caller
        /// can raise it on this object's behalf, and a single <c>=</c> instead of <c>+=</c>
        /// silently wipes every other subscriber. One keyword fixes it, and the first test in
        /// this exercise checks it by reflection.
        /// </remarks>
        public EventHandler<StockLowEventArgs>? StockLow;

        /// <summary>
        /// Reports a stock level, raising <see cref="StockLow"/> if it is below the threshold.
        /// </summary>
        /// <param name="sku">The product.</param>
        /// <param name="remaining">Units left.</param>
        /// <param name="threshold">The reorder level.</param>
        public void Report(string sku, int remaining, int threshold) =>
            throw new NotImplementedException("Lab 05, exercise 5");
    }
}
