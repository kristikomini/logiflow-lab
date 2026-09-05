using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Mailing;
using MimeKit;
using MimeKit.Text;

namespace LogiFlow.Infrastructure.Tests.Mailing;

/// <summary>
/// Tests for the RFC 5322 message the transports actually put on the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>The multipart ordering test is the one that matters.</b> It encodes a rule that is easy to
/// state, easy to get backwards, and invisible when you get it wrong — because the developer's
/// own mail client renders HTML, so a message whose parts are in the wrong order looks perfect
/// to the person who wrote it and shows plain text to everyone else.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class MimeMessageFactoryTests
{
    private static readonly MailingOptions Options = new()
    {
        FromAddress = "no-reply@logiflow.example",
        FromName = "LogiFlow",
    };

    private static EmailMessage AMessage(string? html = "<p>Your order is on its way.</p>") =>
        EmailMessage.Create(
            EmailAddress.Create("mario@rossi.it").Value,
            "Order shipped",
            "Your order is on its way.",
            html,
            "shipment-dispatched");

    [Fact]
    public void SetsTheEnvelope()
    {
        MimeMessage mime = MimeMessageFactory.Create(AMessage(), Options);

        mime.From.Mailboxes.Single().Address.ShouldBe("no-reply@logiflow.example");
        mime.From.Mailboxes.Single().Name.ShouldBe("LogiFlow");
        mime.To.Mailboxes.Single().Address.ShouldBe("mario@rossi.it");
        mime.Subject.ShouldBe("Order shipped");
    }

    [Fact]
    public void AddsReplyToOnlyWhenConfigured()
    {
        MimeMessageFactory.Create(AMessage(), Options).ReplyTo.ShouldBeEmpty();

        var withReplyTo = new MailingOptions
        {
            FromAddress = Options.FromAddress,
            ReplyToAddress = "support@logiflow.example",
        };

        MimeMessageFactory.Create(AMessage(), withReplyTo)
            .ReplyTo.Mailboxes.Single().Address.ShouldBe("support@logiflow.example");
    }

    [Fact]
    public void TagsTheMessageWithItsCategory()
    {
        MimeMessage mime = MimeMessageFactory.Create(AMessage(), Options);

        mime.Headers[MimeMessageFactory.CategoryHeader].ShouldBe("shipment-dispatched");
    }

    /// <summary>
    /// multipart/alternative lists parts least-preferred first, because a client displays the
    /// last one it understands. Reversed, every HTML-capable client shows the plain text.
    /// </summary>
    [Fact]
    public void PutsThePlainTextPartBeforeTheHtmlPart()
    {
        MimeMessage mime = MimeMessageFactory.Create(AMessage(), Options);

        var alternative = mime.Body.ShouldBeOfType<Multipart>();
        alternative.ContentType.MediaSubtype.ShouldBe("alternative");

        alternative[0].ShouldBeOfType<TextPart>().IsPlain.ShouldBeTrue();
        alternative[1].ShouldBeOfType<TextPart>().IsHtml.ShouldBeTrue();
    }

    [Fact]
    public void SendsASinglePartWhenThereIsNoHtmlBody()
    {
        MimeMessage mime = MimeMessageFactory.Create(AMessage(html: null), Options);

        // No pointless multipart wrapper around one part - and no empty HTML alternative, which
        // some clients render as a blank message.
        mime.Body.ShouldBeOfType<TextPart>().IsPlain.ShouldBeTrue();
    }

    /// <summary>
    /// A header may not carry raw non-ASCII. MimeKit applies RFC 2047 encoding on the way out,
    /// and decodes it on the way back in — which is why the round trip is the assertion.
    /// </summary>
    [Fact]
    public void EncodesANonAsciiSubjectOnTheWire()
    {
        EmailMessage message = EmailMessage.Create(
            EmailAddress.Create("mario@rossi.it").Value,
            "Spedizione della Società Rossi è partita",
            "body");

        MimeMessage mime = MimeMessageFactory.Create(message, Options);

        using var stream = new MemoryStream();
        mime.WriteTo(stream);

        string wire = System.Text.Encoding.ASCII.GetString(stream.ToArray());

        wire.ShouldContain("=?utf-8?");
        wire.ShouldNotContain("Società");

        stream.Position = 0;
        MimeMessage.Load(stream).Subject.ShouldBe("Spedizione della Società Rossi è partita");
    }
}
