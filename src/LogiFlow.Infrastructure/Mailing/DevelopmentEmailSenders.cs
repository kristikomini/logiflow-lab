using System.Globalization;
using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Writes the message to the log and delivers nothing. The default transport.
/// </summary>
/// <remarks>
/// <para>
/// <b>A null object, and the right default.</b> Clone this repository, run it, submit an order,
/// and you see the confirmation in the console — with no SMTP server, no container, no
/// credentials and no possibility of mailing a stranger. Every alternative default either fails
/// at startup or sends real mail, and both are worse first impressions than a log line.
/// </para>
/// <para>
/// <b>It logs the body, which in production would be a GDPR problem.</b> Customer names and
/// addresses in a log aggregator are personal data in a system that was never designed to hold
/// it, with its own retention rules and its own access list. That is exactly why this transport
/// is the development default and never the production one — the same reasoning as
/// <c>EnableSensitiveDataLogging</c> being guarded by an environment check in
/// <c>DependencyInjection</c>.
/// </para>
/// </remarks>
/// <param name="logger">Logger.</param>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "EMAIL (not sent) [{Category}] to {Recipient}\nSubject: {Subject}\n\n{Body}",
            message.Category,
            message.To.Value,
            message.Subject,
            message.TextBody);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Writes each message to disk as an <c>.eml</c> file instead of sending it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The transport worth actually using while building templates.</b> A <c>.eml</c> file is the
/// real RFC 5322 message — the same bytes SMTP would carry — and double-clicking one opens it in
/// Outlook, Thunderbird or Windows Mail exactly as the customer would see it, HTML rendering,
/// encoded headers and all. A log line cannot tell you that your table renders as a wall of text
/// in Outlook. This can.
/// </para>
/// <para>
/// <b>It is the direct descendant of a feature the framework used to have:</b>
/// <c>&lt;smtp deliveryMethod="SpecifiedPickupDirectory"&gt;</c> in <c>web.config</c>, which is
/// how everyone tested mail on .NET Framework. It did not survive the move to .NET Core, and
/// rebuilding it is fifteen lines.
/// </para>
/// </remarks>
/// <param name="options">Sender identity and the pickup directory.</param>
/// <param name="clock">Supplies the timestamp in the file name.</param>
/// <param name="logger">Logger.</param>
internal sealed class FileSystemEmailSender(
    IOptions<MailingOptions> options,
    IDateTimeProvider clock,
    ILogger<FileSystemEmailSender> logger)
    : IEmailSender
{
    private readonly MailingOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string directory = Path.GetFullPath(_options.PickupDirectory);

        try
        {
            // Creating it every time rather than once at startup: the directory can be deleted
            // while the process runs (a `git clean`, a colleague tidying up), and a transport
            // that then fails forever until a restart is a worse trade than one syscall.
            Directory.CreateDirectory(directory);

            // Sortable, unique, and free of personal data. Putting the recipient in the file
            // name would be convenient and would also scatter customer addresses across a
            // directory listing, a backup and any screenshot of it.
            string fileName = string.Create(
                CultureInfo.InvariantCulture,
                $"{clock.UtcNow:yyyyMMdd-HHmmss-fff}-{message.Category}-{Guid.CreateVersion7():N}.eml");

            string path = Path.Combine(directory, fileName);

            MimeMessage mime = MimeMessageFactory.Create(message, _options);

            await mime.WriteToAsync(path, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Wrote {Category} email for {Recipient} to {Path}",
                message.Category,
                message.To.Value,
                path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk or a permission problem is genuinely transient - somebody frees space,
            // somebody fixes the ACL - so the message stays in the queue and is retried rather
            // than being thrown away.
            throw new EmailDeliveryException(
                $"Could not write the message to '{directory}'.",
                isTransient: true,
                ex);
        }
    }
}
