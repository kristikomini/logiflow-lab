using LogiFlow.Domain.Common;
using LogiFlow.Domain.Orders;

namespace LogiFlow.Domain.Shipping.Events;

/// <summary>Raised when a carrier takes possession of a consignment.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order being fulfilled.</param>
/// <param name="Carrier">Who is carrying it.</param>
/// <param name="TrackingNumber">The issued reference.</param>
/// <param name="EstimatedDelivery">Carrier's estimate, when given.</param>
/// <remarks>
/// A handler moves the corresponding <see cref="Order"/> to <see cref="OrderStatus.Shipped"/>
/// and emails the customer their tracking link. This is how two aggregates stay in step without
/// either one referencing the other.
/// </remarks>
public sealed record ShipmentDispatchedDomainEvent(
    ShipmentId ShipmentId,
    OrderId OrderId,
    Carrier Carrier,
    TrackingNumber TrackingNumber,
    DateOnly? EstimatedDelivery) : DomainEventBase;

/// <summary>Raised when a consignment is signed for.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order fulfilled.</param>
/// <param name="DeliveredAtUtc">When it landed.</param>
/// <param name="SignedBy">Who took it, when the carrier reports a name.</param>
public sealed record ShipmentDeliveredDomainEvent(
    ShipmentId ShipmentId,
    OrderId OrderId,
    DateTimeOffset DeliveredAtUtc,
    string? SignedBy) : DomainEventBase;

/// <summary>Raised when delivery fails and the consignment returns to sender.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order affected.</param>
/// <param name="Reason">Why it failed.</param>
public sealed record ShipmentFailedDomainEvent(
    ShipmentId ShipmentId,
    OrderId OrderId,
    string Reason) : DomainEventBase;
