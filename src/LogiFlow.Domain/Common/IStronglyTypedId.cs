namespace LogiFlow.Domain.Common;

/// <summary>
/// Contract for a strongly-typed identifier that wraps a <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not just use <c>Guid</c> everywhere?</b> Because this compiles, and it is a bug:
/// </para>
/// <code>
/// void Ship(Guid orderId, Guid warehouseId) { }
/// Ship(warehouseId, orderId);   // arguments swapped — compiler is perfectly happy
/// </code>
/// <para>
/// With <see cref="Orders.OrderId"/> and <see cref="Inventory.WarehouseId"/> as distinct
/// types, that same mistake is a compile error. Whole categories of "wrong ID passed to
/// the wrong parameter" bugs stop existing. This is called making illegal states
/// unrepresentable, and it is the cheapest correctness win in the entire codebase.
/// </para>
/// <para>
/// <b>The C# feature that makes this practical:</b> <c>static abstract</c> interface members
/// (C# 11). Before them, generic code could not say "call the static factory on T", so every
/// ID type needed hand-written glue. Now <see cref="From"/> and <see cref="New"/> are callable
/// from a generic method with a <c>where TId : IStronglyTypedId&lt;TId&gt;</c> constraint —
/// which is exactly how the EF Core layer registers a value converter for every ID type in
/// one loop rather than one line per type.
/// See <c>LogiFlow.Infrastructure.Persistence.Conventions.StronglyTypedIdConvention</c>.
/// </para>
/// <para>
/// <b>Why <c>readonly record struct</c> for the implementations?</b>
/// <list type="bullet">
///   <item><description><c>struct</c> — no heap allocation; an ID is 16 bytes, same as the Guid it wraps.</description></item>
///   <item><description><c>readonly</c> — the compiler stops defensive copies on every member access.</description></item>
///   <item><description><c>record</c> — value equality, <c>GetHashCode</c>, and <c>ToString</c> for free.</description></item>
/// </list>
/// </para>
/// Covered in: <c>course/module-01-csharp-advanced/04-records-and-structs.md</c>
/// </remarks>
/// <typeparam name="TSelf">The implementing type itself (the "curiously recurring" pattern).</typeparam>
public interface IStronglyTypedId<out TSelf> where TSelf : struct
{
    /// <summary>The underlying primitive value stored in the database.</summary>
    Guid Value { get; }

    /// <summary>Rehydrates an ID from its primitive form (from the DB, a route parameter, JSON).</summary>
    static abstract TSelf From(Guid value);

    /// <summary>Mints a brand new identifier.</summary>
    /// <remarks>
    /// Implementations use <see cref="Guid.CreateVersion7()"/>, not <see cref="Guid.NewGuid"/>.
    /// A v7 GUID is time-ordered in its high bits, so rows insert at the *end* of a clustered
    /// index instead of scattering random pages. On SQL Server that is the difference between
    /// an append and a page split on every insert. Measured in <c>Labs.Benchmarks</c>.
    /// </remarks>
    static abstract TSelf New();
}
