using LogiFlow.Application.Abstractions.Mailing;
using MimeKit;
using MimeKit.Text;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Turns an <see cref="EmailMessage"/> into the RFC 5322 message that actually goes on the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared by the SMTP and file transports, which is the point.</b> The <c>.eml</c> files the
/// development transport writes are byte-for-byte what SMTP would have sent, so "it looked fine
/// in the pickup folder" is evidence about production rather than about a second code path that
/// resembles it.
/// </para>
/// <para>
/// <b>MimeKit does the parts that are easy to get wrong:</b> RFC 2047 encoding of non-ASCII
/// names and subjects (an <c>à</c> in "Società" is not ASCII and cannot appear raw in a header),
/// quoted-printable transfer encoding of the bodies, boundary generation for the multipart, and
/// a <c>Message-Id</c> that is actually unique. Hand-rolling any of that is how you produce mail
/// that renders in your client and as mojibake in everyone else's.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
internal static class MimeMessageFactory
{
    /// <summary>The header carrying the message's category, for filtering in a capture UI.</summary>
    internal const string CategoryHeader = "X-LogiFlow-Category";

    /// <summary>Builds the MIME message.</summary>
    /// <param name="message">What to send.</param>
    /// <param name="options">Sender identity and reply-to.</param>
    /// <returns>The message, ready for a transport.</returns>
    public static MimeMessage Create(EmailMessage message, MailingOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        var mime = new MimeMessage
        {
            Subject = message.Subject,
        };

        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To.Value));

        if (options.ReplyToAddress is { Length: > 0 } replyTo)
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        mime.Headers.Add(CategoryHeader, message.Category);

        // ── Why multipart/alternative, and why in this order ────────────────────────────────
        // "Alternative" means: here are two renderings of the SAME content, show the reader
        // whichever you can. Parts are ordered least-preferred FIRST, so the text part comes
        // before the HTML one - a mail client displays the last part it understands. Put them
        // the other way round and HTML-capable clients show the plain text, which is a bug that
        // looks like a design choice.
        //
        // multipart/MIXED would be wrong here: that means "here are several DIFFERENT things",
        // and produces a message showing the text body followed by the HTML body, one after the
        // other. It is the most common mistake in hand-written mail code.
        mime.Body = message.HtmlBody is { Length: > 0 } html
            ? new Multipart("alternative")
            {
                new TextPart(TextFormat.Plain) { Text = message.TextBody },
                new TextPart(TextFormat.Html) { Text = html },
            }
            : new TextPart(TextFormat.Plain) { Text = message.TextBody };

        return mime;
    }
}
