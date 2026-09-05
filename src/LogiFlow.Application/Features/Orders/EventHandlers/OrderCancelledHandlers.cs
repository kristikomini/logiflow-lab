using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Mailing;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Orders.Events;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Features.Orders.EventHandlers;

/// <summary>
/// Releases reserved stock when an order is cancelled.
/// </summary>
/// <remarks>
/// <para>
/// The mirror image of <see cref="ReserveStockOnOrderSubmitted"/>. Without it, cancelled orders
/// would permanently consume stock that is never picked — the warehouse would slowly stop being
/// able to sell anything, and the cause would be invisible until someone compared physical
/// counts against the system.
/// </para>
/// <para>
/// <b>Note the failure policy is the opposite of the reservation handler's.</b> A failed
/// release is logged and swallowed rather than thrown. Reserving stock that does not exist is a
/// correctness failure that must abort. Failing to release stock is an accounting drift that
/// must not prevent a customer from cancelling their order — you fix it with a reconciliation
/// job, not by refusing the cancellation.
/// </para>
/// <para>
/// Deciding which failures abort and which are absorbed, per handler, is the actual design work
/// in event-driven code.
/// </para>
/// </remarks>
/// <param name="warehouses">Warehouse persistence.</param>
/// <param name="orders">Order lookup.</param>
/// <param name="logger">Logger.</param>
internal sealed class ReleaseStockOnOrderCancelled(
    IWarehouseRepository warehouses,
    IOrderRepository orders,
    ILogger<ReleaseStockOnOrderCancelled> logger) : IEventHandler<OrderCancelledDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Order? order = await orders
            .GetWithLinesAsync(domainEvent.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null || order.Lines.Count == 0)
        {
            return;
        }

        // A draft cancelled before submission never reserved anything, so FulfillingWarehouseId
        // is null and there is nothing to give back. Releasing anyway would fail with
        // ReleaseExceedsReserved.
        if (order.FulfillingWarehouseId is not { } warehouseId)
        {
            logger.LogDebug(
                "Order {OrderNumber} had no stock reserved; nothing to release",
                domainEvent.OrderNumber);

            return;
        }

        Dictionary<ProductId, int> requirements = order.Lines
            .ToDictionary(line => line.ProductId, line => line.Quantity);

        // Load only the warehouse that actually holds the reservation, and only the stock rows
        // for the products on this order. Releasing across every site would credit stock to
        // warehouses that never held any.
        Warehouse? warehouse = await warehouses
            .GetWithStockForAsync(warehouseId, requirements.Keys, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            logger.LogWarning(
                "Order {OrderNumber} references warehouse {WarehouseId}, which no longer exists. "
                + "Stock release skipped; flagged for reconciliation.",
                domainEvent.OrderNumber,
                warehouseId);

            return;
        }

        foreach ((ProductId productId, int quantity) in requirements)
        {
            Result release = warehouse.Release(productId, quantity);

            if (release.IsFailure)
            {
                // Logged, not thrown. See the class remarks.
                logger.LogWarning(
                    "Could not release {Quantity} units of {ProductId} at {WarehouseCode} for cancelled "
                    + "order {OrderNumber}: {Error}. Flagged for reconciliation.",
                    quantity,
                    productId,
                    warehouse.Code,
                    domainEvent.OrderNumber,
                    release.Error.Description);
            }
        }

        logger.LogInformation(
            "Released stock for cancelled order {OrderNumber} at {WarehouseCode} ({Reason})",
            domainEvent.OrderNumber,
            warehouse.Code,
            domainEvent.Reason);
    }
}

/// <summary>
/// Tells the customer their order has been cancelled, and what happens to the money.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the handler to copy.</b> It does not send anything: it <i>queues</i> the message
/// through <see cref="IEmailQueue"/>, which writes a row through the same unit of work that is
/// cancelling the order. The two commit together. Compare
/// <c>SendConfirmationOnOrderSubmitted</c> in <c>OrderSubmittedHandlers.cs</c>, which is kept in
/// the naive form — an SMTP round trip inside the transaction — precisely so the difference is
/// visible in one repository.
/// </para>
/// <para>
/// <b>What the difference buys, concretely.</b> If the mail server is unreachable, this handler
/// does not notice and the cancellation still succeeds; the message is delivered when the server
/// comes back. If the cancellation is rolled back by a later failure in the same transaction,
/// the queued message disappears with it and no customer is told about a cancellation that never
/// happened. Neither property is available to code that calls a transport directly, at any
/// level of care.
/// </para>
/// <para>
/// <b>A missing customer is not an error here.</b> The order was cancelled successfully; failing
/// the transaction because nobody can be emailed would undo a valid business operation to
/// protect a notification. Log it and move on — the same failure policy as the stock release
/// above, and for the same reason.
/// </para>
/// </remarks>
/// <param name="customers">Customer lookup, for the address.</param>
/// <param name="queue">The durable email queue.</param>
/// <param name="logger">Logger.</param>
internal sealed class SendCancellationNoticeOnOrderCancelled(
    ICustomerRepository customers,
    IEmailQueue queue,
    ILogger<SendCancellationNoticeOnOrderCancelled> logger) : IEventHandler<OrderCancelledDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        Customer? customer = await customers
            .GetAsync(domainEvent.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            logger.LogWarning(
                "Cannot notify cancellation of {OrderNumber}: customer {CustomerId} not found",
                domainEvent.OrderNumber,
                domainEvent.CustomerId);

            return;
        }

        bool queued = await queue
            .EnqueueAsync(
                OrderEmails.OrderCancelled(
                    customer.Email,
                    domainEvent.OrderNumber,
                    domainEvent.RefundableTotal,
                    domainEvent.Reason),
                cancellationToken)
            .ConfigureAwait(false);

        if (!queued)
        {
            // The deduplication key already existed, so this event has been handled before -
            // a redelivery, or a retried request. Debug, not Warning: this is the idempotency
            // working, not something going wrong.
            logger.LogDebug(
                "Cancellation notice for {OrderNumber} was already queued; skipped",
                domainEvent.OrderNumber);
        }
    }
}
