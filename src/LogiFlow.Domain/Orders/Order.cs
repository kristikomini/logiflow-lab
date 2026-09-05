using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders.Events;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Orders;

/// <summary>
/// A customer order. The central aggregate of the system.
/// </summary>
/// <remarks>
/// <para>
/// This class is the best single example in the solution of what a rich domain model looks
/// like. Read it top to bottom and notice what is <i>absent</i>: no <c>DbContext</c>, no
/// <c>ILogger</c>, no HTTP, no <c>async</c>. It is pure business logic, instantiable in a unit
/// test with no mocks and no setup, which is exactly why <c>OrderTests</c> runs in
/// milliseconds.
/// </para>
/// <para><b>Invariants this aggregate guarantees at all times:</b></para>
/// <list type="number">
///   <item><description>Every line has quantity ≥ 1.</description></item>
///   <item><description>All lines share the order's currency.</description></item>
///   <item><description>Lines can only be modified while the order is in <see cref="OrderStatus.Draft"/>.</description></item>
///   <item><description>A submitted order has at least one line and a shipping address.</description></item>
///   <item><description>Status only ever moves along an edge in <see cref="OrderStateMachine"/>.</description></item>
///   <item><description>Adding an existing product merges into that line rather than duplicating it.</description></item>
/// </list>
/// Covered in: <c>course/module-05-clean-architecture/03-aggregates.md</c>
/// </remarks>
public sealed class Order : AggregateRoot<OrderId>
{
    /// <summary>Maximum distinct lines on one order. A sanity bound, not a business rule.</summary>
    public const int MaxLines = 500;

    // The backing field is a List, but nothing outside this class can reach it.
    // See the Lines property for why that distinction matters.
    private readonly List<OrderLine> _lines = [];

