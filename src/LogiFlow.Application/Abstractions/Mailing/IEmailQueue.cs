namespace LogiFlow.Application.Abstractions.Mailing;

/// <summary>
/// Accepts a message for later delivery, as part of whatever transaction the caller is in.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the outbox pattern, applied to email.</b> Queueing writes a row through the same
/// unit of work as the business change that caused it, so the two commit or roll back together.
/// The customer cannot receive a confirmation for an order that failed to save, and an order
/// that saved cannot silently fail to notify — the two facts are one write.
/// </para>
/// <para>
/// <b>It does not save.</b> No <c>SaveChangesAsync</c> here, on purpose: the caller owns the
/// transaction boundary, and a queue that committed on its own would defeat the guarantee it
/// exists to provide. Enqueue, then let the surrounding unit of work commit.
/// </para>
/// <para>
/// <b>What you give up.</b> Delivery is no longer immediate — it happens on the delivery
/// worker's next tick, a few seconds later. For transactional mail that is invisible. For a
/// one-time password it is not, and the answer there is not to send it synchronously but to
/// shorten the poll or wake the worker on write, because the durability is the part you cannot
/// bolt on afterwards.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-06-efcore/07-outbox-pattern.md</c>
/// </remarks>
public interface IEmailQueue
{
    /// <summary>Adds a message to the queue, within the caller's transaction.</summary>
    /// <param name="message">What to send.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// <c>true</c> when the message was queued; <c>false</c> when
    /// <see cref="EmailMessage.DeduplicationKey"/> matched a message that is already queued or
    /// sent, and this one was therefore dropped.
    /// </returns>
    /// <remarks>
    /// The <c>false</c> return is information, not a failure: it is the mechanism working. Log
    /// it at Debug and carry on — a caller that treats "already queued" as an error will retry
    /// forever against a duplicate that is behaving exactly as designed.
    /// </remarks>
    Task<bool> EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
