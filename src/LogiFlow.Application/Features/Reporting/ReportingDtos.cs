namespace LogiFlow.Application.Features.Reporting;

/// <summary>Calendar bucket for a time-series report.</summary>
public enum SalesPeriod
{
    /// <summary>One bucket per day.</summary>
    Daily = 0,

    /// <summary>One bucket per ISO week.</summary>
    Weekly = 1,

    /// <summary>One bucket per calendar month.</summary>
    Monthly = 2,

    /// <summary>One bucket per quarter.</summary>
    Quarterly = 3,
}

/// <summary>Aggregated sales for one time bucket.</summary>
/// <param name="PeriodStart">First day of the bucket.</param>
/// <param name="PeriodLabel">Display label, e.g. <c>2026-03</c> or <c>2026-Q1</c>.</param>
/// <param name="OrderCount">Orders submitted in the bucket.</param>
/// <param name="TotalRevenue">Sum of order values.</param>
/// <param name="AverageOrderValue">Revenue divided by order count.</param>
/// <param name="UnitsSold">Total units across all lines.</param>
/// <param name="Currency">ISO code for the monetary fields.</param>
public sealed record SalesByPeriodDto(
    DateOnly PeriodStart,
    string PeriodLabel,
    int OrderCount,
    decimal TotalRevenue,
    decimal AverageOrderValue,
    int UnitsSold,
    string Currency);

/// <summary>One row of a best-sellers report.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Product code.</param>
/// <param name="ProductName">Product name.</param>
/// <param name="UnitsSold">Units shipped in the window.</param>
/// <param name="Revenue">Money taken for those units.</param>
/// <param name="OrderCount">How many distinct orders included it.</param>
/// <param name="Rank">1-based position, best first.</param>
public sealed record TopProductDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    int UnitsSold,
    decimal Revenue,
    int OrderCount,
    int Rank);

/// <summary>Lifetime statistics for one customer.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="CompanyName">Trading name.</param>
/// <param name="Tier">Current loyalty tier.</param>
/// <param name="OrderCount">Orders they have placed.</param>
/// <param name="TotalSpent">Total value of those orders.</param>
/// <param name="AverageOrderValue">Mean order value.</param>
/// <param name="LargestOrderValue">Their biggest single order.</param>
/// <param name="FirstOrderUtc">When they first bought.</param>
/// <param name="LastOrderUtc">When they last bought.</param>
/// <param name="DaysSinceLastOrder">Recency, for churn analysis.</param>
public sealed record CustomerOrderStatsDto(
    Guid CustomerId,
    string CompanyName,
    string Tier,
    int OrderCount,
    decimal TotalSpent,
    decimal AverageOrderValue,
    decimal LargestOrderValue,
    DateTimeOffset FirstOrderUtc,
    DateTimeOffset LastOrderUtc,
    int DaysSinceLastOrder);

/// <summary>A product whose available stock has fallen to its reorder threshold.</summary>
/// <param name="WarehouseCode">Which site.</param>
/// <param name="WarehouseName">Site name.</param>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">Product code.</param>
/// <param name="ProductName">Product name.</param>
/// <param name="QuantityAvailable">Units still sellable.</param>
/// <param name="QuantityReserved">Units promised to orders.</param>
/// <param name="ReorderThreshold">The level that was crossed.</param>
/// <param name="Shortfall">How far below the threshold it has fallen.</param>
public sealed record LowStockDto(
    string WarehouseCode,
    string WarehouseName,
    Guid ProductId,
    string Sku,
    string ProductName,
    int QuantityAvailable,
    int QuantityReserved,
    int ReorderThreshold,
    int Shortfall);
