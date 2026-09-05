namespace Labs.Exercises.Exercises;

/// <summary>
/// LAB 04 — Equality, hashing, comparison and collection choice.
/// </summary>
/// <remarks>
/// <para>
/// Implement each member below. Run <c>dotnet test labs/Labs.Exercises</c> until green.
/// Do not edit <c>Lab04_EqualityTests.cs</c> — it is the spec.
/// </para>
/// <para>Read <c>course/module-20-equality-and-collections/</c> first.</para>
/// <para>
/// Every exercise here corresponds to a bug that is currently in production somewhere,
/// in code that was reviewed and approved.
/// </para>
/// </remarks>
public static class Lab04Equality
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 1 — The equality contract
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Identifies one product in one warehouse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is used as a <c>Dictionary</c> key, so it must implement the contract properly.
    /// </para>
    /// <para>REQUIREMENTS:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Two keys are equal when the SKUs match ignoring case (ordinal, not culture) and the
    ///     warehouse ids match exactly.
    ///   </description></item>
    ///   <item><description>Equal keys must return the same hash code.</description></item>
    ///   <item><description>It must not allocate when compared against another StockKey.</description></item>
    /// </list>
    /// <para>
    /// HINT: implement <c>IEquatable&lt;StockKey&gt;</c> so the typed overload is found first;
    /// override <c>Equals(object?)</c> to agree with it; use <c>HashCode.Combine</c> and
    /// <c>StringComparer.OrdinalIgnoreCase.GetHashCode</c>.
    /// </para>
    /// <para>
    /// THINK: why is <c>Sku.ToLower().GetHashCode()</c> the wrong way to make the hash
    /// case-insensitive? There are two independent reasons, and one of them is in module 23.
    /// </para>
    /// </remarks>
    public readonly struct StockKey : IEquatable<StockKey>
    {
        /// <summary>Creates a key.</summary>
        /// <param name="sku">The product code. Compared ignoring case.</param>
        /// <param name="warehouseId">The warehouse. Compared exactly.</param>
        public StockKey(string sku, int warehouseId)
        {
            Sku = sku;
            WarehouseId = warehouseId;
        }

        /// <summary>The product code.</summary>
        public string Sku { get; }

        /// <summary>The warehouse.</summary>
        public int WarehouseId { get; }

        /// <inheritdoc />
        public bool Equals(StockKey other) =>
            throw new NotImplementedException("Lab 04, exercise 1: StockKey.Equals(StockKey)");

        /// <inheritdoc />
        public override bool Equals(object? obj) =>
            throw new NotImplementedException("Lab 04, exercise 1: StockKey.Equals(object)");

        /// <inheritdoc />
        public override int GetHashCode() =>
            throw new NotImplementedException("Lab 04, exercise 1: StockKey.GetHashCode");

        /// <summary>Equality operator.</summary>
        /// <param name="left">Left.</param>
        /// <param name="right">Right.</param>
        /// <returns>Whether they are equal.</returns>
        public static bool operator ==(StockKey left, StockKey right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        /// <param name="left">Left.</param>
        /// <param name="right">Right.</param>
        /// <returns>Whether they differ.</returns>
        public static bool operator !=(StockKey left, StockKey right) => !left.Equals(right);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 2 — A comparison that does not overflow
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>A warehouse and the units it holds.</summary>
    /// <param name="Name">Warehouse name.</param>
    /// <param name="Units">Units in stock. May be any int, including extreme values.</param>
    public sealed record Holding(string Name, int Units);

    /// <summary>
    /// Orders holdings by units DESCENDING, then by name ascending (ordinal) as a tiebreaker.
    /// </summary>
    /// <remarks>
    /// <para>HINT: this is an <c>IComparer&lt;Holding&gt;</c> returned as a value.</para>
    /// <para>
    /// THE TRAP: the obvious implementation of "descending by units" is
    /// <c>y.Units - x.Units</c>. One of the tests passes <c>int.MaxValue</c> and
    /// <c>int.MinValue</c>, and that subtraction overflows and REVERSES the sign — so the
    /// comparer reports that the largest holding is the smallest. Use <c>CompareTo</c>.
    /// </para>
    /// <para>
    /// THINK: why does a tiebreaker matter at all? (Module 07 has the answer, and it is the
    /// same reason pagination without a deterministic ORDER BY returns duplicate rows.)
    /// </para>
    /// </remarks>
    /// <returns>The comparer.</returns>
    public static IComparer<Holding> ByUnitsDescendingThenName() =>
        throw new NotImplementedException("Lab 04, exercise 2");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 3 — Choosing the right collection
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the SKUs present in <paramref name="ordered"/> but missing from
    /// <paramref name="inStock"/>, in their original order, without duplicates.
    /// </summary>
    /// <remarks>
    /// <para>SKUs are compared ignoring case, ordinally.</para>
    /// <para>
    /// THE POINT OF THIS EXERCISE: the naive version is
    /// <c>ordered.Where(s =&gt; !inStock.Contains(s))</c>, which is O(n×m) — and the test
    /// passes 20,000 items in each sequence, so the naive version takes long enough to notice.
    /// Build a lookup first.
    /// </para>
    /// <para>
    /// HINT: <c>HashSet&lt;string&gt;</c> takes an <c>IEqualityComparer&lt;string&gt;</c> in its
    /// constructor, and a second one can do the de-duplication of the output.
    /// </para>
    /// </remarks>
    /// <param name="ordered">SKUs that were ordered.</param>
    /// <param name="inStock">SKUs currently held.</param>
    /// <returns>The missing SKUs, first-seen order, no duplicates.</returns>
    public static IReadOnlyList<string> MissingSkus(
        IEnumerable<string> ordered,
        IEnumerable<string> inStock) =>
        throw new NotImplementedException("Lab 04, exercise 3");

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  EXERCISE 4 — The mutable-key bug, made concrete
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A deliberately broken key type. DO NOT FIX IT — exercise 4 is about observing it.
    /// </summary>
    public sealed class MutableStockKey
    {
        /// <summary>The product code. Mutable, which is the whole problem.</summary>
        public string Sku { get; set; } = "";

        /// <inheritdoc />
        public override bool Equals(object? obj) =>
            obj is MutableStockKey other && string.Equals(other.Sku, Sku, StringComparison.Ordinal);

        /// <inheritdoc />
        public override int GetHashCode() => Sku.GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>
    /// Puts <paramref name="key"/> into a dictionary, then mutates its <c>Sku</c> to
    /// <paramref name="newSku"/>, and reports what the dictionary thinks afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Return <c>(found, count)</c> where <c>found</c> is the result of
    /// <c>ContainsKey(key)</c> AFTER the mutation, and <c>count</c> is <c>dictionary.Count</c>
    /// after the mutation.
    /// </para>
    /// <para>
    /// This exercise has no clever implementation — write the four obvious lines. The test
    /// asserts the values, and the point is that you have to predict them before you run it.
    /// Write your prediction down first. If you get it right for the right reason, you
    /// understand rule 3 of the hashing contract.
    /// </para>
    /// </remarks>
    /// <param name="key">The key to insert and then mutate.</param>
    /// <param name="newSku">The value to mutate it to.</param>
    /// <returns>Whether the key is still findable, and how many entries remain.</returns>
    public static (bool Found, int Count) MutateKeyInPlace(MutableStockKey key, string newSku) =>
        throw new NotImplementedException("Lab 04, exercise 4");
}
