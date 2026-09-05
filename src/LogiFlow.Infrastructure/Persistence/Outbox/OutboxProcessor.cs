using System.Collections.Frozen;
using System.Text.Json;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Common;
using LogiFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Persistence.Outbox;

/// <summary>
/// Background worker that publishes queued outbox messages.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three things about <see cref="BackgroundService"/> that catch people out:</b>
/// </para>
/// <list type="number">
///   <item><description>
///     <b>It is a singleton, so it cannot inject scoped services.</b> Injecting a
///     <c>DbContext</c> here would either fail at startup or — worse, in older versions —
///     capture one context for the lifetime of the process. It takes
///     <see cref="IServiceScopeFactory"/> and creates a scope per iteration instead.
///   </description></item>
///   <item><description>
///     <b>An unhandled exception in <c>ExecuteAsync</c> kills the whole host</b> by default
///     since .NET 6 (<c>BackgroundServiceExceptionBehavior.StopHost</c>). Silent death of a
///     background worker is worse than a crash, so this is the right default — but it means
///     the loop body must catch everything itself, which it does below.
///   </description></item>
///   <item><description>
///     <b><c>stoppingToken</c> must be honoured</b> or the host hangs for its full shutdown
///     timeout on every deploy, and orchestrators eventually SIGKILL you mid-write.
///   </description></item>
/// </list>
/// <para>
/// <b>The type allow-list is a security control, not tidiness.</b> Deserialising a type named
/// in data is how insecure-deserialisation vulnerabilities happen: an attacker who can write
/// to the outbox table names a "gadget" type whose construction has side effects. The messages
/// here are written by our own code, but defence in depth costs one dictionary.
/// </para>
/// Covered in: <c>course/module-06-efcore/07-outbox-pattern.md</c>
/// </remarks>
/// <param name="scopeFactory">Creates a DI scope per polling iteration.</param>
/// <param name="logger">Logger.</param>
public sealed class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The only event types this processor will ever deserialise.
    /// </summary>
    /// <remarks>
    /// Built once from the Domain assembly. Anything not in here is quarantined rather than
    /// instantiated — see the remarks on the class.
    /// </remarks>
    private static readonly FrozenDictionary<string, Type> AllowedEventTypes =
        typeof(OrderId).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToFrozenDictionary(t => t.FullName!, t => t, StringComparer.Ordinal);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox processor started: {EventTypeCount} event types on the allow-list",
            AllowedEventTypes.Count);

        // PeriodicTimer over Task.Delay: it does not drift, it is allocation-free per tick, and
        // it observes the cancellation token cleanly instead of throwing on shutdown.
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown, not a failure. Exit quietly.
                break;
            }
            catch (Exception ex)
            {
                // Catch-all so one bad batch cannot take the host down (see point 2 above).
                // The loop continues and retries on the next tick.
                logger.LogError(ex, "Outbox batch failed; will retry on the next poll");
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

        logger.LogInformation("Outbox processor stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        // A fresh scope, and therefore a fresh DbContext, per iteration. Reusing one would
        // accumulate tracked entities forever and leak memory over days of uptime.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        LogiFlowDbContext context = scope.ServiceProvider.GetRequiredService<LogiFlowDbContext>();

        // Resolved from the same scope as the DbContext, so a publisher that needs request-ish
        // services gets a consistent set. Resolving it once per batch rather than per message
        // matters only because the alternative reads as if a new one were needed each time.
        IIntegrationEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        List<OutboxMessage> pending = await context.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.AttemptCount < MaxAttempts)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (OutboxMessage message in pending)
        {
            try
            {
                if (!AllowedEventTypes.TryGetValue(message.Type, out Type? eventType))
                {
                    // Not an error worth retrying: the type will not appear on the next tick
                    // either. Fail it permanently so it stops being picked up.
                    message.MarkFailed($"Event type '{message.Type}' is not on the allow-list.");

                    logger.LogError(
                        "Outbox message {MessageId} names unknown event type {EventType}; quarantined",
                        message.Id,
                        message.Type);

                    continue;
                }

                object? domainEvent = JsonSerializer.Deserialize(message.Content, eventType, SerializerOptions);

                if (domainEvent is null)
                {
                    message.MarkFailed("Payload deserialised to null.");
                    continue;
                }

                // ── The transport ────────────────────────────────────────────────────────
                // Whatever IIntegrationEventPublisher is registered. In this repository that is
                // a log line by default, and a SignalR broadcast when the API is running - in
                // production it would be RabbitMQ, Azure Service Bus or Kafka. Nothing in this
                // loop changes for any of them, which is the whole reason it is an interface.
                //
                // Note it is awaited INSIDE the try: a transport failure has to fail this
                // message, not the batch. One unreachable broker should cost one retry, not
                // nineteen messages that were never attempted.
                logger.LogInformation(
                    "Publishing integration event {EventType} ({MessageId})",
                    eventType.Name,
                    message.Id);

                await publisher.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);

                message.MarkProcessed(DateTimeOffset.UtcNow);
            }
            catch (JsonException ex)
            {
                message.MarkFailed($"Malformed payload: {ex.Message}");
                logger.LogError(ex, "Outbox message {MessageId} has a malformed payload", message.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed(ex.ToString());
                logger.LogError(ex, "Failed to publish outbox message {MessageId}", message.Id);
            }
        }

        // One save for the whole batch: 20 messages, one round trip. Saving per message would
        // multiply the write cost by the batch size for no benefit.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        int published = pending.Count(m => m.ProcessedAtUtc is not null);

        logger.LogInformation(
            "Outbox batch complete: {Published} published, {Failed} failed",
            published,
            pending.Count - published);
    }
}
