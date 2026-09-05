using FluentValidation;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Common;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Features.Orders;

/// <summary>Which column to sort a search on.</summary>
/// <remarks>
/// <b>An enum, not a string, and that is a security decision.</b> Accepting
/// <c>?sortBy=CustomerName</c> as free text and interpolating it into SQL or into
/// <c>EF.Property&lt;object&gt;(o, sortBy)</c> is a SQL-injection vector in the first case and
/// a runtime crash on a typo in the second. An enum means the only reachable values are the
/// ones listed here, enforced by the model binder before your code runs.
/// </remarks>
public enum OrderSortField
{
    /// <summary>By creation timestamp.</summary>
    CreatedAt = 0,

    /// <summary>By submission timestamp.</summary>
    SubmittedAt = 1,

    /// <summary>By order value.</summary>
    Total = 2,

    /// <summary>By order reference.</summary>
    OrderNumber = 3,

    /// <summary>By lifecycle state.</summary>
    Status = 4,
}

/// <summary>Searches orders with filters, sorting and paging.</summary>
/// <remarks>
/// Every filter is nullable, meaning "no constraint". The read-side implementation appends a
/// <c>Where</c> only for the ones supplied, so the generated SQL contains exactly the predicates
/// the caller asked for — no <c>WHERE (@status IS NULL OR Status = @status)</c> catch-all, which
/// is a well-known way to give SQL Server an unusable cached plan.
/// </remarks>
public sealed record SearchOrdersQuery : PagedQuery, IQuery<PagedResult<OrderSummaryDto>>
{
    /// <summary>Restrict to one customer.</summary>
    public Guid? CustomerId { get; init; }

    /// <summary>Restrict to one lifecycle state.</summary>
    public OrderStatus? Status { get; init; }

    /// <summary>Only orders created at or after this instant.</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Only orders created strictly before this instant.</summary>
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>Only orders worth at least this much.</summary>
    public decimal? MinimumTotal { get; init; }

    /// <summary>Free-text match against order number and customer name.</summary>
    public string? SearchTerm { get; init; }

    /// <summary>Column to sort on. Omit for <see cref="OrderSortField.CreatedAt"/>.</summary>
    /// <remarks>
    /// Nullable so <c>[AsParameters]</c> treats it as optional — see the remarks on
    /// <see cref="PagedQuery"/>. <see cref="SortField"/> resolves the default.
    /// </remarks>
    public OrderSortField? SortBy { get; init; }

    /// <summary>Sort direction. Omit for <see cref="Common.SortDirection.Descending"/>.</summary>
    public SortDirection? SortDirection { get; init; }

    /// <summary>The sort column actually used.</summary>
    public OrderSortField SortField => SortBy ?? OrderSortField.CreatedAt;

    /// <summary>The sort direction actually used. Newest-first is the useful default for orders.</summary>
    public SortDirection Direction => SortDirection ?? Common.SortDirection.Descending;
}

/// <summary>Input validation for <see cref="SearchOrdersQuery"/>.</summary>
public sealed class SearchOrdersQueryValidator : AbstractValidator<SearchOrdersQuery>
{
    /// <summary>Configures the rules.</summary>
    public SearchOrdersQueryValidator()
    {
        // Page and PageSize are clamped by PagedQuery itself, so they need no rule here -
        // an invalid value is impossible to construct rather than merely rejected.

        RuleFor(x => x.SearchTerm)
            .MaximumLength(200)
            .When(x => x.SearchTerm is not null);

        RuleFor(x => x.MinimumTotal)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinimumTotal.HasValue);

        RuleFor(x => x)
            .Must(q => q.FromUtc is null || q.ToUtc is null || q.FromUtc < q.ToUtc)
            .WithMessage("'FromUtc' must be earlier than 'ToUtc'.")
            .WithName(nameof(SearchOrdersQuery.FromUtc));

        // Only validate when supplied. A null means "use the default", which is always valid.
        RuleFor(x => x.SortBy).IsInEnum().When(x => x.SortBy.HasValue);
        RuleFor(x => x.SortDirection).IsInEnum().When(x => x.SortDirection.HasValue);
    }
}

/// <summary>Handles <see cref="SearchOrdersQuery"/>.</summary>
/// <remarks>
/// Deliberately <i>not</i> cacheable. The key would have to encode eight filter values, and the
/// resulting cardinality means almost every request is a unique key — you would fill Redis with
/// entries that are never read twice, and pay a serialisation cost for the privilege. Cache
/// things that are read far more often than they vary.
/// </remarks>
/// <param name="queries">The read side.</param>
internal sealed class SearchOrdersQueryHandler(IOrderQueries queries)
    : IQueryHandler<SearchOrdersQuery, PagedResult<OrderSummaryDto>>
{
    /// <inheritdoc />
    public async Task<Result<PagedResult<OrderSummaryDto>>> HandleAsync(
        SearchOrdersQuery request,
        CancellationToken cancellationToken)
    {
        PagedResult<OrderSummaryDto> page = await queries
            .SearchAsync(request, cancellationToken)
            .ConfigureAwait(false);

        // An empty page is a successful search that found nothing - a 200 with zero rows,
        // not a 404. Returning NotFound here is a classic REST design error.
        return page;
    }
}
