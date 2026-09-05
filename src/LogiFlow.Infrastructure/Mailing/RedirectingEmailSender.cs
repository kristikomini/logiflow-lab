using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Wraps a transport and stops it mailing people it should not.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class exists because of one recurring production incident.</b> Somebody restores a
/// copy of the production database into staging to reproduce a bug. Staging's SMTP settings
/// point at a real relay, because they were copied from production too. A test run, or a
/// replayed queue, or an integration test suite, then emails several thousand real customers
/// that their order has been cancelled. It happens somewhere every month, it is always
/// discovered by the customers, and it is always preventable by exactly this much code.
/// </para>
/// <para>
/// <b>It is a decorator, and that is the point of the shape.</b> Neither
/// <see cref="SmtpEmailSender"/> nor <see cref="FileSystemEmailSender"/> knows this exists;
/// neither had to change to gain the protection; and a fifth transport added next year gets it
/// for free. The alternative — an <c>if (isProduction)</c> inside each transport — is the same
/// check written four times, and the fourth one is the one somebody forgets.
/// </para>
/// <para>
/// <b>Two independent guards, because they answer different questions.</b>
/// <c>Mailing:RedirectAllTo</c> answers "where should mail go instead?" and keeps the message
/// visible to whoever is testing. <c>Mailing:AllowedRecipientDomains</c> answers "who may we
/// mail at all?" and is useful on its own in an environment that should be able to reach the QA
/// team's own addresses and nobody else's.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-26-patterns-and-solid/</c> (decorator)
/// </remarks>
/// <param name="inner">The real transport.</param>
/// <param name="options">Redirect and allow-list settings.</param>
/// <param name="logger">Logger.</param>
internal sealed class RedirectingEmailSender(
    IEmailSender inner,
    IOptions<MailingOptions> options,
    ILogger<RedirectingEmailSender> logger) : IEmailSender
{
    private readonly MailingOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(inner);

        if (_options.RedirectAllTo is { Length: > 0 } redirect)
        {
            // Validated at startup by MailingOptionsValidator, so FromTrusted is safe here and
            // a malformed value cannot reach this line.
            EmailMessage redirected = message
                .RedirectedTo(EmailAddress.FromTrusted(redirect.Trim().ToLowerInvariant()));

            // The original recipient goes in the subject rather than only in a header, because
            // the person testing is looking at an inbox list, not at message source. Twenty
            // messages that all say "Your order has shipped" are indistinguishable without it.
            EmailMessage tagged = EmailMessage.Create(
                redirected.To,
                $"[to: {message.To.Value}] {message.Subject}",
                redirected.TextBody,
                redirected.HtmlBody,
                redirected.Category,
                redirected.DeduplicationKey);

            logger.LogInformation(
                "Redirecting {Category} email for {OriginalRecipient} to {Recipient}",
                message.Category,
                message.To.Value,
                redirect);

            return inner.SendAsync(tagged, cancellationToken);
        }

        if (!IsAllowed(message.To))
        {
            // Dropped, not failed. Throwing would make the delivery worker retry a message that
            // policy says must never be sent, five times, and then dead-letter it as if
            // something had gone wrong. Nothing went wrong: the guard did its job.
            logger.LogWarning(
                "Dropped {Category} email to {Recipient}: domain not in Mailing:AllowedRecipientDomains",
                message.Category,
                message.To.Value);

            return Task.CompletedTask;
        }

        return inner.SendAsync(message, cancellationToken);
    }

    private bool IsAllowed(EmailAddress recipient) =>
        _options.AllowedRecipientDomains.Count == 0
        || _options.AllowedRecipientDomains.Any(domain =>
            string.Equals(domain.Trim().TrimStart('@'), recipient.Domain, StringComparison.OrdinalIgnoreCase));
}
