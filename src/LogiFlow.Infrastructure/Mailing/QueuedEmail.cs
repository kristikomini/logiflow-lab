using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// One message waiting to be delivered, or the record that it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a queue built on a table, and that is a defensible choice, not a compromise.</b>
/// The alternative is a broker — RabbitMQ, Azure Service Bus, SQS — and the honest comparison
/// is: a broker gives you throughput this cannot approach, delivery to consumers in other
/// processes, and someone else's operational maturity. A table gives you transactional
/// enqueueing with the business data (see <see cref="DatabaseEmailQueue"/>), one fewer system to
/// run and monitor, and a queue you can inspect with <c>SELECT</c> during an incident.
/// </para>
/// <para>
/// For transactional mail — thousands a day, not millions an hour, and always produced by the
/// same database transaction that produced the order — the table wins on every axis that
/// matters here. Knowing <i>why</i> it wins, and at what volume the answer flips, is the part
/// worth being able to say out loud.
/// </para>
/// <para>
/// <b>The state machine is three nullable timestamps rather than a status column.</b>
/// <see cref="SentAtUtc"/>, <see cref="AbandonedAtUtc"/> and <see cref="NextAttemptUtc"/>
/// between them encode pending, in-flight, delivered and dead — and each one answers "when",
/// which a status enum cannot. "How far behind is the queue right now?" is the metric you
/// actually alert on, and it is a subtraction here and impossible with a <c>Status = 1</c>.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class QueuedEmail
{
    /// <summary>Longest error text kept on the row.</summary>
    public const int MaxErrorLength = 2000;

    /// <summary>Queues a message.</summary>
    /// <param name="id">The row's id.</param>
    /// <param name="message">What to send.</param>
    /// <param name="queuedAtUtc">Now, from the injected clock.</param>
    public QueuedEmail(Guid id, EmailMessage message, DateTimeOffset queuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(message);

        Id = id;
        Recipient = message.To.Value;
        Subject = message.Subject;
        TextBody = message.TextBody;
        HtmlBody = message.HtmlBody;
        Category = message.Category;
        DeduplicationKey = message.DeduplicationKey;
        QueuedAtUtc = queuedAtUtc;

        // Visible to the delivery worker immediately. Every later value of this field is either
        // a lease (claimed, do not touch until it expires) or a backoff (failed, try again after).
        NextAttemptUtc = queuedAtUtc;
    }

    // EF Core materialisation.
    private QueuedEmail()
    {
    }

    /// <summary>The row's id.</summary>
    public Guid Id { get; private set; }

    /// <summary>Recipient address, already normalised by <see cref="EmailAddress"/>.</summary>
    public string Recipient { get; private set; } = null!;

    /// <summary>Subject line.</summary>
    public string Subject { get; private set; } = null!;

    /// <summary>Plain-text body.</summary>
    public string TextBody { get; private set; } = null!;

    /// <summary>HTML body, or <c>null</c>.</summary>
    public string? HtmlBody { get; private set; }

    /// <summary>Coarse grouping label, e.g. <c>shipment-dispatched</c>.</summary>
    public string Category { get; private set; } = null!;

    /// <summary>Idempotency key, or <c>null</c> when duplicates are allowed.</summary>
    public string? DeduplicationKey { get; private set; }

    /// <summary>When it was queued.</summary>
    public DateTimeOffset QueuedAtUtc { get; private set; }

    /// <summary>
    /// The earliest moment a worker may claim this message.
    /// </summary>
    /// <remarks>
    /// Does triple duty: the initial "send it now", the lease that hides an in-flight message
    /// from other workers, and the backoff after a failure. One column, because all three are
    /// the same question — when is this row eligible again?
    /// </remarks>
    public DateTimeOffset NextAttemptUtc { get; private set; }

    /// <summary>When it was delivered, or <c>null</c> while it has not been.</summary>
    public DateTimeOffset? SentAtUtc { get; private set; }

    /// <summary>When it was given up on, or <c>null</c>.</summary>
    /// <remarks>
    /// The dead-letter marker. An abandoned row is never retried automatically and is never
    /// deleted by the retention sweep — a message a customer should have received and did not
    /// is evidence, and deleting evidence on a timer is how a support team ends up unable to
    /// answer "what happened to my confirmation?".
    /// </remarks>
    public DateTimeOffset? AbandonedAtUtc { get; private set; }

    /// <summary>How many delivery attempts have been made.</summary>
    /// <remarks>
    /// Incremented when a worker <i>claims</i> the row, not when it finishes with it. That is
    /// deliberate: a message whose content crashes the sending process would otherwise be
    /// claimed, kill the worker, be claimed again on restart, and repeat forever with the count
    /// still at zero. Counting the attempt up front means even a poison message runs out of
    /// lives.
    /// </remarks>
    public int AttemptCount { get; private set; }

    /// <summary>What went wrong on the last attempt, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>True when the message will never be attempted again.</summary>
    public bool IsAbandoned => AbandonedAtUtc is not null;

    /// <summary>Rebuilds the message to hand to a transport.</summary>
    /// <returns>The message as it was queued.</returns>
    /// <remarks>
    /// <see cref="EmailAddress.FromTrusted"/> rather than <c>Create</c>: the address was
    /// validated on the way in, and re-validating storage on every read is how a validation rule
    /// tightened in 2027 silently strands mail queued in 2026.
    /// </remarks>
    public EmailMessage ToMessage() =>
        EmailMessage.Create(
            EmailAddress.FromTrusted(Recipient),
            Subject,
            TextBody,
            HtmlBody,
            Category,
            DeduplicationKey);

    /// <summary>Records a successful delivery.</summary>
    /// <param name="sentAtUtc">Now.</param>
    public void MarkSent(DateTimeOffset sentAtUtc)
    {
        SentAtUtc = sentAtUtc;
        LastError = null;
    }

    /// <summary>Records a failed attempt, and schedules or abandons accordingly.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="now">Now.</param>
    /// <param name="isTransient">Whether another attempt could plausibly succeed.</param>
    /// <param name="options">Retry limits and backoff base.</param>
    /// <remarks>
    /// <para>
    /// <b>A permanent failure is abandoned immediately.</b> Retrying
    /// <c>550 mailbox does not exist</c> five times over twenty minutes produces five identical
    /// rejections and one delayed piece of bad news; the address is not going to start existing.
    /// </para>
    /// <para>
    /// <b>The backoff is exponential with jitter.</b> Exponential because a mail server that is
    /// overloaded needs less traffic, not the same traffic on a fixed timer. Jitter because
    /// without it, a hundred messages that failed together retry together forever — the
    /// thundering herd — and the spike that caused the failure reproduces itself exactly.
    /// </para>
    /// </remarks>
    public void MarkFailed(string error, DateTimeOffset now, bool isTransient, DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(error);

        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        if (!isTransient || AttemptCount >= options.MaxAttempts)
        {
            AbandonedAtUtc = now;

            // Push the eligibility far enough out that a claim query with a bug in its filter
            // still cannot pick the row up. Belt and braces: AbandonedAtUtc is the real guard.
            NextAttemptUtc = now;

            return;
        }

        NextAttemptUtc = now + BackoffFor(AttemptCount, options);
    }

    /// <summary>
    /// Exponential backoff with full jitter, capped so a long-dead server does not schedule a
    /// retry next week.
    /// </summary>
    /// <param name="attempt">Attempts made so far — 1 after the first failure.</param>
    /// <param name="options">Backoff base and limits.</param>
    /// <returns>How long to wait before the next attempt.</returns>
    /// <remarks>
    /// <para>
    /// "Full jitter" means the delay is a random value in <c>[0, exponential]</c> rather than
    /// the exponential itself. It is the variant AWS's own architecture guidance recommends,
    /// and it spreads a failed batch across the whole window instead of moving the spike.
    /// </para>
    /// <para>
    /// <b><see cref="Random.Shared"/>, not <c>new Random()</c>.</b> Constructing one per call
    /// used to seed from the clock, so instances created in the same millisecond produced
    /// identical sequences — which is exactly the herd this is trying to avoid. Modern .NET
    /// seeds randomly, but <see cref="Random.Shared"/> is also thread-safe and allocation-free,
    /// and this runs on a background worker.
    /// </para>
    /// </remarks>
    internal static TimeSpan BackoffFor(int attempt, DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Shift rather than Math.Pow: integral, exact, and it cannot overflow to infinity.
        // Clamped at 2^10 because MaxAttempts is capped at 20 and 2^19 seconds is six days.
        int exponent = Math.Min(Math.Max(attempt - 1, 0), 10);
        double seconds = options.RetryBaseSeconds * (1 << exponent);

        // One hour is long enough that an outage does not cost attempts, and short enough that
        // recovery is measured in minutes rather than in the next working day.
        seconds = Math.Min(seconds, TimeSpan.FromHours(1).TotalSeconds);

        // Never zero: a "retry immediately" schedule busy-loops the worker against a server that
        // has just told it to slow down.
        return TimeSpan.FromSeconds(Math.Max(1, Random.Shared.NextDouble() * seconds));
    }
}
