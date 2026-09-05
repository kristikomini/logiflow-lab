using LogiFlow.Application.Features.Reporting;
using LogiFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// Analytical reads. The most LINQ-dense file in the solution, and the subject of module 9.
/// </summary>
/// <param name="context">The scoped session.</param>
public sealed class ReportingQueries(LogiFlowDbContext context) : IReportingQueries
{
    /// <summary>
    /// Every monetary figure in these reports is assumed to be in this currency.
    /// </summary>
    /// <remarks>
    /// The system supports multiple currencies per order, so this is a simplifying assumption
    /// for the reporting layer, not a fact about the data. Making it a named constant rather
    /// than a literal keeps it greppable for whoever has to lift the restriction.
    /// </remarks>
    private const string ReportingCurrency = "EUR";

    /// <summary>Statuses that count as revenue. Cancelled and draft orders never do.</summary>
    private static readonly OrderStatus[] RevenueStatuses =
    [
        OrderStatus.Submitted,
        OrderStatus.Confirmed,
        OrderStatus.Shipped,
        OrderStatus.Delivered,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>GroupBy is where LINQ-to-SQL beginners come unstuck, so read this carefully.</b>
    /// </para>
    /// <para>
    /// In LINQ-to-Objects, <c>GroupBy</c> gives you <c>IGrouping&lt;TKey, TElement&gt;</c> and
    /// you can enumerate the members of each group freely. In LINQ-to-SQL you cannot: SQL's
    /// <c>GROUP BY</c> only ever produces one row per group, containing the key and aggregates.
    /// There is no way to get the individual rows back.
    /// </para>
    /// <para>
    /// So this translates and runs in the database:
    /// </para>
    /// <code>
    /// .GroupBy(o =&gt; o.Year).Select(g =&gt; new { g.Key, Total = g.Sum(x =&gt; x.Amount) })
    /// </code>
    /// <para>
    /// ...and this does not, because it asks for the group's members:
    /// </para>
    /// <code>
    /// .GroupBy(o =&gt; o.Year).Select(g =&gt; new { g.Key, Orders = g.ToList() })
    /// </code>
    /// <para>
    /// EF Core 3.0 onwards throws on the second rather than silently pulling the whole table
    /// into memory and grouping there, which earlier versions did. That change broke a lot of
    /// applications and made a lot of them much faster.
    /// </para>
    /// <para>
    /// <b>The grouping key here is computed in SQL</b> — <c>SubmittedAtUtc.Value.Year</c> and
    /// <c>.Month</c> become <c>DATEPART(year, ...)</c> and <c>DATEPART(month, ...)</c>. Grouping
    /// on a computed expression like this cannot use an index on <c>SubmittedAtUtc</c>, so it is
    /// a scan of the filtered range. Acceptable for a report; not for a hot path.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<SalesByPeriodDto>> GetSalesByPeriodAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        SalesPeriod period,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Order> revenueOrders = context.Orders
            .AsNoTracking()
            .Where(o => o.SubmittedAtUtc != null
                        && o.SubmittedAtUtc >= fromUtc
                        && o.SubmittedAtUtc < toUtc
                        && RevenueStatuses.Contains(o.Status));

        // Each branch produces (year, subdivision) so one shared aggregation shape can follow.
        // Building a dictionary of Expression<Func<...>> and picking one is the alternative, and
        // is worse here: the switch keeps each translated shape visible and reviewable.
        var grouped = period switch
        {
            SalesPeriod.Daily => await revenueOrders
                .GroupBy(o => new
                {
                    o.SubmittedAtUtc!.Value.Year,
                    o.SubmittedAtUtc!.Value.Month,
                    o.SubmittedAtUtc!.Value.Day,
                })
                .Select(g => new PeriodBucket(
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    g.Count(),
                    g.Sum(o => o.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity))) ?? 0m,
                    g.Sum(o => o.Lines.Sum(l => (int?)l.Quantity)) ?? 0))
                .ToListAsync(cancellationToken).ConfigureAwait(false),

