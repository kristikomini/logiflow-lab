using LogiFlow.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Messaging;

/// <summary>
/// The default <see cref="IIntegrationEventPublisher"/>: writes the event to the log and stops.
/// </summary>
/// <param name="logger">Where the event goes.</param>
/// <remarks>
/// <para>
/// A null object, not a placeholder. The outbox pattern is complete without a broker — claiming
/// the row, counting the attempt, committing the state change — and this makes that testable and
/// runnable with nothing installed. Swapping in RabbitMQ or Azure Service Bus replaces this one
/// class and touches nothing else, which is the claim the abstraction exists to make.
/// </para>
/// <para>
/// The API project registers a SignalR publisher <i>after</i> this one, and the later
/// registration wins for a single <c>GetRequiredService</c> — see the comment where it does.
/// </para>
/// Covered in: <c>course/module-25-distributed-systems/</c>
/// </remarks>
internal sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        logger.LogInformation(
            "Integration event {EventType} would be published to a broker here",
            integrationEvent.GetType().Name);

        return Task.CompletedTask;
    }
}
