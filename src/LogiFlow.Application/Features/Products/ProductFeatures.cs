using FluentValidation;
using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Features.Products;

/// <summary>A product as returned to clients.</summary>
/// <param name="Id">Product identity.</param>
/// <param name="Sku">Product code.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Long-form description.</param>
/// <param name="UnitPrice">List price.</param>
/// <param name="Currency">ISO code of <paramref name="UnitPrice"/>.</param>
/// <param name="WeightGrams">Shipping weight per unit.</param>
/// <param name="IsActive">Whether it can be ordered.</param>
public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal UnitPrice,
    string Currency,
    int WeightGrams,
    bool IsActive);

// ── Create ───────────────────────────────────────────────────────────────────────────────

/// <summary>Adds a product to the catalogue.</summary>
/// <param name="Sku">Product code, e.g. <c>ELE-100234</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="UnitPrice">List price.</param>
/// <param name="CurrencyCode">ISO-4217 code.</param>
/// <param name="WeightGrams">Shipping weight per unit.</param>
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    decimal UnitPrice,
    string CurrencyCode,
    int WeightGrams) : ICommand<Guid>;

/// <summary>Input validation for <see cref="CreateProductCommand"/>.</summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    /// <summary>Configures the rules.</summary>
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.WeightGrams).GreaterThan(0).LessThanOrEqualTo(2_000_000);
    }
}

/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
/// <param name="products">Product persistence.</param>
internal sealed class CreateProductCommandHandler(IProductRepository products)
    : ICommandHandler<CreateProductCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(CreateProductCommand request, CancellationToken cancellationToken)
    {
        Result<Sku> sku = Sku.Create(request.Sku);
        if (sku.IsFailure)
        {
            return sku.Error;
        }

        Result<Money> price = Money.CreateNonNegative(request.UnitPrice, request.CurrencyCode);
        if (price.IsFailure)
        {
            return price.Error;
        }

        Result<Weight> weight = Weight.FromGrams(request.WeightGrams);
        if (weight.IsFailure)
        {
            return weight.Error;
        }

        // A pre-check for a friendly error message, NOT a correctness guarantee. Two concurrent
        // requests can both pass this and both insert. The unique index on Sku is what actually
        // prevents the duplicate; the DbUpdateException it raises is translated back into
        // ProductErrors.DuplicateSku by the exception handler.
        //
        // Check-then-act is always a race. Use it for UX, never for invariants.
        if (await products.SkuExistsAsync(sku.Value, cancellationToken).ConfigureAwait(false))
        {
            return ProductErrors.DuplicateSku(sku.Value);
        }

        Result<Product> product = Product.Create(
            sku.Value,
            request.Name,
            request.Description,
            price.Value,
            weight.Value);

        if (product.IsFailure)
        {
            return product.Error;
        }

        products.Add(product.Value);
        return product.Value.Id.Value;
    }
}

// ── Change price ─────────────────────────────────────────────────────────────────────────

/// <summary>Changes a product's list price.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="NewPrice">The new list price.</param>
public sealed record ChangeProductPriceCommand(Guid ProductId, decimal NewPrice) : ICommand;

/// <summary>Input validation for <see cref="ChangeProductPriceCommand"/>.</summary>
public sealed class ChangeProductPriceCommandValidator : AbstractValidator<ChangeProductPriceCommand>
{
    /// <summary>Configures the rules.</summary>
    public ChangeProductPriceCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.NewPrice).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Handles <see cref="ChangeProductPriceCommand"/>.</summary>
/// <param name="products">Product persistence.</param>
internal sealed class ChangeProductPriceCommandHandler(IProductRepository products)
    : ICommandHandler<ChangeProductPriceCommand>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(ChangeProductPriceCommand request, CancellationToken cancellationToken)
    {
        var productId = ProductId.From(request.ProductId);

        Product? product = await products.GetAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return ProductErrors.NotFound(productId);
        }

        // The new price reuses the product's existing currency. Changing currency is a different
        // operation with different consequences (every open order referencing it), so it is not
        // silently allowed as a side effect of a price change.
        return product.ChangePrice(new Money(request.NewPrice, product.UnitPrice.Currency));
    }
}

// ── Read ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Looks up a product by SKU.</summary>
/// <param name="Sku">The product code.</param>
public sealed record GetProductBySkuQuery(string Sku) : IQuery<ProductDto>, Behaviors.ICacheableQuery
{
    /// <inheritdoc />
    public string CacheKey => $"product:sku:{Sku.ToUpperInvariant()}";

    /// <inheritdoc />
    /// <remarks>
    /// Ten minutes, far longer than an order's sixty seconds. Catalogue data changes rarely and
    /// is read constantly — the ideal cache profile. Match the TTL to how volatile the data
    /// actually is, not to a number you picked once and copied everywhere.
    /// </remarks>
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(10);
}

/// <summary>Handles <see cref="GetProductBySkuQuery"/>.</summary>
/// <param name="products">Product lookup.</param>
internal sealed class GetProductBySkuQueryHandler(IProductRepository products)
    : IQueryHandler<GetProductBySkuQuery, ProductDto>
{
    /// <inheritdoc />
    public async Task<Result<ProductDto>> HandleAsync(
        GetProductBySkuQuery request,
        CancellationToken cancellationToken)
    {
        Result<Sku> sku = Sku.Create(request.Sku);
        if (sku.IsFailure)
        {
            return sku.Error;
        }

        Product? product = await products.GetBySkuAsync(sku.Value, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return ProductErrors.SkuNotFound(sku.Value);
        }

        return new ProductDto(
            product.Id.Value,
            product.Sku.Value,
            product.Name,
            product.Description,
            product.UnitPrice.Amount,
            product.UnitPrice.Currency.Code,
            product.Weight.Grams,
            product.IsActive);
    }
}
