namespace LogiFlow.Application.Common;

/// <summary>
/// One page of results plus the metadata a client needs to render pagination controls.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The rows on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows returned per page.</param>
/// <param name="TotalCount">Total rows matching the filter, across all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>True when a previous page exists.</summary>
    public bool HasPrevious => Page > 1;

    /// <summary>True when a further page exists.</summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>An empty page.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>
/// Base for paged queries. Clamps its own inputs so no handler has to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why every property is nullable — a minimal-API binding lesson learned the hard way.</b>
/// </para>
/// <para>
/// The obvious design is <c>public int Page { get; init; } = 1;</c>. It compiles, it looks
/// correct, and it fails at runtime the moment a client omits the parameter:
/// </para>
/// <code>Required parameter "int Page" was not provided from query string.</code>
/// <para>
/// <c>[AsParameters]</c> binding treats a non-nullable property as <b>required</b>. A C#
/// property initialiser is invisible to the binder — it only sees the type, and a non-nullable
/// <c>int</c> has no way to represent "absent". So the request is rejected before your default
/// ever runs.
/// </para>
/// <para>
/// The fix is to make the wire-facing property nullable, which is the binder's signal for
/// optional, and expose the resolved value through a separate computed property. That also
/// draws a genuinely useful distinction: <see cref="Page"/> is <i>what the client sent</i>,
/// <see cref="PageNumber"/> is <i>what we will actually use</i>.
/// </para>
/// <para>
/// <b>The clamping is a denial-of-service guard, not politeness.</b> A public endpoint accepting
/// <c>?pageSize=1000000</c> lets one request pull your whole orders table into memory. It will
/// be found — by a scraper if not an attacker. Enforce the ceiling server-side; never trust that
/// the client's dropdown only offers 10/25/50.
/// </para>
/// <para>
/// <b>A caveat worth knowing:</b> offset pagination (<c>OFFSET 50000 ROWS</c>) makes SQL Server
/// read and discard 50,000 rows before returning anything, so deep pages get progressively
/// slower. It also skips or repeats rows when the underlying data changes between requests.
/// Keyset pagination ("give me the 25 after this id") fixes both and is what you want for
/// infinite scroll — see <c>course/module-07-sql-and-transactions/06-pagination.md</c>.
/// Offset is used here because it is what an admin UI with page numbers needs.
/// </para>
/// </remarks>
public abstract record PagedQuery
{
    /// <summary>Hard ceiling on rows per page.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Rows per page when the client does not say.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>The page number as supplied by the client, or <c>null</c> if omitted.</summary>
    public int? Page { get; init; }

    /// <summary>The page size as supplied by the client, or <c>null</c> if omitted.</summary>
    public int? PageSize { get; init; }

    /// <summary>The 1-based page actually used. Nonsense input resolves to page 1.</summary>
    public int PageNumber => Page is null or < 1 ? 1 : Page.Value;

    /// <summary>The page size actually used, clamped to 1..<see cref="MaxPageSize"/>.</summary>
    public int Size => PageSize switch
    {
        null or <= 0 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => PageSize.Value,
    };

    /// <summary>Rows to skip. Derived, never supplied by the caller.</summary>
    public int Skip => (PageNumber - 1) * Size;
}

/// <summary>Sort direction for a query.</summary>
public enum SortDirection
{
    /// <summary>Smallest first.</summary>
    Ascending = 0,

    /// <summary>Largest first.</summary>
    Descending = 1,
}