            SalesPeriod.Weekly => await revenueOrders
                .GroupBy(o => new
                {
                    o.SubmittedAtUtc!.Value.Year,
                    Week = o.SubmittedAtUtc!.Value.DayOfYear / 7,
                })
                .Select(g => new PeriodBucket(
                    g.Key.Year,
                    g.Key.Week,
                    1,
                    g.Count(),
                    g.Sum(o => o.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity))) ?? 0m,
                    g.Sum(o => o.Lines.Sum(l => (int?)l.Quantity)) ?? 0))
                .ToListAsync(cancellationToken).ConfigureAwait(false),

            SalesPeriod.Monthly => await revenueOrders
                .GroupBy(o => new { o.SubmittedAtUtc!.Value.Year, o.SubmittedAtUtc!.Value.Month })
                .Select(g => new PeriodBucket(
                    g.Key.Year,
                    g.Key.Month,
                    1,
                    g.Count(),
                    g.Sum(o => o.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity))) ?? 0m,
                    g.Sum(o => o.Lines.Sum(l => (int?)l.Quantity)) ?? 0))
                .ToListAsync(cancellationToken).ConfigureAwait(false),

            SalesPeriod.Quarterly => await revenueOrders
                .GroupBy(o => new
                {
                    o.SubmittedAtUtc!.Value.Year,
                    Quarter = ((o.SubmittedAtUtc!.Value.Month - 1) / 3) + 1,
                })
                .Select(g => new PeriodBucket(
                    g.Key.Year,
                    g.Key.Quarter,
                    1,
                    g.Count(),
                    g.Sum(o => o.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity))) ?? 0m,
                    g.Sum(o => o.Lines.Sum(l => (int?)l.Quantity)) ?? 0))
                .ToListAsync(cancellationToken).ConfigureAwait(false),

            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unhandled reporting period."),
        };

        // ── Formatting happens in memory, and should ────────────────────────────────────
        // At most a few hundred already-aggregated rows reach this point. Trying to build the
        // "2026-Q1" label in SQL would need string concatenation and DATEPART gymnastics that
        // are unreadable, unportable, and no faster.
        return [.. grouped
            .Select(b => ToDto(b, period))
            .OrderBy(dto => dto.PeriodStart)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The join-group-project-rank pipeline.</b> Worth reading as five distinct steps:
    /// </para>
    /// <list type="number">
    ///   <item><description><b>Filter</b> lines down to revenue-generating orders in the window.</description></item>
    ///   <item><description><b>Group</b> by product, so each group is one product's sales.</description></item>
    ///   <item><description><b>Aggregate</b> into counts and sums — all in SQL.</description></item>
    ///   <item><description><b>Order and take</b>, so only the top N cross the wire.</description></item>
    ///   <item><description><b>Rank</b> in memory, because the position depends on the final ordering.</description></item>
    /// </list>
    /// <para>
    /// Note <c>Count()</c> on the distinct order ids: <c>g.Select(x =&gt; x.OrderId).Distinct().Count()</c>
    /// becomes <c>COUNT(DISTINCT OrderId)</c>. Getting "how many separate orders included this
    /// product" wrong — by counting lines instead — is an easy and invisible reporting error,
    /// because the number still looks plausible.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.OrderLines
            .AsNoTracking()
            // An explicit join, again because Order has no navigation TO its lines' parent in a
            // form the read side can traverse upward. Joining on the FK is the read model's job.
            .Join(
                context.Orders.Where(o =>
                    o.SubmittedAtUtc != null
                    && o.SubmittedAtUtc >= fromUtc
                    && o.SubmittedAtUtc < toUtc
                    && RevenueStatuses.Contains(o.Status)),
                line => line.OrderId,
                order => order.Id,
                (line, order) => new { line, order.Id })
            .GroupBy(x => new { x.line.ProductId, x.line.Sku, x.line.ProductName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Sku,
                g.Key.ProductName,
                UnitsSold = g.Sum(x => x.line.Quantity),
                Revenue = g.Sum(x => x.line.UnitPrice.Amount * x.line.Quantity),
                OrderCount = g.Select(x => x.Id).Distinct().Count(),
            })
            .OrderByDescending(x => x.UnitsSold)
            .ThenByDescending(x => x.Revenue)   // deterministic tiebreak
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Select with an index — the overload people forget exists. Ranking has to happen after
        // the ordering is final, and SQL Server's ROW_NUMBER() is not worth the complexity for
        // at most 100 already-materialised rows.
        return [.. rows.Select((r, index) => new TopProductDto(
            r.ProductId.Value,
            r.Sku.Value,
            r.ProductName,
            r.UnitsSold,
            r.Revenue,
            r.OrderCount,
            index + 1))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Several aggregates over one grouping, in a single pass.</b> Count, Sum, Average, Max,
    /// Min and Max-of-a-date all come from one <c>GROUP BY</c> — SQL Server computes them
    /// together while scanning the group once. Issuing six separate queries for six numbers is
    /// the mistake this avoids.
    /// </para>
    /// <para>
    /// <b><c>HAVING</c>, not <c>WHERE</c>.</b> The <c>Where</c> after the <c>Select</c> filters
    /// on an <i>aggregate</i> (<c>OrderCount</c>), so EF renders it as <c>HAVING</c>. A
    /// <c>Where</c> placed before the <c>GroupBy</c> would filter individual rows instead —
    /// a completely different question, and a classic SQL interview trap.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<CustomerOrderStatsDto>> GetCustomerStatsAsync(
        int minimumOrders,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var rows = await context.Orders
            .AsNoTracking()
            .Where(o => o.SubmittedAtUtc != null && RevenueStatuses.Contains(o.Status))
            .Join(
                context.Customers,
                order => order.CustomerId,
                customer => customer.Id,
                (order, customer) => new
                {
                    order.CustomerId,
                    customer.CompanyName,
                    customer.Tier,
                    order.SubmittedAtUtc,
                    Total = order.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity)) ?? 0m,
                })
            .GroupBy(x => new { x.CustomerId, x.CompanyName, x.Tier })
            .Select(g => new
            {
                g.Key.CustomerId,
                g.Key.CompanyName,
                g.Key.Tier,
                OrderCount = g.Count(),
                TotalSpent = g.Sum(x => x.Total),
                AverageOrderValue = g.Average(x => x.Total),
                LargestOrderValue = g.Max(x => x.Total),
                FirstOrderUtc = g.Min(x => x.SubmittedAtUtc!.Value),
                LastOrderUtc = g.Max(x => x.SubmittedAtUtc!.Value),
            })
            // Filters on an aggregate -> HAVING COUNT(*) >= @minimumOrders
            .Where(x => x.OrderCount >= minimumOrders)
            .OrderByDescending(x => x.TotalSpent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(r => new CustomerOrderStatsDto(
            r.CustomerId.Value,
            r.CompanyName,
            r.Tier.ToString(),
            r.OrderCount,
            decimal.Round(r.TotalSpent, 2, MidpointRounding.ToEven),
            decimal.Round(r.AverageOrderValue, 2, MidpointRounding.ToEven),
            decimal.Round(r.LargestOrderValue, 2, MidpointRounding.ToEven),
            r.FirstOrderUtc,
            r.LastOrderUtc,
            (int)(now - r.LastOrderUtc).TotalDays))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>A three-way join with the comparison in the WHERE clause.</b> The predicate
    /// <c>QuantityOnHand - QuantityReserved &lt;= ReorderThreshold</c> compares columns to each
    /// other rather than to a constant. That is perfectly valid SQL, but no index can help with
    /// it — the database must evaluate the arithmetic for every row. On a large stock table the
    /// fix is a persisted computed column (<c>QuantityAvailable AS QuantityOnHand -
    /// QuantityReserved PERSISTED</c>) with an index on it. Noted here because "why is my
    /// perfectly-indexed table still scanning?" is a question worth being able to answer.
    /// </remarks>
    public async Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.StockItems
            .AsNoTracking()
            .Where(s => s.QuantityOnHand - s.QuantityReserved <= s.ReorderThreshold)
            .Join(
                context.Warehouses.Where(w => w.IsActive),
                stock => stock.WarehouseId,
                warehouse => warehouse.Id,
                (stock, warehouse) => new { stock, warehouse })
            .Join(
                context.Products,
                x => x.stock.ProductId,
                product => product.Id,
                (x, product) => new
                {
                    x.warehouse.Code,
                    WarehouseName = x.warehouse.Name,
                    product.Id,
                    product.Sku,
                    ProductName = product.Name,
                    x.stock.QuantityOnHand,
                    x.stock.QuantityReserved,
                    x.stock.ReorderThreshold,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows
            .Select(r => new LowStockDto(
                r.Code,
                r.WarehouseName,
                r.Id.Value,
                r.Sku.Value,
                r.ProductName,
                r.QuantityOnHand - r.QuantityReserved,
                r.QuantityReserved,
                r.ReorderThreshold,
                r.ReorderThreshold - (r.QuantityOnHand - r.QuantityReserved)))
            // Worst shortfall first: that is the order a purchasing team wants to work through.
            .OrderByDescending(dto => dto.Shortfall)
            .ThenBy(dto => dto.Sku)];
    }

    /// <summary>Intermediate shape shared by all four grouping branches.</summary>
    /// <remarks>
    /// A named record rather than an anonymous type, because an anonymous type cannot be the
    /// declared result of a <c>switch</c> expression whose arms are built separately — each arm
    /// would produce its own distinct anonymous type and the compiler could not find a common one.
    /// </remarks>
    private sealed record PeriodBucket(
        int Year,
        int Subdivision,
        int Day,
        int OrderCount,
        decimal Revenue,
        int UnitsSold);

    private static SalesByPeriodDto ToDto(PeriodBucket bucket, SalesPeriod period)
    {
        (DateOnly start, string label) = period switch
        {
            SalesPeriod.Daily => (
                new DateOnly(bucket.Year, bucket.Subdivision, bucket.Day),
                $"{bucket.Year:0000}-{bucket.Subdivision:00}-{bucket.Day:00}"),

            SalesPeriod.Weekly => (
                new DateOnly(bucket.Year, 1, 1).AddDays(bucket.Subdivision * 7),
                $"{bucket.Year:0000}-W{bucket.Subdivision:00}"),

            SalesPeriod.Monthly => (
                new DateOnly(bucket.Year, bucket.Subdivision, 1),
                $"{bucket.Year:0000}-{bucket.Subdivision:00}"),

            SalesPeriod.Quarterly => (
                new DateOnly(bucket.Year, ((bucket.Subdivision - 1) * 3) + 1, 1),
                $"{bucket.Year:0000}-Q{bucket.Subdivision}"),

            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unhandled reporting period."),
        };

        decimal average = bucket.OrderCount == 0
            ? 0m
            : decimal.Round(bucket.Revenue / bucket.OrderCount, 2, MidpointRounding.ToEven);

        return new SalesByPeriodDto(
            start,
            label,
            bucket.OrderCount,
            decimal.Round(bucket.Revenue, 2, MidpointRounding.ToEven),
            average,
            bucket.UnitsSold,
            // Single-currency reporting, stated as an assumption rather than hidden. A
            // multi-currency system must group by currency as well and convert at an explicit
            // rate and date - summing mixed currencies produces a number that means nothing.
            ReportingCurrency);
    }
}
