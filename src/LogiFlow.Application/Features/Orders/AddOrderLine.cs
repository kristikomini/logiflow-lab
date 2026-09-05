using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Features.Orders;

/// <summary>Adds a product to a draft order, or increases its quantity if already present.</summary>
/// <param name="OrderId">The draft order.</param>
/// <param name="ProductId">The product to add.</param>
/// <param name="Quantity">Units to add.</param>
public sealed record AddOrderLineCommand(Guid OrderId, Guid ProductId, int Quantity) : ICommand;

/// <summary>Input validation for <see cref="AddOrderLineCommand"/>.</summary>
public sealed class AddOrderLineCommandValidator : AbstractValidator<AddOrderLineCommand>
{
    /// <summary>Configures the rules.</summary>
    public AddOrderLineCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();

        // An upper bound as well as a lower one. Without it, `quantity: int.MaxValue` overflows
        // when multiplied by the unit price - and a negative order total is a very bad day.
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .LessThanOrEqualTo(10_000)
            .WithMessage("Quantity must be between 1 and 10000.");
    }
}

/// <summary>Handles <see cref="AddOrderLineCommand"/>.</summary>
/// <param name="orders">Order persistence.</param>
/// <param name="products">Product lookup.</param>
internal sealed class AddOrderLineCommandHandler(
    IOrderRepository orders,
    IProductRepository products) : ICommandHandler<AddOrderLineCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(AddOrderLineCommand request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);

        // GetWithLinesAsync, not GetAsync. Order.AddLine has to scan the existing lines to decide
        // whether to merge, and a partially-loaded aggregate would report an empty collection -
        // silently creating a duplicate line instead of merging. This is the single most common
        // way to break an aggregate: loading less of it than its invariants need.
        Order? order = await orders.GetWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return OrderErrors.NotFound(orderId);
        }

        var productId = ProductId.From(request.ProductId);
        Product? product = await products.GetAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return ProductErrors.NotFound(productId);
        }

        // Every rule - is the order still editable, is the product active, do the currencies
        // match, does this line already exist - lives in the aggregate. The handler just asks.
        return order.AddLine(product, request.Quantity);
    }
}
