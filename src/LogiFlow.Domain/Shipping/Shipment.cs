using LogiFlow.Domain.Common;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.Shipping.Events;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Shipping;

/// <summary>
/// A physical consignment leaving a warehouse for a customer. Aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is this separate from <see cref="Order"/>?</b> Because one order can ship as several
/// parcels, from several warehouses, on different days. Modelling shipment as a field on
/// <c>Order</c> works right up to the first split delivery and then requires a painful
/// migration. Separating them also keeps the <c>Order</c> aggregate small: an order being
/// updated does not lock shipment rows and vice versa.
/// </para>
/// <para>
/// The two aggregates stay in step through domain events, not through a foreign-key-driven
/// object graph — <see cref="ShipmentDeliveredDomainEvent"/> is what eventually moves the
/// order to <see cref="OrderStatus.Delivered"/>.
/// </para>
/// </remarks>
public sealed class Shipment : AggregateRoot<ShipmentId>
{
    private readonly List<ShipmentEvent> _timeline = [];

    private Shipment(
        ShipmentId id,
        OrderId orderId,
        WarehouseId warehouseId,
        Address destination,
        Carrier carrier,
        Weight totalWeight) : base(id)
    {
        OrderId = orderId;
        WarehouseId = warehouseId;
        Destination = destination;
        Carrier = carrier;
        TotalWeight = totalWeight;
        Status = ShipmentStatus.Preparing;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    private Shipment()
    {
    }

    /// <summary>The order being fulfilled.</summary>
    public OrderId OrderId { get; private set; }

    /// <summary>Where it ships from.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Where it ships to.</summary>
    public Address Destination { get; private set; } = null!;

    /// <summary>Who is carrying it.</summary>
    public Carrier Carrier { get; private set; }

    /// <summary>
    /// The raw carrier reference as stored, or <c>null</c> before dispatch.
    /// </summary>
    /// <remarks>
    /// <b>Why is the string mapped rather than the <see cref="Shipping.TrackingNumber"/> value
    /// object?</b> Because reconstructing the value object needs two pieces of data — the
    /// reference and the <see cref="Carrier"/> — and an EF Core value converter only ever sees
    /// one column. Rather than fight the ORM, the primitive is stored and
    /// <see cref="TrackingNumber"/> rebuilds the value object from both fields.
    /// <para>
    /// This is a real and recurring tension: persistence constraints do shape domain models, and
    /// pretending otherwise produces either a mangled schema or a pile of mapping hacks. Letting
    /// one property be a stored primitive with a computed value-object accessor is the smallest
    /// honest compromise available here.
    /// </para>
    /// </remarks>
    public string? TrackingReference { get; private set; }

    /// <summary>
    /// The carrier reference as a validated value object, or <c>null</c> before dispatch.
    /// </summary>
    /// <remarks>Computed from <see cref="TrackingReference"/> and <see cref="Carrier"/>; not mapped.</remarks>
    public TrackingNumber? TrackingNumber => TrackingReference is null
        ? null
        : Shipping.TrackingNumber.FromTrusted(TrackingReference, Carrier);

    /// <summary>Combined weight of the consignment.</summary>
    public Weight TotalWeight { get; private set; }

    /// <summary>Where it is in the delivery process.</summary>
    public ShipmentStatus Status { get; private set; }

    /// <summary>When the consignment record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>When the carrier took possession.</summary>
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    /// <summary>When it was signed for.</summary>
    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    /// <summary>Carrier's estimated delivery date, if provided.</summary>
    public DateOnly? EstimatedDeliveryDate { get; private set; }

    /// <summary>
    /// Every scan and status change, oldest first.
    /// </summary>
    /// <remarks>
    /// An append-only log. This is the customer-facing "where is my parcel" view, and it is also
    /// the evidence trail when a customer disputes a delivery. Nothing is ever edited or removed
    /// from it — corrections are appended as new entries.
    /// </remarks>
    public IReadOnlyList<ShipmentEvent> Timeline => _timeline;

    /// <summary>Creates a consignment ready to be packed.</summary>
    public static Result<Shipment> Create(
        OrderId orderId,
        WarehouseId warehouseId,
        Address destination,
        Carrier carrier,
        Weight totalWeight)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (totalWeight.Grams <= 0)
        {
            return ShippingErrors.WeightRequired;
        }

        var shipment = new Shipment(ShipmentId.New(), orderId, warehouseId, destination, carrier, totalWeight);
        shipment.Append(ShipmentStatus.Preparing, "Shipment created", null);
        return shipment;
    }

