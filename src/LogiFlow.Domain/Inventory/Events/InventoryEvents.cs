using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;

namespace LogiFlow.Domain.Inventory.Events;

/// <summary>Raised when units are promised to an order.</summary>
/// <param name="WarehouseId">Where the stock is.</param>
/// <param name="ProductId">What was reserved.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="RemainingAvailable">Sellable units left after the reservation.</param>
public sealed record StockReservedDomainEvent(
    WarehouseId WarehouseId,
    ProductId ProductId,
    int Quantity,
    int RemainingAvailable) : DomainEventBase;

/// <summary>Raised when a reservation is given back, e.g. after a cancellation.</summary>
/// <param name="WarehouseId">Where the stock is.</param>
/// <param name="ProductId">What was released.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="RemainingAvailable">Sellable units after the release.</param>
public sealed record StockReleasedDomainEvent(
    WarehouseId WarehouseId,
    ProductId ProductId,
    int Quantity,
    int RemainingAvailable) : DomainEventBase;

/// <summary>Raised when a picker physically removes stock from the shelf.</summary>
/// <param name="WarehouseId">Where the stock was.</param>
/// <param name="ProductId">What was picked.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="RemainingOnHand">Physical units left.</param>
public sealed record StockPickedDomainEvent(
    WarehouseId WarehouseId,
    ProductId ProductId,
    int Quantity,
    int RemainingOnHand) : DomainEventBase;

/// <summary>Raised when a supplier delivery is booked in.</summary>
/// <param name="WarehouseId">Where it landed.</param>
/// <param name="ProductId">What arrived.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="NewQuantityOnHand">Physical units after receipt.</param>
public sealed record StockReceivedDomainEvent(
    WarehouseId WarehouseId,
    ProductId ProductId,
    int Quantity,
    int NewQuantityOnHand) : DomainEventBase;

/// <summary>Raised when a stock take corrects the recorded quantity.</summary>
/// <param name="WarehouseId">Where.</param>
/// <param name="ProductId">Which product.</param>
/// <param name="PreviousQuantity">What the system thought.</param>
/// <param name="NewQuantity">What was actually counted.</param>
/// <param name="Reason">Why — shrinkage, breakage, miscount.</param>
/// <remarks>
/// Discrepancies are a financial and audit matter, so this event carries both numbers and a
/// justification. It is exactly the sort of thing an auditor will ask you to produce a year later.
/// </remarks>
public sealed record StockAdjustedDomainEvent(
    WarehouseId WarehouseId,
    ProductId ProductId,
    int PreviousQuantity,
    int NewQuantity,
    string Reason) : DomainEventBase
{
    /// <summary>Signed difference; negative means stock was lost.</summary>
    public int Delta => NewQuantity - PreviousQuantity;
}

/// <summary>Raised when available stock falls to or below the reorder threshold.</summary>
/// <param name="WarehouseId">Where.</param>
/// <param name="WarehouseCode">Site code, denormalised for alerting.</param>
/// <param name="ProductId">Which product.</param>
/// <param name="QuantityAvailable">Sellable units remaining.</param>
/// <param name="ReorderThreshold">The threshold that was crossed.</param>
public sealed record StockRunningLowDomainEvent(
    WarehouseId WarehouseId,
    string WarehouseCode,
    ProductId ProductId,
    int QuantityAvailable,
    int ReorderThreshold) : DomainEventBase;
