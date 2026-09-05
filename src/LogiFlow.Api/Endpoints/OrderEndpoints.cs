using LogiFlow.Api.Infrastructure;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Common;
using LogiFlow.Application.Features.Orders;
using LogiFlow.Domain.Results;

namespace LogiFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for orders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Minimal APIs rather than controllers.</b> Both are fully supported and neither is going
/// away, but minimal APIs are the modern default: less ceremony, measurably faster (no
/// reflection-based action invocation), and better suited to the one-endpoint-one-handler shape
/// CQRS already imposes. Controllers still earn their place when you need OData, complex model
/// binding, or a large inherited filter hierarchy.
/// </para>
/// <para>
/// <b>Look at how little each endpoint does:</b> bind the request, dispatch it, map the result.
/// No business logic, no validation, no try/catch, no <c>SaveChanges</c>. Every one of those
/// lives in a behaviour or in the domain. An endpoint that grows an <c>if</c> statement about
/// business state is a smell — that rule belongs one layer down.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/02-minimal-apis.md</c>
/// </remarks>
public static class OrderEndpoints
{
    /// <summary>Registers the <c>orders</c> routes, under whichever version group they are mapped onto.</summary>
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A route group: shared prefix, shared tag, shared auth policy. Configure the
        // cross-cutting concerns once instead of decorating six endpoints identically.
        RouteGroupBuilder group = app
            .MapGroup("/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapPost("/", CreateOrder)
            .WithName(nameof(CreateOrder))
            .WithSummary("Opens a new draft order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{orderId:guid}", GetOrder)
            .WithName(nameof(GetOrder))
            .WithSummary("Reads a single order in full.")
            .Produces<OrderDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", SearchOrders)
            .WithName(nameof(SearchOrders))
            .WithSummary("Searches orders with filtering, sorting and paging.")
            .Produces<PagedResult<OrderSummaryDto>>();

        group.MapPost("/{orderId:guid}/lines", AddLine)
            .WithName(nameof(AddLine))
            .WithSummary("Adds a product to a draft order, merging into an existing line if present.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{orderId:guid}/submit", SubmitOrder)
            .WithName(nameof(SubmitOrder))
            .WithSummary("Places the order, reserving stock and confirming to the customer.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{orderId:guid}/cancel", CancelOrder)
            .WithName(nameof(CancelOrder))
            .WithSummary("Cancels an order that has not yet shipped.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <remarks>
    /// <b>Why POST and not PUT?</b> The server chooses the id, so the same request sent twice
    /// creates two orders — it is not idempotent, and PUT promises idempotency. Getting this
    /// backwards means clients and proxies retry your creates and you get duplicates.
    /// </remarks>
    private static async Task<IResult> CreateOrder(
        CreateOrderCommand command,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<Guid> result = await dispatcher.SendAsync(command, cancellationToken);

        return result.ToCreatedResult(id => $"/api/orders/{id}");
    }

    private static async Task<IResult> GetOrder(
        Guid orderId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<OrderDetailDto> result = await dispatcher.SendAsync(
            new GetOrderByIdQuery(orderId),
            cancellationToken);

        return result.ToHttpResult();
    }

    /// <remarks>
    /// <para>
    /// <c>[AsParameters]</c> binds the query string onto the record's properties, so
    /// <c>?status=Submitted&amp;page=2&amp;sortBy=Total</c> populates <see cref="SearchOrdersQuery"/>
    /// directly. Without it you would list ten parameters in the signature and construct the
    /// query by hand.
    /// </para>
    /// <para>
    /// Note that <see cref="PagedQuery"/> clamps <c>PageSize</c> in its own <c>init</c> accessor,
    /// so a client sending <c>?pageSize=1000000</c> is capped before any of your code runs.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SearchOrders(
        [AsParameters] SearchOrdersQuery query,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<OrderSummaryDto>> result = await dispatcher.SendAsync(query, cancellationToken);

        return result.ToHttpResult();
    }

    /// <remarks>
    /// The order id comes from the ROUTE, the rest from the body. Rebuilding the command with the
    /// route value means a body claiming a different <c>orderId</c> is ignored rather than
    /// obeyed — otherwise a caller could authorise against one order and act on another.
    /// </remarks>
    private static async Task<IResult> AddLine(
        Guid orderId,
        AddOrderLineRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new AddOrderLineCommand(orderId, request.ProductId, request.Quantity),
            cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> SubmitOrder(
        Guid orderId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        Result result = await dispatcher.SendAsync(new SubmitOrderCommand(orderId), cancellationToken);

        return result.ToHttpResult();
    }

    /// <remarks>
    /// <b>POST /cancel rather than DELETE.</b> Cancelling is a state transition that keeps the
    /// order and records why; DELETE implies the resource ceases to exist. Modelling business
    /// verbs as sub-resource POSTs is the pragmatic middle ground between strict REST and an
    /// RPC free-for-all, and it is what most production APIs settle on.
    /// </remarks>
    private static async Task<IResult> CancelOrder(
        Guid orderId,
        CancelOrderRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result result = await dispatcher.SendAsync(
            new CancelOrderCommand(orderId, request.Reason),
            cancellationToken);

        return result.ToHttpResult();
    }
}

/// <summary>Request body for adding a line to an order.</summary>
/// <param name="ProductId">The product to add.</param>
/// <param name="Quantity">Units to add.</param>
/// <remarks>
/// A separate type from <see cref="AddOrderLineCommand"/> because the command's
/// <c>OrderId</c> comes from the route, not the body. Binding the command directly would put
/// <c>orderId</c> in two places and invite them to disagree.
/// </remarks>
public sealed record AddOrderLineRequest(Guid ProductId, int Quantity);

/// <summary>Request body for cancelling an order.</summary>
/// <param name="Reason">Why the order is being cancelled.</param>
public sealed record CancelOrderRequest(string Reason);
