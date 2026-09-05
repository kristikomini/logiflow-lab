using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Shipping;
using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiFlow.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Warehouse"/>.</summary>
public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Warehouses");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(w => w.Code).IsUnique();

        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.IsActive).IsRequired();

        builder.OwnsOne(w => w.Address, address =>
        {
            address.Property(a => a.Line1).HasColumnName("Address_Line1").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.Line2).HasColumnName("Address_Line2").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.City).HasColumnName("Address_City").HasMaxLength(100);
            address.Property(a => a.Region).HasColumnName("Address_Region").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("Address_PostalCode").HasMaxLength(20);
            address.Property(a => a.CountryCode).HasColumnName("Address_Country").HasMaxLength(2);
        });

        builder.Navigation(w => w.Address).IsRequired();

        builder.HasMany(w => w.Stock)
            .WithOne()
            .HasForeignKey(s => s.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Warehouse.Stock))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(w => w.RowVersion).IsRowVersion();
        builder.Ignore(w => w.DomainEvents);
    }
}

/// <summary>Maps <see cref="StockItem"/>.</summary>
public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StockItems");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.WarehouseId).IsRequired();
        builder.Property(s => s.ProductId).IsRequired();
        builder.Property(s => s.QuantityOnHand).IsRequired();
        builder.Property(s => s.QuantityReserved).IsRequired();
        builder.Property(s => s.ReorderThreshold).IsRequired();

        // QuantityAvailable is QuantityOnHand - QuantityReserved. Derived, never stored.
        builder.Ignore(s => s.QuantityAvailable);
        builder.Ignore(s => s.NeedsReplenishment);

        // A product may appear at most once per warehouse. Without this, a race between two
        // AddStockItem calls creates two rows for the same product and every subsequent
        // reservation silently uses only one of them.
        builder.HasIndex(s => new { s.WarehouseId, s.ProductId }).IsUnique();

        // ── A CHECK constraint the application cannot bypass ─────────────────────────────
        // The domain enforces this too, but a bad migration, a manual UPDATE, or a bug in a
        // future handler could all violate it. The database is the last line of defence and the
        // only one that applies to every writer, including a DBA at 2am.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StockItems_ReservedNotExceedingOnHand",
            "[QuantityReserved] <= [QuantityOnHand] AND [QuantityReserved] >= 0"));
    }
}

/// <summary>Maps <see cref="Shipment"/> and its timeline.</summary>
public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Shipments");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.OrderId).IsRequired();
        builder.HasIndex(s => s.OrderId);

        builder.Property(s => s.WarehouseId).IsRequired();
        builder.Property(s => s.Carrier).HasConversion<int>().IsRequired();
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();

        builder.Property(s => s.TrackingReference).HasMaxLength(50);

        // Filtered UNIQUE index. A plain unique index would reject every shipment still in
        // Preparing, because SQL Server treats multiple NULLs as duplicates in a unique index -
        // unlike the SQL standard. The filter excludes NULLs so only real references are checked.
        // This bites people regularly and the fix is not obvious.
        builder.HasIndex(s => s.TrackingReference)
            .IsUnique()
            .HasFilter("[TrackingReference] IS NOT NULL")
            .HasDatabaseName("UX_Shipments_TrackingReference");

        builder.ComplexProperty(s => s.TotalWeight, weight =>
            weight.Property(w => w.Grams).HasColumnName("TotalWeight_Grams"));

        builder.OwnsOne(s => s.Destination, address =>
        {
            address.Property(a => a.Line1).HasColumnName("Destination_Line1").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.Line2).HasColumnName("Destination_Line2").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.City).HasColumnName("Destination_City").HasMaxLength(100);
            address.Property(a => a.Region).HasColumnName("Destination_Region").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("Destination_PostalCode").HasMaxLength(20);
            address.Property(a => a.CountryCode).HasColumnName("Destination_Country").HasMaxLength(2);
        });

        builder.Navigation(s => s.Destination).IsRequired();

        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.DispatchedAtUtc);
        builder.Property(s => s.DeliveredAtUtc);
        builder.Property(s => s.EstimatedDeliveryDate);

        // The tracking timeline. OwnsMany gives it its own table but keeps it owned by the
        // shipment: it cannot be queried or saved independently, which matches the domain
        // exactly - a scan has no meaning without the parcel it belongs to.
        builder.OwnsMany(s => s.Timeline, timeline =>
        {
            timeline.ToTable("ShipmentEvents");
            timeline.HasKey(e => e.Id);
            timeline.WithOwner().HasForeignKey(e => e.ShipmentId);

            timeline.Property(e => e.Status).HasConversion<int>().IsRequired();
            timeline.Property(e => e.Description).HasMaxLength(500).IsRequired();
            timeline.Property(e => e.Location).HasMaxLength(200);
            timeline.Property(e => e.OccurredAtUtc).IsRequired();

            timeline.HasIndex(e => new { e.ShipmentId, e.OccurredAtUtc });
        });

        builder.Navigation(s => s.Timeline).Metadata.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Ignore(s => s.DomainEvents);
        builder.Ignore(s => s.TrackingNumber);   // computed from TrackingReference + Carrier
        builder.Ignore(s => s.TransitDays);
        builder.Ignore(s => s.IsOverdue);
    }
}

/// <summary>Maps <see cref="OutboxMessage"/>.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(500).IsRequired();

        // The one place nvarchar(max) is right: an event payload has no sensible upper bound,
        // and the model-wide 500-character default would silently truncate it.
        builder.Property(m => m.Content).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(m => m.OccurredAtUtc).IsRequired();
        builder.Property(m => m.ProcessedAtUtc);
        builder.Property(m => m.Error).HasMaxLength(4000);
        builder.Property(m => m.AttemptCount).IsRequired();

        // The processor's only query is "unprocessed messages, oldest first". A filtered index on
        // exactly that keeps the scan tiny no matter how many million processed rows accumulate -
        // and processed rows are the overwhelming majority within a day of going live.
        builder.HasIndex(m => m.OccurredAtUtc)
            .HasFilter("[ProcessedAtUtc] IS NULL")
            .HasDatabaseName("IX_Outbox_Unprocessed");
    }
}
