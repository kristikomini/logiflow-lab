using LogiFlow.Domain.Automation;

namespace LogiFlow.Application.Abstractions.Automation;

/// <summary>
/// Durable storage for transport orders, so a warehouse control system that restarts knows what
/// it had been asked to do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transport orders are stored and telemetry is not.</b> An order is a <i>decision</i> — the
/// warehouse was asked to move something and somebody is waiting for the answer — so it belongs on
/// the durable path with everything else the domain decides. Tag samples are observations arriving
/// tens of times a second, and putting them through change tracking and <c>SaveChanges</c> is how
/// a context ends up holding a million entities. They go down a separate, cheaper path with their
/// own retention.
/// </para>
/// <para>
/// <b>Equipment is deliberately not stored either.</b> The fleet comes from the gateway, which
/// gets it from commissioning data, and a second copy in a table is a second thing to be wrong.
/// Machine <i>state</i> is telemetry by another name.
/// </para>
/// <para>
/// <b>What persistence does not solve.</b> Restoring the orders is the easy half. The zone table
/// is the hard one and it is not in here on purpose: after a crash the vehicles are physically
/// where they are, so occupancy has to be rebuilt from what the machines report — see
/// <see cref="ZoneAllocator.RebuildFromFloor"/>. An allocator restored from a table this process
/// wrote before it died will route a second vehicle into an occupied aisle, and be confident
/// about it.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
public interface ITransportOrderStore
{
    /// <summary>
    /// Every order that had not finished, oldest first.
    /// </summary>
    /// <remarks>
    /// Returns both <see cref="TransportOrderStatus.Pending"/> and
    /// <see cref="TransportOrderStatus.Assigned"/> orders, and the caller decides what each one
    /// deserves — which is not the same answer for the two. See the restore rule in the
    /// dispatcher.
    /// </remarks>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<TransportOrder>> LoadOpenAsync(CancellationToken ct);

    /// <summary>Inserts new orders and updates ones already stored.</summary>
    /// <param name="orders">The orders that changed since the last save.</param>
    /// <param name="ct">Cancellation.</param>
    Task SaveAsync(IReadOnlyCollection<TransportOrder> orders, CancellationToken ct);
}
