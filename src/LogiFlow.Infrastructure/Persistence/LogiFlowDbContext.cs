using System.Reflection;
using LogiFlow.Domain.Automation;
using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Shipping;
using LogiFlow.Infrastructure.Mailing;
using LogiFlow.Infrastructure.Persistence.Conventions;
using LogiFlow.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Infrastructure.Persistence;

/// <summary>
/// The EF Core session. One per HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// <b>DbContext is not thread-safe, and its lifetime is not negotiable.</b> Registering it as a
/// singleton produces the single most confusing bug in ASP.NET Core: two requests share a change
/// tracker, one saves the other's half-finished entities, and you get
/// "A second operation was started on this context instance before a previous operation
/// completed" — intermittently, under load, never in development. Always scoped.
/// </para>
/// <para>
/// <b>It is already a Unit of Work and a set of Repositories.</b> <c>DbSet&lt;T&gt;</c> is a
/// repository; <c>SaveChanges</c> is a unit of work. The extra interfaces in this solution exist
/// so the Application layer need not reference EF Core — not because <c>DbContext</c> lacks
/// the patterns.
/// </para>
/// Covered in: <c>course/module-06-efcore/01-dbcontext-and-change-tracking.md</c>
/// </remarks>
/// <param name="options">Provider and connection configuration, supplied by DI.</param>
public sealed class LogiFlowDbContext(DbContextOptions<LogiFlowDbContext> options) : DbContext(options)
{
    /// <summary>Order aggregates.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Order lines. Exposed for projections; never loaded independently of an order.</summary>
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    /// <summary>Catalogue products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Customers.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Warehouses.</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>Per-warehouse stock rows.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <summary>Shipments.</summary>
    public DbSet<Shipment> Shipments => Set<Shipment>();

    /// <summary>
    /// Work for the warehouse control system: move this load unit from A to B.
    /// </summary>
    /// <remarks>
    /// The only thing the machine layer persists. Equipment is commissioning data owned by the
    /// gateway, machine state is telemetry on a cheaper path, and zone occupancy must be rebuilt
    /// from the floor after a restart rather than read back from here — see
    /// <c>ZoneAllocator.RebuildFromFloor</c> for why that one is not a storage problem.
    /// </remarks>
    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();

    /// <summary>Messages awaiting publication. See <see cref="OutboxMessage"/>.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Email awaiting delivery, and the record of what was delivered.</summary>
    /// <remarks>
    /// Exposed on the same context as the business tables on purpose: that is what lets
    /// <see cref="DatabaseEmailQueue"/> write a message inside the caller's transaction, so an
    /// order and its confirmation commit together or not at all.
    /// </remarks>
    public DbSet<QueuedEmail> QueuedEmails => Set<QueuedEmail>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Applies every IEntityTypeConfiguration<T> in this assembly. The alternative - a wall of
        // modelBuilder.Entity<Order>(...) lambdas inline - turns OnModelCreating into a
        // thousand-line method that nobody can review.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Everything lives in one schema. Naming it explicitly rather than relying on `dbo`
        // makes it obvious in a shared database which tables belong to this service.
        modelBuilder.HasDefaultSchema("logiflow");

        // Backs IOrderNumberGenerator. A SEQUENCE is atomic and lock-free, unlike
        // SELECT MAX(...) + 1, which produces duplicates the moment two requests overlap.
        modelBuilder.HasSequence<int>("OrderNumbers", "logiflow")
            .StartsAt(1)
            .IncrementsBy(1);

        base.OnModelCreating(modelBuilder);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // One line, and every OrderId / ProductId / WarehouseId in the model maps to a Guid.
        configurationBuilder.RegisterStronglyTypedIds();

        // Model-wide defaults. Setting these here means no individual configuration has to
        // remember them, and - more importantly - a new entity added next year gets them
        // automatically instead of silently defaulting to nvarchar(max) and decimal(18,2).
        configurationBuilder.Properties<string>().HaveMaxLength(500);

        // decimal(19,4): 15 digits before the point, 4 after. Enough for any realistic amount,
        // and 4 decimal places because unit prices in some industries genuinely need them.
        // Without this EF picks decimal(18,2) and SILENTLY TRUNCATES anything finer - a warning
        // in the log that nobody reads until the accounts do not balance.
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);

        base.ConfigureConventions(configurationBuilder);
    }
}
