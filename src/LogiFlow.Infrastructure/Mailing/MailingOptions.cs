using System.ComponentModel.DataAnnotations;
using LogiFlow.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>Which transport actually delivers a message.</summary>
public enum EmailTransport
{
    /// <summary>Write the message to the log and drop it. The default, and the safest.</summary>
    Log = 0,

    /// <summary>Write the message to disk as a <c>.eml</c> file. Openable in a real mail client.</summary>
    File = 1,

    /// <summary>Send it over SMTP. The only one that reaches a human.</summary>
    Smtp = 2,
}

/// <summary>
/// Everything the mailing system reads from configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default is <see cref="EmailTransport.Log"/>, and that is a security decision.</b> A
/// missing configuration section must not produce a system that mails strangers. Every default
/// here answers the question "what should happen if somebody clones this repository and presses
/// run?" — and the answer is always "something visible and harmless".
/// </para>
/// <para>
/// <b>Options pattern, not <c>IConfiguration</c> injected everywhere.</b> A class that takes
/// <c>IConfiguration</c> and reads <c>["Mailing:Smtp:Host"]</c> has no schema, no validation, no
/// discoverability, and turns a typo into <c>null</c> at 3am. <see cref="IOptions{TOptions}"/>
/// binds once, validates once, and gives every consumer a typed object.
/// </para>
/// <para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-10-cross-cutting/01-dependency-injection.md</c>
/// </para>
/// </remarks>
public sealed class MailingOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Mailing";

    /// <summary>Which transport to use. Defaults to <see cref="EmailTransport.Log"/>.</summary>
    public EmailTransport Transport { get; set; } = EmailTransport.Log;

    /// <summary>The envelope sender. Must be a valid address.</summary>
    /// <remarks>
    /// <b>Not just any address you like.</b> Deliverability depends on the domain here having
    /// SPF, DKIM and DMARC records that authorise your sending host. Getting this wrong is the
    /// single most common reason transactional mail lands in spam, and no amount of C# fixes
    /// it — it is three DNS records. Keep the address on a domain you control.
    /// </remarks>
    [Required]
    public string FromAddress { get; set; } = "no-reply@logiflow.example";

    /// <summary>The display name shown beside the address.</summary>
    public string FromName { get; set; } = "LogiFlow";

    /// <summary>Where replies should go, or <c>null</c> to leave the header off.</summary>
    /// <remarks>
    /// A <c>no-reply</c> sender with no <c>Reply-To</c> tells a customer with a genuine question
    /// to go away. Setting this to a monitored mailbox costs nothing and is the difference
    /// between a support channel and a wall.
    /// </remarks>
    public string? ReplyToAddress { get; set; }

    /// <summary>SMTP connection settings. Used only when <see cref="Transport"/> is Smtp.</summary>
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Where <see cref="EmailTransport.File"/> writes its <c>.eml</c> files.</summary>
    /// <remarks>
    /// Relative paths resolve against the content root, so the default lands inside the running
    /// project rather than in whatever directory the process happened to start in.
    /// </remarks>
    public string PickupDirectory { get; set; } = "App_Data/mail";

    /// <summary>
    /// When set, every message is re-addressed here instead of to its real recipient.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This exists because of a specific, extremely common production incident:</b> somebody
    /// restores a copy of the production database into staging to reproduce a bug, staging is
    /// pointed at a real SMTP server "temporarily", and a test run emails four thousand real
    /// customers that their order has been cancelled. It happens somewhere every month.
    /// </para>
    /// <para>
    /// Setting this in every non-production environment makes that impossible rather than
    /// unlikely. The original recipient is preserved in the subject and in a header, so testers
    /// can still see who each message was for.
    /// </para>
    /// </remarks>
    public string? RedirectAllTo { get; set; }

    /// <summary>
    /// When non-empty, only recipients in these domains are delivered to; anything else is
    /// dropped with a warning.
    /// </summary>
    /// <remarks>
    /// The belt to <see cref="RedirectAllTo"/>'s braces, and useful on its own in a staging
    /// environment that should be able to mail the QA team but nobody else. Compared
    /// case-insensitively, without the <c>@</c>: <c>logiflow.example</c>, not
    /// <c>@logiflow.example</c>.
    /// </remarks>
    public IList<string> AllowedRecipientDomains { get; } = [];

    /// <summary>How the background worker drains the queue.</summary>
    public DeliveryOptions Delivery { get; set; } = new();
}

/// <summary>SMTP connection settings.</summary>
public sealed class SmtpOptions
{
    /// <summary>Host name of the SMTP server.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Port. 1025 is the convention for local capture servers such as Mailpit.</summary>
    /// <remarks>
    /// The three ports worth knowing: <b>25</b> is server-to-server relay and is blocked
    /// outbound by essentially every cloud provider and consumer ISP; <b>587</b> is submission
    /// with STARTTLS and is what you almost always want; <b>465</b> is implicit TLS, deprecated
    /// in 1998, un-deprecated by RFC 8314 in 2018, and still widely used.
    /// </remarks>
    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Whether to require TLS. Off by default because local capture servers have none.</summary>
    /// <remarks>
    /// <b>Turn this on for anything that leaves the machine.</b> Without it, credentials and the
    /// full message cross the network in clear text. It is off by default only because the
    /// development transport of choice — a container that captures mail and shows it in a web
    /// UI — does not speak TLS at all, and a default that fails to connect teaches nothing.
    /// </remarks>
    public bool UseTls { get; set; }

