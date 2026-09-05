using LogiFlow.Application.Common;
using LogiFlow.Domain.Orders;

namespace LogiFlow.Application.Features.Orders;

/// <summary>
/// The read side for orders. Projects straight to DTOs without loading aggregates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate interface from <c>IOrderRepository</c>?</b> This is CQRS made concrete.
/// The repository serves the <i>write</i> side: it returns fully-loaded <see cref="Order"/>
/// aggregates so business rules can be enforced. This serves the <i>read</i> side: it returns
/// flat DTOs, projected in SQL, with no change tracking and no domain objects at all.
/// </para>
/// <para>
/// Forcing reads through the repository is the mistake that makes people conclude "Clean
/// Architecture is slow". Rendering a 25-row order list would load 25 aggregates with all their
/// lines, materialise every column, and register them all with the change tracker — to display
/// six fields. The implementation of this interface issues a single query returning exactly
/// those six columns.
/// </para>
/// <para>
/// The trade-off you are accepting: the read side bypasses the domain model, so any logic
/// expressed in C# properties (like <c>Order.Total</c>) has to be re-expressed in the
/// projection. That duplication is real. It is the price of fast reads, and it is why the
/// projections in <c>OrderQueries</c> are covered by integration tests that compare them
/// against the domain's own calculation.
/// </para>
/// Covered in: <c>course/module-08-cqrs/04-read-models-and-projections.md</c>
/// </remarks>
public interface IOrderQueries
{
    /// <summary>Loads the full detail view for one order.</summary>
    Task<OrderDetailDto?> GetDetailAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>Searches orders with filtering, sorting and paging.</summary>
    Task<PagedResult<OrderSummaryDto>> SearchAsync(
        SearchOrdersQuery query,
        CancellationToken cancellationToken = default);
}
