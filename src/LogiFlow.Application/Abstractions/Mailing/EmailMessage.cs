using System.Globalization;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Abstractions.Mailing;

/// <summary>
/// One message, ready to send: who it goes to, what it says, and how to recognise it again.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a type instead of four string parameters.</b> The signature this replaced was
/// <c>SendAsync(string to, string subject, string body)</c>, and every feature mail grows — a
/// plain-text alternative beside the HTML, a category to group deliveries by, a key to stop the
/// same message being queued twice — would add another parameter to every implementation and
/// every call site. A message is a thing; giving it a name lets it acquire properties without
/// breaking anyone.
/// </para>
/// <para>
/// <b>Both bodies, always.</b> <see cref="TextBody"/> is required and <see cref="HtmlBody"/> is
/// optional, which is the opposite of what most systems do. Clients that cannot or will not
/// render HTML — screen readers, terminal clients, watch previews, corporate gateways that
/// strip it — fall back to the text part, and a text part reading "please enable HTML to view
/// this email" is one those readers cannot read. Spam filters score a missing text part too.
/// </para>
/// <para>
/// <b>No attachments, deliberately.</b> An invoice PDF belongs in blob storage behind a signed,
/// expiring URL that the mail links to. Carrying it in the message means megabytes in the queue
/// table, a size limit you meet the day somebody orders forty lines, and a copy of a financial
/// document sitting in every intermediate mail spool indefinitely. If a message genuinely must
/// carry a file — some EDI and pubblica amministrazione integrations require exactly that —
/// it is a different transport with a different retention policy, not this one.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed record EmailMessage
{
    private EmailMessage(
        EmailAddress to,
        string subject,
        string textBody,
        string? htmlBody,
        string category,
        string? deduplicationKey)
    {
        To = to;
        Subject = subject;
        TextBody = textBody;
        HtmlBody = htmlBody;
        Category = category;
        DeduplicationKey = deduplicationKey;
    }

    /// <summary>Longest subject that will be kept. Anything longer is truncated by the factory.</summary>
    /// <remarks>
    /// RFC 5322 recommends header lines of at most 78 characters and requires folding beyond
    /// 998. Mail clients cut the display off between roughly 60 and 90 anyway, so a subject
    /// longer than this is not a subject — it is a paragraph in the wrong place.
    /// </remarks>
    public const int MaxSubjectLength = 200;

    /// <summary>Who receives it.</summary>
    public EmailAddress To { get; }

    /// <summary>The subject line. Never blank, never contains a line break.</summary>
    public string Subject { get; }

    /// <summary>The plain-text body. Always present.</summary>
    public string TextBody { get; }

    /// <summary>The HTML body, or <c>null</c> for a text-only message.</summary>
    public string? HtmlBody { get; }

    /// <summary>
    /// A coarse label such as <c>order-confirmation</c>, used for metrics, log filtering, and
    /// eventually for honouring per-category unsubscribes.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// A caller-chosen key that makes queueing idempotent, or <c>null</c> to allow duplicates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The queue puts a unique index on this column, so enqueueing the same key twice is a
    /// no-op rather than a second email. That matters more than it first looks: domain events
    /// are dispatched at-least-once, a retried HTTP request re-runs its handler, and a
    /// redeployed worker replays whatever it had not yet marked done. Every one of those paths
    /// ends with a customer receiving the same confirmation twice unless something
    /// deduplicates, and the only place that can do it reliably is the write.
    /// </para>
    /// <para>
    /// Derive it from the <i>fact</i>, never from the moment:
    /// <c>order-confirmation:2026-000123</c> survives a replay, <c>Guid.NewGuid()</c> does not
    /// and quietly defeats the entire mechanism.
    /// </para>
    /// </remarks>
    public string? DeduplicationKey { get; }

    /// <summary>Builds a message, normalising the parts that must not reach a mail server raw.</summary>
    /// <param name="to">The recipient.</param>
    /// <param name="subject">Subject line. Trimmed, flattened and truncated; must not be blank.</param>
    /// <param name="textBody">Plain-text body. Must not be blank.</param>
    /// <param name="htmlBody">Optional HTML body.</param>
    /// <param name="category">Coarse grouping label. Defaults to <c>general</c>.</param>
    /// <param name="deduplicationKey">Optional idempotency key; see <see cref="DeduplicationKey"/>.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentException">The subject or the text body is blank.</exception>
    /// <remarks>
    /// <para>
    /// <b>Flattening the subject is a security control, not tidiness.</b> An SMTP header ends at
    /// a CRLF, so a subject containing one lets whoever supplied it write further headers:
    /// </para>
    /// <code>
    /// subject = "Order shipped\r\nBcc: everyone@competitor.example\r\n"
    /// </code>
    /// <para>
    /// That is header injection, and it turns transactional mail into somebody else's
    /// distribution list. MimeKit encodes headers properly and would not be fooled, but two of
    /// the transports here write a file and a log line by hand — and the defence belongs where
    /// the value is created, not in each of the places it is consumed.
    /// </para>
    /// <para>
    /// <b>Why throw, when the rest of the codebase returns <c>Result</c>?</b> Because these are
    /// not user inputs. Subjects come from templates in this assembly; a blank one is a bug in
    /// code rather than a customer typing badly, and the right response to a bug is a loud
    /// failure in the first test that sends it. <c>Result</c> is for outcomes a caller can
    /// sensibly act on.
    /// </para>
    /// </remarks>
    public static EmailMessage Create(
        EmailAddress to,
        string subject,
        string textBody,
        string? htmlBody = null,
        string category = "general",
        string? deduplicationKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(textBody);

        string flattened = Flatten(subject);

        if (flattened.Length > MaxSubjectLength)
        {
            // The ellipsis is one character, so the result is exactly MaxSubjectLength long.
            flattened = string.Concat(flattened.AsSpan(0, MaxSubjectLength - 1), "…");
        }

        return new EmailMessage(
            to,
            flattened,
            textBody,
            string.IsNullOrWhiteSpace(htmlBody) ? null : htmlBody,
            string.IsNullOrWhiteSpace(category) ? "general" : category.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(deduplicationKey) ? null : deduplicationKey.Trim());
    }

    /// <summary>Returns a copy addressed to somebody else, keeping everything else.</summary>
    /// <param name="recipient">The new recipient.</param>
    /// <returns>The redirected message.</returns>
    /// <remarks>
    /// Used by the guard that stops a staging environment mailing real customers. It is a named
    /// method rather than a public <c>init</c> setter so that redirecting is a deliberate,
    /// greppable act: <c>message with { To = ... }</c> buried in a feature would be a bug
    /// nobody catches in review.
    /// </remarks>
    public EmailMessage RedirectedTo(EmailAddress recipient) =>
        new(recipient, Subject, TextBody, HtmlBody, Category, DeduplicationKey);

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately prints neither body. A record's generated <c>ToString</c> prints every
    /// property, and this type reaches log scopes and exception messages — where a full body
    /// means customer names and addresses in the log aggregator, the same GDPR problem
    /// <c>EnableSensitiveDataLogging</c> causes over in Infrastructure.
    /// </remarks>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Category} to {To.Value}: \"{Subject}\" ({TextBody.Length} chars)");

    // Tabs and stray control characters get the same treatment as CR and LF: a subject is one
    // line of text, and anything claiming otherwise is either a bug or an attack.
    private static string Flatten(string subject)
    {
        string trimmed = subject.Trim();

        if (!trimmed.Any(char.IsControl))
        {
            return trimmed;
        }

        string spaced = string.Concat(trimmed.Select(c => char.IsControl(c) ? ' ' : c));

        // "a\r\nb" becomes "a  b" after the substitution above; collapse the run so the result
        // reads like the single line it now is.
        while (spaced.Contains("  ", StringComparison.Ordinal))
        {
            spaced = spaced.Replace("  ", " ", StringComparison.Ordinal);
        }

        return spaced.Trim();
    }
}
