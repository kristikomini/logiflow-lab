using LogiFlow.Application.Abstractions.Automation;
using LogiFlow.Domain.Automation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Wcs;

/// <summary>
/// Writes the dispatcher's changed orders to storage, off the dispatch loop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why persistence is not done inside the loop.</b> The dispatcher runs one single-threaded
/// pass over the whole zone table, and every millisecond it spends is a millisecond no vehicle is
/// being dispatched. A database round trip inside that loop puts a network call on the critical
/// path of a warehouse — so the loop marks orders dirty and returns, and this drains them.
/// </para>
/// <para>
/// <b>The consequence, stated rather than hidden:</b> a crash can lose up to one interval of
/// order-state changes. That is the right trade here and it is worth being able to defend. The
/// authority on where a pallet is has never been this database — it is the floor, which is why
/// <see cref="TransportDispatcher.Restore"/> throws away in-flight orders rather than trusting
/// them. Losing a second of writes to something that is already not authoritative costs a
/// cancellation notice; blocking the loop on a slow disk costs throughput on every pass.
/// </para>
/// <para>
/// If that trade ever stops being acceptable — a regulated plant where every dispatch must be
/// journalled before it happens — the answer is not to move the write into the loop. It is to
/// write ahead of the command, in the same shape as the outbox in module 06.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/01-the-boundary.md</c>
/// </remarks>
/// <param name="dispatcher">The loop whose changes are being saved.</param>
/// <param name="store">Durable storage. Opens its own scope per call.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">Logger.</param>
public sealed class TransportOrderPersister(
    TransportDispatcher dispatcher,
    ITransportOrderStore store,
    TimeProvider timeProvider,
    ILogger<TransportOrderPersister> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await FlushAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(FlushInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            // One last drain on the way out, with a fresh token: the stopping token is already
            // cancelled here, and passing it would guarantee the final batch is thrown away —
            // which is the shutdown every deploy performs, so it would not be a rare loss.
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        IReadOnlyCollection<TransportOrder> batch = dispatcher.DrainDirty();

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await store.SaveAsync(batch, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Deliberately not requeued. A batch that failed to save is stale by the time a retry
            // would run — the loop has moved those orders on — and re-saving an old snapshot over
            // a newer one is worse than the gap. The next change to each order writes the current
            // state anyway, so the system is self-correcting; the log line is what makes the gap
            // visible in the meantime.
            logger.LogError(e, "Could not save {Count} transport order(s); their state will be written on the next change", batch.Count);
        }
    }
}