    private Order(
        OrderId id,
        OrderNumber orderNumber,
        CustomerId customerId,
        CustomerTier customerTier,
        Currency currency,
        Address? shippingAddress) : base(id)
    {
        OrderNumber = orderNumber;
        CustomerId = customerId;
        CustomerTier = customerTier;
        Currency = currency;
        ShippingAddress = shippingAddress;
        Status = OrderStatus.Draft;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    private Order()
    {
    }

    /// <summary>Human-facing reference, e.g. <c>ORD-2026-000417</c>.</summary>
    public OrderNumber OrderNumber { get; private set; }

    /// <summary>
    /// Who placed it. An ID, not a <see cref="Customer"/> reference.
    /// </summary>
    /// <remarks>
    /// Aggregates reference each other by identity. A <c>Customer</c> navigation property here
    /// would let a caller write <c>order.Customer.Deactivate()</c> and mutate two aggregates in
    /// one transaction — exactly what aggregate boundaries exist to prevent.
    /// </remarks>
    public CustomerId CustomerId { get; private set; }

    /// <summary>
    /// The customer's tier at the moment of ordering.
    /// </summary>
    /// <remarks>
    /// Copied, not looked up. If the customer is upgraded to Gold tomorrow, yesterday's order
    /// keeps the discount it was actually placed under. Same reasoning as
    /// <see cref="OrderLine.UnitPrice"/>.
    /// </remarks>
    public CustomerTier CustomerTier { get; private set; }

    /// <summary>The currency every monetary value on this order is denominated in.</summary>
    public Currency Currency { get; private set; } = null!;

    /// <summary>Where the goods go. Required before submission.</summary>
    public Address? ShippingAddress { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>When the draft was opened.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>When the customer submitted it, if they have.</summary>
    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    /// <summary>When it was handed to a carrier, if it has been.</summary>
    public DateTimeOffset? ShippedAtUtc { get; private set; }

    /// <summary>When the carrier confirmed delivery, if they have.</summary>
    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    /// <summary>Why it was cancelled, if it was.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Which warehouse reserved stock for this order, once one has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded at reservation time so a later cancellation knows where to give the stock back.
    /// Without it, the release handler would have to guess — and "release from every warehouse"
    /// would credit stock to sites that never held any, silently inflating inventory across the
    /// whole estate.
    /// </para>
    /// <para>
    /// An ID rather than a <c>Warehouse</c> reference, per the aggregate rule at the top of
    /// <see cref="AggregateRoot{TId}"/>.
    /// </para>
    /// </remarks>
    public WarehouseId? FulfillingWarehouseId { get; private set; }

    /// <summary>
    /// The lines on this order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see cref="IReadOnlyList{T}"/> over the private list, which is the single most
    /// important line in this class. Exposing <c>List&lt;OrderLine&gt;</c> directly would let
    /// any caller do <c>order.Lines.Add(...)</c> or <c>.Clear()</c>, bypassing every rule in
    /// <see cref="AddLine"/> and every domain event.
    /// </para>
    /// <para>
    /// <b>A caveat worth knowing for interviews:</b> <c>IReadOnlyList&lt;T&gt;</c> is not a
    /// guarantee of immutability, it is a guarantee about the <i>interface</i>. A determined
    /// caller can still cast back to <c>List&lt;T&gt;</c>. Use <c>.AsReadOnly()</c> when you
    /// need a genuine barrier; here the intent-signalling is enough and it avoids an
    /// allocation on a hot path.
    /// </para>
    /// </remarks>
    public IReadOnlyList<OrderLine> Lines => _lines;

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  Computed money. None of these are stored columns.
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sum of all line totals, before discount and shipping.</summary>
    /// <remarks>
    /// <c>Aggregate</c> with an explicit <see cref="Money.Zero(Currency)"/> seed, rather than
    /// <c>Sum()</c>, because LINQ's <c>Sum</c> only knows about the built-in numeric types.
    /// The seed also gives the right answer — zero in the correct currency — for an empty order,
    /// where the seedless overload would throw.
    /// </remarks>
    public Money Subtotal => _lines.Aggregate(Money.Zero(Currency), (total, line) => total + line.LineTotal);

    /// <summary>Discount earned by the customer's tier.</summary>
    public Money DiscountAmount => Subtotal * CustomerTier.DiscountRate();

    /// <summary>Combined shipping weight of every line.</summary>
    public Weight TotalWeight => _lines.Aggregate(Weight.Zero, (total, line) => total + line.LineWeight);

    /// <summary>
    /// Shipping cost, after the tier's free-shipping threshold.
    /// </summary>
    /// <remarks>
    /// Deliberately simple — €4.90 base plus €1.50 per kilo over 5kg. Real carrier rating is a
    /// zone/dimensional-weight/surcharge matrix that belongs behind an
    /// <c>IShippingRateProvider</c> in the Application layer. This is a domain <i>default</i>,
    /// not a claim that shipping is really this simple.
    /// </remarks>
    public Money ShippingCost
    {
        get
        {
            Money discounted = Subtotal - DiscountAmount;
            decimal threshold = CustomerTier.FreeShippingThresholdEur();

            if (threshold == 0m || discounted.Amount >= threshold)
            {
                return Money.Zero(Currency);
            }

            const decimal baseRate = 4.90m;
            const int freeKilos = 5;
            decimal excessKilos = Math.Max(0m, TotalWeight.Kilograms - freeKilos);
            return new Money(baseRate + (excessKilos * 1.50m), Currency);
        }
    }

    /// <summary>What the customer actually pays.</summary>
    public Money Total => Subtotal - DiscountAmount + ShippingCost;

    /// <summary>True while lines may still be changed.</summary>
    public bool IsEditable => OrderStateMachine.IsEditable(Status);

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  Behaviour
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens a new draft order.</summary>
    public static Result<Order> CreateDraft(
        OrderNumber orderNumber,
        CustomerId customerId,
        CustomerTier customerTier,
        Currency currency,
        Address? shippingAddress = null)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (customerId.Value == Guid.Empty)
        {
            return OrderErrors.CustomerRequired;
        }

        return new Order(OrderId.New(), orderNumber, customerId, customerTier, currency, shippingAddress);
    }

    /// <summary>
    /// Adds a product to the order, or increases the quantity if it is already present.
    /// </summary>
    /// <remarks>
    /// The merge behaviour is a real business decision, not an implementation detail. Two
    /// separate lines for the same SKU confuse pickers in the warehouse and look like a bug on
    /// an invoice. It is documented here and asserted in <c>OrderTests.AddLine_Merges...</c>.
    /// </remarks>
    public Result AddLine(Product product, int quantity)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!IsEditable)
        {
            return OrderErrors.NotEditable(Status);
        }

        if (quantity < 1)
        {
            return OrderErrors.InvalidQuantity;
        }

        if (!product.IsActive)
        {
            return ProductErrors.Inactive(product.Sku);
        }

        if (product.UnitPrice.Currency != Currency)
        {
            return OrderErrors.CurrencyMismatch(Currency, product.UnitPrice.Currency);
        }

        OrderLine? existing = _lines.Find(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
            Raise(new OrderLineQuantityChangedDomainEvent(Id, existing.Id, existing.Quantity));
            return Result.Success();
        }

        if (_lines.Count >= MaxLines)
        {
            return OrderErrors.TooManyLines;
        }

        var line = new OrderLine(
            OrderLineId.New(),
            Id,
            product.Id,
            product.Sku,
            product.Name,
            quantity,
            product.UnitPrice,
            product.Weight);

