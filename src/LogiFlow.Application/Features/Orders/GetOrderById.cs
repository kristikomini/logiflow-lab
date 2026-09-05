using FluentValidation;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Behaviors;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Features.Orders;

/// <summary>Loads one order's full detail.</summary>
/// <param name="OrderId">The order wanted.</param>
/// <remarks>
/// Implements <see cref="ICacheableQuery"/>, so <c>CachingBehavior</c> serves repeat reads from
/// Redis. Note the short TTL: an order's status changes as it moves through fulfilment, and a
/// customer refreshing the page expects to see that. Sixty seconds absorbs the burst of reads
/// that follow a page load without making the data visibly stale.
/// </remarks>
public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDetailDto>, ICacheableQuery
{
    /// <inheritdoc />
    public string CacheKey => $"order:detail:{OrderId}";

    /// <inheritdoc />
    public TimeSpan? CacheDuration => TimeSpan.FromSeconds(60);
}

/// <summary>Input validation for <see cref="GetOrderByIdQuery"/>.</summary>
public sealed class GetOrderByIdQueryValidator : AbstractValidator<GetOrderByIdQuery>
{
    /// <summary>Configures the rules.</summary>
    public GetOrderByIdQueryValidator() => RuleFor(x => x.OrderId).NotEmpty();
}

/// <summary>Handles <see cref="GetOrderByIdQuery"/>.</summary>
/// <remarks>
/// Three lines of substance. A query handler that does more than call the read side and map a
/// null to a NotFound is usually a command wearing a disguise.
/// </remarks>
/// <param name="queries">The read side.</param>
internal sealed class GetOrderByIdQueryHandler(IOrderQueries queries)
    : IQueryHandler<GetOrderByIdQuery, OrderDetailDto>
{
    /// <inheritdoc />
    public async Task<Result<OrderDetailDto>> HandleAsync(
        GetOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);

        OrderDetailDto? order = await queries.GetDetailAsync(orderId, cancellationToken).ConfigureAwait(false);

        return order is null
            ? OrderErrors.NotFound(orderId)
            : order;
    }
}
