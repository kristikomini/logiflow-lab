using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Orders.Events;
using Microsoft.AspNetCore.SignalR;

namespace LogiFlow.Api.RealTime;

/// <summary>
/// Translates the order domain events the outbox publishes into pushes on
/// <see cref="OrderTrackingHub"/>.
/// </summary>
/// <param name="hub">The hub context — the way to send from outside a hub method.</param>
/// <param name="logger">For the events nobody is listening to.</param>
/// <remarks>
/// <para>
/// <b>This is the anti-corruption layer</b>, and it is four lines of <c>switch</c> doing a job
/// that matters out of proportion to its size. On one side: domain events, free to change,
/// carrying value objects and everything a handler might need. On the other: a browser that
/// wants a status string and cannot be redeployed with the server. Between them, a translation
/// somebody has to write on purpose. Skip it — push the domain event — and the domain record's
/// property names become a public API discovered only when renaming one breaks a UI.
/// </para>
/// <para>
/// <b>It lives in the API project</b>, which is the only place that may know SignalR exists. The
/// outbox processor in Infrastructure calls it through
/// <see cref="IIntegrationEventPublisher"/> and never learns what it does — Infrastructure has no
/// reference to this assembly and could not, since the dependency points the other way.
/// </para>
/// <para>
/// <b>Unknown events are ignored, not rejected.</b> Most of what the outbox carries — stock
/// movements, price changes — has no browser waiting for it. Throwing would make the outbox count
/// a delivery failure, retry five times, and quarantine a message that was never a problem.
/// </para>
/// Covered in: <c>course/module-25-distributed-systems/06-real-time.md</c>
/// </remarks>
internal sealed class SignalRIntegrationEventPublisher(
    IHubContext<OrderTrackingHub, IOrderTrackingClient> hub,
    ILogger<SignalRIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // One arm per state a watcher can see. The events carry strongly-typed ids, so .Value
        // unwraps the OrderId struct back to the Guid a JSON client can read.
        OrderStatusUpdate? update = integrationEvent switch
        {
            OrderSubmittedDomainEvent e => new OrderStatusUpdate(e.OrderId.Value, e.OrderNumber.Value, "Submitted", e.OccurredAtUtc),
            OrderConfirmedDomainEvent e => new OrderStatusUpdate(e.OrderId.Value, e.OrderNumber.Value, "Confirmed", e.OccurredAtUtc),
            OrderShippedDomainEvent e => new OrderStatusUpdate(e.OrderId.Value, e.OrderNumber.Value, "Shipped", e.OccurredAtUtc),
            OrderDeliveredDomainEvent e => new OrderStatusUpdate(e.OrderId.Value, e.OrderNumber.Value, "Delivered", e.OccurredAtUtc),
            OrderCancelledDomainEvent e => new OrderStatusUpdate(e.OrderId.Value, e.OrderNumber.Value, "Cancelled", e.OccurredAtUtc),
            _ => null,
        };

        if (update is null)
        {
            return;
        }

        // Sending to a group nobody is in is a no-op, not an error — which is the normal case,
        // since most orders are not being watched at the moment they change.
        await hub.Clients
            .Group(OrderTrackingHub.GroupFor(update.OrderId))
            .OrderStatusChanged(update)
            .ConfigureAwait(false);

        logger.LogDebug(
            "Pushed {Status} for order {OrderNumber} to its tracking group",
            update.Status,
            update.OrderNumber);
    }
}
