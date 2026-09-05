using LogiFlow.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LogiFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>.
/// </summary>
/// <param name="context">The scoped session.</param>
public sealed class UnitOfWork(LogiFlowDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Read this method carefully — it fixes a bug most codebases have.</b>
    /// </para>
    /// <para>
    /// The obvious implementation is <c>await using var tx = await BeginTransactionAsync()</c>,
    /// run the work, commit. With <c>EnableRetryOnFailure</c> configured (and you should have it
    /// configured — SQL Server drops connections, and Azure SQL does so routinely), that version
    /// throws at runtime:
    /// </para>
    /// <code>
    /// The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support
    /// user-initiated transactions.
    /// </code>
    /// <para>
    /// The reason is subtle: the retry strategy can only retry an operation it controls
    /// end-to-end. If you open the transaction yourself, a retry would re-run half the work
    /// inside a transaction that has already been rolled back. EF refuses rather than corrupt
    /// your data.
    /// </para>
    /// <para>
    /// The fix is to hand the <i>whole</i> block to the strategy via
    /// <see cref="IExecutionStrategy.ExecuteAsync{TState,TResult}"/>, so it can re-run the
    /// transaction from the beginning. Which also means: <b>the operation must be idempotent</b>,
    /// because it genuinely may run more than once.
    /// </para>
    /// </remarks>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            async ct =>
            {
                await using IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                T result = await operation(ct).ConfigureAwait(false);

                await context.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return result;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
