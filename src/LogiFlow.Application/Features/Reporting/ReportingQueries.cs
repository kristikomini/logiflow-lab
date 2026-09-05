using FluentValidation;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Behaviors;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Features.Reporting;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  Reporting queries. Thin by design — the interesting work is the LINQ in
//  Infrastructure/Persistence/Queries/ReportingQueries.cs, which module 9 dissects.
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Revenue and volume over time.</summary>
/// <param name="FromUtc">Inclusive start.</param>
/// <param name="ToUtc">Exclusive end.</param>
/// <param name="Period">Bucket size.</param>
public sealed record GetSalesReportQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc, SalesPeriod Period)
    : IQuery<IReadOnlyList<SalesByPeriodDto>>, ICacheableQuery
{
    /// <inheritdoc />
    /// <remarks>
    /// Every parameter that changes the result is in the key. The date components are rendered
    /// with a round-trip format so two different <see cref="DateTimeOffset"/> values can never
    /// collide on the same string.
    /// </remarks>
    public string CacheKey => $"report:sales:{FromUtc:O}:{ToUtc:O}:{Period}";

    /// <inheritdoc />
    /// <remarks>
    /// Fifteen minutes. Aggregates over a date range barely move minute to minute, and this
    /// query is expensive enough that a dashboard refreshing every 30 seconds would otherwise
    /// hammer the database for no new information.
    /// </remarks>
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(15);
}

/// <summary>Input validation for <see cref="GetSalesReportQuery"/>.</summary>
public sealed class GetSalesReportQueryValidator : AbstractValidator<GetSalesReportQuery>
{
    /// <summary>Configures the rules.</summary>
    public GetSalesReportQueryValidator()
    {
        RuleFor(x => x.FromUtc)
            .LessThan(x => x.ToUtc)
            .WithMessage("'FromUtc' must be earlier than 'ToUtc'.");

        // A guard against someone asking for a daily report spanning ten years and receiving
        // 3,650 rows they will not read - while the database sorts every order ever placed.
        RuleFor(x => x)
            .Must(q => (q.ToUtc - q.FromUtc).TotalDays <= 366 * 3)
            .WithMessage("A reporting window cannot exceed three years.")
            .WithName(nameof(GetSalesReportQuery.FromUtc));

        RuleFor(x => x.Period).IsInEnum();
    }
}

/// <summary>Handles <see cref="GetSalesReportQuery"/>.</summary>
/// <param name="reporting">The reporting read side.</param>
internal sealed class GetSalesReportQueryHandler(IReportingQueries reporting)
    : IQueryHandler<GetSalesReportQuery, IReadOnlyList<SalesByPeriodDto>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SalesByPeriodDto>>> HandleAsync(
        GetSalesReportQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SalesByPeriodDto> rows = await reporting
            .GetSalesByPeriodAsync(request.FromUtc, request.ToUtc, request.Period, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rows);
    }
}

/// <summary>Best-selling products in a window.</summary>
/// <param name="FromUtc">Inclusive start.</param>
/// <param name="ToUtc">Exclusive end.</param>
/// <param name="Take">How many rows.</param>
public sealed record GetTopProductsQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Take = 10)
    : IQuery<IReadOnlyList<TopProductDto>>, ICacheableQuery
{
    /// <inheritdoc />
    public string CacheKey => $"report:topproducts:{FromUtc:O}:{ToUtc:O}:{Take}";

    /// <inheritdoc />
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(15);
}

/// <summary>Input validation for <see cref="GetTopProductsQuery"/>.</summary>
public sealed class GetTopProductsQueryValidator : AbstractValidator<GetTopProductsQuery>
{
    /// <summary>Configures the rules.</summary>
    public GetTopProductsQueryValidator()
    {
        RuleFor(x => x.FromUtc).LessThan(x => x.ToUtc);
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
    }
}

/// <summary>Handles <see cref="GetTopProductsQuery"/>.</summary>
/// <param name="reporting">The reporting read side.</param>
internal sealed class GetTopProductsQueryHandler(IReportingQueries reporting)
    : IQueryHandler<GetTopProductsQuery, IReadOnlyList<TopProductDto>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TopProductDto>>> HandleAsync(
        GetTopProductsQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TopProductDto> rows = await reporting
            .GetTopProductsAsync(request.FromUtc, request.ToUtc, request.Take, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rows);
    }
}

/// <summary>Per-customer lifetime statistics.</summary>
/// <param name="MinimumOrders">Exclude customers below this order count.</param>
public sealed record GetCustomerStatsQuery(int MinimumOrders = 1)
    : IQuery<IReadOnlyList<CustomerOrderStatsDto>>;

/// <summary>Handles <see cref="GetCustomerStatsQuery"/>.</summary>
/// <param name="reporting">The reporting read side.</param>
internal sealed class GetCustomerStatsQueryHandler(IReportingQueries reporting)
    : IQueryHandler<GetCustomerStatsQuery, IReadOnlyList<CustomerOrderStatsDto>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CustomerOrderStatsDto>>> HandleAsync(
        GetCustomerStatsQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomerOrderStatsDto> rows = await reporting
            .GetCustomerStatsAsync(request.MinimumOrders, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rows);
    }
}

/// <summary>Products at or below their reorder threshold.</summary>
public sealed record GetLowStockQuery : IQuery<IReadOnlyList<LowStockDto>>;

/// <summary>Handles <see cref="GetLowStockQuery"/>.</summary>
/// <param name="reporting">The reporting read side.</param>
internal sealed class GetLowStockQueryHandler(IReportingQueries reporting)
    : IQueryHandler<GetLowStockQuery, IReadOnlyList<LowStockDto>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LowStockDto>>> HandleAsync(
        GetLowStockQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LowStockDto> rows = await reporting
            .GetLowStockAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rows);
    }
}
