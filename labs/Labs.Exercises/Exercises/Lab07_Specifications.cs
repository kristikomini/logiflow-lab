using System.Linq.Expressions;

namespace Labs.Exercises.Exercises;

/// <summary>
/// LAB 07 — Composable specifications and keyset pagination.
/// </summary>
/// <remarks>
/// <para>
/// Implement each member below. Run <c>dotnet test labs/Labs.Exercises</c> until green.
/// Do not edit <c>Lab07_SpecificationsTests.cs</c> — it is the spec.
/// </para>
/// <para>
/// Read <c>course/module-06-efcore/</c> section 6, "Repositories and the specification pattern"
/// and <c>course/module-07-sql-and-transactions/</c> section 6, "Pagination that does not lie".
/// </para>
/// <para>
/// This is the EF Core and SQL material that can be practised without a database, because both
/// problems are really about building the right <i>expression</i> and the right <i>WHERE
/// clause</i> — and both are places where the obvious implementation silently produces wrong
/// results in production rather than an error.
/// </para>
/// </remarks>
public static class Lab07Specifications
{
    /// <summary>A product, for the exercises below.</summary>
    /// <param name="Sku">Product code.</param>
    /// <param name="Category">Category name.</param>
    /// <param name="Price">Unit price.</param>
    /// <param name="Discontinued">Whether it is still sold.</param>
    public sealed record Product(string Sku, string Category, decimal Price, bool Discontinued);

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 1 — Compose predicates without compiling them
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Combines two predicates with <c>AND</c>, producing a single expression tree.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="left">The first predicate.</param>
    /// <param name="right">The second predicate.</param>
    /// <returns>A predicate matching both.</returns>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     The result must be a real expression tree whose body is an <c>AndAlso</c> node. The
    ///     test asserts this, because it is the entire point.
    ///   </description></item>
    ///   <item><description>Short-circuiting: <c>AndAlso</c>, not <c>And</c>.</description></item>
    ///   <item><description>Both sides must be rewritten to use a single shared parameter.</description></item>
    /// </list>
    /// <para>
    /// THE OBVIOUS WRONG ANSWER:
    /// </para>
    /// <code>
    /// return x =&gt; left.Compile()(x) &amp;&amp; right.Compile()(x);
    /// </code>
    /// <para>
    /// It passes any test that only checks the results. It is also useless, because EF Core
    /// cannot translate a call to a compiled delegate — so the moment this reaches
    /// <c>IQueryable</c>, either the whole table is pulled into memory and filtered there, or
    /// you get a translation exception. This is the difference between <c>IEnumerable</c> and
    /// <c>IQueryable</c> from module 03 with money attached.
    /// </para>
    /// <para>
    /// HINT: the difficulty is that <paramref name="left"/> and <paramref name="right"/> each
    /// have their <i>own</i> <c>ParameterExpression</c>, and you cannot mix parameters from two
    /// trees. Write a small <c>ExpressionVisitor</c> that replaces one parameter with another,
    /// apply it to the right-hand body, then
    /// <c>Expression.Lambda&lt;Func&lt;T, bool&gt;&gt;(Expression.AndAlso(...), parameter)</c>.
    /// </para>
    /// </remarks>
    public static Expression<Func<T, bool>> And<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right) =>
        throw new NotImplementedException("Lab 07, exercise 1");

    /// <summary>
    /// Combines two predicates with <c>OR</c>, producing a single expression tree.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="left">The first predicate.</param>
    /// <param name="right">The second predicate.</param>
    /// <returns>A predicate matching either.</returns>
    /// <remarks>Same rules as <see cref="And{T}"/>; the body must be an <c>OrElse</c> node.</remarks>
    public static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right) =>
        throw new NotImplementedException("Lab 07, exercise 1");

    /// <summary>
    /// Negates a predicate, producing a single expression tree.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="predicate">The predicate to negate.</param>
    /// <returns>A predicate matching the opposite.</returns>
    /// <remarks>The body must be a <c>Not</c> node, and the original parameter is reused.</remarks>
    public static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> predicate) =>
        throw new NotImplementedException("Lab 07, exercise 1");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 2 — An opaque, tamper-evident cursor
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>Where the previous page stopped.</summary>
    /// <param name="CreatedAt">The sort key of the last row returned.</param>
    /// <param name="Id">The tie-breaker of the last row returned.</param>
    public readonly record struct Cursor(DateTimeOffset CreatedAt, Guid Id)
    {
        /// <summary>
        /// Encodes this cursor as a URL-safe opaque token.
        /// </summary>
        /// <returns>The token.</returns>
        /// <remarks>
        /// <para>REQUIREMENTS:</para>
        /// <list type="bullet">
        ///   <item><description>Round-trips exactly, including the offset on <c>CreatedAt</c>.</description></item>
        ///   <item><description>
        ///     URL-safe: the result must contain no <c>+</c>, <c>/</c> or <c>=</c>, because this
        ///     goes in a query string.
        ///   </description></item>
        ///   <item><description>Opaque: a caller must not be able to read a date out of it by eye.</description></item>
        /// </list>
        /// <para>
        /// HINT: <c>Base64Url</c> exists in .NET 9 and later. Format the timestamp with
        /// the round-trip specifier <c>"O"</c> — anything else loses precision or the offset,
        /// and a cursor that loses precision skips rows.
        /// </para>
        /// <para>
        /// THINK: opaque is not the same as secure. Anyone can decode this. That is fine for a
        /// cursor and would not be fine for anything else — see chapter 15 of the site on why
        /// Base64 is an encoding, not a cipher.
        /// </para>
        /// </remarks>
        public string Encode() => throw new NotImplementedException("Lab 07, exercise 2");

        /// <summary>
        /// Decodes a token produced by <see cref="Encode"/>.
        /// </summary>
        /// <param name="token">The token. May be null or empty, meaning "the first page".</param>
        /// <param name="cursor">The decoded cursor, if the token was valid.</param>
        /// <returns><c>true</c> if a cursor was decoded.</returns>
        /// <remarks>
        /// <para>
        /// REQUIREMENT: never throw. A cursor arrives from a URL, which means users, crawlers and
        /// people editing it by hand. Garbage in must produce <c>false</c>, not a 500. This is
        /// the <c>TryParse</c> discipline from the screening-test chapter, applied where it
        /// actually matters.
        /// </para>
        /// </remarks>
        public static bool TryDecode(string? token, out Cursor cursor) =>
            throw new NotImplementedException("Lab 07, exercise 2");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 3 — Pagination that does not lie
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>One page of results, plus where to continue from.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="Items">The rows on this page.</param>
    /// <param name="NextCursor">Token for the next page, or <c>null</c> if this was the last.</param>
    public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

    /// <summary>An orderable row.</summary>
    /// <param name="Id">Unique, and the tie-breaker.</param>
    /// <param name="CreatedAt">The sort key. Not unique.</param>
    /// <param name="Name">Payload.</param>
    public sealed record Row(Guid Id, DateTimeOffset CreatedAt, string Name);

    /// <summary>
    /// Returns one page of rows, newest first, continuing after <paramref name="afterToken"/>.
    /// </summary>
    /// <param name="source">All rows, in any order.</param>
    /// <param name="pageSize">How many rows to return. Must be positive.</param>
    /// <param name="afterToken">Cursor from the previous page, or null for the first page.</param>
    /// <returns>The page and the cursor for the next one.</returns>
    /// <remarks>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Order by <c>CreatedAt</c> descending, then <c>Id</c> descending. The tie-breaker is
    ///     not optional: <c>CreatedAt</c> is not unique, and without a deterministic total
    ///     ordering the same row can appear on two pages.
    ///   </description></item>
    ///   <item><description>
    ///     A cursor means "strictly after this row in that ordering" — which is a compound
    ///     comparison, not just <c>CreatedAt &lt; cursor.CreatedAt</c>. Rows sharing the
    ///     cursor's timestamp but with a smaller id are still to come.
    ///   </description></item>
    ///   <item><description>
    ///     <c>NextCursor</c> is <c>null</c> when there are no further rows — not when the page
    ///     came back short, which is a subtly different thing.
    ///   </description></item>
    ///   <item><description>An unreadable token is treated as "start from the beginning".</description></item>
    ///   <item><description><c>pageSize</c> of zero or less throws <c>ArgumentOutOfRangeException</c>.</description></item>
    /// </list>
    /// <para>
    /// WHY NOT <c>Skip(n).Take(m)</c>: because <c>OFFSET</c> counts rows at read time. Insert a
    /// row while somebody is paging and every later page shifts by one — the reader sees a row
    /// twice and never sees another. The test proves exactly this, and it is the reason every
    /// serious API uses cursors. It is also faster: <c>OFFSET 100000</c> makes the database read
    /// and discard a hundred thousand rows.
    /// </para>
    /// </remarks>
    public static Page<Row> KeysetPage(
        IEnumerable<Row> source,
        int pageSize,
        string? afterToken = null) =>
        throw new NotImplementedException("Lab 07, exercise 3");
}
