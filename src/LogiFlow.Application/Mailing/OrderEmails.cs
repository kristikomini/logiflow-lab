using System.Globalization;
using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Shipping;
using LogiFlow.Domain.ValueObjects;

namespace LogiFlow.Application.Mailing;

/// <summary>
/// The messages this system sends about an order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Templates are static factories, not a template engine and not injected services.</b> Each
/// one is a pure function from facts to an <see cref="EmailMessage"/>: no clock, no repository,
/// no configuration. That makes them trivially testable — assert on the returned strings, no
/// container, no mocks — and it makes the deduplication key obvious, because a pure function
/// cannot accidentally derive it from <c>DateTime.Now</c>.
/// </para>
/// <para>
/// <b>They live in the Application layer, not Infrastructure.</b> What a customer is told when
/// their order ships is a business decision, versioned with the use case that triggers it.
/// Infrastructure owns <i>how</i> a message is delivered — SMTP, retries, credentials — and
/// nothing about what it says.
/// </para>
/// <para>
/// <b>Every subject is in one language here.</b> A real Emilia-Romagna deployment would key the
/// copy off the customer's language, and the shape that survives is passing an
/// <c>IStringLocalizer</c> or a <c>CultureInfo</c> into these factories rather than sprinkling
/// <c>if (italian)</c> through them. It is left monolingual because a half-translated mailer is
/// worse than an honestly untranslated one.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public static class OrderEmails
{
    /// <summary>Sender's sign-off, used by every message so they read as one voice.</summary>
    private const string SignOff = "LogiFlow Logistics";

    /// <summary>Tells a customer their consignment is on its way, and how to follow it.</summary>
    /// <param name="to">The customer's address.</param>
    /// <param name="orderNumber">The order being fulfilled.</param>
    /// <param name="carrier">Who is carrying it.</param>
    /// <param name="trackingNumber">The carrier's reference.</param>
    /// <param name="estimatedDelivery">The carrier's estimate, when it gave one.</param>
    /// <returns>The message, ready to queue.</returns>
    /// <remarks>
    /// The deduplication key is the tracking number, because that is the fact: one consignment
    /// gets one dispatch notice however many times the event is replayed. Keying it on the order
    /// instead would be wrong — a split order dispatches twice, legitimately, and the customer
    /// should hear about both.
    /// </remarks>
    public static EmailMessage ShipmentDispatched(
        EmailAddress to,
        OrderNumber orderNumber,
        Carrier carrier,
        TrackingNumber trackingNumber,
        DateOnly? estimatedDelivery)
    {
        (string text, string html) = new EmailBodyBuilder()
            .Paragraph("Your order has left our warehouse and is with the carrier.")
            .Fact("Order", orderNumber.Value)
            .Fact("Carrier", CarrierName(carrier))
            .Fact("Tracking number", trackingNumber.Value)
            .FactIfPresent("Estimated delivery", FormatDate(estimatedDelivery))
            .Paragraph(
                "Tracking information can take a few hours to appear on the carrier's own site "
                + "after collection.")
            .Build($"Order {orderNumber.Value} is on its way", SignOff);

        return EmailMessage.Create(
            to,
            $"Your LogiFlow order {orderNumber.Value} has shipped",
            text,
            html,
            category: "shipment-dispatched",
            deduplicationKey: $"shipment-dispatched:{trackingNumber.Value}");
    }

    /// <summary>Confirms a cancellation, and what happens to the money.</summary>
    /// <param name="to">The customer's address.</param>
    /// <param name="orderNumber">The cancelled order.</param>
    /// <param name="refundableTotal">What will be released or refunded.</param>
    /// <param name="reason">Why it was cancelled, as recorded on the order.</param>
    /// <returns>The message, ready to queue.</returns>
    /// <remarks>
    /// <para>
    /// <b>The reason is customer-visible, and that is a decision.</b> It reaches this template
    /// from whoever cancelled the order, which may be a support agent typing free text. It is
    /// escaped on the way into the HTML, so it cannot break the markup — but "escaped" is not
    /// "appropriate", and a system where internal notes can end up in a customer's inbox needs
    /// that to be a deliberate, documented choice rather than an accident of plumbing.
    /// </para>
    /// <para>
    /// <b>A zero refund is worth a different sentence.</b> "Refundable total: €0.00" reads like
    /// a mistake to somebody who has just cancelled a draft they never paid for.
    /// </para>
    /// </remarks>
    public static EmailMessage OrderCancelled(
        EmailAddress to,
        OrderNumber orderNumber,
        Money refundableTotal,
        string reason)
    {
        EmailBodyBuilder body = new EmailBodyBuilder()
            .Paragraph("We have cancelled your order as requested. Nothing further will be shipped.")
            .Fact("Order", orderNumber.Value)
            .FactIfPresent("Reason", reason);

        body = refundableTotal.IsZero
            ? body.Paragraph("There was nothing to refund on this order.")
            : body
                .Fact("To be refunded", refundableTotal.ToString())
                .Paragraph(
                    "Refunds are returned to the original payment method and usually appear "
                    + "within five working days.");

        (string text, string html) = body.Build($"Order {orderNumber.Value} cancelled", SignOff);

        return EmailMessage.Create(
            to,
            $"Your LogiFlow order {orderNumber.Value} has been cancelled",
            text,
            html,
            category: "order-cancelled",
            deduplicationKey: $"order-cancelled:{orderNumber.Value}");
    }

    /// <summary>
    /// Renders a carrier for a human rather than for a switch statement.
    /// </summary>
    /// <remarks>
    /// <c>carrier.ToString()</c> would put "PosteItaliane" and "FedEx" in front of a customer.
    /// The switch is exhaustive, so adding a carrier to the enum breaks this build — which is
    /// the compile-time safety net argued for in module 01, doing its job on a mailer.
    /// </remarks>
    private static string CarrierName(Carrier carrier) => carrier switch
    {
        Carrier.OwnFleet => "LogiFlow own fleet",
        Carrier.Dhl => "DHL",
        Carrier.Ups => "UPS",
        Carrier.FedEx => "FedEx",
        Carrier.PosteItaliane => "Poste Italiane",
        _ => "our carrier",
    };

    // "Wednesday 9 September 2026", invariant. Formatting a date with the SERVER's culture is
    // the classic bug: an Italian customer on a machine set to en-US gets 09/09/2026, which
    // half of Europe reads as the ninth of September and half as… also the ninth, until the
    // date is the third of April and the two readings differ by five months. Spelling the month
    // out removes the ambiguity entirely, which is why every carrier's email does it.
    private static string? FormatDate(DateOnly? date) =>
        date?.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
}
