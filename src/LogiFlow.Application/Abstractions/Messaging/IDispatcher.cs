using LogiFlow.Domain.Common;

namespace LogiFlow.Application.Abstractions.Messaging;

/// <summary>
/// Routes a request to its handler, through the pipeline, and returns the result.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is this hand-written instead of using MediatR?</b> Three reasons, in order of honesty:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>You should know how it works.</b> MediatR is roughly the 200 lines in
///     <see cref="Dispatcher"/> plus polish. Interviewers ask "how does MediatR resolve a
///     handler when <c>Send</c> only knows the response type?" and the answer is the reflection
///     + cached-wrapper trick you can read in this repository.
///   </description></item>
///   <item><description>
///     <b>MediatR is no longer free for commercial use.</b> Version 13 moved to a paid licence.
///     Plenty of teams have had to answer this exact question recently; knowing the pattern
///     rather than the package is what makes that a non-event.
///   </description></item>
///   <item><description>
///     <b>It is genuinely small.</b> Not every abstraction needs a dependency.
///   </description></item>
/// </list>
/// <para>
/// Everything here maps one-to-one onto MediatR, so the concepts transfer directly:
/// <c>IDispatcher</c> ≈ <c>IMediator</c>, <c>IRequestHandler</c> is identical,
/// <c>IPipelineBehavior</c> is identical, <c>IEventHandler</c> ≈ <c>INotificationHandler</c>.
/// </para>
/// Covered in: <c>course/module-08-cqrs/03-building-a-mediator.md</c>
/// </remarks>
public interface IDispatcher
{
    /// <summary>Sends a request to its single handler and returns the response.</summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the request type.</exception>
    Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a domain event to every registered handler.
    /// </summary>
    /// <remarks>
    /// Fan-out, not routing: zero handlers is legal and normal. Handlers run sequentially and an
    /// exception in one aborts the rest — a deliberate choice, since these run inside the same
    /// transaction as the state change that produced them. Swallowing failures would mean a
    /// committed order whose stock was never reserved.
    /// </remarks>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reacts to a domain event. Any number may exist for one event, including none.
/// </summary>
/// <typeparam name="TEvent">The event handled.</typeparam>
public interface IEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the event.</summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