        _lines.Add(line);
        Raise(new OrderLineAddedDomainEvent(Id, line.Id, product.Id, quantity));
        return Result.Success();
    }

    /// <summary>Removes a line entirely.</summary>
    public Result RemoveLine(OrderLineId lineId)
    {
        if (!IsEditable)
        {
            return OrderErrors.NotEditable(Status);
        }

        OrderLine? line = _lines.Find(l => l.Id == lineId);
        if (line is null)
        {
            return OrderErrors.LineNotFound(lineId);
        }

        _lines.Remove(line);
        Raise(new OrderLineRemovedDomainEvent(Id, lineId, line.ProductId));
        return Result.Success();
    }

    /// <summary>Sets an exact quantity for a line. A quantity of zero removes it.</summary>
    public Result ChangeLineQuantity(OrderLineId lineId, int quantity)
    {
        if (!IsEditable)
        {
            return OrderErrors.NotEditable(Status);
        }

        if (quantity < 0)
        {
            return OrderErrors.InvalidQuantity;
        }

        if (quantity == 0)
        {
            return RemoveLine(lineId);
        }

        OrderLine? line = _lines.Find(l => l.Id == lineId);
        if (line is null)
        {
            return OrderErrors.LineNotFound(lineId);
        }

        line.ChangeQuantity(quantity);
        Raise(new OrderLineQuantityChangedDomainEvent(Id, lineId, quantity));
        return Result.Success();
    }

    /// <summary>Sets or replaces the delivery address.</summary>
    public Result SetShippingAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Deliberately permitted right up to shipping: address corrections are the single most
        // common support request in fulfilment, and forcing a cancel-and-reorder for a typo'd
        // house number is the kind of rule that generates angry customers.
        if (Status is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return OrderErrors.NotEditable(Status);
        }

        ShippingAddress = address;
        return Result.Success();
    }

    /// <summary>Customer places the order.</summary>
    public Result Submit()
    {
        Result transition = EnsureCanTransitionTo(OrderStatus.Submitted);
        if (transition.IsFailure)
        {
            return transition;
        }

        if (_lines.Count == 0)
        {
            return OrderErrors.EmptyOrder;
        }

        if (ShippingAddress is null)
        {
            return OrderErrors.ShippingAddressRequired;
        }

        Status = OrderStatus.Submitted;
        SubmittedAtUtc = DateTimeOffset.UtcNow;

        Raise(new OrderSubmittedDomainEvent(
            Id,
            OrderNumber,
            CustomerId,
            Total,
            TotalWeight,
            ShippingAddress));

        return Result.Success();
    }

    /// <summary>
    /// Records which warehouse has reserved stock for this order.
    /// </summary>
    /// <remarks>
    /// Called by the stock-reservation event handler immediately after a successful reservation,
    /// inside the same transaction. Re-assigning to a different site is rejected: it would strand
    /// the reservation at the original warehouse with nothing left pointing at it.
    /// </remarks>
    public Result AssignFulfillingWarehouse(WarehouseId warehouseId)
    {
        if (FulfillingWarehouseId is { } existing && existing != warehouseId)
        {
            return OrderErrors.AlreadyAssignedToWarehouse(existing);
        }

        FulfillingWarehouseId = warehouseId;
        return Result.Success();
    }

    /// <summary>Stock is reserved and payment authorised; release to the warehouse.</summary>
    public Result Confirm()
    {
        Result transition = EnsureCanTransitionTo(OrderStatus.Confirmed);
        if (transition.IsFailure)
        {
            return transition;
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(Id, OrderNumber, CustomerId));
        return Result.Success();
    }

    /// <summary>Marks the order as handed to a carrier.</summary>
    public Result MarkShipped()
    {
        Result transition = EnsureCanTransitionTo(OrderStatus.Shipped);
        if (transition.IsFailure)
        {
            return transition;
        }

        Status = OrderStatus.Shipped;
        ShippedAtUtc = DateTimeOffset.UtcNow;
        Raise(new OrderShippedDomainEvent(Id, OrderNumber, CustomerId));
        return Result.Success();
    }

    /// <summary>Records confirmed delivery.</summary>
    public Result MarkDelivered()
    {
        Result transition = EnsureCanTransitionTo(OrderStatus.Delivered);
        if (transition.IsFailure)
        {
            return transition;
        }

        Status = OrderStatus.Delivered;
        DeliveredAtUtc = DateTimeOffset.UtcNow;
        Raise(new OrderDeliveredDomainEvent(Id, OrderNumber, CustomerId, DeliveredAtUtc.Value));
        return Result.Success();
    }

    /// <summary>Cancels the order before it ships.</summary>
    public Result Cancel(string? reason)
    {
        Result transition = EnsureCanTransitionTo(OrderStatus.Cancelled);
        if (transition.IsFailure)
        {
            return transition;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OrderErrors.CancellationReasonRequired;
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason.Trim();

        // Carries the total so a downstream handler can release the payment authorisation
        // and reverse the stock reservation without loading the order again.
        Raise(new OrderCancelledDomainEvent(Id, OrderNumber, CustomerId, Total, CancellationReason));
        return Result.Success();
    }

    /// <summary>
    /// Single choke point for every status change.
    /// </summary>
    /// <remarks>
    /// Every transition goes through here, so the state machine cannot be bypassed by a new
    /// method that forgets to check. If you add a lifecycle step later, this is the only place
    /// that has to know.
    /// </remarks>
    private Result EnsureCanTransitionTo(OrderStatus target) =>
        OrderStateMachine.CanTransition(Status, target)
            ? Result.Success()
            : OrderErrors.InvalidTransition(Status, target);
}
