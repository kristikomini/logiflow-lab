using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Abstractions.Messaging;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  CQRS MESSAGE CONTRACTS
//
//  Command Query Responsibility Segregation: the claim that reading and writing are different
//  enough to deserve different models.
//
//    * A COMMAND changes state. It has a name in the imperative ("SubmitOrder"), it enforces
//      business rules, it runs in a transaction, and it returns as little as possible.
//    * A QUERY reads state. It changes nothing, it can skip the domain model entirely and
//      project straight to a DTO, it can be cached, and it can go to a read replica.
//
//  What CQRS is NOT: two databases, event sourcing, or microservices. Those combine well
//  with it and are frequently confused for it. At its core CQRS is this — two interfaces —
//  and it is worth doing at exactly this level in most applications.
//
//  Why bother? Because the shapes genuinely diverge. Submitting an order must load the full
//  Order aggregate to enforce its invariants. Rendering an order list needs six columns from
//  three tables and no behaviour at all. Forcing both through one model gives you either
//  slow reads or an anaemic domain.
//
//  Covered in: course/module-08-cqrs/01-what-cqrs-actually-is.md
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Marker for anything that can be sent through the <see cref="IDispatcher"/>.
/// </summary>
/// <typeparam name="TResponse">What the handler gives back.</typeparam>
/// <remarks>
/// <c>out</c> makes this covariant, so an <c>IRequest&lt;Result&lt;OrderDto&gt;&gt;</c> is
/// usable where an <c>IRequest&lt;Result&lt;object&gt;&gt;</c> is expected. That is what lets
/// the dispatcher accept any request through a single generic method.
/// </remarks>
#pragma warning disable CA1040 // Avoid empty interfaces — this is a type-level marker, which is the point.
public interface IRequest<out TResponse>;

/// <summary>
/// Marks a request as state-changing, so behaviours can recognise it at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Necessary because <see cref="ICommand"/> and <see cref="ICommand{TResponse}"/> are two
/// unrelated interfaces as far as the CLR is concerned. Without a shared non-generic base, a
/// runtime check in <c>TransactionBehavior</c> would need:
/// </para>
/// <code>
/// request is ICommand || request.GetType().GetInterfaces()
///     .Any(i =&gt; i.IsGenericType &amp;&amp; i.GetGenericTypeDefinition() == typeof(ICommand&lt;&gt;))
/// </code>
/// <para>
/// — reflection on every single request. One shared marker turns that into
/// <c>request is ICommandMarker</c>, which the JIT compiles to a couple of instructions.
/// This is a recurring trick with generic interface families: give them a non-generic base
/// so runtime code can recognise members cheaply.
/// </para>
/// </remarks>
public interface ICommandMarker;

/// <summary>A state-changing operation returning no payload.</summary>
public interface ICommand : IRequest<Result>, ICommandMarker;

/// <summary>
/// A state-changing operation returning a value — typically just the new entity's id.
/// </summary>
/// <typeparam name="TResponse">The payload.</typeparam>
/// <remarks>
/// Resist returning the whole entity from a command. It couples your write model to whatever
/// the caller happens to want to render today, and the next caller wants something different.
/// Return the id and let them issue a query.
/// </remarks>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, ICommandMarker;

/// <summary>A read that changes nothing.</summary>
/// <typeparam name="TResponse">The projection returned.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
#pragma warning restore CA1040

/// <summary>
/// Handles exactly one request type.
/// </summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">What it returns.</typeparam>
/// <remarks>
/// <b>One handler, one file, one job.</b> This is the single biggest practical win of CQRS over
/// a service layer. A traditional <c>OrderService</c> accumulates fifteen methods and eight
/// constructor dependencies, of which any given method uses two — so every unit test has to
/// mock six things it does not care about. A handler declares only what it actually needs.
/// </remarks>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Executes the request.</summary>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Convenience alias for a handler of a payload-free command.</summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>Convenience alias for a handler of a value-returning command.</summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
/// <typeparam name="TResponse">The payload.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>Convenience alias for a query handler.</summary>
/// <typeparam name="TQuery">The query handled.</typeparam>
/// <typeparam name="TResponse">The projection returned.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>
/// The next step in the pipeline. Calling it runs the rest of the chain; not calling it
/// short-circuits.
/// </summary>
/// <typeparam name="TResponse">The response type flowing through the pipeline.</typeparam>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting behaviour wrapped around every handler — logging, validation, transactions, caching.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// Russian dolls: each behaviour receives the request and a <c>next</c> delegate, does something
/// before, calls next, does something after. Registration order is nesting order.
/// </para>
/// <code>
/// Logging ─► Validation ─► Transaction ─► YourHandler
///    ▲            ▲             ▲
///    └── each can inspect, modify, or refuse to call the one inside it
/// </code>
/// <para>
/// This is the same idea as ASP.NET Core middleware, applied one layer down. It is why no
/// handler in this codebase contains a try/catch, a validation call, or a
/// <c>SaveChangesAsync</c> — those concerns live in behaviours, written once.
/// </para>
/// </remarks>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Wraps the rest of the pipeline.</summary>
    Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
