namespace LogiFlow.Application.Abstractions.Messaging;

/// <summary>
/// Sends an event that has already been committed out of this process.
/// </summary>
/// <remarks>
/// <para>
/// <b>The word "integration" is doing real work here.</b> A <i>domain</i> event is internal: it
/// says something happened inside the model, it is handled in the same transaction, and its shape
/// is nobody else's business. An <i>integration</i> event has left the building — another service,
/// a browser, a queue — and the moment it does, its shape becomes a contract you cannot change
/// unilaterally. Publishing your domain events directly is the most common way a codebase
/// acquires a public API it never agreed to, and it is why this interface takes the event as
/// something the implementation must deliberately translate.
/// </para>
/// <para>
/// <b>Why it lives in Application and not Infrastructure.</b> The outbox processor needs to send
/// things; it must not know whether that means RabbitMQ, Azure Service Bus, or a WebSocket held
/// open by a browser. The API project supplies the SignalR implementation and the Infrastructure
/// project supplies a logging one, and the processor is unchanged by either — which is the entire
/// argument for the dependency rule, in one file.
/// </para>
/// <para>
/// <b>It is called AFTER the transaction commits</b>, by the outbox processor, never inline with
/// the work. That ordering is the point of the outbox: sending inside the transaction risks
/// telling the world about something that then rolls back, and sending after it without an outbox
/// risks the send being lost. See <c>Infrastructure/Persistence/Outbox/</c>.
/// </para>
/// Covered in: <c>course/module-25-distributed-systems/</c>
/// </remarks>
public interface IIntegrationEventPublisher
{
    /// <summary>Publishes one committed event.</summary>
    /// <param name="integrationEvent">The event, already deserialised from the outbox row.</param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    /// <returns>A task that completes when the event has been handed to the transport.</returns>
    /// <remarks>
    /// An implementation <b>must not throw for an event it does not care about</b>. Most events
    /// are of no interest to most transports, and the outbox treats a throw as a delivery failure
    /// worth retrying — so an over-eager guard here turns "not interested" into a message that
    /// retries five times and is then quarantined forever.
    /// </remarks>
    Task PublishAsync(object integrationEvent, CancellationToken cancellationToken);
}
