using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Orders.Events;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Features.Orders.EventHandlers;

/// <summary>
/// Reserves warehouse stock when an order is submitted.
/// </summary>
/// <remarks>
/// <para>
/// <b>This handler runs inside the same transaction as the submission.</b> Domain events are
/// dispatched from within <c>SaveChangesAsync</c>, so if the reservation fails, the whole thing
/// rolls back and the order is never submitted at all. That is the correct behaviour: an order
/// promising stock that does not exist is worse than a rejected order.
/// </para>
/// <para>
/// <b>The design tension worth understanding.</b> Strictly, DDD says one transaction should
/// modify one aggregate — and this modifies both <c>Order</c> and <c>Warehouse</c>. The purist
/// alternative is a saga: submit the order, publish an integration event, reserve stock in a
/// separate transaction, and compensate by cancelling the order if it fails.
/// </para>
/// <para>
/// That is genuinely better at scale and genuinely more complex: you need idempotency, a
/// message broker, compensation logic, and an answer for the window where an order exists with
/// no stock behind it. For a single-database system, doing it transactionally is the right
/// call — but you should be able to explain both, because the trade-off is a standard senior
/// interview question. The saga version is sketched in
/// <c>course/module-25-distributed-systems/05-sagas-and-eventual-consistency.md</c>.
/// </para>
/// </remarks>
/// <param name="warehouses">Warehouse persistence.</param>
/// <param name="orders">Order lookup, to read the lines that need reserving.</param>
/// <param name="logger">Logger.</param>
internal sealed class ReserveStockOnOrderSubmitted(
    IWarehouseRepository warehouses,
    IOrderRepository orders,
    ILogger<ReserveStockOnOrderSubmitted> logger) : IEventHandler<OrderSubmittedDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderSubmittedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        // Genuinely impossible - we are inside the transaction that just submitted it - so this
        // throws rather than returning a Result. It is a "the world is broken" case, not a
        // business outcome, and those are exactly what exceptions are for.
        Order order = await orders
            .GetWithLinesAsync(domainEvent.OrderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Order '{domainEvent.OrderId}' vanished while handling its own submission event.");

        // Build the requirement map in one pass. ToDictionary over the lines is safe because
        // Order.AddLine guarantees one line per product.
        Dictionary<ProductId, int> requirements = order.Lines
            .ToDictionary(line => line.ProductId, line => line.Quantity);

        Warehouse? warehouse = await warehouses
            .FindWarehouseWithStockAsync(requirements, cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            // Throwing aborts SaveChangesAsync and rolls the submission back. A Result cannot
            // express failure here: IEventHandler returns Task, and there is nobody to hand a
            // Result to - the caller is the persistence layer, not a use case.
            logger.LogWarning(
                "No warehouse can fulfil order {OrderNumber}; rolling back submission",
                domainEvent.OrderNumber);

            throw new StockReservationFailedException(
                domainEvent.OrderId,
                InventoryErrors.NoWarehouseCanFulfil.Description);
        }

        foreach ((ProductId productId, int quantity) in requirements)
        {
            Result reservation = warehouse.Reserve(productId, quantity);

            if (reservation.IsFailure)
            {
                logger.LogWarning(
                    "Reservation failed for order {OrderNumber} at {WarehouseCode}: {Error}",
                    domainEvent.OrderNumber,
                    warehouse.Code,
                    reservation.Error.Description);

                throw new StockReservationFailedException(domainEvent.OrderId, reservation.Error.Description);
            }
        }

        // Record WHERE the stock was reserved, so a later cancellation gives it back to the
        // right site. Assigning after the loop means a partial failure above leaves the order
        // unassigned - correct, because the transaction is about to roll back anyway.
        Result assignment = order.AssignFulfillingWarehouse(warehouse.Id);
        if (assignment.IsFailure)
        {
            throw new StockReservationFailedException(domainEvent.OrderId, assignment.Error.Description);
        }

        logger.LogInformation(
            "Reserved stock for {LineCount} line(s) of order {OrderNumber} at warehouse {WarehouseCode}",
            requirements.Count,
            domainEvent.OrderNumber,
            warehouse.Code);
    }
}

