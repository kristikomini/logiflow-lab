using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiFlow.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Product"/>.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku)
            .HasConversion(s => s.Value, v => Sku.FromTrusted(v))
            .HasMaxLength(20)
            .IsRequired();

        // The real duplicate-SKU guard. CreateProductCommandHandler also checks first, but only
        // to produce a friendly message - two concurrent inserts would both pass that check and
        // only this index stops them.
        builder.HasIndex(p => p.Sku).IsUnique();

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.IsActive).IsRequired();

        builder.ComplexProperty(p => p.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("UnitPrice_Amount").HasPrecision(19, 4);
            money.Property(m => m.Currency)
                .HasColumnName("UnitPrice_Currency")
                .HasConversion(c => c.Code, code => Currency.FromCodeOrThrow(code))
                .HasMaxLength(3);
        });

        builder.ComplexProperty(p => p.Weight, weight =>
            weight.Property(w => w.Grams).HasColumnName("Weight_Grams"));

        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.DomainEvents);

        // Filtered index: catalogue browsing only ever asks for active products, and roughly
        // nothing is ever asked about discontinued ones. Indexing only the rows you query keeps
        // the index a fraction of the table's size.
        builder.HasIndex(p => p.IsActive)
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_Products_Active");
    }
}

/// <summary>Maps <see cref="Customer"/>.</summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CompanyName).HasMaxLength(200).IsRequired();

        builder.Property(c => c.Email)
            .HasConversion(e => e.Value, v => EmailAddress.FromTrusted(v))
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();

        builder.HasIndex(c => c.Email).IsUnique();

        builder.Property(c => c.Tier).HasConversion<int>().IsRequired();
        builder.Property(c => c.RegisteredAtUtc).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();

        // Required here, unlike an order's shipping address: you cannot invoice a customer
        // without somewhere to send the invoice.
        builder.OwnsOne(c => c.BillingAddress, address =>
        {
            address.Property(a => a.Line1).HasColumnName("BillingAddress_Line1").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.Line2).HasColumnName("BillingAddress_Line2").HasMaxLength(Address.MaxLineLength);
            address.Property(a => a.City).HasColumnName("BillingAddress_City").HasMaxLength(100);
            address.Property(a => a.Region).HasColumnName("BillingAddress_Region").HasMaxLength(100);
            address.Property(a => a.PostalCode).HasColumnName("BillingAddress_PostalCode").HasMaxLength(20);
            address.Property(a => a.CountryCode).HasColumnName("BillingAddress_Country").HasMaxLength(2);
        });

        builder.Navigation(c => c.BillingAddress).IsRequired();

        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.DiscountRate);
    }
}
