using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Common.Specifications;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Shipping;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Abstractions.Data;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  REPOSITORY CONTRACTS
//
//  These live in Application; the EF Core implementations live in Infrastructure. That is the
//  dependency inversion this architecture is named for: the inner layer declares what it needs,
//  and the outer layer supplies it. Application depends on nothing but Domain.
//
//  ── The generic-repository debate, settled ──
//  You will meet IRepository<T> with Add/Update/Delete/GetAll in a hundred tutorials. It is a
//  bad default, for three concrete reasons:
//
//    1. It leaks. GetAll() returning IQueryable<T> means callers write queries anywhere they
//       like, and your data access is no longer reviewable in one place.
//    2. It lies. Not every aggregate supports every operation. Products are never deleted.
//       A generic Delete() advertises an operation that must not exist.
//    3. It is redundant. DbSet<T> IS a generic repository, and a better one. Wrapping it to
//       get "abstraction" buys nothing you did not already have.
//
//  Per-aggregate interfaces with named, intention-revealing methods are the alternative used
//  here. `GetWithLinesAsync` says exactly what it loads; a generic `GetById` does not, and the
//  caller finds out when a navigation property is null.
//
//  Covered in: course/module-06-efcore/05-repositories-and-uow.md
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Reads and writes <see cref="Order"/> aggregates.</summary>
public interface IOrderRepository
{
    /// <summary>Loads an order without its lines. Use when only header data is needed.</summary>
    Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an order together with its lines.
    /// </summary>
    /// <remarks>
    /// A separate method rather than a bool parameter, because the two produce genuinely
    /// different SQL and the caller should choose deliberately. Any command that mutates lines
    /// must use this one — <see cref="Order.AddLine"/> on a partially-loaded aggregate would
    /// silently compute the wrong total.
    /// </remarks>
    Task<Order?> GetWithLinesAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>Loads an order by its human-facing reference.</summary>
    Task<Order?> GetByNumberAsync(OrderNumber number, CancellationToken cancellationToken = default);

    /// <summary>Lists orders matching a specification.</summary>
    Task<IReadOnlyList<Order>> ListAsync(
        Specification<Order> specification,
        CancellationToken cancellationToken = default);

    /// <summary>Counts orders matching a specification without materialising them.</summary>
    Task<int> CountAsync(Specification<Order> specification, CancellationToken cancellationToken = default);

    /// <summary>True when any order matches. Translates to <c>EXISTS</c>, not <c>COUNT(*) &gt; 0</c>.</summary>
    Task<bool> AnyAsync(Specification<Order> specification, CancellationToken cancellationToken = default);

    /// <summary>Stages a new order for insertion. Nothing hits the database until the unit of work commits.</summary>
    void Add(Order order);

    /// <summary>Stages an order for deletion. Used only for abandoned drafts.</summary>
    void Remove(Order order);
}

/// <summary>Reads and writes <see cref="Product"/> aggregates.</summary>
public interface IProductRepository
{
    /// <summary>Loads a product by id.</summary>
    Task<Product?> GetAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>Loads a product by SKU.</summary>
    Task<Product?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads several products at once.
    /// </summary>
    /// <remarks>
    /// Exists specifically to avoid the N+1 problem: building an order with 20 lines must issue
    /// one <c>WHERE Id IN (...)</c>, not 20 round trips. N+1 is the most common performance
    /// defect in ORM-backed applications and the most common thing an interviewer will probe.
    /// See <c>course/module-06-efcore/04-n-plus-one.md</c>.
    /// </remarks>
    Task<IReadOnlyDictionary<ProductId, Product>> GetManyAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>True when a product with this SKU already exists.</summary>
    Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken = default);

    /// <summary>Stages a new product for insertion.</summary>
    void Add(Product product);
}

/// <summary>Reads and writes <see cref="Customer"/> aggregates.</summary>
public interface ICustomerRepository
{
    /// <summary>Loads a customer by id.</summary>
    Task<Customer?> GetAsync(CustomerId id, CancellationToken cancellationToken = default);

    /// <summary>Loads a customer by email.</summary>
    Task<Customer?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>True when the email is already registered.</summary>
    Task<bool> EmailExistsAsync(EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>Stages a new customer for insertion.</summary>
    void Add(Customer customer);
}

/// <summary>Reads and writes <see cref="Warehouse"/> aggregates.</summary>
public interface IWarehouseRepository
{
    /// <summary>Loads a warehouse without any stock rows.</summary>
    Task<Warehouse?> GetAsync(WarehouseId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a warehouse with only the stock rows for the given products.
    /// </summary>
    /// <remarks>
    /// The filtered-include that makes the <see cref="Warehouse"/> aggregate viable. Loading all
    /// 50,000 stock rows to reserve three of them would be unusable; this issues
    /// <c>WHERE ProductId IN (...)</c> against the child table.
    /// </remarks>
    Task<Warehouse?> GetWithStockForAsync(
        WarehouseId id,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a site that can satisfy every requested line in one shipment.
    /// </summary>
    /// <param name="requirements">Product ids mapped to required quantities.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The first suitable warehouse, or <c>null</c> when no single site can.</returns>
    Task<Warehouse?> FindWarehouseWithStockAsync(
        IReadOnlyDictionary<ProductId, int> requirements,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all active sites.</summary>
    Task<IReadOnlyList<Warehouse>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Stages a new warehouse for insertion.</summary>
    void Add(Warehouse warehouse);
}

/// <summary>Reads and writes <see cref="Shipment"/> aggregates.</summary>
public interface IShipmentRepository
{
    /// <summary>Loads a shipment by id, including its timeline.</summary>
    Task<Shipment?> GetAsync(ShipmentId id, CancellationToken cancellationToken = default);

    /// <summary>Loads a shipment by carrier tracking reference.</summary>
    Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>Lists every shipment raised against an order.</summary>
    Task<IReadOnlyList<Shipment>> ListForOrderAsync(OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new shipment for insertion.</summary>
    void Add(Shipment shipment);
}
