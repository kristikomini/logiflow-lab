using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Background worker that drains the email queue.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same <see cref="BackgroundService"/> rules as the outbox processor apply</b> — a
/// singleton that cannot inject scoped services, an unhandled exception that stops the whole
/// host, a <c>stoppingToken</c> that must be honoured — and they are explained in full on
/// <c>OutboxProcessor</c>. What follows is what this worker does that the outbox one does not.
/// </para>
/// <para>
/// <b>It claims rows atomically, which the outbox processor is criticised for not doing.</b> The
/// naive shape reads pending rows, sends them, then marks them done. Run two instances — which
/// happens the first time anyone scales out, and during every rolling deploy where old and new
/// overlap for thirty seconds — and both read the same rows and both send them. Customers get
/// everything twice. <see cref="QueuedEmailStore.ClaimBatchAsync"/> is the single statement that
/// prevents it.
/// </para>
/// <para>
/// <b>It sends sequentially, on purpose.</b> <c>Task.WhenAll</c> over the batch looks faster and
/// is not: the SMTP transport holds one connection behind a semaphore, so twenty parallel sends
/// become twenty tasks queueing on a lock, with twenty times the memory and a stack trace nobody
/// can read when one fails. Concurrency that immediately serialises is cost without benefit.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
/// <param name="scopeFactory">Creates a DI scope per polling iteration.</param>
/// <param name="options">Poll interval, batch size, retry policy, retention.</param>
/// <param name="logger">Logger.</param>
public sealed class EmailDeliveryService(
    IServiceScopeFactory scopeFactory,
    IOptions<MailingOptions> options,
    ILogger<EmailDeliveryService> logger) : BackgroundService
{
    /// <summary>How many delivered rows one retention sweep removes. See the store's remarks.</summary>
    private const int RetentionBatchSize = 5_000;

    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);

    private readonly MailingOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    private DateTimeOffset _lastRetentionSweepUtc = DateTimeOffset.MinValue;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Email delivery started: transport {Transport}, from {From}, poll {PollSeconds}s, batch {BatchSize}{Redirect}",
            _options.Transport,
            _options.FromAddress,
            _options.Delivery.PollSeconds,
            _options.Delivery.BatchSize,
            _options.RedirectAllTo is { Length: > 0 } redirect
                ? $", ALL MAIL REDIRECTED TO {redirect}"
                : string.Empty);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.Delivery.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Catch-all: an exception escaping ExecuteAsync stops the entire host by default
                // since .NET 6, and a mail problem must not take the API down with it.
                logger.LogError(ex, "Email delivery batch failed; will retry on the next poll");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Email delivery stopped");
    }

    private async Task DeliverBatchAsync(CancellationToken cancellationToken)
    {
        // A fresh scope, and therefore a fresh DbContext, per iteration. Reusing one would
        // accumulate tracked entities forever and leak memory over days of uptime.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();
        IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        IDateTimeProvider clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<Guid> claimed = await QueuedEmailStore
            .ClaimBatchAsync(
                context,
                now,
                _options.Delivery.BatchSize,
                TimeSpan.FromSeconds(_options.Delivery.LeaseSeconds),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed.Count == 0)
        {
            await SweepDeliveredAsync(context, now, cancellationToken).ConfigureAwait(false);

            return;
        }

        List<QueuedEmail> emails = await context.QueuedEmails
            .Where(email => claimed.Contains(email.Id))
            .OrderBy(email => email.QueuedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sent = 0;
        var failed = 0;
        var cancelled = false;

        foreach (QueuedEmail email in emails)
        {
            try
            {
                await sender.SendAsync(email.ToMessage(), cancellationToken).ConfigureAwait(false);

                email.MarkSent(clock.UtcNow);
                sent++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown mid-batch. Stop sending, but do NOT leave the loop without saving:
                // the messages already sent have to be recorded. Whatever was not attempted
                // keeps its lease and is picked up by the next worker once it expires.
                cancelled = true;

                break;
            }
            catch (EmailDeliveryException ex)
            {
                email.MarkFailed(ex.Message, clock.UtcNow, ex.IsTransient, _options.Delivery);
                failed++;

                LogFailure(email, ex);
            }
            catch (Exception ex)
            {
                // A transport throwing something other than EmailDeliveryException has a bug.
                // Treat it as transient - the message is probably fine and the code is not - and
                // let MaxAttempts stop the bleeding.
                email.MarkFailed(ex.ToString(), clock.UtcNow, isTransient: true, _options.Delivery);
                failed++;

                logger.LogError(ex, "Unexpected failure sending email {EmailId}", email.Id);
            }
        }

        // CancellationToken.None deliberately. This save records deliveries that have ALREADY
        // happened; abandoning it because the host is shutting down means those customers are
        // emailed again when the process returns. That trades a duplicate email for a few
        // milliseconds of shutdown time, which is the wrong way round.
        await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation(
            "Email batch complete: {Sent} sent, {Failed} failed, {Skipped} not attempted",
            sent,
            failed,
            emails.Count - sent - failed);

        if (!cancelled)
        {
            await SweepDeliveredAsync(context, now, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Prunes delivered messages, at most once an hour and never before sending.</summary>
    /// <remarks>
    /// Cleanup is the least urgent thing this worker does and must never delay the most urgent,
    /// so it runs on the idle path — when there was nothing to deliver, or after a batch has
    /// been sent and recorded.
    /// </remarks>
    private async Task SweepDeliveredAsync(
        LogiFlowDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (now - _lastRetentionSweepUtc < RetentionInterval)
        {
            return;
        }

        _lastRetentionSweepUtc = now;

        DateTimeOffset cutoff = now.AddDays(-_options.Delivery.RetentionDays);

        int deleted = await QueuedEmailStore
            .DeleteDeliveredBeforeAsync(context, cutoff, RetentionBatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Retention sweep removed {Deleted} delivered emails older than {Cutoff:u}",
                deleted,
                cutoff);
        }
    }

    private void LogFailure(QueuedEmail email, EmailDeliveryException ex)
    {
        if (email.IsAbandoned)
        {
            // Error, not Warning. This is the line that should page somebody: a customer was
            // told something and never heard it, and no further attempt will be made.
            logger.LogError(
                ex,
                "Abandoned {Category} email {EmailId} after {Attempts} attempt(s): {Reason}",
                email.Category,
                email.Id,
                email.AttemptCount,
                ex.Message);

            return;
        }

        logger.LogWarning(
            "Delivery of {Category} email {EmailId} failed (attempt {Attempts}); next attempt at {NextAttempt:u}",
            email.Category,
            email.Id,
            email.AttemptCount,
            email.NextAttemptUtc);
    }
}
