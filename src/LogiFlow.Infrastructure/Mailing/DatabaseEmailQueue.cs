using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Queues mail as a row in the application's own database, inside the caller's transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one line that matters is the one that is missing:</b> there is no
/// <c>SaveChangesAsync</c> in this class. The row is <i>added</i> to the same
/// <see cref="LogiFlowDbContext"/> the caller is using, and it is committed — or rolled back —
/// by whatever unit of work the caller is inside. That is the entire mechanism. An order that
/// fails to save cannot leave a confirmation behind, and an order that saves cannot fail to
/// queue one.
/// </para>
/// <para>
/// <b>Why not a separate DbContext for mail?</b> Because it would be a second transaction, and
/// two transactions cannot commit atomically — which is the dual-write problem the outbox exists
/// to avoid, reintroduced by an architecture diagram that looked tidier.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
/// <param name="context">The caller's session. Scoped, and shared with the use case.</param>
/// <param name="clock">Supplies the queue timestamp.</param>
/// <param name="logger">Logger.</param>
internal sealed class DatabaseEmailQueue(
    LogiFlowDbContext context,
    IDateTimeProvider clock,
    ILogger<DatabaseEmailQueue> logger) : IEmailQueue
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The duplicate check here is an optimisation; the unique index is the guarantee.</b>
    /// Check-then-act across a network is always racy — two transactions can both read "not
    /// present" before either writes. What this method avoids is the <i>common</i> case, a
    /// redelivered event arriving seconds or minutes after the first, where a cheap
    /// <c>EXISTS</c> saves a pointless insert and a rolled-back transaction.
    /// </para>
    /// <para>
    /// When two genuinely concurrent transactions do collide, the unique index rejects the
    /// second and its <c>SaveChangesAsync</c> throws. That failure propagates and rolls back the
    /// business change too, which sounds severe until you notice what it means: the same
    /// business fact was being written twice at once, and exactly one of those writes should
    /// win. The loser retries, finds the row, and skips — which is the outcome you wanted.
    /// </para>
    /// </remarks>
    public async Task<bool> EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.DeduplicationKey is { Length: > 0 } key)
        {
            // Two places to look, and forgetting the first is a genuinely confusing bug: a
            // handler that queues the same message twice within ONE transaction has nothing in
            // the database yet, so the EXISTS below returns false both times and SaveChanges
            // fails on a unique-index violation that names neither call site. The change
            // tracker holds the pending insert; ask it first.
            bool pending = context.ChangeTracker
                .Entries<QueuedEmail>()
                .Any(entry =>
                    entry.State == EntityState.Added
                    && string.Equals(entry.Entity.DeduplicationKey, key, StringComparison.Ordinal));

            if (pending)
            {
                logger.LogDebug("Email {Key} is already queued in this transaction; skipping", key);

                return false;
            }

            bool exists = await context.QueuedEmails
                .AnyAsync(email => email.DeduplicationKey == key, cancellationToken)
                .ConfigureAwait(false);

            if (exists)
            {
                logger.LogDebug("Email {Key} has already been queued; skipping", key);

                return false;
            }
        }

        // Version 7 GUIDs are time-ordered. On a clustered primary key that is the difference
        // between appending to the end of the index and inserting into the middle of it a
        // thousand times a minute - the page splits and fragmentation that make people blame
        // "GUIDs" for a problem that is really Version 4's randomness.
        var queued = new QueuedEmail(Guid.CreateVersion7(), message, clock.UtcNow);

        // Add, not AddAsync. AddAsync exists solely for value generators that hit the database
        // to produce a key - the HiLo generator is the only one in the box that does - and using
        // it elsewhere is a pointless state machine that makes readers think an insert happens
        // here. Nothing is written until SaveChangesAsync, which is the caller's job.
        context.QueuedEmails.Add(queued);

        logger.LogDebug(
            "Queued {Category} email {EmailId} to {Recipient}",
            message.Category,
            queued.Id,
            message.To.Value);

        return true;
    }
}
