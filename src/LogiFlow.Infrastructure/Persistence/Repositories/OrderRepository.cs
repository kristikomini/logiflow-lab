using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Domain.Common.Specifications;
using LogiFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IOrderRepository"/>.</summary>
/// <param name="context">The scoped session.</param>
public sealed class OrderRepository(LogiFlowDbContext context) : IOrderRepository
{
    /// <inheritdoc />
    public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken = default) =>
        context.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b><c>Include</c> is what makes this different from <see cref="GetAsync"/>, and it is
    /// the fix for the N+1 problem.</b> Without it, EF returns the order with an empty
    /// <c>Lines</c> collection (lazy loading is deliberately off), and code that iterates the
    /// lines silently sees nothing.
    /// </para>
    /// <para>
    /// With lazy loading <i>on</i>, the same code would issue one extra query per order — the
    /// N+1 problem. Fetch what you need, explicitly, and the query count stays predictable.
    /// </para>
    /// <para>
    /// <b>Why <c>AsSplitQuery</c> is deliberately NOT used here.</b> A single order with a dozen
    /// lines produces a small, harmless cartesian product in one round trip. Split query would
    /// make it two round trips to save duplicating a few header columns — a bad trade. Split
    /// query earns its keep with <i>multiple</i> collection includes, where the product
    /// multiplies. See <c>course/module-06-efcore/04-n-plus-one.md</c>.
    /// </para>
    /// </remarks>
    public Task<Order?> GetWithLinesAsync(OrderId id, CancellationToken cancellationToken = default) =>
        context.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Order?> GetByNumberAsync(OrderNumber number, CancellationToken cancellationToken = default) =>
        context.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.OrderNumber == number, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b><c>AsNoTracking</c> because these orders are read, not modified.</b> By default EF
    /// snapshots every entity it returns so it can detect changes later. For a read-only list
    /// that is pure overhead — roughly 20-30% slower and materially more memory on large
    /// result sets, for a capability nobody uses.
    /// </para>
    /// <para>
    /// The rule: <b>tracking for writes, no-tracking for reads.</b> The one thing to watch is
    /// that no-tracking queries return a fresh instance every time, so identity resolution is
    /// off — two references to the same row give you two objects.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Order>> ListAsync(
        Specification<Order> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        // The implicit conversion on Specification<T> means .Where(specification) works directly.
        // What arrives at EF is an expression tree, so the predicate becomes a SQL WHERE clause
        // rather than a filter applied after loading the table.
        return await context.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
            .Where(specification)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(Specification<Order> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        // SELECT COUNT(*). No rows leave the database.
        return context.Orders.CountAsync(specification, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> AnyAsync(Specification<Order> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        // Translates to `IF EXISTS(...)`, which stops at the first matching row.
        // `CountAsync(...) > 0` would count every match before answering a yes/no question -
        // a small, extremely common, and completely free win.
        return context.Orders.AnyAsync(specification, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Synchronous on purpose. <c>Add</c> only attaches the entity to the change tracker; no I/O
    /// happens until <c>SaveChangesAsync</c>. EF's own <c>AddAsync</c> exists solely for value
    /// generators that need a database round trip (like HiLo), and using it elsewhere just
    /// makes readers think a query is being issued.
    /// </remarks>
    public void Add(Order order) => context.Orders.Add(order);

    /// <inheritdoc />
    public void Remove(Order order) => context.Orders.Remove(order);
}