    /// <summary>Hands the consignment to the carrier.</summary>
    public Result Dispatch(TrackingNumber trackingNumber, DateOnly? estimatedDelivery)
    {
        if (Status != ShipmentStatus.Preparing)
        {
            return ShippingErrors.InvalidTransition(Status, ShipmentStatus.Dispatched);
        }

        // A DHL tracking number on a UPS shipment means someone mixed up two consignments in
        // the packing area - a mistake that ends with a parcel that cannot be traced at all.
        if (trackingNumber.Carrier != Carrier)
        {
            return ShippingErrors.CarrierMismatch(Carrier, trackingNumber.Carrier);
        }

        TrackingReference = trackingNumber.Value;
        EstimatedDeliveryDate = estimatedDelivery;
        Status = ShipmentStatus.Dispatched;
        DispatchedAtUtc = DateTimeOffset.UtcNow;

        Append(ShipmentStatus.Dispatched, $"Collected by {Carrier}", null);
        Raise(new ShipmentDispatchedDomainEvent(Id, OrderId, Carrier, trackingNumber, estimatedDelivery));
        return Result.Success();
    }

    /// <summary>Records a carrier scan somewhere en route.</summary>
    public Result RecordTransitScan(string location, string? description)
    {
        if (Status is ShipmentStatus.Delivered or ShipmentStatus.Failed)
        {
            return ShippingErrors.AlreadyFinalised(Status);
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            return ShippingErrors.LocationRequired;
        }

        Status = ShipmentStatus.InTransit;
        Append(ShipmentStatus.InTransit, description ?? "In transit", location.Trim());
        return Result.Success();
    }

    /// <summary>Records successful delivery.</summary>
    public Result MarkDelivered(string? signedBy)
    {
        if (Status is ShipmentStatus.Delivered)
        {
            // Carriers genuinely do send duplicate delivery webhooks. Treating a repeat as
            // success rather than an error is what makes the endpoint safely idempotent -
            // a retry must never turn into a 500 and a paged engineer at 3am.
            return Result.Success();
        }

        if (Status is ShipmentStatus.Preparing or ShipmentStatus.Failed)
        {
            return ShippingErrors.InvalidTransition(Status, ShipmentStatus.Delivered);
        }

        Status = ShipmentStatus.Delivered;
        DeliveredAtUtc = DateTimeOffset.UtcNow;

        Append(
            ShipmentStatus.Delivered,
            string.IsNullOrWhiteSpace(signedBy) ? "Delivered" : $"Delivered, signed by {signedBy.Trim()}",
            Destination.City);

        Raise(new ShipmentDeliveredDomainEvent(Id, OrderId, DeliveredAtUtc.Value, signedBy?.Trim()));
        return Result.Success();
    }

    /// <summary>Records a failed delivery; the consignment returns to sender.</summary>
    public Result MarkFailed(string reason)
    {
        if (Status is ShipmentStatus.Delivered)
        {
            return ShippingErrors.AlreadyFinalised(Status);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ShippingErrors.FailureReasonRequired;
        }

        Status = ShipmentStatus.Failed;
        Append(ShipmentStatus.Failed, reason.Trim(), null);
        Raise(new ShipmentFailedDomainEvent(Id, OrderId, reason.Trim()));
        return Result.Success();
    }

    /// <summary>Days in transit, or days elapsed so far if still moving.</summary>
    public int? TransitDays => DispatchedAtUtc is null
        ? null
        : (int)((DeliveredAtUtc ?? DateTimeOffset.UtcNow) - DispatchedAtUtc.Value).TotalDays;

    /// <summary>True when the carrier's estimate has passed and it still has not arrived.</summary>
    public bool IsOverdue =>
        EstimatedDeliveryDate is { } eta &&
        Status is not (ShipmentStatus.Delivered or ShipmentStatus.Failed) &&
        DateOnly.FromDateTime(DateTime.UtcNow) > eta;

    private void Append(ShipmentStatus status, string description, string? location) =>
        _timeline.Add(new ShipmentEvent(Guid.CreateVersion7(), Id, status, description, location, DateTimeOffset.UtcNow));
}

/// <summary>
/// One entry in a shipment's tracking history. Immutable once written.
/// </summary>
/// <param name="Id">Row identity.</param>
/// <param name="ShipmentId">The consignment this belongs to.</param>
/// <param name="Status">Status recorded at this point.</param>
/// <param name="Description">Human-readable note.</param>
/// <param name="Location">Where the scan happened, when known.</param>
/// <param name="OccurredAtUtc">When it happened.</param>
/// <remarks>
/// A record rather than a class: nothing about a historical scan should ever change, and
/// <c>record</c> makes that the default rather than a convention people have to remember.
/// </remarks>
public sealed record ShipmentEvent(
    Guid Id,
    ShipmentId ShipmentId,
    ShipmentStatus Status,
    string Description,
    string? Location,
    DateTimeOffset OccurredAtUtc);
