using System.Linq.Expressions;
using LogiFlow.Domain.Common.Specifications;
using LogiFlow.Domain.Customers;

namespace LogiFlow.Domain.Orders;

// ─────────────────────────────────────────────────────────────────────────────────────────
//  Named, reusable business rules over orders.
//
//  Each is a real rule with a name a domain expert would recognise. `new StaleDraftsSpec(30)`
//  says something; `o => o.Status == OrderStatus.Draft && o.CreatedAtUtc < cutoff` makes the
//  reader reconstruct the intent every time they meet it.
//
//  They compose:
//      var target = new HighValueOrdersSpec(1000m).And(new PlacedByTierSpec(CustomerTier.Gold));
//
//  ...and that composition still becomes a single SQL WHERE clause, because every one of them
//  is an expression tree rather than a compiled delegate.
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Orders currently in a given status.</summary>
/// <param name="status">The status to match.</param>
public sealed class OrdersInStatusSpec(OrderStatus status) : Specification<Order>
{
    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() => order => order.Status == status;
}

/// <summary>Orders belonging to one customer.</summary>
/// <param name="customerId">The customer.</param>
public sealed class OrdersForCustomerSpec(CustomerId customerId) : Specification<Order>
{
    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() => order => order.CustomerId == customerId;
}

/// <summary>Orders submitted within a date window.</summary>
/// <param name="fromUtc">Inclusive lower bound.</param>
/// <param name="toUtc">Exclusive upper bound.</param>
/// <remarks>
/// Inclusive-start / exclusive-end (<c>[from, to)</c>) is the convention to use for date ranges.
/// It makes adjacent periods tile perfectly with no gap and no overlap, and it sidesteps the
/// "does 23:59:59.997 count as part of the day?" bug that inclusive-end ranges always produce
/// once someone stores a timestamp with sub-second precision.
/// </remarks>
public sealed class OrdersSubmittedBetweenSpec(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    : Specification<Order>
{
    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.SubmittedAtUtc != null
                 && order.SubmittedAtUtc >= fromUtc
                 && order.SubmittedAtUtc < toUtc;
}

/// <summary>Draft orders abandoned for longer than a given number of days.</summary>
/// <param name="olderThanDays">Age threshold in days.</param>
/// <remarks>
/// The cutoff is computed once in the constructor rather than inside the expression. Putting
/// <c>DateTimeOffset.UtcNow</c> inside the tree would leave EF Core to translate it into
/// <c>SYSDATETIMEOFFSET()</c>, which makes the query non-deterministic, unrepeatable in a test,
/// and impossible for the database to cache a plan for meaningfully.
/// </remarks>
public sealed class StaleDraftsSpec(int olderThanDays) : Specification<Order>
{
    private readonly DateTimeOffset _cutoff = DateTimeOffset.UtcNow.AddDays(-olderThanDays);

    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Status == OrderStatus.Draft && order.CreatedAtUtc < _cutoff;
}

/// <summary>Orders still owed to a customer: submitted or confirmed, not yet shipped.</summary>
public sealed class OpenOrdersSpec : Specification<Order>
{
    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Status == OrderStatus.Submitted || order.Status == OrderStatus.Confirmed;
}

/// <summary>Orders placed by customers in a given loyalty tier.</summary>
/// <param name="tier">The tier to match.</param>
public sealed class PlacedByTierSpec(CustomerTier tier) : Specification<Order>
{
    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() => order => order.CustomerTier == tier;
}

/// <summary>
/// Orders whose line values sum above a threshold.
/// </summary>
/// <param name="minimumSubtotal">Threshold, in the order's own currency.</param>
/// <remarks>
/// <para>
/// <b>Read the expression carefully — this is the important one.</b> It filters on
/// <c>order.Lines.Sum(...)</c>, a computation over a child collection. It cannot filter on
/// <c>order.Subtotal</c>, even though that property exists and would be far more readable,
/// because <c>Subtotal</c> is a C# property with no column behind it. EF Core has no idea
/// what it means and throws a translation error.
/// </para>
/// <para>
/// The rule: <b>an expression tree may only reference things the database knows about</b> —
/// mapped columns, navigations, and the subset of methods the provider recognises. Everything
/// else must be written in terms of those. Here EF turns it into a correlated subquery:
/// </para>
/// <code>
/// WHERE (SELECT SUM(l.UnitPrice_Amount * l.Quantity) FROM OrderLines l
///        WHERE l.OrderId = o.Id) &gt;= @minimumSubtotal
/// </code>
/// </remarks>
public sealed class HighValueOrdersSpec(decimal minimumSubtotal) : Specification<Order>
{
    /// <inheritdoc />
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.Lines.Sum(line => line.UnitPrice.Amount * line.Quantity) >= minimumSubtotal;
}
