using System.Text.Json;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Common;
using LogiFlow.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LogiFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Dispatches domain events and writes outbox rows as part of <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an interceptor rather than calling this from the handlers?</b> Because it would have
/// to be called from <i>every</i> handler, and the one someone forgets is the one whose events
/// silently never fire. An interceptor is impossible to bypass — every save goes through it,
/// including saves from code written next year by someone who has never read this file.
/// </para>
/// <para>
/// <b>SavingChanges, not SavedChanges — and this is the subtle part.</b> The events are
/// dispatched <i>before</i> the database write completes, which means:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Handlers run inside the same transaction. If one throws, the whole save rolls back —
///     so an order cannot be submitted without its stock being reserved.
///   </description></item>
///   <item><description>
///     Handlers can make further changes to tracked entities and those changes are included in
///     the same save. That is why <c>ReserveStockOnOrderSubmitted</c> can call
///     <c>warehouse.Reserve(...)</c> and simply return.
///   </description></item>
/// </list>
/// <para>
/// The cost is that a slow handler holds the transaction open. Anything slow or external
/// (email, HTTP, a message broker) must go through the outbox instead, which is why both
/// mechanisms exist here.
/// </para>
/// <para>
/// <b>The re-entrancy trap.</b> Handlers modify entities, which can raise further events. This
/// implementation drains events in a loop with a hard iteration cap rather than recursing, so a
/// handler that raises the event it handles produces a clear exception instead of a
/// <see cref="StackOverflowException"/> — which cannot be caught and takes the process down.
/// </para>
/// Covered in: <c>course/module-06-efcore/08-interceptors.md</c>
/// </remarks>
/// <param name="dispatcher">Publishes events to their handlers.</param>
public sealed class DomainEventDispatchingInterceptor(IDispatcher dispatcher) : SaveChangesInterceptor
{
    /// <summary>Guard against a handler that raises the event it handles.</summary>
    private const int MaxDispatchRounds = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is not null)
        {
            await DispatchAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchAsync(DbContext context, CancellationToken cancellationToken)
    {
        for (int round = 0; round < MaxDispatchRounds; round++)
        {
            // Re-read the tracked aggregates every round: a handler may have loaded and modified
            // an aggregate that was not tracked when the previous round started.
            List<IHasDomainEvents> aggregates = [.. context.ChangeTracker
                .Entries()
                .Select(e => e.Entity)
                .OfType<IHasDomainEvents>()
                .Where(a => a.DomainEvents.Count > 0)];

            if (aggregates.Count == 0)
            {
                return;
            }

            List<IDomainEvent> events = [.. aggregates.SelectMany(a => a.DomainEvents)];

            // Clear BEFORE dispatching. If a handler raises a new event on the same aggregate,
            // clearing afterwards would wipe it out unfired - a bug that only shows up in the
            // one workflow where handlers chain, and is miserable to track down.
            foreach (IHasDomainEvents aggregate in aggregates)
            {
                aggregate.ClearDomainEvents();
            }

            foreach (IDomainEvent domainEvent in events)
            {
                // In-process handlers first: these run inside the transaction and may abort it.
                await dispatcher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);

                // Then the outbox row, for anything that must reach the outside world. Written
                // through the same DbContext, so it commits atomically with the state change -
                // that is the entire point of the pattern.
                context.Set<OutboxMessage>().Add(new OutboxMessage(
                    domainEvent.EventId,
                    domainEvent.GetType().FullName!,
                    JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions),
                    domainEvent.OccurredAtUtc));
            }
        }

        throw new InvalidOperationException(
            $"Domain event dispatch did not settle after {MaxDispatchRounds} rounds. "
            + "A handler is almost certainly raising an event that re-triggers itself.");
    }
}
