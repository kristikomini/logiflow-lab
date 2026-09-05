namespace LogiFlow.Application.Abstractions.Mailing;

/// <summary>
/// Thrown by a transport when a message could not be delivered.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole point of this type is <see cref="IsTransient"/>.</b> A delivery worker that
/// retries everything spends five attempts and twenty minutes on
/// <c>550 5.1.1 recipient does not exist</c>, which will never succeed; a worker that retries
/// nothing gives up on <c>421 4.7.0 too many connections</c>, which would have worked ten
/// seconds later. Both failures arrive at the same <c>catch</c>, so the transport — the only
/// party that understands SMTP status codes — has to say which one it was.
/// </para>
/// <para>
/// <b>The 4xx/5xx split, in one line:</b> in SMTP a 4xx reply is "not now" and a 5xx reply is
/// "not ever". <c>SmtpEmailSender</c> maps them onto this flag, and everything above it stays
/// free of any knowledge that SMTP exists — which is what lets the same worker drive a REST
/// mail API later without a rewrite.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class EmailDeliveryException : Exception
{
    /// <summary>Creates a delivery failure.</summary>
    /// <param name="message">What went wrong, for the log and the queue row.</param>
    /// <param name="isTransient">
    /// <c>true</c> when another attempt could plausibly succeed (timeout, connection refused,
    /// SMTP 4xx); <c>false</c> when it cannot (bad address, message rejected, SMTP 5xx).
    /// </param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public EmailDeliveryException(string message, bool isTransient, Exception? innerException = null)
        : base(message, innerException) => IsTransient = isTransient;

    /// <summary>
    /// Required by exception design guidelines (CA1032). Defaults to transient, because
    /// retrying a message that cannot succeed wastes attempts, while not retrying one that
    /// could loses a customer's email — and only one of those is recoverable.
    /// </summary>
    public EmailDeliveryException() : base("Email delivery failed.") => IsTransient = true;

    /// <summary>Required by exception design guidelines (CA1032).</summary>
    /// <param name="message">What went wrong.</param>
    public EmailDeliveryException(string message) : base(message) => IsTransient = true;

    /// <summary>Required by exception design guidelines (CA1032).</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException) => IsTransient = true;

    /// <summary>Whether another attempt could plausibly succeed.</summary>
    public bool IsTransient { get; }
}
