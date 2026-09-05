namespace LogiFlow.Infrastructure.Persistence.Outbox;

/// <summary>
/// A message queued for delivery outside the current transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem the outbox solves — the dual-write problem.</b> Consider submitting an order
/// that must also publish a message to a broker:
/// </para>
/// <code>
/// await db.SaveChangesAsync();          // 1. commits
/// await bus.PublishAsync(orderPlaced);  // 2. process dies here
/// </code>
/// <para>
/// The order exists and nobody was told. Swap the order of the two calls and you get the
/// mirror-image bug: the message goes out for an order that was never committed. There is no
/// ordering of two separate systems that is safe, because you cannot commit both atomically.
/// </para>
/// <para>
/// <b>The outbox makes it one write.</b> The message is inserted as a row in <i>the same
/// transaction</i> as the state change. Either both land or neither does. A background worker
/// (<c>OutboxProcessor</c>) then reads unprocessed rows and publishes them, marking each done.
/// </para>
/// <para>
/// <b>The guarantee you get is at-least-once, not exactly-once.</b> If the process dies after
/// publishing but before marking the row processed, it publishes again on restart. Exactly-once
/// delivery is impossible in a distributed system; the practical answer is at-least-once
/// delivery plus idempotent consumers — which is why <see cref="Id"/> is carried on the message
/// as a deduplication key.
/// </para>
/// Covered in: <c>course/module-06-efcore/07-outbox-pattern.md</c>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Creates a queued message.</summary>
    /// <param name="id">Unique id, also the consumer's idempotency key.</param>
    /// <param name="type">Assembly-qualified name of the event type.</param>
    /// <param name="content">JSON payload.</param>
    /// <param name="occurredAtUtc">When the fact happened.</param>
    public OutboxMessage(Guid id, string type, string content, DateTimeOffset occurredAtUtc)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredAtUtc = occurredAtUtc;
    }

    // EF Core materialisation.
    private OutboxMessage()
    {
    }

    /// <summary>Unique id. Consumers use it to detect a redelivery.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The event's type name, used to deserialise <see cref="Content"/> back into an object.
    /// </summary>
    /// <remarks>
    /// <b>A security note that costs people dearly:</b> never feed this straight into
    /// <c>Type.GetType(x)</c> and deserialise. If an attacker can influence the value, they can
    /// name a gadget type whose constructor or setters do something dangerous — this is the
    /// classic insecure-deserialisation vulnerability. <c>OutboxProcessor</c> resolves types
    /// against an allow-list built from the Domain assembly.
    /// </remarks>
    public string Type { get; private set; } = null!;

    /// <summary>The serialised event payload.</summary>
    public string Content { get; private set; } = null!;

    /// <summary>When the originating fact occurred.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>When it was successfully published, or <c>null</c> while pending.</summary>
    /// <remarks>
    /// Nullable rather than a boolean flag, because "when" answers strictly more questions than
    /// "whether" — including "how far behind is the processor right now?", which is the metric
    /// you actually want to alert on.
    /// </remarks>
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    /// <summary>The failure from the last attempt, or <c>null</c>.</summary>
    public string? Error { get; private set; }

    /// <summary>How many delivery attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Marks the message delivered.</summary>
    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
        Error = null;
    }

    /// <summary>Records a failed attempt so the processor can back off or give up.</summary>
    public void MarkFailed(string error)
    {
        AttemptCount++;

        // Truncated because a full stack trace can run to tens of kilobytes, and a poison
        // message retried a thousand times would otherwise bloat the table faster than the
        // orders it describes.
        Error = error.Length > 4000 ? error[..4000] : error;
    }
}
