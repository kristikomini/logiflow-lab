using LogiFlow.Domain.Common;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Domain.Catalog;

/// <summary>
/// A sellable item in the catalogue. Aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// Note the shape of this class, because every aggregate in the solution follows it:
/// </para>
/// <list type="number">
///   <item><description>Private fields for any collection.</description></item>
///   <item><description>Public properties with <c>private set</c> — readable, not writable, from outside.</description></item>
///   <item><description>A private constructor plus a static <c>Create</c> factory returning <see cref="Result{TValue}"/>.</description></item>
///   <item><description>Behaviour methods that enforce invariants and raise domain events.</description></item>
/// </list>
/// <para>
/// The thing this shape prevents is the <i>anaemic domain model</i>: a class of public
/// get/set properties with all the actual logic living in a "ProductService". That design
/// is popular and it is a trap — once anyone can write <c>product.Price = -5</c>, the rule
/// "price must be positive" has to be re-checked at every call site forever. Here, an invalid
/// Product cannot be constructed at all.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/03-aggregates.md</c>
/// </remarks>
public sealed class Product : AggregateRoot<ProductId>
{
    private Product(
        ProductId id,
        Sku sku,
        string name,
        string? description,
        Money unitPrice,
        Weight weight,
        bool isActive) : base(id)
    {
        Sku = sku;
        Name = name;
        Description = description;
        UnitPrice = unitPrice;
        Weight = weight;
        IsActive = isActive;
    }

    // EF Core materialisation only. See Entity<TId>.
    private Product()
    {
    }

    /// <summary>Business-facing product code. Unique across the catalogue.</summary>
    public Sku Sku { get; private set; }

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Optional long-form description.</summary>
    public string? Description { get; private set; }

    /// <summary>Current list price, before any customer-tier discount.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>Shipping weight of a single unit.</summary>
    public Weight Weight { get; private set; }

    /// <summary>
    /// Whether the product can be added to new orders.
    /// </summary>
    /// <remarks>
    /// Products are deactivated, never deleted. Historical orders must keep pointing at a real
    /// product row, and "how much did we sell of the discontinued SKU last year?" has to stay
    /// answerable. Hard-deleting master data is one of the most expensive mistakes a junior
    /// developer can make in a system that has been live for a while.
    /// </remarks>
    public bool IsActive { get; private set; }

    /// <summary>Creates a valid product, or explains why it cannot.</summary>
    public static Result<Product> Create(
        Sku sku,
        string? name,
        string? description,
        Money unitPrice,
        Weight weight)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ProductErrors.NameRequired;
        }

        if (name.Length > 200)
        {
            return ProductErrors.NameTooLong;
        }

        if (unitPrice.IsNegative)
        {
            return ProductErrors.NegativePrice;
        }

        var product = new Product(
            ProductId.New(),
            sku,
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            unitPrice,
            weight,
            isActive: true);

        return product;
    }

    /// <summary>Changes the list price.</summary>
    /// <remarks>
    /// Raises <see cref="Events.ProductPriceChangedDomainEvent"/> carrying both the old and new
    /// price. Carrying the previous value is what makes an event useful downstream — a price
    /// history projection or a "your wishlist item dropped in price" notification cannot be
    /// built from the new value alone.
    /// </remarks>
    public Result ChangePrice(Money newPrice)
    {
        if (newPrice.IsNegative)
        {
            return ProductErrors.NegativePrice;
        }

        if (newPrice.Currency != UnitPrice.Currency)
        {
            return ProductErrors.CurrencyMismatch;
        }

        if (newPrice == UnitPrice)
        {
            // Idempotent: setting the same price is a no-op, and must NOT raise an event.
            // Emitting events for non-changes is how you end up sending customers three
            // identical "price drop!" emails.
            return Result.Success();
        }

        Money previous = UnitPrice;
        UnitPrice = newPrice;
        Raise(new Events.ProductPriceChangedDomainEvent(Id, Sku, previous, newPrice));
        return Result.Success();
    }

    /// <summary>Renames the product.</summary>
    public Result Rename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ProductErrors.NameRequired;
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Removes the product from sale without deleting it.</summary>
    public Result Discontinue()
    {
        if (!IsActive)
        {
            return ProductErrors.AlreadyDiscontinued;
        }

        IsActive = false;
        Raise(new Events.ProductDiscontinuedDomainEvent(Id, Sku));
        return Result.Success();
    }

    /// <summary>Returns the product to sale.</summary>
    public Result Reactivate()
    {
        IsActive = true;
        return Result.Success();
    }
}

/// <summary>
/// Everything that can go wrong with a <see cref="Product"/>, in one greppable place.
/// </summary>
public static class ProductErrors
{
    /// <summary>Name was blank.</summary>
    public static readonly Error NameRequired =
        Error.Validation("Product.NameRequired", "Product name is required.");

    /// <summary>Name exceeded the stored length.</summary>
    public static readonly Error NameTooLong =
        Error.Validation("Product.NameTooLong", "Product name cannot exceed 200 characters.");

    /// <summary>Price was below zero.</summary>
    public static readonly Error NegativePrice =
        Error.Validation("Product.NegativePrice", "Product price cannot be negative.");

    /// <summary>Attempted to set a price in a different currency.</summary>
    public static readonly Error CurrencyMismatch =
        Error.Validation("Product.CurrencyMismatch", "New price must use the product's existing currency.");

    /// <summary>Product was already discontinued.</summary>
    public static readonly Error AlreadyDiscontinued =
        Error.Conflict("Product.AlreadyDiscontinued", "Product is already discontinued.");

    /// <summary>No product exists with the requested id.</summary>
    public static Error NotFound(ProductId id) =>
        Error.NotFound("Product.NotFound", $"No product found with id '{id}'.");

    /// <summary>No product exists with the requested SKU.</summary>
    public static Error SkuNotFound(Sku sku) =>
        Error.NotFound("Product.SkuNotFound", $"No product found with SKU '{sku}'.");

    /// <summary>A product with that SKU already exists.</summary>
    public static Error DuplicateSku(Sku sku) =>
        Error.Conflict("Product.DuplicateSku", $"A product with SKU '{sku}' already exists.");

    /// <summary>Attempted to order a discontinued product.</summary>
    public static Error Inactive(Sku sku) =>
        Error.Validation("Product.Inactive", $"Product '{sku}' is discontinued and cannot be ordered.");
}