    /// <summary>User name, or <c>null</c> for an unauthenticated relay.</summary>
    public string? UserName { get; set; }

    /// <summary>Password.</summary>
    /// <remarks>
    /// <b>Never in appsettings.json.</b> That file is committed. Use user-secrets locally
    /// (<c>dotnet user-secrets set "Mailing:Smtp:Password" "..."</c>), and the environment
    /// variable <c>Mailing__Smtp__Password</c> or a key vault in production. The double
    /// underscore is the separator on every platform, including Linux, where a colon is not a
    /// legal environment-variable character.
    /// </remarks>
    public string? Password { get; set; }

    /// <summary>How long to wait on a connection or command before giving up.</summary>
    /// <remarks>
    /// A mail server that accepts a TCP connection and then says nothing will otherwise hold the
    /// delivery worker forever. Thirty seconds is generous for SMTP and short enough that a
    /// wedged server costs one poll interval rather than a night's mail.
    /// </remarks>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>How the background worker drains the queue.</summary>
public sealed class DeliveryOptions
{
    /// <summary>Seconds between polls of the queue table.</summary>
    [Range(1, 3600)]
    public int PollSeconds { get; set; } = 5;

    /// <summary>How many messages one poll claims.</summary>
    /// <remarks>
    /// Bounded on purpose. An unbounded "send everything pending" loop after an outage means one
    /// iteration that runs for an hour, ignores shutdown, and cannot be observed making
    /// progress. Small batches drain just as fast and stay interruptible.
    /// </remarks>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 20;

    /// <summary>How many attempts a message gets before it is abandoned.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Base delay for the exponential backoff between attempts.</summary>
    [Range(1, 3600)]
    public int RetryBaseSeconds { get; set; } = 30;

    /// <summary>
    /// How long a claimed message stays invisible to other workers while it is being sent.
    /// </summary>
    /// <remarks>
    /// This is a lease, and it is what makes the queue safe to run on two instances. If the
    /// process holding a message dies mid-send, the lease expires and another worker picks the
    /// message up. Too short and two workers send the same message; it must comfortably exceed
    /// the SMTP timeout.
    /// </remarks>
    [Range(10, 3600)]
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>How long delivered messages are kept before the worker deletes them.</summary>
    /// <remarks>
    /// <para>
    /// A queue table nobody prunes is a table that grows forever, and the index that made the
    /// "what is pending?" query fast stops helping once the table is a hundred million rows of
    /// history. Thirty days is long enough to answer "did we send it?" during a support call.
    /// </para>
    /// <para>
    /// It is also a GDPR consideration: these rows contain a customer's address and the full
    /// text of what they were told. Keeping them forever is a decision that needs a reason,
    /// and "we forgot to write a cleanup job" is not one.
    /// </para>
    /// </remarks>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Cross-field validation that data annotations cannot express.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered with <c>ValidateOnStart()</c>, which is the whole point.</b> Without it,
/// <see cref="IOptions{TOptions}"/> validates lazily — on first resolution — so a typo in the
/// SMTP host is discovered by the first customer who places an order, at whatever hour that is,
/// as a failure inside a background worker. With it, the process refuses to start and the
/// deployment fails in front of the person who caused it.
/// </para>
/// <para>
/// <b>Fail fast is not a slogan.</b> It is the difference between an error message on a
/// deployment screen and a support ticket a week later saying "we never got the email".
/// </para>
/// </remarks>
internal sealed class MailingOptionsValidator : IValidateOptions<MailingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MailingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (EmailAddress.Create(options.FromAddress).IsFailure)
        {
            failures.Add($"Mailing:FromAddress '{options.FromAddress}' is not a valid email address.");
        }

        if (options.ReplyToAddress is { Length: > 0 } replyTo && EmailAddress.Create(replyTo).IsFailure)
        {
            failures.Add($"Mailing:ReplyToAddress '{replyTo}' is not a valid email address.");
        }

        if (options.RedirectAllTo is { Length: > 0 } redirect && EmailAddress.Create(redirect).IsFailure)
        {
            failures.Add($"Mailing:RedirectAllTo '{redirect}' is not a valid email address.");
        }

        if (options.Transport == EmailTransport.Smtp && string.IsNullOrWhiteSpace(options.Smtp.Host))
        {
            failures.Add("Mailing:Smtp:Host is required when Mailing:Transport is Smtp.");
        }

        // A user name with no password is the shape of a half-finished secret rotation, and the
        // resulting error at send time is an opaque "535 authentication failed".
        if (!string.IsNullOrWhiteSpace(options.Smtp.UserName) && string.IsNullOrEmpty(options.Smtp.Password))
        {
            failures.Add("Mailing:Smtp:Password is required when Mailing:Smtp:UserName is set.");
        }

        if (options.Transport == EmailTransport.File && string.IsNullOrWhiteSpace(options.PickupDirectory))
        {
            failures.Add("Mailing:PickupDirectory is required when Mailing:Transport is File.");
        }

        // The lease has to outlast a send, or a slow SMTP server produces duplicate deliveries
        // rather than a slow one. Compared against the timeout that bounds an attempt.
        if (options.Delivery.LeaseSeconds <= options.Smtp.TimeoutSeconds)
        {
            failures.Add(
                $"Mailing:Delivery:LeaseSeconds ({options.Delivery.LeaseSeconds}) must exceed "
                + $"Mailing:Smtp:TimeoutSeconds ({options.Smtp.TimeoutSeconds}), or a slow send "
                + "can be claimed and delivered twice.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
