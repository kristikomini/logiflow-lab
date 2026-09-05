using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Mailing;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Shipping;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Tests.Mailing;

/// <summary>
/// Tests for the message templates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Templates are pure functions, so they are tested like arithmetic.</b> No container, no
/// renderer, no golden-file comparison — call the factory and assert on the strings. That is
/// only possible because the templates take facts rather than repositories, which is the
/// argument for that design made concrete.
/// </para>
/// <para>
/// <b>What is worth asserting.</b> Not the exact prose — a test that pins every word makes
/// copy changes fail the build for no benefit. What matters is that the facts a customer needs
/// are present, that both bodies exist and agree, that the HTML is escaped, and that the
/// deduplication key is derived from the fact rather than from the moment.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class OrderEmailsTests
{
    private static readonly EmailAddress Recipient = EmailAddress.Create("mario@rossi.it").Value;
    private static readonly OrderNumber Number = OrderNumber.Create(2026, 123).Value;

    private static TrackingNumber ATrackingNumber() =>
        TrackingNumber.Create("1Z999AA10123456784", Carrier.Ups).Value;

    [Fact]
    public void ShipmentDispatched_CarriesTheFactsTheCustomerNeeds()
    {
        EmailMessage message = OrderEmails.ShipmentDispatched(
            Recipient,
            Number,
            Carrier.Ups,
            ATrackingNumber(),
            new DateOnly(2026, 9, 9));

        message.Subject.ShouldContain(Number.Value);
        message.TextBody.ShouldContain("1Z999AA10123456784");
        message.TextBody.ShouldContain("UPS");
        message.TextBody.ShouldContain("Wednesday 9 September 2026");
        message.Category.ShouldBe("shipment-dispatched");
    }

    [Fact]
    public void ShipmentDispatched_RendersBothBodies()
    {
        EmailMessage message = OrderEmails.ShipmentDispatched(
            Recipient,
            Number,
            Carrier.Dhl,
            TrackingNumber.Create("1234567890", Carrier.Dhl).Value,
            estimatedDelivery: null);

        message.TextBody.ShouldNotBeNullOrWhiteSpace();
        message.HtmlBody.ShouldNotBeNullOrWhiteSpace();

        // The same fact has to appear in both, or the plain-text reader is told less than the
        // HTML one - which is the drift that a single description of the content prevents.
        message.TextBody.ShouldContain("1234567890");
        message.HtmlBody!.ShouldContain("1234567890");
    }

    [Fact]
    public void ShipmentDispatched_OmitsAnAbsentEstimate()
    {
        EmailMessage message = OrderEmails.ShipmentDispatched(
            Recipient,
            Number,
            Carrier.PosteItaliane,
            TrackingNumber.Create("RR123456785IT", Carrier.PosteItaliane).Value,
            estimatedDelivery: null);

        // Not "Estimated delivery:" followed by nothing, which reads as a bug to the customer.
        message.TextBody.ShouldNotContain("Estimated delivery");
        message.HtmlBody!.ShouldNotContain("Estimated delivery");
    }

    /// <summary>
    /// The deduplication key must be a function of the fact, so a redelivered event produces the
    /// same key and the queue drops the duplicate.
    /// </summary>
    [Fact]
    public void ShipmentDispatched_DerivesAStableDeduplicationKey()
    {
        TrackingNumber tracking = ATrackingNumber();

        EmailMessage first = OrderEmails.ShipmentDispatched(Recipient, Number, Carrier.Ups, tracking, null);
        EmailMessage second = OrderEmails.ShipmentDispatched(Recipient, Number, Carrier.Ups, tracking, null);

        first.DeduplicationKey.ShouldBe(second.DeduplicationKey);
        first.DeduplicationKey.ShouldBe("shipment-dispatched:1Z999AA10123456784");
    }

    /// <summary>
    /// A split order dispatches twice, legitimately. Keying on the order rather than on the
    /// consignment would silence the second notification.
    /// </summary>
    [Fact]
    public void ShipmentDispatched_KeysOnTheConsignmentNotTheOrder()
    {
        EmailMessage first = OrderEmails.ShipmentDispatched(
            Recipient, Number, Carrier.Ups, ATrackingNumber(), null);

        EmailMessage second = OrderEmails.ShipmentDispatched(
            Recipient,
            Number,
            Carrier.Dhl,
            TrackingNumber.Create("9876543210", Carrier.Dhl).Value,
            null);

        first.DeduplicationKey.ShouldNotBe(second.DeduplicationKey);
    }

    [Fact]
    public void OrderCancelled_StatesTheRefund()
    {
        EmailMessage message = OrderEmails.OrderCancelled(
            Recipient,
            Number,
            new Money(249.90m, Currency.Eur),
            "Customer changed their mind");

        message.TextBody.ShouldContain("249.90");
        message.TextBody.ShouldContain("Customer changed their mind");
        message.Category.ShouldBe("order-cancelled");
        message.DeduplicationKey.ShouldBe($"order-cancelled:{Number.Value}");
    }

    [Fact]
    public void OrderCancelled_SaysSomethingSensibleWhenThereIsNothingToRefund()
    {
        EmailMessage message = OrderEmails.OrderCancelled(
            Recipient,
            Number,
            Money.ZeroEur,
            "Draft abandoned");

        // "To be refunded: €0.00" reads like a mistake to somebody who never paid.
        message.TextBody.ShouldNotContain("To be refunded");
        message.TextBody.ShouldContain("nothing to refund");
    }

    /// <summary>
    /// The cancellation reason is free text typed by a human. Unescaped, one angle bracket
    /// silently swallows the rest of the paragraph in every HTML client.
    /// </summary>
    [Fact]
    public void OrderCancelled_EscapesTheReasonInTheHtmlBody()
    {
        EmailMessage message = OrderEmails.OrderCancelled(
            Recipient,
            Number,
            Money.ZeroEur,
            "Rossi & Figli <script>alert(1)</script>");

        string html = message.HtmlBody!;

        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldContain("&amp;");

        // The plain-text part is not markup and must NOT be escaped - a customer reading
        // "Rossi &amp; Figli" in a terminal client is a bug in the other direction.
        message.TextBody.ShouldContain("Rossi & Figli");
    }
}
