namespace LogiFlow.Application.Features.Reporting;

/// <summary>
/// Analytical reads. Separate from <c>IOrderQueries</c> because reporting has different
/// performance characteristics and, in a mature system, a different data source.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why split reporting out at all?</b> These queries scan and aggregate across the whole
/// table rather than fetching a handful of rows by key. That means table scans, big sorts and
/// long-held read locks — exactly the workload that will make your checkout endpoint time out
/// if it runs against the same database at 9am.
/// </para>
/// <para>
/// Keeping the seam here means the day that becomes a problem you point this one interface at a
/// read replica, a nightly snapshot, or a proper warehouse — and nothing else in the codebase
/// changes. Putting these methods on <c>IOrderQueries</c> would make that a refactor instead of
/// a configuration change.
/// </para>
/// <para>
/// Covered in: <c>course/module-09-advanced-linq/</c>, which builds every one of these queries
/// step by step and shows the SQL each produces.
/// </para>
/// </remarks>
public interface IReportingQueries
{
    /// <summary>Revenue and volume grouped into calendar buckets.</summary>
    /// <param name="fromUtc">Inclusive start of the window.</param>
    /// <param name="toUtc">Exclusive end of the window.</param>
    /// <param name="period">Bucket size.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<SalesByPeriodDto>> GetSalesByPeriodAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        SalesPeriod period,
        CancellationToken cancellationToken = default);

    /// <summary>Best-selling products in a window, ranked by units sold.</summary>
    /// <param name="fromUtc">Inclusive start of the window.</param>
    /// <param name="toUtc">Exclusive end of the window.</param>
    /// <param name="take">How many rows to return.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Lifetime value and recency per customer.</summary>
    /// <param name="minimumOrders">Exclude customers below this order count.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<CustomerOrderStatsDto>> GetCustomerStatsAsync(
        int minimumOrders,
        CancellationToken cancellationToken = default);

    /// <summary>Products at or below their reorder threshold, across all active sites.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(CancellationToken cancellationToken = default);
}