/// <summary>
/// Emails the customer their order confirmation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A deliberate flaw, left in and labelled.</b> This sends email inside the database
/// transaction. If the email provider is slow, the transaction stays open and holds locks; if
/// the transaction later rolls back, the customer has an email for an order that does not exist.
/// </para>
/// <para>
/// The correct fix is the transactional outbox: write the intent to send as a row in the same
/// transaction, and have a background worker deliver it afterwards. That machinery exists in
/// this codebase — see <c>OutboxMessage</c> and <c>OutboxProcessor</c> in Infrastructure — and
/// <c>course/module-06-efcore/07-outbox-pattern.md</c> walks through converting this handler to
/// use it as an exercise.
/// </para>
/// <para>
/// <b>The mailing system has since made that exercise a five-line change.</b>
/// <see cref="IEmailQueue"/> writes the message through the caller's own unit of work, and
/// <c>SendCancellationNoticeOnOrderCancelled</c> — a few files away in
/// <c>OrderCancelledHandlers.cs</c> — is the same idea done right, on purpose, so the two can be
/// read side by side. Doing the conversion here is left to the reader precisely because the
/// wrong version is more instructive standing next to the right one than deleted.
/// </para>
/// <para>
/// It is left in the naive form here because seeing the wrong version next to the right one is
/// how the distinction sticks.
/// </para>
/// </remarks>
/// <param name="customers">Customer lookup, for the email address.</param>
/// <param name="email">Email transport.</param>
/// <param name="logger">Logger.</param>
internal sealed class SendConfirmationOnOrderSubmitted(
    ICustomerRepository customers,
    IEmailSender email,
    ILogger<SendConfirmationOnOrderSubmitted> logger) : IEventHandler<OrderSubmittedDomainEvent>
{
    /// <inheritdoc />
    public async Task HandleAsync(OrderSubmittedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Customer? customer = await customers
            .GetAsync(domainEvent.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            logger.LogWarning(
                "Cannot send confirmation for {OrderNumber}: customer {CustomerId} not found",
                domainEvent.OrderNumber,
                domainEvent.CustomerId);

            return;
        }

        await email.SendAsync(
            customer.Email.Value,
            $"Order {domainEvent.OrderNumber} confirmed",
            $"""
             Thank you for your order.

             Order number: {domainEvent.OrderNumber}
             Total:        {domainEvent.Total}
             Delivering to:
             {domainEvent.ShippingAddress}
             """,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Thrown when stock cannot be reserved for a submitted order, aborting the transaction.
/// </summary>
/// <remarks>
/// A custom exception type rather than <c>InvalidOperationException</c>, so the global handler
/// can map it to <c>409 Conflict</c> instead of <c>500</c>. The distinction matters: 500 tells
/// the client "we broke, try again later"; 409 tells them "your request conflicts with reality,
/// change it". Only the second is true here.
/// </remarks>
public sealed class StockReservationFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="orderId">The order that could not be fulfilled.</param>
    /// <param name="reason">Why.</param>
    public StockReservationFailedException(OrderId orderId, string reason)
        : base($"Could not reserve stock for order '{orderId}': {reason}")
    {
        OrderId = orderId;
        Reason = reason;
    }

    /// <summary>Required by exception design guidelines (CA1032).</summary>
    public StockReservationFailedException() : base("Could not reserve stock for the order.")
    {
        OrderId = default;
        Reason = string.Empty;
    }

    /// <summary>Required by exception design guidelines (CA1032).</summary>
    /// <param name="message">The message.</param>
    public StockReservationFailedException(string message) : base(message)
    {
        OrderId = default;
        Reason = message;
    }

    /// <summary>Required by exception design guidelines (CA1032).</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public StockReservationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
        OrderId = default;
        Reason = message;
    }

    /// <summary>The order that could not be fulfilled.</summary>
    public OrderId OrderId { get; }

    /// <summary>Why it could not be fulfilled.</summary>
    public string Reason { get; }
}
