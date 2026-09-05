using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Inventory;

/// <summary>Failure modes for the inventory aggregate.</summary>
public static class InventoryErrors
{
    /// <summary>Quantity was zero or negative.</summary>
    public static readonly Error InvalidQuantity =
        Error.Validation("Inventory.InvalidQuantity", "Quantity must be at least 1.");

    /// <summary>Stock level cannot go below zero.</summary>
    public static readonly Error NegativeStock =
        Error.Validation("Inventory.NegativeStock", "Stock quantity cannot be negative.");

    /// <summary>Warehouse code was blank.</summary>
    public static readonly Error WarehouseCodeRequired =
        Error.Validation("Inventory.WarehouseCodeRequired", "Warehouse code is required.");

    /// <summary>Warehouse name was blank.</summary>
    public static readonly Error WarehouseNameRequired =
        Error.Validation("Inventory.WarehouseNameRequired", "Warehouse name is required.");

    /// <summary>Stock adjustments must be justified — this is an audit trail, not a free-for-all.</summary>
    public static readonly Error AdjustmentReasonRequired =
        Error.Validation("Inventory.AdjustmentReasonRequired", "A reason is required for a stock adjustment.");

    /// <summary>Not enough sellable stock.</summary>
    /// <remarks>
    /// The message reports what <i>is</i> available, so the caller can offer a partial
    /// fulfilment instead of a bare rejection.
    /// </remarks>
    public static Error InsufficientStock(ProductId productId, int requested, int available) =>
        Error.Conflict(
            "Inventory.InsufficientStock",
            $"Requested {requested} units of product '{productId}' but only {available} are available.");

    /// <summary>Tried to release more than was reserved.</summary>
    public static Error ReleaseExceedsReserved(int requested, int reserved) =>
        Error.Conflict(
            "Inventory.ReleaseExceedsReserved",
            $"Cannot release {requested} units; only {reserved} are reserved.");

    /// <summary>Tried to pick more than was reserved.</summary>
    public static Error PickExceedsReserved(int requested, int reserved) =>
        Error.Conflict(
            "Inventory.PickExceedsReserved",
            $"Cannot pick {requested} units; only {reserved} are reserved.");

    /// <summary>A stock take cannot report fewer units than are already promised to orders.</summary>
    public static Error AdjustmentBelowReserved(int actual, int reserved) =>
        Error.Conflict(
            "Inventory.AdjustmentBelowReserved",
            $"Cannot set stock to {actual}; {reserved} units are already reserved against orders.");

    /// <summary>Product is not tracked at this site.</summary>
    public static Error StockItemNotFound(ProductId productId, string warehouseCode) =>
        Error.NotFound(
            "Inventory.StockItemNotFound",
            $"Product '{productId}' is not stocked at warehouse '{warehouseCode}'.");

    /// <summary>Product is already tracked at this site.</summary>
    public static Error DuplicateStockItem(ProductId productId) =>
        Error.Conflict(
            "Inventory.DuplicateStockItem",
            $"Product '{productId}' is already tracked at this warehouse.");

    /// <summary>No warehouse with that id.</summary>
    public static Error WarehouseNotFound(WarehouseId id) =>
        Error.NotFound("Inventory.WarehouseNotFound", $"No warehouse found with id '{id}'.");

    /// <summary>Warehouse is closed.</summary>
    public static Error WarehouseInactive(string code) =>
        Error.Conflict("Inventory.WarehouseInactive", $"Warehouse '{code}' is not operating.");

    /// <summary>No single site could satisfy the order.</summary>
    public static readonly Error NoWarehouseCanFulfil =
        Error.Conflict(
            "Inventory.NoWarehouseCanFulfil",
            "No single warehouse holds enough stock to fulfil this order.");
}
