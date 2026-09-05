namespace Labs.Exercises.Exercises;

/// <summary>
/// LAB 02 — LINQ, from the operators to how they actually work.
/// </summary>
/// <remarks>
/// <para>
/// Implement each method below. Run <c>dotnet test labs/Labs.Exercises</c> until green.
/// Do not edit <c>Lab02_LinqTests.cs</c> — it is the spec.
/// </para>
/// <para>Read <c>course/module-03-linq-internals/</c> and <c>course/module-09-advanced-linq/</c> first.</para>
/// </remarks>
public static class Lab02Linq
{
    /// <summary>An order line, simplified for the exercises.</summary>
    /// <param name="OrderId">Which order it belongs to.</param>
    /// <param name="Sku">Product code.</param>
    /// <param name="Category">Category prefix, e.g. <c>ELE</c>.</param>
    /// <param name="Quantity">Units ordered.</param>
    /// <param name="UnitPrice">Price per unit.</param>
    /// <param name="OrderedOn">Date of the order.</param>
    public sealed record Line(
        int OrderId,
        string Sku,
        string Category,
        int Quantity,
        decimal UnitPrice,
        DateOnly OrderedOn);

    /// <summary>Revenue for one product category.</summary>
    /// <param name="Category">The category.</param>
    /// <param name="Revenue">Total money taken.</param>
    /// <param name="UnitsSold">Total units.</param>
    public sealed record CategoryRevenue(string Category, decimal Revenue, int UnitsSold);

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 1 — Projection and filtering
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the distinct SKUs of every line worth more than <paramref name="minimumValue"/>,
    /// sorted alphabetically.
    /// </summary>
    /// <remarks>
    /// A line's value is <c>Quantity * UnitPrice</c>.
    /// <para>HINT: <c>Where</c>, then <c>Select</c>, then <c>Distinct</c>, then <c>OrderBy</c>.</para>
    /// <para>
    /// THINK: does the order of <c>Distinct</c> and <c>OrderBy</c> matter for correctness?
    /// Does it matter for performance?
    /// </para>
    /// </remarks>
    public static IEnumerable<string> HighValueSkus(IEnumerable<Line> lines, decimal minimumValue) =>
        throw new NotImplementedException("Lab 02, exercise 1");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 2 — Grouping and aggregation
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Groups lines by category and returns revenue and units per category,
    /// highest revenue first.
    /// </summary>
    /// <remarks>
    /// HINT: <c>GroupBy(l =&gt; l.Category)</c>, then <c>Select</c> over the groups.
    /// Inside a group you can call <c>Sum</c>, <c>Count</c>, <c>Average</c>, <c>Max</c>.
    /// </remarks>
    public static IEnumerable<CategoryRevenue> RevenueByCategory(IEnumerable<Line> lines) =>
        throw new NotImplementedException("Lab 02, exercise 2");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 3 — The one everybody gets wrong
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the single largest order by total value, or <c>null</c> when there are no lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Group by <c>OrderId</c>, sum each group's line values, and return the winning
    /// <c>OrderId</c>.
    /// </para>
    /// <para>
    /// TRAP: <c>OrderByDescending(...).First()</c> throws on an empty sequence. So does
    /// <c>Max()</c>. Which operator returns a default instead? And what does
    /// <c>MaxBy</c> (.NET 6+) give you that <c>OrderByDescending().First()</c> does not?
    /// </para>
    /// </remarks>
    public static int? LargestOrderId(IEnumerable<Line> lines) =>
        throw new NotImplementedException("Lab 02, exercise 3");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 4 — Deferred execution
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a query that yields SKUs longer than <paramref name="minLength"/>, and reports
    /// through <paramref name="onEvaluate"/> every time an element is actually examined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test asserts that <b>nothing</b> is evaluated until the result is enumerated, and
    /// that enumerating it twice evaluates it twice.
    /// </para>
    /// <para>
    /// HINT: build the query with <c>Where</c>/<c>Select</c> and call
    /// <paramref name="onEvaluate"/> inside the lambda. Do NOT call <c>ToList()</c> — that
    /// would force execution immediately and fail the test.
    /// </para>
    /// <para>
    /// THIS IS THE MOST IMPORTANT CONCEPT IN LINQ. A query is a recipe, not a result.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> LazySkuQuery(
        IEnumerable<Line> lines,
        int minLength,
        Action<string> onEvaluate) =>
        throw new NotImplementedException("Lab 02, exercise 4");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 5 — Write your own operator
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Splits a sequence into consecutive chunks of at most <paramref name="size"/> items,
    /// lazily.
    /// </summary>
    /// <remarks>
    /// <para>
    /// .NET 6 added <c>Enumerable.Chunk</c>. Write your own anyway — implementing an operator
    /// with <c>yield return</c> is what makes deferred execution click.
    /// </para>
    /// <para>
    /// REQUIREMENTS:
    /// <list type="bullet">
    ///   <item><description>Lazy: enumerating the source must not start until the result is enumerated.</description></item>
    ///   <item><description>Single-pass: the source may only be iterated once.</description></item>
    ///   <item><description><paramref name="size"/> below 1 throws <see cref="ArgumentOutOfRangeException"/> IMMEDIATELY, not on first enumeration.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// THE TRAP: an iterator method's body does not run until it is enumerated, so a guard
    /// clause inside a <c>yield return</c> method throws far too late. The fix is the
    /// two-method pattern — a normal method that validates and then returns a private iterator.
    /// Look at how the BCL does it; almost every LINQ operator is written this way.
    /// </para>
    /// </remarks>
    public static IEnumerable<IReadOnlyList<T>> ChunkBy<T>(IEnumerable<T> source, int size) =>
        throw new NotImplementedException("Lab 02, exercise 5");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 6 — Joins
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the SKUs present in <paramref name="lines"/> that have no matching entry in
    /// <paramref name="catalogueSkus"/> — the "orphaned" products.
    /// </summary>
    /// <remarks>
    /// <para>HINT: <c>Except</c> is the direct route. A left outer join via
    /// <c>GroupJoin</c> + <c>DefaultIfEmpty</c> is the general one — try both.</para>
    /// <para>
    /// THINK: what is the complexity of each? <c>Except</c> builds a hash set;
    /// a naive <c>Where(x =&gt; !list.Contains(x))</c> is O(n·m). With 10,000 lines and
    /// 10,000 SKUs that is a hundred million comparisons.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> OrphanedSkus(
        IEnumerable<Line> lines,
        IEnumerable<string> catalogueSkus) =>
        throw new NotImplementedException("Lab 02, exercise 6");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 7 — Windowing
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a running total of daily revenue, ordered by date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For lines on 3 days with revenue 10, 20, 30, the result is
    /// <c>[(day1, 10), (day2, 30), (day3, 60)]</c>.
    /// </para>
    /// <para>
    /// HINT: group by date, order by date, then accumulate. <c>Aggregate</c> can do it, but a
    /// plain <c>foreach</c> with a running variable is clearer — and clarity beats cleverness
    /// in code someone else has to maintain.
    /// </para>
    /// </remarks>
    public static IEnumerable<(DateOnly Date, decimal RunningTotal)> CumulativeDailyRevenue(
        IEnumerable<Line> lines) =>
        throw new NotImplementedException("Lab 02, exercise 7");
}
