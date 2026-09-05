using LogiFlow.Domain.Orders;
using LogiFlow.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Order"/> and its lines.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a configuration class instead of data annotations on the entity?</b> Because
/// <c>[Column("Total")]</c> and <c>[MaxLength(200)]</c> on a domain class are persistence
/// concerns leaking into the Domain layer — which would need a reference to
/// <c>System.ComponentModel.DataAnnotations</c>, and would let a schema decision be made by
/// someone editing business logic. The Fluent API keeps all of it out here, and it can express
/// things annotations simply cannot: composite keys, filtered indexes, owned types, converters.
/// </para>
/// Covered in: <c>course/module-06-efcore/03-fluent-configuration.md</c>
/// </remarks>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        // ── Order number ─────────────────────────────────────────────────────────────────
        builder.Property(o => o.OrderNumber)
            .HasConversion(n => n.Value, v => OrderNumber.FromTrusted(v))
            .HasMaxLength(20)
            .IsRequired();

        // Unique so a duplicate reference is impossible even under concurrency. Application-level
        // checks cannot make this guarantee; only the database can.
        builder.HasIndex(o => o.OrderNumber).IsUnique();

        // ── Foreign keys to other aggregates ─────────────────────────────────────────────
        // NOTE: no navigation property and no HasOne(). Order references Customer by ID only,
        // so EF is told about the column but NOT given a way to traverse to the Customer entity.
        // That is deliberate: a navigation would let application code load and mutate two
        // aggregates in one transaction, which is exactly what aggregate boundaries forbid.
        builder.Property(o => o.CustomerId).IsRequired();
        builder.HasIndex(o => o.CustomerId);

        builder.Property(o => o.FulfillingWarehouseId);

        builder.Property(o => o.CustomerTier)
            .HasConversion<int>()   // store the numeric value, not the name
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<int>()
            .IsRequired();

        // ── Currency ─────────────────────────────────────────────────────────────────────
        builder.Property(o => o.Currency)
            .HasConversion(c => c.Code, code => Currency.FromCodeOrThrow(code))
            .HasMaxLength(3)
            .IsRequired();

        // ── Shipping address ─────────────────────────────────────────────────────────────
        // OwnsOne performs TABLE SPLITTING: Address has no table of its own, its columns live on
        // Orders as ShippingAddress_Line1, ShippingAddress_City, and so on. No join, no
        // AddressId, no orphan rows.
        //
        // OwnsOne rather than ComplexProperty because this address is OPTIONAL (a draft order may
        // not have one yet) and owned types have supported optional mapping the longest.
        builder.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.Line1).HasColumnName("ShippingAddress_Line1").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.Line2).HasColumnName("ShippingAddress_Line2").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.City).HasColumnName("ShippingAddress_City").HasMaxLength(100);
            address.Property(a => a.Region).HasColumnName("ShippingAddress_Region").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("ShippingAddress_PostalCode").HasMaxLength(20);
            address.Property(a => a.CountryCode).HasColumnName("ShippingAddress_Country").HasMaxLength(2);
        });

        // ── Timestamps ───────────────────────────────────────────────────────────────────
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.SubmittedAtUtc);
        builder.Property(o => o.ShippedAtUtc);
        builder.Property(o => o.DeliveredAtUtc);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // ── Optimistic concurrency ───────────────────────────────────────────────────────
        // IsRowVersion maps to SQL Server's `rowversion`: an 8-byte value the DATABASE bumps on
        // every UPDATE. EF adds it to the WHERE clause, so a save that matches zero rows means
        // somebody else got there first, and throws DbUpdateConcurrencyException instead of
        // silently overwriting their work.
        builder.Property(o => o.RowVersion).IsRowVersion();

        // ── Lines ────────────────────────────────────────────────────────────────────────
        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            // Cascade is correct HERE and rarely elsewhere: a line has no meaning without its
            // order, so deleting the order must delete the lines. Cascading between AGGREGATES
            // is how you accidentally delete a customer's entire order history.
            .OnDelete(DeleteBehavior.Cascade);

        // Tells EF to read and write the private List<OrderLine> field rather than the
        // IReadOnlyList property - which has no setter, by design. Without this, EF cannot
        // materialise the collection and the encapsulation would have to be given up.
        builder.Metadata
            .FindNavigation(nameof(Order.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // ── Computed properties are NOT columns ──────────────────────────────────────────
        // Subtotal, Total, DiscountAmount, ShippingCost and TotalWeight are all derived from the
        // lines. Storing them would create a second source of truth that can drift. EF would
        // otherwise try to map them and fail, since they have no setter.
        builder.Ignore(o => o.Subtotal);
        builder.Ignore(o => o.DiscountAmount);
        builder.Ignore(o => o.ShippingCost);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.TotalWeight);
        builder.Ignore(o => o.IsEditable);

        // Domain events live in memory only. Persisting them would double-store what the outbox
        // already handles properly.
        builder.Ignore(o => o.DomainEvents);

        // ── Indexes ──────────────────────────────────────────────────────────────────────
        // Composite index supporting the most common query: "this customer's orders, newest
        // first". COLUMN ORDER MATTERS - a (CustomerId, CreatedAtUtc) index serves
        // "WHERE CustomerId = x ORDER BY CreatedAtUtc" perfectly, while (CreatedAtUtc, CustomerId)
        // is nearly useless for it. Leftmost-prefix rule: the index can be used for the first
        // column alone, or the first two together, never the second alone.
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_Customer_Created");

        // Filtered index for the fulfilment queue. Only submitted and confirmed orders are
        // indexed, so the index stays small even when the table holds ten years of delivered
        // orders. This is one of SQL Server's genuinely underused features.
        builder.HasIndex(o => o.Status)
            .HasFilter("[Status] IN (2, 3)")
            .HasDatabaseName("IX_Orders_OpenStatus");
    }
}

/// <summary>Maps <see cref="OrderLine"/>.</summary>
public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OrderLines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrderId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();

        builder.Property(l => l.Sku)
            .HasConversion(s => s.Value, v => Sku.FromTrusted(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();

        // ── Money as a complex type ──────────────────────────────────────────────────────
        // ComplexProperty (EF Core 8+) maps a value object inline WITHOUT making it an entity.
        // The difference from OwnsOne matters: an owned type is still an entity with its own
        // identity in the change tracker, which for a struct like Money is both wasteful and
        // conceptually wrong - €10 has no identity. A complex type is treated as part of the
        // parent row, exactly like a value object should be.
        builder.ComplexProperty(l => l.UnitPrice, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("UnitPrice_Amount")
                .HasPrecision(19, 4);

            money.Property(m => m.Currency)
                .HasColumnName("UnitPrice_Currency")
                .HasConversion(c => c.Code, code => Currency.FromCodeOrThrow(code))
                .HasMaxLength(3);
        });

        builder.ComplexProperty(l => l.UnitWeight, weight =>
            weight.Property(w => w.Grams).HasColumnName("UnitWeight_Grams"));

        // Computed from UnitPrice and Quantity.
        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.LineWeight);

        // Supports the "top selling products" report, which groups lines by product.
        builder.HasIndex(l => l.ProductId);
    }
}
