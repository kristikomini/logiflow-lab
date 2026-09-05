using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Shipping;
using LogiFlow.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IProductRepository"/>.</summary>
/// <param name="context">The scoped session.</param>
public sealed class ProductRepository(LogiFlowDbContext context) : IProductRepository
{
    /// <inheritdoc />
    public Task<Product?> GetAsync(ProductId id, CancellationToken cancellationToken = default) =>
        context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Product?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        context.Products.FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>This method exists to kill an N+1.</b> Building an order with 20 lines needs 20
    /// products. The naive version is a loop of <c>GetAsync</c> calls — 20 round trips, each
    /// costing a network hop, and the whole thing scaling linearly with basket size.
    /// </para>
    /// <para>
    /// <c>Contains</c> over a list translates to <c>WHERE Id IN (@p0, @p1, ...)</c>: one round
    /// trip regardless of count. Returning a dictionary means the caller does not then loop the
    /// result list looking for each id, which would be O(n²) in memory to fix an O(n) problem
    /// in SQL.
    /// </para>
    /// <para>
    /// <b>The caveat worth knowing:</b> SQL Server has a hard limit of 2,100 parameters per
    /// command, and each id here is one parameter. Above roughly two thousand ids you must
    /// batch, or send a table-valued parameter. EF Core 8+ can also render the list as a JSON
    /// <c>OPENJSON</c> call, which sidesteps the limit entirely and keeps the query plan stable.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<ProductId, Product>> GetManyAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            // Short-circuit. `WHERE Id IN ()` is not valid SQL, and EF would otherwise have to
            // build a query that provably returns nothing.
            return new Dictionary<ProductId, Product>();
        }

        List<Product> products = await context.Products
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return products.ToDictionary(p => p.Id);
    }

    /// <inheritdoc />
    public Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken = default) =>
        context.Products.AnyAsync(p => p.Sku == sku, cancellationToken);

    /// <inheritdoc />
    public void Add(Product product) => context.Products.Add(product);
}

/// <summary>EF Core implementation of <see cref="ICustomerRepository"/>.</summary>
/// <param name="context">The scoped session.</param>
public sealed class CustomerRepository(LogiFlowDbContext context) : ICustomerRepository
{
    /// <inheritdoc />
    public Task<Customer?> GetAsync(CustomerId id, CancellationToken cancellationToken = default) =>
        context.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default) =>
        context.Customers.FirstOrDefaultAsync(c => c.Email == email, cancellationToken);

    /// <inheritdoc />
    public Task<bool> EmailExistsAsync(EmailAddress email, CancellationToken cancellationToken = default) =>
        context.Customers.AnyAsync(c => c.Email == email, cancellationToken);

    /// <inheritdoc />
    public void Add(Customer customer) => context.Customers.Add(customer);
}

/// <summary>EF Core implementation of <see cref="IWarehouseRepository"/>.</summary>
/// <param name="context">The scoped session.</param>
public sealed class WarehouseRepository(LogiFlowDbContext context) : IWarehouseRepository
{
    /// <inheritdoc />
    public Task<Warehouse?> GetAsync(WarehouseId id, CancellationToken cancellationToken = default) =>
        context.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>A filtered include, and the thing that makes the <see cref="Warehouse"/> aggregate
    /// workable at all.</b> A real warehouse holds tens of thousands of SKUs. Loading the whole
    /// <c>Stock</c> collection to reserve three products would materialise every row on every
    /// reservation.
    /// </para>
    /// <para>
    /// <c>Include(w =&gt; w.Stock.Where(...))</c> (EF Core 5+) pushes the filter into the JOIN,
    /// so only the relevant child rows come back. The aggregate is then partially loaded — which
    /// is safe here because every operation on it is scoped to specific products, and unsafe in
    /// general. It is a trade-off to make consciously, not a default.
    /// </para>
    /// </remarks>
    public Task<Warehouse?> GetWithStockForAsync(
        WarehouseId id,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        return context.Warehouses
            .Include(w => w.Stock.Where(s => productIds.Contains(s.ProductId)))
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Finds the first active site holding enough of every requested product.
    /// </para>
    /// <para>
    /// <b>The filtering happens in memory, and that is a deliberate limitation.</b> Expressing
    /// "has sufficient stock for every entry in this dictionary" as a single translatable
    /// expression tree requires a dynamically-built predicate per product — doable, and covered
    /// as an exercise in <c>course/module-09-advanced-linq/05-dynamic-predicates.md</c>. With a
    /// handful of warehouses the current version is fine. With hundreds it would not be, and
    /// the honest fix is a stored procedure or a purpose-built query.
    /// </para>
    /// <para>
    /// Saying so in a comment is better than pretending the naive version scales.
    /// </para>
    /// </remarks>
    public async Task<Warehouse?> FindWarehouseWithStockAsync(
        IReadOnlyDictionary<ProductId, int> requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        if (requirements.Count == 0)
        {
            return null;
        }

        ProductId[] productIds = [.. requirements.Keys];

        // One query: every active warehouse, with only the stock rows for the products wanted.
        List<Warehouse> candidates = await context.Warehouses
            .Include(w => w.Stock.Where(s => productIds.Contains(s.ProductId)))
            .Where(w => w.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The all-or-nothing check, in memory. See the remarks.
        return candidates.Find(warehouse =>
            requirements.All(requirement =>
                warehouse.AvailableQuantity(requirement.Key) >= requirement.Value));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Warehouse>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await context.Warehouses
            .Where(w => w.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Warehouse warehouse) => context.Warehouses.Add(warehouse);
}

/// <summary>EF Core implementation of <see cref="IShipmentRepository"/>.</summary>
/// <param name="context">The scoped session.</param>
public sealed class ShipmentRepository(LogiFlowDbContext context) : IShipmentRepository
{
    /// <inheritdoc />
    public Task<Shipment?> GetAsync(ShipmentId id, CancellationToken cancellationToken = default) =>
        context.Shipments
            .Include(s => s.Timeline)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Shipment?> GetByTrackingNumberAsync(
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        // Normalised the same way TrackingNumber.Create does, so a webhook sending lower case
        // still matches. Comparing with ToUpperInvariant() INSIDE the expression would prevent
        // the index from being used (a non-SARGable predicate); normalising the parameter first
        // keeps the lookup an index seek.
        string normalised = trackingNumber?.Trim().ToUpperInvariant() ?? string.Empty;

        return context.Shipments
            .Include(s => s.Timeline)
            .FirstOrDefaultAsync(s => s.TrackingReference == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Shipment>> ListForOrderAsync(
        OrderId orderId,
        CancellationToken cancellationToken = default) =>
        await context.Shipments
            .Include(s => s.Timeline)
            .Where(s => s.OrderId == orderId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Shipment shipment) => context.Shipments.Add(shipment);
}
