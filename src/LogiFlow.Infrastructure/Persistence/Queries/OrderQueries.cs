using LogiFlow.Application.Common;
using LogiFlow.Application.Features.Orders;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Infrastructure.Persistence.Queries;

/// <summary>
/// The read side for orders. Projects to DTOs in SQL; never materialises an aggregate.
/// </summary>
/// <param name="context">The scoped session.</param>
public sealed class OrderQueries(LogiFlowDbContext context) : IOrderQueries
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The join is explicit, and it has to be.</b> <see cref="Order"/> has no
    /// <c>Customer</c> navigation property — that was a deliberate aggregate-boundary decision.
    /// So the customer name is fetched with a LINQ <c>join</c>, which is exactly what the write
    /// model refuses to give you and exactly what a read model is allowed to do.
    /// </para>
    /// <para>
    /// This is CQRS earning its keep: the write side stays strict, the read side stays fast.
    /// </para>
    /// </remarks>
    public async Task<OrderDetailDto?> GetDetailAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        var row = await context.Orders
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Join(
                context.Customers,
                order => order.CustomerId,
                customer => customer.Id,
                (order, customer) => new { order, customer })
            .Select(x => new
            {
                x.order.Id,
                x.order.OrderNumber,
                x.order.CustomerId,
                x.customer.CompanyName,
                CustomerEmail = x.customer.Email,
                x.order.Status,
                x.order.Currency,
                x.order.ShippingAddress,
                x.order.CreatedAtUtc,
                x.order.SubmittedAtUtc,
                x.order.ShippedAtUtc,
                x.order.DeliveredAtUtc,
                x.order.CancellationReason,
                x.order.CustomerTier,

                // A correlated subquery per line, projected inside the same round trip. EF
                // renders this as a LEFT JOIN and groups the results - one query, not one per line.
                Lines = x.order.Lines.Select(l => new
                {
                    l.Id,
                    l.ProductId,
                    l.Sku,
                    l.ProductName,
                    l.Quantity,
                    UnitPrice = l.UnitPrice.Amount,
                }).ToList(),

                // Aggregation pushed into SQL. Summing in C# after the fact would give the same
                // answer here, but the habit matters: on a list endpoint the difference is
                // between SUM() over an index and dragging every line across the wire.
                LineSubtotal = x.order.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity)) ?? 0m,
                TotalWeightGrams = x.order.Lines.Sum(l => (int?)(l.UnitWeight.Grams * l.Quantity)) ?? 0,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // ── The rest happens in memory, on ONE row, and that is the right call ────────────
        // Discount and shipping rules live in the domain (CustomerTierExtensions, and the
        // thresholds on Order). Re-expressing them as translatable SQL would duplicate business
        // logic in a place nobody would think to update. Applying them to a single already-
        // fetched row costs nothing.
        //
        // The line to hold: aggregate in SQL, apply business rules in C#.
        decimal discount = decimal.Round(row.LineSubtotal * row.CustomerTier.DiscountRate(), 2, MidpointRounding.ToEven);
        decimal discountedSubtotal = row.LineSubtotal - discount;
        decimal shipping = CalculateShipping(row.CustomerTier, discountedSubtotal, row.TotalWeightGrams);

        return new OrderDetailDto(
            row.Id.Value,
            row.OrderNumber.Value,
            row.CustomerId.Value,
            row.CompanyName,
            row.CustomerEmail.Value,
            row.Status,
            row.Currency.Code,
            row.LineSubtotal,
            discount,
            shipping,
            discountedSubtotal + shipping,
            row.TotalWeightGrams,
            row.ShippingAddress is null
                ? null
                : new AddressDto(
                    row.ShippingAddress.Line1,
                    row.ShippingAddress.Line2,
                    row.ShippingAddress.City,
                    row.ShippingAddress.Region,
                    row.ShippingAddress.PostalCode,
                    row.ShippingAddress.CountryCode),
            [.. row.Lines.Select(l => new OrderLineDto(
                l.Id.Value,
                l.ProductId.Value,
                l.Sku.Value,
                l.ProductName,
                l.Quantity,
                l.UnitPrice,
                l.UnitPrice * l.Quantity))],
            row.CreatedAtUtc,
            row.SubmittedAtUtc,
            row.ShippedAtUtc,
            row.DeliveredAtUtc,
            row.CancellationReason,
            OrderStateMachine.NextStates(row.Status));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The composable-query pattern.</b> Each optional filter appends a <c>Where</c> to an
    /// <see cref="IQueryable{T}"/>. Nothing executes until <c>ToListAsync</c>, so the generated
    /// SQL contains exactly the predicates the caller supplied — no more.
    /// </para>
    /// <para>
    /// The alternative you will meet in older codebases is one giant SQL string with
    /// <c>WHERE (@status IS NULL OR Status = @status)</c> repeated per filter. That gives SQL
    /// Server a single cached plan that has to work for every combination, and it is usually
    /// terrible for all of them (the "parameter sniffing" problem). Composed queries produce a
    /// distinct, well-optimised plan per shape.
    /// </para>
    /// </remarks>
    public async Task<PagedResult<OrderSummaryDto>> SearchAsync(
        SearchOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Order> orders = context.Orders.AsNoTracking();

        if (query.CustomerId is { } customerId)
        {
            var typed = CustomerId.From(customerId);
            orders = orders.Where(o => o.CustomerId == typed);
        }

        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.FromUtc is { } from)
        {
            orders = orders.Where(o => o.CreatedAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            orders = orders.Where(o => o.CreatedAtUtc < to);
        }

        if (query.MinimumTotal is { } minimum)
        {
            orders = orders.Where(o => o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity) >= minimum);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            string term = query.SearchTerm.Trim();

            // EF.Functions.Like maps to SQL LIKE. A leading wildcard prevents an index seek, so
            // this is a scan - acceptable for an admin search over a filtered set, and the wrong
            // tool for a public search box. Full-text indexing is the answer at that point.
            orders = orders.Where(o => EF.Functions.Like(o.OrderNumber.Value, $"%{term}%"));
        }

        // ── COUNT BEFORE PAGING ──────────────────────────────────────────────────────────
        // This is a SECOND query, and it must run before Skip/Take is applied - otherwise it
        // would count only the current page and every client would see "Page 1 of 1".
        //
        // The cost is real: two round trips, and on a large filtered set the COUNT can be
        // slower than the page itself. Alternatives are an approximate count from statistics,
        // or keyset pagination which needs no count at all.
        int totalCount = await orders.CountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<OrderSummaryDto>.Empty(query.PageNumber, query.Size);
        }

        orders = ApplySort(orders, query.SortField, query.Direction);

        List<OrderSummaryDto> items = await orders
            .Skip(query.Skip)
            .Take(query.Size)
            .Join(
                context.Customers,
                order => order.CustomerId,
                customer => customer.Id,
                (order, customer) => new { order, customer })
            .Select(x => new OrderSummaryDto(
                x.order.Id.Value,
                x.order.OrderNumber.Value,
                x.order.CustomerId.Value,
                x.customer.CompanyName,
                x.order.Status,
                x.order.Lines.Sum(l => (decimal?)(l.UnitPrice.Amount * l.Quantity)) ?? 0m,
                x.order.Currency.Code,
                x.order.Lines.Count,
                x.order.CreatedAtUtc,
                x.order.SubmittedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<OrderSummaryDto>(items, query.PageNumber, query.Size, totalCount);
    }

    /// <summary>
    /// Applies sorting without string-based property lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A switch expression over an enum, returning a differently-ordered <see cref="IQueryable{T}"/>.
    /// Verbose compared with <c>EF.Property&lt;object&gt;(o, sortByString)</c> — and much better:
    /// it is compile-time checked, it cannot be handed an arbitrary column name by a caller, and
    /// adding a sort field without handling it here fails the build (no <c>default</c> arm).
    /// </para>
    /// <para>
    /// <b>The secondary sort on <c>Id</c> is not decoration.</b> <c>OFFSET/FETCH</c> over a
    /// non-unique sort key has undefined ordering among ties, so two rows with the same
    /// <c>CreatedAtUtc</c> can appear on both page 1 and page 2, or on neither. Appending a
    /// unique tiebreaker makes the order total and pagination stable. Almost every paginated
    /// endpoint in the wild has this bug.
    /// </para>
    /// </remarks>
    private static IQueryable<Order> ApplySort(
        IQueryable<Order> orders,
        OrderSortField sortBy,
        SortDirection direction)
    {
        bool ascending = direction == SortDirection.Ascending;

        IOrderedQueryable<Order> sorted = sortBy switch
        {
            OrderSortField.CreatedAt => ascending
                ? orders.OrderBy(o => o.CreatedAtUtc)
                : orders.OrderByDescending(o => o.CreatedAtUtc),

            OrderSortField.SubmittedAt => ascending
                ? orders.OrderBy(o => o.SubmittedAtUtc)
                : orders.OrderByDescending(o => o.SubmittedAtUtc),

            OrderSortField.Total => ascending
                ? orders.OrderBy(o => o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity))
                : orders.OrderByDescending(o => o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity)),

            OrderSortField.OrderNumber => ascending
                ? orders.OrderBy(o => o.OrderNumber.Value)
                : orders.OrderByDescending(o => o.OrderNumber.Value),

            OrderSortField.Status => ascending
                ? orders.OrderBy(o => o.Status)
                : orders.OrderByDescending(o => o.Status),

            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, "Unhandled sort field."),
        };

        // ThenBy on a unique column: the stable-pagination tiebreaker.
        return sorted.ThenBy(o => o.Id);
    }

    /// <summary>Mirrors <c>Order.ShippingCost</c> for the read model.</summary>
    /// <remarks>
    /// <b>Duplicated logic, and it is a genuine cost of CQRS.</b> The rule lives in
    /// <c>Order.ShippingCost</c> for the write side and here for the read side, and the two can
    /// drift. The mitigation is a test —
    /// <c>OrderQueriesTests.Projection_matches_domain_calculation</c> — that builds an order
    /// through the domain, reads it back through this projection, and asserts the totals agree.
    /// If you take one habit from this file, make it that test.
    /// </remarks>
    private static decimal CalculateShipping(CustomerTier tier, decimal discountedSubtotal, int totalWeightGrams)
    {
        decimal threshold = tier.FreeShippingThresholdEur();

        if (threshold == 0m || discountedSubtotal >= threshold)
        {
            return 0m;
        }

        const decimal baseRate = 4.90m;
        const int freeKilos = 5;
        decimal excessKilos = Math.Max(0m, (totalWeightGrams / 1000m) - freeKilos);

        return decimal.Round(baseRate + (excessKilos * 1.50m), 2, MidpointRounding.ToEven);
    }
}
