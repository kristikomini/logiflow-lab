using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Orders;

/// <summary>
/// One product line on an order. An entity, but <b>not</b> an aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// Every member that mutates state is <c>internal</c>, not <c>public</c>. That is the
/// enforcement mechanism for "you may only change a line through its <see cref="Order"/>".
/// If <c>ChangeQuantity</c> were public, application code could bypass <see cref="Order"/>
/// and the order's total would silently disagree with the sum of its lines — the invariant
/// the aggregate exists to protect.
/// </para>
/// <para>
/// <b>Why is it an entity and not a value object?</b> Because two lines for 3× the same
/// widget at the same price are still two <i>distinct</i> lines: you can remove one and keep
/// the other. Identity matters, so it is an entity.
/// </para>
/// <para>
/// <b>Why store <see cref="UnitPrice"/> instead of reading it from the product?</b> Price is
/// captured at the moment of ordering. If the catalogue price changes next week, historical
/// orders must not retroactively change value. Forgetting this is one of the most common —
/// and most expensive — bugs in e-commerce systems, because it silently corrupts every
/// financial report over the affected period.
/// </para>
/// </remarks>
public sealed class OrderLine : Entity<OrderLineId>
{
    internal OrderLine(
        OrderLineId id,
        OrderId orderId,
        ProductId productId,
        Sku sku,
        string productName,
        int quantity,
        Money unitPrice,
        Weight unitWeight) : base(id)
    {
        OrderId = orderId;
        ProductId = productId;
        Sku = sku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
        UnitWeight = unitWeight;
    }

    private OrderLine()
    {
    }

    /// <summary>The owning order. Part of the foreign key EF Core writes.</summary>
    public OrderId OrderId { get; private set; }

    /// <summary>Which product was ordered.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>SKU captured at order time.</summary>
    public Sku Sku { get; private set; }

    /// <summary>
    /// Product name captured at order time.
    /// </summary>
    /// <remarks>
    /// Denormalised on purpose. An invoice reprinted in three years must show the name the
    /// customer actually bought, not whatever marketing renamed the product to since.
    /// </remarks>
    public string ProductName { get; private set; } = null!;

    /// <summary>How many units. Always at least one.</summary>
    public int Quantity { get; private set; }

    /// <summary>Price per unit, frozen at order time.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Shipping weight per unit, frozen at order time.</summary>
    public Weight UnitWeight { get; private set; }

    /// <summary>Line total: <see cref="UnitPrice"/> × <see cref="Quantity"/>.</summary>
    /// <remarks>
    /// A computed property, never a stored column. Storing it would create a second source of
    /// truth that can drift from the two values it derives from. EF Core is told to ignore it
    /// in <c>OrderConfiguration</c>.
    /// </remarks>
    public Money LineTotal => UnitPrice * Quantity;

    /// <summary>Total shipping weight for this line.</summary>
    public Weight LineWeight => UnitWeight * Quantity;

    /// <summary>Changes the quantity. Callable only from <see cref="Order"/>.</summary>
    internal void ChangeQuantity(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        Quantity = quantity;
    }

    /// <summary>Adds to the quantity. Callable only from <see cref="Order"/>.</summary>
    internal void IncreaseQuantity(int by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, 1);
        Quantity += by;
    }
}
