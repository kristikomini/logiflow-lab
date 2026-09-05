using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Mailing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Tests.Mailing;

/// <summary>
/// Tests for the guard that stops a non-production environment mailing real customers.
/// </summary>
/// <remarks>
/// <para>
/// <b>These tests are the reason the guard is trustworthy.</b> A safety net nobody has verified
/// is worse than no safety net, because people rely on it. The incident it prevents — staging
/// restored from a production backup, pointed at a real relay, emailing four thousand real
/// customers — is expensive enough to be worth six assertions.
/// </para>
/// <para>
/// <b>The decorator is what makes them possible.</b> The inner transport is a substitute, so
/// nothing connects to anything; the test asserts on what the decorator decided, which is the
/// only behaviour that belongs to this class.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class RedirectingEmailSenderTests
{
    private readonly IEmailSender _inner = Substitute.For<IEmailSender>();

    private static EmailMessage AMessage(string to = "mario@rossi.it") =>
        EmailMessage.Create(
            EmailAddress.Create(to).Value,
            "Order shipped",
            "Your order is on its way.",
            "<p>Your order is on its way.</p>",
            "shipment-dispatched");

    private RedirectingEmailSender SenderWith(MailingOptions options) =>
        new(_inner, Options.Create(options), NullLogger<RedirectingEmailSender>.Instance);

    [Fact]
    public async Task PassesTheMessageThroughWhenNoGuardIsConfigured()
    {
        RedirectingEmailSender sender = SenderWith(new MailingOptions());
        EmailMessage message = AMessage();

        await sender.SendAsync(message, CancellationToken.None);

        await _inner.Received(1).SendAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedirectsEveryRecipientWhenRedirectAllToIsSet()
    {
        RedirectingEmailSender sender = SenderWith(new MailingOptions
        {
            RedirectAllTo = "qa@logiflow.example",
        });

        await sender.SendAsync(AMessage(), CancellationToken.None);

        await _inner.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To.Value == "qa@logiflow.example"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Twenty redirected messages all saying "Your order has shipped" are indistinguishable in
    /// an inbox list unless the original recipient is visible without opening them.
    /// </summary>
    [Fact]
    public async Task RedirectingKeepsTheOriginalRecipientVisibleInTheSubject()
    {
        RedirectingEmailSender sender = SenderWith(new MailingOptions
        {
            RedirectAllTo = "qa@logiflow.example",
        });

        await sender.SendAsync(AMessage("mario@rossi.it"), CancellationToken.None);

        await _inner.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m =>
                m.Subject.Contains("mario@rossi.it", StringComparison.Ordinal)
                && m.Subject.Contains("Order shipped", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedirectingPreservesTheBodies()
    {
        RedirectingEmailSender sender = SenderWith(new MailingOptions
        {
            RedirectAllTo = "qa@logiflow.example",
        });

        EmailMessage original = AMessage();

        await sender.SendAsync(original, CancellationToken.None);

        await _inner.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.TextBody == original.TextBody && m.HtmlBody == original.HtmlBody),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DropsARecipientOutsideTheAllowedDomains()
    {
        var options = new MailingOptions();
        options.AllowedRecipientDomains.Add("logiflow.example");

        RedirectingEmailSender sender = SenderWith(options);

        await sender.SendAsync(AMessage("mario@rossi.it"), CancellationToken.None);

        // Dropped silently rather than thrown: throwing would make the delivery worker retry a
        // message that policy says must never be sent, then dead-letter it as a failure.
        await _inner.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowsARecipientInsideTheAllowedDomains()
    {
        var options = new MailingOptions();
        options.AllowedRecipientDomains.Add("@LogiFlow.Example");

        RedirectingEmailSender sender = SenderWith(options);

        // Case-insensitive, and tolerant of a leading @ - both are how people actually write a
        // domain into a configuration file, and rejecting either would be a trap.
        await sender.SendAsync(AMessage("qa@logiflow.example"), CancellationToken.None);

        await _inner.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RedirectWinsOverTheAllowList()
    {
        var options = new MailingOptions { RedirectAllTo = "qa@logiflow.example" };
        options.AllowedRecipientDomains.Add("nobody.example");

        RedirectingEmailSender sender = SenderWith(options);

        // The redirect has already made the message harmless; applying the allow-list to the
        // ORIGINAL recipient afterwards would drop mail the tester is waiting to read.
        await sender.SendAsync(AMessage("mario@rossi.it"), CancellationToken.None);

        await _inner.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To.Value == "qa@logiflow.example"),
            Arg.Any<CancellationToken>());
    }
}
