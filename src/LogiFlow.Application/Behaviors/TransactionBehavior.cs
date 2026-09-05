using LogiFlow.Application.Abstractions.Data;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Behaviors;

/// <summary>
/// Commits the unit of work after a command succeeds. Queries are left alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>This behaviour is why no handler in this solution calls <c>SaveChangesAsync</c>.</b>
/// A handler expresses intent — load the order, submit it, reserve the stock — and this decides
/// when that becomes durable. Handlers stay focused on business logic and cannot forget to save.
/// </para>
/// <para>
/// <b>Only commands, and only on success.</b> Two guards matter:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Queries are skipped entirely. Calling <c>SaveChanges</c> after a read is not harmless —
///     if a query accidentally mutated a tracked entity, it would silently persist that. Skipping
///     makes an accidental write impossible rather than merely unlikely.
///   </description></item>
///   <item><description>
///     A failed <see cref="Result"/> is not committed. The handler returned a business failure,
///     so any partial changes it made must be discarded. Because nothing was ever saved, the
///     scoped <c>DbContext</c> is simply disposed at the end of the request and the changes
///     evaporate. There is no rollback to perform.
///   </description></item>
/// </list>
/// <para>
/// <b>The ordering constraint.</b> This must be registered AFTER
/// <see cref="ValidationBehavior{TRequest,TResponse}"/>, so validation runs outside it. Reverse
/// them and you open a transaction, run validation, find the request invalid, and close the
/// transaction having done nothing — holding a database connection the whole time.
/// </para>
/// Covered in: <c>course/module-07-sql-and-transactions/02-unit-of-work.md</c>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="Result"/>.</typeparam>
/// <param name="unitOfWork">The unit of work to commit.</param>
/// <param name="logger">Logger.</param>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Type check against the marker interface. A query never reaches SaveChanges.
        if (request is not ICommandMarker)
        {
            return await next().ConfigureAwait(false);
        }

        TResponse response = await next().ConfigureAwait(false);

        if (response.IsFailure)
        {
            logger.LogDebug(
                "Skipping commit for {RequestName}: handler returned {ErrorCode}",
                typeof(TRequest).Name,
                response.Error.Code);

            return response;
        }

        int affected = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Committed {RowCount} row change(s) for {RequestName}",
            affected,
            typeof(TRequest).Name);

        return response;
    }
}

