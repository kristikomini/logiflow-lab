using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Inventory;

/// <summary>
/// How much of one product sits in one warehouse. An entity inside the <see cref="Warehouse"/> aggregate.
/// </summary>
/// <remarks>
/// <para>
/// <b>The three-number model.</b> Naive inventory systems store a single "quantity" and get
/// oversells. This one separates:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="QuantityOnHand"/> — units physically on the shelf.</description></item>
///   <item><description><see cref="QuantityReserved"/> — of those, how many are already promised to submitted orders.</description></item>
///   <item><description><see cref="QuantityAvailable"/> — the difference; what you may still sell.</description></item>
/// </list>
/// <para>
/// The distinction between "on hand" and "available" is where oversells come from. Stock is
/// only physically removed when the picker takes it off the shelf, but it stops being sellable
/// the moment an order is submitted. A system tracking one number either oversells (decrements
/// too late) or shows phantom stock-outs (decrements too early).
/// </para>
/// <para>
/// Reservations are then a purely logical operation with no physical movement — which is what
/// makes them cheap to reverse when an order is cancelled.
/// </para>
/// Covered in: <c>course/module-07-sql-and-transactions/05-inventory-concurrency.md</c>
/// </remarks>
public sealed class StockItem : Entity<StockItemId>
{
    internal StockItem(
        StockItemId id,
        WarehouseId warehouseId,
        ProductId productId,
        int quantityOnHand,
        int reorderThreshold) : base(id)
    {
        WarehouseId = warehouseId;
        ProductId = productId;
        QuantityOnHand = quantityOnHand;
        QuantityReserved = 0;
        ReorderThreshold = reorderThreshold;
    }

    private StockItem()
    {
    }

    /// <summary>The warehouse holding this stock.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>The product being held.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Units physically present.</summary>
    public int QuantityOnHand { get; private set; }

    /// <summary>Units promised to submitted orders but not yet picked.</summary>
    public int QuantityReserved { get; private set; }

    /// <summary>Units that can still be sold.</summary>
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>Available level at or below which replenishment should be triggered.</summary>
    public int ReorderThreshold { get; private set; }

    /// <summary>True when available stock has fallen to the reorder threshold.</summary>
    public bool NeedsReplenishment => QuantityAvailable <= ReorderThreshold;

    /// <summary>Promises units to an order.</summary>
    internal Result Reserve(int quantity)
    {
        if (quantity < 1)
        {
            return InventoryErrors.InvalidQuantity;
        }

        if (quantity > QuantityAvailable)
        {
            return InventoryErrors.InsufficientStock(ProductId, quantity, QuantityAvailable);
        }

        QuantityReserved += quantity;
        return Result.Success();
    }

    /// <summary>Gives promised units back, e.g. after a cancellation.</summary>
    internal Result Release(int quantity)
    {
        if (quantity < 1)
        {
            return InventoryErrors.InvalidQuantity;
        }

        if (quantity > QuantityReserved)
        {
            return InventoryErrors.ReleaseExceedsReserved(quantity, QuantityReserved);
        }

        QuantityReserved -= quantity;
        return Result.Success();
    }

    /// <summary>
    /// Physically removes reserved units from the shelf when a picker collects them.
    /// </summary>
    /// <remarks>
    /// This is the only operation that reduces <see cref="QuantityOnHand"/> in normal flow, and
    /// it reduces <see cref="QuantityReserved"/> by the same amount — so
    /// <see cref="QuantityAvailable"/> is unchanged. That is the invariant proving the model is
    /// consistent: picking does not make anything newly sellable, because it was already spoken for.
    /// </remarks>
    internal Result Pick(int quantity)
    {
        if (quantity < 1)
        {
            return InventoryErrors.InvalidQuantity;
        }

        if (quantity > QuantityReserved)
        {
            return InventoryErrors.PickExceedsReserved(quantity, QuantityReserved);
        }

        QuantityOnHand -= quantity;
        QuantityReserved -= quantity;
        return Result.Success();
    }

    /// <summary>Adds newly delivered stock to the shelf.</summary>
    internal Result Receive(int quantity)
    {
        if (quantity < 1)
        {
            return InventoryErrors.InvalidQuantity;
        }

        QuantityOnHand += quantity;
        return Result.Success();
    }

    /// <summary>
    /// Corrects the on-hand count after a physical stock take.
    /// </summary>
    /// <remarks>
    /// Reality wins over the database. Shrinkage, breakage and miscounts are facts of
    /// warehouse life, so there must be a way to set the true number — but never below what is
    /// already reserved, or the model would be promising units that do not exist.
    /// </remarks>
    internal Result AdjustTo(int actualQuantityOnHand)
    {
        if (actualQuantityOnHand < 0)
        {
            return InventoryErrors.NegativeStock;
        }

        if (actualQuantityOnHand < QuantityReserved)
        {
            return InventoryErrors.AdjustmentBelowReserved(actualQuantityOnHand, QuantityReserved);
        }

        QuantityOnHand = actualQuantityOnHand;
        return Result.Success();
    }

    /// <summary>Changes the replenishment trigger level.</summary>
    internal void SetReorderThreshold(int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        ReorderThreshold = threshold;
    }
}
