using System.Collections.Concurrent;
using LogiFlow.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LogiFlow.Application.Abstractions.Messaging;

/// <summary>
/// The default <see cref="IDispatcher"/>. Resolves handlers from DI and runs them
/// through the behaviour pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The core problem this file solves.</b> The signature is
/// <c>SendAsync&lt;TResponse&gt;(IRequest&lt;TResponse&gt; request)</c>. At compile time we know
/// <c>TResponse</c> — but the handler we need is
/// <c>IRequestHandler&lt;SubmitOrderCommand, Result&gt;</c>, and <c>SubmitOrderCommand</c> is
/// only known at <i>runtime</i>, from <c>request.GetType()</c>. You cannot write
/// <c>GetRequiredService&lt;IRequestHandler&lt;request.GetType(), TResponse&gt;&gt;()</c>;
/// generics are resolved by the compiler.
/// </para>
/// <para>
/// <b>The standard solution</b> — used here and by MediatR — is a generic wrapper class:
/// </para>
/// <list type="number">
///   <item><description>Build the closed type <c>RequestHandlerWrapper&lt;SubmitOrderCommand, Result&gt;</c> with <see cref="Type.MakeGenericType"/>.</description></item>
///   <item><description>Instantiate it once via <see cref="Activator.CreateInstance(Type)"/>.</description></item>
///   <item><description>Call it through the non-generic base <see cref="RequestHandlerWrapperBase{TResponse}"/>, where <c>TResponse</c> <i>is</i> statically known.</description></item>
///   <item><description>Cache the instance forever, keyed on request type — reflection happens once per request type per process, not once per call.</description></item>
/// </list>
/// <para>
/// That cache is what makes the reflection cost irrelevant: after the first
/// <c>SubmitOrderCommand</c>, dispatching one is a dictionary lookup and a virtual call.
/// The benchmark in <c>Labs.Benchmarks/DispatcherBenchmarks.cs</c> measures it.
/// </para>
/// </remarks>
/// <param name="serviceProvider">The scoped container used to resolve handlers and behaviours.</param>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    // Static, so the reflection cost is paid once per process rather than once per request.
    // ConcurrentDictionary because ASP.NET Core dispatches from many threads at once.
    private static readonly ConcurrentDictionary<Type, object> RequestHandlerCache = new();
    private static readonly ConcurrentDictionary<Type, Type> EventHandlerTypeCache = new();

    /// <inheritdoc />
    public Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Type requestType = request.GetType();

        var wrapper = (RequestHandlerWrapperBase<TResponse>)RequestHandlerCache.GetOrAdd(
            requestType,
            static (type, responseType) =>
            {
                Type wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(type, responseType);
                return Activator.CreateInstance(wrapperType)
                       ?? throw new InvalidOperationException($"Could not create a dispatcher wrapper for '{type}'.");
            },
            typeof(TResponse));

        return wrapper.HandleAsync(request, serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        Type eventType = domainEvent.GetType();
        Type handlerType = EventHandlerTypeCache.GetOrAdd(
            eventType,
            static type => typeof(IEventHandler<>).MakeGenericType(type));

        // GetServices returns an empty sequence when nothing is registered, which is the
        // correct semantic for an event: nobody is obliged to care that it happened.
        IEnumerable<object?> handlers = serviceProvider.GetServices(handlerType);

        foreach (object? handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            // The handler's HandleAsync is strongly typed to the concrete event, so it has to be
            // invoked reflectively here. This runs once per event per handler and only inside a
            // SaveChanges, so it is not on any hot path worth optimising.
            object? task = handlerType
                .GetMethod(nameof(IEventHandler<IDomainEvent>.HandleAsync))!
                .Invoke(handler, [domainEvent, cancellationToken]);

            if (task is Task awaitable)
            {
                await awaitable.ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Non-generic-over-request base, so <see cref="SendAsync{TResponse}"/> can hold a reference
    /// to a wrapper whose request type it does not statically know.
    /// </summary>
    private abstract class RequestHandlerWrapperBase<TResponse>
    {
        public abstract Task<TResponse> HandleAsync(
            object request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Closed over both the request and response type, so everything inside is strongly typed.
    /// </summary>
    private sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapperBase<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> HandleAsync(
            object request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var typedRequest = (TRequest)request;

            IRequestHandler<TRequest, TResponse> handler =
                serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

            IPipelineBehavior<TRequest, TResponse>[] behaviors =
                [.. serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>()];

            // Fast path: nothing wrapping the handler, so skip building the delegate chain.
            if (behaviors.Length == 0)
            {
                return handler.HandleAsync(typedRequest, cancellationToken);
            }

            // Build the chain from the INSIDE OUT. Start with the handler itself as the
            // innermost step, then wrap each behaviour around what we have so far, walking the
            // registration list backwards. The last one wrapped ends up outermost, so the
            // FIRST-registered behaviour runs FIRST. Get this backwards and your transaction
            // behaviour ends up inside your validation behaviour, which means you open a
            // transaction to run validation that was about to reject the request.
            RequestHandlerDelegate<TResponse> pipeline = () => handler.HandleAsync(typedRequest, cancellationToken);

            for (int i = behaviors.Length - 1; i >= 0; i--)
            {
                // Copy both into locals. Capturing the loop variable `i` directly would give
                // every closure the SAME variable, and by the time they run, i is -1.
                // This is the classic closure-capture bug; C# 5 fixed it for foreach but NOT
                // for `for`. See course/module-02-delegates-and-closures/03-capture-pitfalls.md
                IPipelineBehavior<TRequest, TResponse> behavior = behaviors[i];
                RequestHandlerDelegate<TResponse> next = pipeline;

                pipeline = () => behavior.HandleAsync(typedRequest, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
