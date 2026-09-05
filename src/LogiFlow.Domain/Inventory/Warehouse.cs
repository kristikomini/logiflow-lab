using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Inventory.Events;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Inventory;

/// <summary>
/// A physical stock location and everything held in it. Aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// <b>An aggregate-boundary judgement call worth understanding.</b> <see cref="StockItem"/>
/// lives inside this aggregate, which means reserving one product's stock loads the whole
/// warehouse. For a warehouse with 50,000 SKUs that would be catastrophic.
/// </para>
/// <para>
/// The reason it works here is that the repository never loads the full collection: it filters
/// to the specific stock rows a command needs (see <c>WarehouseRepository.GetWithStockForAsync</c>),
/// so a reservation touches a handful of rows regardless of catalogue size.
/// </para>
/// <para>
/// The purist alternative is to make <c>StockItem</c> its own aggregate root keyed on
/// (warehouse, product). That scales better and is the right call above a certain size — but it
/// gives up the ability to enforce cross-item rules transactionally. Knowing that this is a
/// trade-off, rather than a rule, is what separates a mid-level from a senior answer in
/// an interview.
/// </para>
/// </remarks>
public sealed class Warehouse : AggregateRoot<WarehouseId>
{
    private readonly List<StockItem> _stock = [];

    private Warehouse(WarehouseId id, string code, string name, Address address) : base(id)
    {
        Code = code;
        Name = name;
        Address = address;
    }

    private Warehouse()
    {
    }

    /// <summary>Short operational code, e.g. <c>MIL-01</c>. Unique.</summary>
    public string Code { get; private set; } = null!;

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Physical location — drives shipping distance and customs.</summary>
    public Address Address { get; private set; } = null!;

    /// <summary>Whether the site is currently operating.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Stock rows loaded for this warehouse. See the class remarks on partial loading.</summary>
    public IReadOnlyList<StockItem> Stock => _stock;

    /// <summary>Opens a warehouse.</summary>
    public static Result<Warehouse> Create(string? code, string? name, Address address)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return InventoryErrors.WarehouseCodeRequired;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return InventoryErrors.WarehouseNameRequired;
        }

        ArgumentNullException.ThrowIfNull(address);

        return new Warehouse(WarehouseId.New(), code.Trim().ToUpperInvariant(), name.Trim(), address);
    }

    /// <summary>Starts tracking a product at this site.</summary>
    public Result<StockItem> AddStockItem(ProductId productId, int initialQuantity, int reorderThreshold)
    {
        if (initialQuantity < 0)
        {
            return InventoryErrors.NegativeStock;
        }

        if (_stock.Exists(s => s.ProductId == productId))
        {
            return InventoryErrors.DuplicateStockItem(productId);
        }

        var item = new StockItem(StockItemId.New(), Id, productId, initialQuantity, reorderThreshold);
        _stock.Add(item);
        return item;
    }

    /// <summary>Promises stock to an order.</summary>
    /// <remarks>
    /// <para>
    /// <b>This method is where oversells are prevented — and where they are not.</b> The
    /// in-memory check below is necessary but on its own insufficient: two concurrent requests
    /// can both load a stock row showing 10 available, both pass this check for 8 units, and
    /// both save. That is a lost update, and the result is 16 units promised out of 10.
    /// </para>
    /// <para>
    /// The aggregate cannot solve that alone; correctness needs the database. Two layers do it:
    /// <see cref="AggregateRoot{TId}.RowVersion"/> makes the second save throw
    /// <c>DbUpdateConcurrencyException</c>, and <c>ReserveStockCommandHandler</c> retries on
    /// that exception with freshly-read data. The integration test
    /// <c>InventoryConcurrencyTests.Concurrent_reservations_never_oversell</c> fires 20
    /// parallel reservations at 10 units of stock and asserts exactly 10 succeed.
    /// </para>
    /// </remarks>
    public Result Reserve(ProductId productId, int quantity)
    {
        if (!IsActive)
        {
            return InventoryErrors.WarehouseInactive(Code);
        }

        StockItem? item = _stock.Find(s => s.ProductId == productId);
        if (item is null)
        {
            return InventoryErrors.StockItemNotFound(productId, Code);
        }

        Result result = item.Reserve(quantity);
        if (result.IsFailure)
        {
            return result;
        }

        Raise(new StockReservedDomainEvent(Id, productId, quantity, item.QuantityAvailable));

        if (item.NeedsReplenishment)
        {
            Raise(new StockRunningLowDomainEvent(Id, Code, productId, item.QuantityAvailable, item.ReorderThreshold));
        }

        return Result.Success();
    }

    /// <summary>Returns promised stock to the available pool.</summary>
    public Result Release(ProductId productId, int quantity)
    {
        StockItem? item = _stock.Find(s => s.ProductId == productId);
        if (item is null)
        {
            return InventoryErrors.StockItemNotFound(productId, Code);
        }

        Result result = item.Release(quantity);
        if (result.IsFailure)
        {
            return result;
        }

        Raise(new StockReleasedDomainEvent(Id, productId, quantity, item.QuantityAvailable));
        return Result.Success();
    }

    /// <summary>Records a picker physically removing reserved stock.</summary>
    public Result Pick(ProductId productId, int quantity)
    {
        StockItem? item = _stock.Find(s => s.ProductId == productId);
        if (item is null)
        {
            return InventoryErrors.StockItemNotFound(productId, Code);
        }

        Result result = item.Pick(quantity);
        if (result.IsFailure)
        {
            return result;
        }

        Raise(new StockPickedDomainEvent(Id, productId, quantity, item.QuantityOnHand));
        return Result.Success();
    }

    /// <summary>Books in a delivery from a supplier.</summary>
    public Result Receive(ProductId productId, int quantity)
    {
        StockItem? item = _stock.Find(s => s.ProductId == productId);
        if (item is null)
        {
            return InventoryErrors.StockItemNotFound(productId, Code);
        }

        Result result = item.Receive(quantity);
        if (result.IsFailure)
        {
            return result;
        }

        Raise(new StockReceivedDomainEvent(Id, productId, quantity, item.QuantityOnHand));
        return Result.Success();
    }

    /// <summary>Applies the result of a physical stock take.</summary>
    public Result AdjustStock(ProductId productId, int actualQuantity, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return InventoryErrors.AdjustmentReasonRequired;
        }

        StockItem? item = _stock.Find(s => s.ProductId == productId);
        if (item is null)
        {
            return InventoryErrors.StockItemNotFound(productId, Code);
        }

        int before = item.QuantityOnHand;
        Result result = item.AdjustTo(actualQuantity);
        if (result.IsFailure)
        {
            return result;
        }

        Raise(new StockAdjustedDomainEvent(Id, productId, before, actualQuantity, reason.Trim()));
        return Result.Success();
    }

    /// <summary>How many units of a product can currently be sold from this site.</summary>
    public int AvailableQuantity(ProductId productId) =>
        _stock.Find(s => s.ProductId == productId)?.QuantityAvailable ?? 0;

    /// <summary>Takes the site out of service. Existing reservations survive.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        return Result.Success();
    }
}
