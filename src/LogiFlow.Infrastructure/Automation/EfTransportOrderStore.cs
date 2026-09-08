using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Infrastructure.Automation;

/// <summary>
/// Stores transport orders in the application's own database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the same database rather than one of its own.</b> A warehouse control system is a
/// separate <i>process</i> — it must keep dispatching when the web application recycles — but it
/// is not a separate <i>system</i>: the transport orders it executes come from the same business
/// that owns the orders being fulfilled, and the day somebody asks "which pallet moves belong to
/// this customer order" a join is the honest answer. Splitting the store would buy independent
/// deployment nobody needs and cost a distributed query somebody will.
/// </para>
/// <para>
/// <b>Not tracked on read.</b> Restored orders are re-attached explicitly on save rather than
/// held in a long-lived context, because the dispatcher's loop runs for weeks and a change
/// tracker that never gets scoped away is a memory leak with an ORM in front of it.
/// </para>
/// Covered in: <c>course/module-06-efcore/01-dbcontext-and-change-tracking.md</c>
/// </remarks>
/// <param name="context">The application's context.</param>
public sealed class EfTransportOrderStore(LogiFlowDbContext context) : ITransportOrderStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TransportOrder>> LoadOpenAsync(CancellationToken ct)
    {
        return await context.TransportOrders
            .AsNoTracking()
            .Where(o => o.Status == TransportOrderStatus.Pending || o.Status == TransportOrderStatus.Assigned)
            .OrderBy(o => o.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyCollection<TransportOrder> orders, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orders);

        if (orders.Count == 0)
        {
            return;
        }

        // The dispatcher does not know which of its orders the database has already seen — it
        // holds domain objects, not rows — so existence is asked once for the whole batch rather
        // than once per order. One round trip instead of N, which matters because this runs
        // continuously.
        List<TransportOrderId> ids = [.. orders.Select(o => o.Id)];

        HashSet<TransportOrderId> known =
        [
            .. await context.TransportOrders
                .AsNoTracking()
                .Where(o => ids.Contains(o.Id))
                .Select(o => o.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false),
        ];

        foreach (TransportOrder order in orders)
        {
            if (known.Contains(order.Id))
            {
                context.Entry(order).State = EntityState.Modified;
            }
            else
            {
                context.TransportOrders.Add(order);
            }
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        // Detach everything again. This context belongs to a scope the caller creates per batch,
        // but being explicit costs nothing and makes the intent obvious to whoever reuses this.
        context.ChangeTracker.Clear();
    }
}
