using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Mailing;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.Shipping;
using LogiFlow.Domain.Shipping.Events;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Features.Shipments;

/// <summary>A shipment as returned to clients.</summary>
/// <param name="Id">Shipment identity.</param>
/// <param name="OrderId">The order being fulfilled.</param>
/// <param name="Carrier">Delivery company.</param>
/// <param name="TrackingNumber">Carrier reference, once dispatched.</param>
/// <param name="TrackingUrl">Public tracking link, once dispatched.</param>
/// <param name="Status">Where it is in the process.</param>
/// <param name="EstimatedDeliveryDate">Carrier estimate.</param>
/// <param name="IsOverdue">Whether the estimate has passed.</param>
/// <param name="Timeline">Every scan, oldest first.</param>
public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    Carrier Carrier,
    string? TrackingNumber,
    string? TrackingUrl,
    ShipmentStatus Status,
    DateOnly? EstimatedDeliveryDate,
    bool IsOverdue,
    IReadOnlyList<ShipmentTimelineDto> Timeline);

/// <summary>One tracking entry.</summary>
/// <param name="Status">Status at this point.</param>
/// <param name="Description">What happened.</param>
/// <param name="Location">Where, when known.</param>
/// <param name="OccurredAtUtc">When.</param>
public sealed record ShipmentTimelineDto(
    ShipmentStatus Status,
    string Description,
    string? Location,
    DateTimeOffset OccurredAtUtc);

// ── Dispatch ─────────────────────────────────────────────────────────────────────────────

/// <summary>Hands a prepared shipment to its carrier.</summary>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="TrackingNumber">Reference issued by the carrier.</param>
/// <param name="EstimatedDeliveryDate">Carrier's estimate, if given.</param>
public sealed record DispatchShipmentCommand(
    Guid ShipmentId,
    string TrackingNumber,
    DateOnly? EstimatedDeliveryDate) : ICommand;

/// <summary>Input validation for <see cref="DispatchShipmentCommand"/>.</summary>
public sealed class DispatchShipmentCommandValidator : AbstractValidator<DispatchShipmentCommand>
{
    /// <summary>Configures the rules.</summary>
    public DispatchShipmentCommandValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(50);

        // A delivery estimate in the past means someone typed the year wrong. Rejecting it here
        // avoids a shipment that is "overdue" the moment it is created.
        RuleFor(x => x.EstimatedDeliveryDate)
            .GreaterThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.EstimatedDeliveryDate.HasValue)
            .WithMessage("Estimated delivery date cannot be in the past.");
    }
}

/// <summary>Handles <see cref="DispatchShipmentCommand"/>.</summary>
/// <param name="shipments">Shipment persistence.</param>
internal sealed class DispatchShipmentCommandHandler(IShipmentRepository shipments)
    : ICommandHandler<DispatchShipmentCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(DispatchShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipmentId = ShipmentId.From(request.ShipmentId);

        Shipment? shipment = await shipments.GetAsync(shipmentId, cancellationToken).ConfigureAwait(false);
        if (shipment is null)
        {
            return ShippingErrors.NotFound(shipmentId);
        }

        // The tracking number is validated against the shipment's OWN carrier. Parsing it here
        // rather than in the validator is why: the validator has no way to know which carrier
        // this shipment is booked with without loading it, and validators must not hit the database.
        Result<TrackingNumber> tracking = TrackingNumber.Create(request.TrackingNumber, shipment.Carrier);
        if (tracking.IsFailure)
        {
            return tracking.Error;
        }

        return shipment.Dispatch(tracking.Value, request.EstimatedDeliveryDate);
    }
}

// ── Carrier webhook ──────────────────────────────────────────────────────────────────────

/// <summary>Records a carrier delivery confirmation.</summary>
/// <param name="TrackingNumber">The carrier's reference.</param>
/// <param name="SignedBy">Who accepted it, when reported.</param>
/// <remarks>
/// Keyed on tracking number rather than shipment id, because that is the only identifier a
/// carrier webhook knows about — they have never heard of our <see cref="ShipmentId"/>.
/// Designing the command around what the caller actually has is what keeps the adapter thin.
/// </remarks>
public sealed record ConfirmDeliveryCommand(string TrackingNumber, string? SignedBy) : ICommand;

/// <summary>Input validation for <see cref="ConfirmDeliveryCommand"/>.</summary>
public sealed class ConfirmDeliveryCommandValidator : AbstractValidator<ConfirmDeliveryCommand>
{
    /// <summary>Configures the rules.</summary>
    public ConfirmDeliveryCommandValidator()
    {
        RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SignedBy).MaximumLength(200);
    }
}

/// <summary>Handles <see cref="ConfirmDeliveryCommand"/>.</summary>
/// <remarks>
/// <b>Idempotent by design.</b> Carriers retry webhooks, sometimes for days, and a duplicate
/// must be a 200 rather than a 500 — otherwise the carrier keeps retrying forever and someone
/// gets paged. <see cref="Shipment.MarkDelivered"/> returns success for an already-delivered
/// consignment for exactly this reason.
/// </remarks>
/// <param name="shipments">Shipment persistence.</param>
internal sealed class ConfirmDeliveryCommandHandler(IShipmentRepository shipments)
    : ICommandHandler<ConfirmDeliveryCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(ConfirmDeliveryCommand request, CancellationToken cancellationToken)
    {
        Shipment? shipment = await shipments
            .GetByTrackingNumberAsync(request.TrackingNumber, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null)
        {
            return Error.NotFound(
                "Shipping.TrackingNotFound",
                $"No shipment found with tracking number '{request.TrackingNumber}'.");
        }

        return shipment.MarkDelivered(request.SignedBy);
    }
}

