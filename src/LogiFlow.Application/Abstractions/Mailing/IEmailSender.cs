using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Abstractions.Mailing;

/// <summary>
/// A mail transport: hands one message to whatever actually delivers it, right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the seam, and it is deliberately the smallest one that can exist.</b> One method,
/// one message, no retry policy, no queueing, no templates. Everything else in the mailing
/// system is built <i>around</i> this interface rather than inside it, which is why the SMTP
/// implementation is a hundred lines and the development one is ten.
/// </para>
/// <para>
/// <b>Do not inject this into a feature.</b> Sending inline means an SMTP round trip inside
/// whatever transaction the caller has open, and a message that has already left when that
/// transaction rolls back. Application code enqueues through <see cref="IEmailQueue"/>; the
/// background delivery worker is what calls this. The one place in this codebase that still
/// calls it directly — <c>SendConfirmationOnOrderSubmitted</c> — is kept wrong on purpose and
/// labelled as such, because the contrast is the lesson.
/// </para>
/// <para>
/// <b>Implementations must throw, not swallow.</b> A transport that logs a failure and returns
/// normally tells the queue the message was delivered, and the row is marked sent. Throw
/// <see cref="EmailDeliveryException"/> — saying whether the failure is worth retrying — and
/// the worker can do the right thing.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public interface IEmailSender
{
    /// <summary>Delivers one message, or throws.</summary>
    /// <param name="message">What to send.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>A task that completes when the message has been accepted by the transport.</returns>
    /// <exception cref="EmailDeliveryException">Delivery failed.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Convenience overloads over <see cref="IEmailSender"/>.</summary>
/// <remarks>
/// <para>
/// <b>Why these are extension methods rather than interface members.</b> Every method on an
/// interface is a method each implementation must write, and each test double must stub. There
/// are four transports here; a three-string convenience overload on the interface would be the
/// same four lines of delegation written four times, plus a fifth in every mock. As an
/// extension it exists once and cannot be implemented inconsistently.
/// </para>
/// <para>
/// This is the same reasoning behind <c>LINQ</c>: <c>IEnumerable&lt;T&gt;</c> has one method,
/// and the other two hundred are extensions. Default interface members (C# 8) are the modern
/// alternative and are the right tool when you are adding to an interface others already
/// implement — but for new code an extension keeps the contract honest about what an
/// implementer actually owes you.
/// </para>
/// </remarks>
public static class EmailSenderExtensions
{
    /// <summary>Sends a plain-text message to a raw address.</summary>
    /// <param name="sender">The transport.</param>
    /// <param name="to">Recipient address; validated here.</param>
    /// <param name="subject">Subject line.</param>
    /// <param name="body">Plain-text body.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>A task that completes when the message has been accepted by the transport.</returns>
    /// <exception cref="ArgumentException"><paramref name="to"/> is not a valid address.</exception>
    public static Task SendAsync(
        this IEmailSender sender,
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sender);

        Domain.Results.Result<EmailAddress> address = EmailAddress.Create(to);

        if (address.IsFailure)
        {
            // An unparseable recipient is a programming error by the time it reaches a
            // transport: the address came out of a Customer aggregate that validated it on the
            // way in. Failing loudly beats sending to nobody and logging about it.
            throw new ArgumentException(address.Error.Description, nameof(to));
        }

        return sender.SendAsync(
            EmailMessage.Create(address.Value, subject, body),
            cancellationToken);
    }
}
