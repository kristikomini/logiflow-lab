using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Tests.Mailing;

/// <summary>
/// Tests for the message factory's normalisation rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the cheapest tests in the repository and they cover a security control.</b> No
/// container, no database, no mocks — a factory method and a string assertion. The header
/// injection case in particular is the kind of thing that gets written once, works, and is
/// quietly broken two years later by somebody "simplifying" the subject handling; the test is
/// what makes that a build failure rather than a vulnerability.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class EmailMessageTests
{
    private static readonly EmailAddress Recipient = EmailAddress.Create("mario@rossi.it").Value;

    [Fact]
    public void Create_KeepsAWellFormedSubject()
    {
        EmailMessage message = EmailMessage.Create(Recipient, "Order 2026-000123 shipped", "body");

        message.Subject.ShouldBe("Order 2026-000123 shipped");
    }

    /// <summary>
    /// The header injection case: a CRLF in a subject would let the caller write extra SMTP
    /// headers, turning one message into a mailing list.
    /// </summary>
    [Theory]
    [InlineData("Shipped\r\nBcc: everyone@competitor.example")]
    [InlineData("Shipped\nBcc: everyone@competitor.example")]
    [InlineData("Shipped\tBcc: everyone@competitor.example")]
    public void Create_FlattensControlCharactersInTheSubject(string hostile)
    {
        EmailMessage message = EmailMessage.Create(Recipient, hostile, "body");

        message.Subject.ShouldNotContain("\r");
        message.Subject.ShouldNotContain("\n");
        message.Subject.ShouldNotContain("\t");
        message.Subject.ShouldBe("Shipped Bcc: everyone@competitor.example");
    }

    [Fact]
    public void Create_TruncatesAnOverlongSubjectToTheLimit()
    {
        string huge = new('a', EmailMessage.MaxSubjectLength + 50);

        EmailMessage message = EmailMessage.Create(Recipient, huge, "body");

        message.Subject.Length.ShouldBe(EmailMessage.MaxSubjectLength);
        message.Subject.ShouldEndWith("…");
    }

    [Fact]
    public void Create_TrimsTheSubject()
    {
        EmailMessage message = EmailMessage.Create(Recipient, "   Order shipped   ", "body");

        message.Subject.ShouldBe("Order shipped");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsABlankSubject(string? subject)
    {
        Should.Throw<ArgumentException>(() => EmailMessage.Create(Recipient, subject!, "body"));
    }

    [Fact]
    public void Create_RejectsABlankTextBody()
    {
        // The HTML body being optional is the point: a message with only HTML is unreadable to
        // a screen reader and scores badly with spam filters, so it cannot be constructed.
        Should.Throw<ArgumentException>(
            () => EmailMessage.Create(Recipient, "subject", "  ", "<p>rich</p>"));
    }

    [Fact]
    public void Create_TreatsABlankHtmlBodyAsAbsent()
    {
        EmailMessage message = EmailMessage.Create(Recipient, "subject", "body", "   ");

        message.HtmlBody.ShouldBeNull();
    }

    [Fact]
    public void Create_NormalisesTheCategory()
    {
        EmailMessage message = EmailMessage.Create(Recipient, "s", "b", category: "  Order-Confirmation ");

        message.Category.ShouldBe("order-confirmation");
    }

    [Fact]
    public void RedirectedTo_ChangesOnlyTheRecipient()
    {
        EmailMessage original = EmailMessage.Create(
            Recipient,
            "subject",
            "text",
            "<p>html</p>",
            "order-cancelled",
            "order-cancelled:2026-000123");

        EmailMessage redirected = original.RedirectedTo(EmailAddress.Create("qa@logiflow.example").Value);

        redirected.To.Value.ShouldBe("qa@logiflow.example");
        redirected.Subject.ShouldBe(original.Subject);
        redirected.TextBody.ShouldBe(original.TextBody);
        redirected.HtmlBody.ShouldBe(original.HtmlBody);
        redirected.Category.ShouldBe(original.Category);
        redirected.DeduplicationKey.ShouldBe(original.DeduplicationKey);

        // The original is untouched: records are values, and a guard that mutated the message it
        // was handed would corrupt whatever the caller does with it next.
        original.To.Value.ShouldBe("mario@rossi.it");
    }

    /// <summary>
    /// <c>ToString</c> reaches log scopes and exception messages, so it must not carry the body.
    /// </summary>
    [Fact]
    public void ToString_DoesNotLeakTheBody()
    {
        EmailMessage message = EmailMessage.Create(
            Recipient,
            "Order shipped",
            "Dear Mario Rossi, your parcel is going to Via Roma 1, Modena.");

        string description = message.ToString();

        description.ShouldNotContain("Via Roma 1");
        description.ShouldContain("mario@rossi.it");
        description.ShouldContain("Order shipped");
    }
}