// ── Keeping the order in step ────────────────────────────────────────────────────────────

/// <summary>Moves the order to Shipped when its consignment is dispatched.</summary>
/// <remarks>
/// The bridge between the <see cref="Shipment"/> and <see cref="Order"/> aggregates. Neither
/// references the other; the event carries the <see cref="OrderId"/> and this handler does the
/// rest. Adding a third aggregate that also cares about dispatch means adding a handler, not
/// editing <see cref="Shipment"/>.
/// </remarks>
/// <param name="orders">Order persistence.</param>
/// <param name="logger">Logger.</param>
internal sealed class MarkOrderShippedOnDispatch(
    IOrderRepository orders,
    ILogger<MarkOrderShippedOnDispatch> logger) : IEventHandler<ShipmentDispatchedDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(ShipmentDispatchedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Order? order = await orders.GetAsync(domainEvent.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            logger.LogWarning(
                "Shipment {ShipmentId} dispatched for order {OrderId}, which no longer exists",
                domainEvent.ShipmentId,
                domainEvent.OrderId);

            return;
        }

        Result result = order.MarkShipped();

        if (result.IsFailure)
        {
            // A partial shipment can dispatch a second consignment for an already-shipped order.
            // That is normal, not an error, so it is logged at Debug and absorbed.
            logger.LogDebug(
                "Order {OrderId} not moved to Shipped: {Error}",
                domainEvent.OrderId,
                result.Error.Description);
        }
    }
}

/// <summary>Moves the order to Delivered when its consignment arrives.</summary>
/// <param name="orders">Order persistence.</param>
/// <param name="logger">Logger.</param>
internal sealed class MarkOrderDeliveredOnDelivery(
    IOrderRepository orders,
    ILogger<MarkOrderDeliveredOnDelivery> logger) : IEventHandler<ShipmentDeliveredDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(ShipmentDeliveredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Order? order = await orders.GetAsync(domainEvent.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return;
        }

        Result result = order.MarkDelivered();

        if (result.IsFailure)
        {
            logger.LogDebug(
                "Order {OrderId} not moved to Delivered: {Error}",
                domainEvent.OrderId,
                result.Error.Description);
        }
    }
}

/// <summary>
/// Emails the customer their tracking details when a consignment is dispatched.
/// </summary>
/// <remarks>
/// <para>
/// <b>The notification promised by <see cref="ShipmentDispatchedDomainEvent"/>'s own remarks.</b>
/// It queues rather than sends — see <see cref="IEmailQueue"/> — so the message is written in the
/// same transaction that recorded the dispatch, and no customer is told a parcel is on its way
/// by a transaction that then rolls back.
/// </para>
/// <para>
/// <b>Two lookups, and no way around them.</b> The event carries an <see cref="OrderId"/>,
/// because a shipment knows which order it fulfils and nothing about who placed it — that is the
/// aggregate boundary doing its job. Widening the event to carry the customer's email instead
/// would remove these queries and put a piece of personal data into every serialised copy of the
/// event, including the ones sitting in the outbox table. The round trips are the cheaper cost.
/// </para>
/// <para>
/// <b>Ordering with <c>MarkOrderShippedOnDispatch</c> is not guaranteed and does not matter.</b>
/// Both handlers observe the same event independently; neither reads what the other wrote. When
/// two handlers <i>do</i> depend on each other's output, that dependency belongs in one handler
/// or in an explicitly sequenced process — never in the dispatcher's registration order, which
/// is where people put it and where it silently breaks on the next refactor.
/// </para>
/// </remarks>
/// <param name="orders">Order lookup, for the number and the customer.</param>
/// <param name="customers">Customer lookup, for the address.</param>
/// <param name="queue">The durable email queue.</param>
/// <param name="logger">Logger.</param>
internal sealed class SendTrackingDetailsOnDispatch(
    IOrderRepository orders,
    ICustomerRepository customers,
    IEmailQueue queue,
    ILogger<SendTrackingDetailsOnDispatch> logger) : IEventHandler<ShipmentDispatchedDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(ShipmentDispatchedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        Order? order = await orders.GetAsync(domainEvent.OrderId, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            logger.LogWarning(
                "Cannot send tracking details for shipment {ShipmentId}: order {OrderId} not found",
                domainEvent.ShipmentId,
                domainEvent.OrderId);

            return;
        }

        Customer? customer = await customers
            .GetAsync(order.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            // Absorbed, like every other notification failure in this codebase. A parcel that
            // has physically left the warehouse cannot be un-dispatched because an email address
            // is missing, and throwing here would roll the dispatch back.
            logger.LogWarning(
                "Cannot send tracking details for order {OrderNumber}: customer {CustomerId} not found",
                order.OrderNumber,
                order.CustomerId);

            return;
        }

        bool queued = await queue
            .EnqueueAsync(
                OrderEmails.ShipmentDispatched(
                    customer.Email,
                    order.OrderNumber,
                    domainEvent.Carrier,
                    domainEvent.TrackingNumber,
                    domainEvent.EstimatedDelivery),
                cancellationToken)
            .ConfigureAwait(false);

        if (!queued)
        {
            logger.LogDebug(
                "Tracking details for {TrackingNumber} were already queued; skipped",
                domainEvent.TrackingNumber);
        }
    }
}
