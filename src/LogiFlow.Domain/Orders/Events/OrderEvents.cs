using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Orders.Events;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  Everything an order can announce about itself.
//
//  Design note: the "big" lifecycle events (Submitted, Cancelled) carry a rich payload,
//  because the handlers that react to them - reserve stock, email the customer, release the
//  payment authorisation - would otherwise all have to re-query the order. The fine-grained
//  line events carry only IDs, because their consumers are internal projections that already
//  have the order loaded.
//
//  Choosing payload size per event, rather than applying one rule everywhere, is the point.
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Raised when a line is added to a draft order.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderLineId">The new line.</param>
/// <param name="ProductId">The product added.</param>
/// <param name="Quantity">Units added.</param>
public sealed record OrderLineAddedDomainEvent(
    OrderId OrderId,
    OrderLineId OrderLineId,
    ProductId ProductId,
    int Quantity) : DomainEventBase;

/// <summary>Raised when a line is removed from a draft order.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderLineId">The removed line.</param>
/// <param name="ProductId">The product that was on it.</param>
public sealed record OrderLineRemovedDomainEvent(
    OrderId OrderId,
    OrderLineId OrderLineId,
    ProductId ProductId) : DomainEventBase;

/// <summary>Raised when a line's quantity changes.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderLineId">The line.</param>
/// <param name="NewQuantity">The quantity after the change.</param>
public sealed record OrderLineQuantityChangedDomainEvent(
    OrderId OrderId,
    OrderLineId OrderLineId,
    int NewQuantity) : DomainEventBase;

/// <summary>
/// Raised when a customer submits an order. The most consequential event in the system.
/// </summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Human-facing reference, for emails and logs.</param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="Total">What they will pay.</param>
/// <param name="TotalWeight">Combined shipping weight, for carrier selection.</param>
/// <param name="ShippingAddress">Where it goes.</param>
/// <remarks>
/// Handlers of this event reserve warehouse stock, request payment authorisation, and send the
/// confirmation email. Everything it carries is here so none of them need a database round trip.
/// </remarks>
public sealed record OrderSubmittedDomainEvent(
    OrderId OrderId,
    OrderNumber OrderNumber,
    CustomerId CustomerId,
    Money Total,
    Weight TotalWeight,
    Address ShippingAddress) : DomainEventBase;

/// <summary>Raised when stock and payment are secured and the order is released to the warehouse.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
public sealed record OrderConfirmedDomainEvent(
    OrderId OrderId,
    OrderNumber OrderNumber,
    CustomerId CustomerId) : DomainEventBase;

/// <summary>Raised when the order is handed to a carrier.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
public sealed record OrderShippedDomainEvent(
    OrderId OrderId,
    OrderNumber OrderNumber,
    CustomerId CustomerId) : DomainEventBase;

/// <summary>Raised when the carrier confirms delivery.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="DeliveredAtUtc">When it landed.</param>
public sealed record OrderDeliveredDomainEvent(
    OrderId OrderId,
    OrderNumber OrderNumber,
    CustomerId CustomerId,
    DateTimeOffset DeliveredAtUtc) : DomainEventBase;

/// <summary>Raised when an order is cancelled before shipping.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="RefundableTotal">Amount to release or refund.</param>
/// <param name="Reason">Why it was cancelled.</param>
public sealed record OrderCancelledDomainEvent(
    OrderId OrderId,
    OrderNumber OrderNumber,
    CustomerId CustomerId,
    Money RefundableTotal,
    string Reason) : DomainEventBase;
