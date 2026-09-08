using LogiFlow.Domain.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TransportOrder"/> — the one thing the warehouse control system keeps.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only this aggregate is persisted from the machine layer, and the omissions are the
/// interesting part.</b> <c>Equipment</c> is not stored: the fleet comes from the gateway's
/// commissioning data, and machine state is telemetry, which belongs on a cheaper path with its
/// own retention rather than in a change-tracked context. Zone occupancy is not stored either,
/// and must not be — after a crash the vehicles are physically where they are, so the allocator
/// rebuilds from what the floor reports rather than from anything this process last wrote.
/// </para>
/// <para>
/// <b>The zone names are plain strings.</b> They come from a customer's floor plan, they change
/// when an aisle is re-racked, and giving them a table would buy referential integrity over data
/// whose authority lives outside this system entirely.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/04-traffic-and-deadlock.md</c>
/// </remarks>
public sealed class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TransportOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TransportOrders");
        builder.HasKey(o => o.Id);

        // The label on the physical thing — a pallet, a tote, an UDC. Whatever the customer's
        // system already calls it, which is why it is not a value object with a format rule.
        builder.Property(o => o.LoadUnit)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.FromZone).HasMaxLength(32).IsRequired();
        builder.Property(o => o.ToZone).HasMaxLength(32).IsRequired();

        // Stored as the integer, not the name. A renamed enum member must not silently become a
        // different status in rows written last year.
        builder.Property(o => o.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(o => o.AssignedTo)
            .HasConversion(
                id => id!.Value.Value,
                value => EquipmentId.From(value));

        builder.Property(o => o.CreatedAtUtc).IsRequired();

        // Same optimistic-concurrency token as every other aggregate here. It matters more than
        // it looks: a WCS is restarted while the old process may still be finishing a write, and
        // two instances briefly overlapping is the normal shape of a deploy. Without this the
        // later write silently wins and an order can be resurrected after being completed.
        builder.Property(o => o.RowVersion).IsRowVersion();

        // The index the restart path runs: "everything that had not finished". Filtered, because
        // completed orders are the overwhelming majority within a week of go-live and there is no
        // reason to carry them in an index that only ever reads the open ones.
        builder.HasIndex(o => o.Status)
            .HasFilter("[Status] IN (0, 1)")
            .HasDatabaseName("IX_TransportOrders_Open");
    }
}
