using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Features.Orders;

/// <summary>Places a draft order.</summary>
/// <param name="OrderId">The draft to submit.</param>
public sealed record SubmitOrderCommand(Guid OrderId) : ICommand;

/// <summary>Input validation for <see cref="SubmitOrderCommand"/>.</summary>
public sealed class SubmitOrderCommandValidator : AbstractValidator<SubmitOrderCommand>
{
    /// <summary>Configures the rules.</summary>
    public SubmitOrderCommandValidator() => RuleFor(x => x.OrderId).NotEmpty();
}

/// <summary>
/// Handles <see cref="SubmitOrderCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Look at how little this does.</b> Submitting an order also has to reserve warehouse stock,
/// email the customer, and authorise payment — yet none of that appears here.
/// </para>
/// <para>
/// It happens because <see cref="Order.Submit"/> raises
/// <c>OrderSubmittedDomainEvent</c>, and handlers subscribed to that event do the rest. The
/// events are dispatched inside <c>SaveChangesAsync</c>, in the same transaction, so stock is
/// reserved if and only if the submission commits.
/// </para>
/// <para>
/// The payoff is that adding "notify the customer's account manager" tomorrow means adding one
/// event handler and touching nothing here. Compare with the alternative, where this method
/// grows a fourth injected service and a fourth reason to change.
/// </para>
/// </remarks>
/// <param name="orders">Order persistence.</param>
internal sealed class SubmitOrderCommandHandler(IOrderRepository orders) : ICommandHandler<SubmitOrderCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(SubmitOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);

        // Lines are needed: Submit() rejects an empty order, and the event payload carries the total.
        Order? order = await orders.GetWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return OrderErrors.NotFound(orderId);
        }

        return order.Submit();
    }
}
