using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Features.Orders;

/// <summary>Cancels an order that has not yet shipped.</summary>
/// <param name="OrderId">The order to cancel.</param>
/// <param name="Reason">Why. Recorded on the order and carried in the domain event.</param>
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>Input validation for <see cref="CancelOrderCommand"/>.</summary>
public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    /// <summary>Configures the rules.</summary>
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(500)
            .WithMessage("Provide a cancellation reason between 3 and 500 characters.");
    }
}

/// <summary>Handles <see cref="CancelOrderCommand"/>.</summary>
/// <param name="orders">Order persistence.</param>
internal sealed class CancelOrderCommandHandler(IOrderRepository orders) : ICommandHandler<CancelOrderCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);

        // Lines are loaded because OrderCancelledDomainEvent carries the refundable total, and
        // the total is computed from the lines. Loading the header alone would emit an event
        // claiming a refund of zero - and the payment handler downstream would believe it.
        Order? order = await orders.GetWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return OrderErrors.NotFound(orderId);
        }

        // The aggregate refuses if the order has already shipped, via the state machine.
        return order.Cancel(request.Reason);
    }
}
