using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Mailing;

namespace LogiFlow.Infrastructure.Tests.Mailing;

/// <summary>
/// Tests for the queue row's state machine and its retry policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>The retry policy is business logic wearing infrastructure clothes.</b> How many attempts a
/// message gets, whether a rejection is retried at all, and how long the gaps grow are decisions
/// somebody will be asked to justify after an incident — so they are tested like decisions, with
/// no database anywhere near them.
/// </para>
/// <para>
/// <b>The clock is a parameter, which is what makes this possible.</b> Every method here takes
/// "now" rather than reading it, so a test can assert that the fourth attempt is scheduled hours
/// out without waiting hours. That is the entire argument for <c>IDateTimeProvider</c>, visible
/// in a test that runs in under a millisecond.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class QueuedEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private static QueuedEmail AQueuedEmail() =>
        new(
            Guid.CreateVersion7(),
            EmailMessage.Create(
                EmailAddress.Create("mario@rossi.it").Value,
                "Order shipped",
                "Your order is on its way.",
                category: "shipment-dispatched",
                deduplicationKey: "shipment-dispatched:1Z999"),
            Now);

    private static DeliveryOptions Options(int maxAttempts = 5, int retryBaseSeconds = 30) =>
        new() { MaxAttempts = maxAttempts, RetryBaseSeconds = retryBaseSeconds };

    [Fact]
    public void ANewMessageIsImmediatelyDue()
    {
        QueuedEmail email = AQueuedEmail();

        email.NextAttemptUtc.ShouldBe(Now);
        email.SentAtUtc.ShouldBeNull();
        email.AbandonedAtUtc.ShouldBeNull();
        email.AttemptCount.ShouldBe(0);
    }

    [Fact]
    public void MarkSent_RecordsTheTimeAndClearsTheError()
    {
        QueuedEmail email = AQueuedEmail();

        email.MarkFailed("temporary glitch", Now, isTransient: true, Options());
        email.MarkSent(Now.AddMinutes(1));

        email.SentAtUtc.ShouldBe(Now.AddMinutes(1));

        // The error is cleared on success, so a row's LastError always describes the state it is
        // actually in rather than something that was recovered from an hour ago.
        email.LastError.ShouldBeNull();
    }

    /// <summary>
    /// A 5xx rejection — "mailbox does not exist" — will not succeed on the fourth attempt
    /// either. Retrying it wastes twenty minutes and puts four more rejections on your sending
    /// reputation.
    /// </summary>
    [Fact]
    public void MarkFailed_AbandonsAPermanentFailureImmediately()
    {
        QueuedEmail email = AQueuedEmail();

        email.MarkFailed("550 mailbox unavailable", Now, isTransient: false, Options());

        email.IsAbandoned.ShouldBeTrue();
        email.AbandonedAtUtc.ShouldBe(Now);
        email.LastError.ShouldBe("550 mailbox unavailable");
    }

    [Fact]
    public void MarkFailed_SchedulesARetryForATransientFailure()
    {
        QueuedEmail email = AQueuedEmail();

        // The worker increments the count when it claims the row; simulate one claim.
        Claim(email);
        email.MarkFailed("421 service unavailable", Now, isTransient: true, Options());

        email.IsAbandoned.ShouldBeFalse();
        email.NextAttemptUtc.ShouldBeGreaterThan(Now);
    }

    /// <summary>
    /// Even a transient failure runs out of lives, or a permanently broken relay produces a row
    /// that is retried until the heat death of the universe.
    /// </summary>
    [Fact]
    public void MarkFailed_AbandonsOnceAttemptsAreExhausted()
    {
        QueuedEmail email = AQueuedEmail();
        DeliveryOptions options = Options(maxAttempts: 3);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Claim(email);
            email.MarkFailed("421 service unavailable", Now, isTransient: true, options);
        }

        email.AttemptCount.ShouldBe(3);
        email.IsAbandoned.ShouldBeTrue();
    }

    [Fact]
    public void MarkFailed_TruncatesAnEnormousError()
    {
        QueuedEmail email = AQueuedEmail();

        email.MarkFailed(new string('x', 10_000), Now, isTransient: false, Options());

        // A full stack trace can run to tens of kilobytes. A poison message retried repeatedly
        // would otherwise grow the queue table faster than the orders that caused it.
        email.LastError!.Length.ShouldBe(QueuedEmail.MaxErrorLength);
    }

    /// <summary>
    /// Full jitter means the delay is random within the exponential window, so a batch that
    /// failed together does not retry together. The assertion is therefore about the WINDOW,
    /// not about an exact value.
    /// </summary>
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    public void BackoffFor_GrowsExponentiallyWithinAJitteredWindow(int attempt, int ceilingSeconds)
    {
        DeliveryOptions options = Options(retryBaseSeconds: 30);

        for (var run = 0; run < 50; run++)
        {
            TimeSpan delay = QueuedEmail.BackoffFor(attempt, options);

            delay.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
            delay.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(ceilingSeconds));
        }
    }

    [Fact]
    public void BackoffFor_IsCappedSoAnOutageDoesNotScheduleARetryNextWeek()
    {
        TimeSpan delay = QueuedEmail.BackoffFor(attempt: 20, Options(retryBaseSeconds: 30));

        delay.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [Fact]
    public void BackoffFor_IsNeverZero()
    {
        // A zero delay busy-loops the worker against a server that has just asked it to slow
        // down - which is how a rate limit becomes a ban.
        for (var run = 0; run < 200; run++)
        {
            QueuedEmail.BackoffFor(attempt: 1, Options(retryBaseSeconds: 1))
                .ShouldBeGreaterThan(TimeSpan.Zero);
        }
    }

    [Fact]
    public void ToMessage_RoundTripsWhatWasQueued()
    {
        EmailMessage original = EmailMessage.Create(
            EmailAddress.Create("mario@rossi.it").Value,
            "Order shipped",
            "text body",
            "<p>html body</p>",
            "shipment-dispatched",
            "shipment-dispatched:1Z999");

        var queued = new QueuedEmail(Guid.CreateVersion7(), original, Now);

        EmailMessage restored = queued.ToMessage();

        restored.ShouldBe(original);
    }

    /// <summary>
    /// Reproduces what a worker claiming this row does to it.
    /// </summary>
    /// <remarks>
    /// <b>Reflection in a test, deliberately.</b> <c>AttemptCount</c> is incremented by the
    /// claim statement in SQL — see <c>EmailDeliveryService.ClaimBatchAsync</c> — precisely so
    /// that a message which crashes the worker still runs out of attempts. The entity therefore
    /// has no public way to increment it, and EF Core writes the value through this same private
    /// setter when it materialises a claimed row. Reaching for it here reproduces the production
    /// sequence instead of inventing a second one; adding a public method just to make the test
    /// tidier would put a way to forge the attempt count into the production API.
    /// </remarks>
    private static void Claim(QueuedEmail email) =>
        typeof(QueuedEmail)
            .GetProperty(nameof(QueuedEmail.AttemptCount))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(email, [email.AttemptCount + 1]);
}
