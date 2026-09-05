namespace LogiFlow.Application.Abstractions.Data;

/// <summary>
/// Commits every change made during one request as a single atomic transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why does this exist when repositories could just save themselves?</b> Because a use case
/// usually touches more than one aggregate. Submitting an order updates the <c>Order</c>,
/// reserves stock on a <c>Warehouse</c>, and writes an outbox row. If each repository called
/// <c>SaveChanges</c>, a failure halfway through would leave the order submitted with no stock
/// reserved — and no way to tell.
/// </para>
/// <para>
/// One <c>SaveChangesAsync</c> at the end of the request means all of it lands or none of it
/// does. <c>TransactionBehavior</c> calls it exactly once, so no handler in this codebase
/// contains a save call at all.
/// </para>
/// <para>
/// <b>A common interview question:</b> "isn't EF Core's DbContext already a unit of work?"
/// Yes — precisely. <c>DbContext</c> implements both patterns already (<c>DbSet&lt;T&gt;</c> is
/// the repository, <c>SaveChanges</c> is the unit of work). This interface exists purely so the
/// Application layer can express "commit now" without referencing EF Core. If you were content
/// to depend on EF everywhere, you could delete this file and lose nothing but testability
/// and the ability to swap providers.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Writes every pending change and dispatches the domain events raised along the way.
    /// </summary>
    /// <returns>The number of rows affected.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit database transaction, retrying on
    /// transient failures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the ordinary case you do not need this: <c>SaveChangesAsync</c> already wraps itself
    /// in a transaction. Reach for it when a use case must read, decide, and write atomically
    /// across several saves — for example the stock-reservation retry loop.
    /// </para>
    /// <para>
    /// It also matters because EF Core's execution strategy (which retries transient SQL Server
    /// errors like deadlocks and connection resets) refuses to run when a user-initiated
    /// transaction is open, unless the whole block is handed to the strategy. This method does
    /// that correctly — a subtle failure mode that produces the runtime error
    /// "The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
    /// user-initiated transactions".
    /// </para>
    /// </remarks>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
