using System.Data;
using System.Data.Common;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// The two raw statements the email queue needs: claim a batch, and prune delivered history.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separated from the worker so the SQL can be tested.</b> <see cref="EmailDeliveryService"/>
/// is a <c>BackgroundService</c> with a timer loop; testing the claim through it would mean
/// starting a host and waiting for a tick. Here it is a method taking a context — an integration
/// test calls it twice concurrently against a real SQL Server and proves that two workers split
/// the queue rather than duplicating it, which is the one property a unit test cannot establish.
/// </para>
/// <para>
/// <b>This class is where the provider-specific code lives, and all of it.</b> The hints and
/// <c>OUTPUT</c> below are T-SQL. Everything else in the mailing system is provider-agnostic, so
/// a port to PostgreSQL (<c>FOR UPDATE SKIP LOCKED</c> in a CTE with <c>RETURNING</c>) is one
/// file rather than a rewrite. Concentrating the untranslatable parts is what makes that true.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-07-sql-and-transactions/</c>
/// </remarks>
internal static class QueuedEmailStore
{
    /// <summary>
    /// Takes ownership of up to <paramref name="batchSize"/> due messages, in one statement.
    /// </summary>
    /// <param name="context">The session to run against.</param>
    /// <param name="now">The current instant, from the injected clock.</param>
    /// <param name="batchSize">How many messages to claim at most.</param>
    /// <param name="lease">How long the claimed messages stay invisible to other workers.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The ids the caller now owns.</returns>
    /// <remarks>
    /// <para>
    /// <b>Read the SQL, because every clause in it is load-bearing.</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>A CTE with <c>TOP ... ORDER BY</c>, updated in place.</b> Selecting the rows and
    ///     taking them happen in one statement, so there is no window between deciding what to
    ///     claim and claiming it. <c>UPDATE TOP (n)</c> alone would work but cannot be ordered,
    ///     and a queue that delivers in arbitrary order is not a queue.
    ///   </description></item>
    ///   <item><description>
    ///     <b><c>UPDLOCK</c></b> takes the update lock while reading rather than upgrading a
    ///     shared lock afterwards — which is the classic deadlock between two workers that have
    ///     each read the same row and both want to write it.
    ///   </description></item>
    ///   <item><description>
    ///     <b><c>READPAST</c></b> skips rows another worker already holds instead of blocking on
    ///     them. Without it, two workers serialise into one and the second spends its life
    ///     waiting; with it, they split the queue.
    ///   </description></item>
    ///   <item><description>
    ///     <b><c>ROWLOCK</c></b> discourages SQL Server from escalating to a page or table lock
    ///     on a larger batch, which would defeat <c>READPAST</c> entirely.
    ///   </description></item>
    ///   <item><description>
    ///     <b><c>OUTPUT INSERTED.Id</c></b> returns what was claimed in the same round trip.
    ///     Update-then-select-what-you-think-you-updated is a second race.
    ///   </description></item>
    ///   <item><description>
    ///     <b><c>AttemptCount</c> is incremented here, not after sending.</b> A message whose
    ///     content crashes the worker would otherwise be claimed, kill the process, be claimed
    ///     again on restart and repeat forever with its count still at zero.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <b>Why a raw <see cref="DbCommand"/> rather than <c>FromSql</c>?</b> The same reason
    /// <c>OrderNumberGenerator</c> uses one: EF's raw-SQL helpers are composable query builders
    /// that may wrap the statement in a derived table, and a data-modifying statement cannot be
    /// wrapped. A command runs exactly the text it is given.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<Guid>> ClaimBatchAsync(
        LogiFlowDbContext context,
        DateTimeOffset now,
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        const string sql = """
            WITH due AS (
                SELECT TOP (@batchSize) Id, AttemptCount, NextAttemptUtc
                FROM logiflow.QueuedEmails WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE SentAtUtc IS NULL
                  AND AbandonedAtUtc IS NULL
                  AND NextAttemptUtc <= @now
                ORDER BY NextAttemptUtc, Id
            )
            UPDATE due
            SET AttemptCount = AttemptCount + 1,
                NextAttemptUtc = @leaseUntil
            OUTPUT INSERTED.Id;
            """;

        return await ExecuteAsync(
            context,
            sql,
            command =>
            {
                AddParameter(command, "@batchSize", DbType.Int32, batchSize);
                AddParameter(command, "@now", DbType.DateTimeOffset, now);
                AddParameter(command, "@leaseUntil", DbType.DateTimeOffset, now + lease);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a bounded slice of delivered messages older than the cutoff.</summary>
    /// <param name="context">The session to run against.</param>
    /// <param name="cutoff">Delivered before this instant.</param>
    /// <param name="batchSize">Upper bound on rows removed by this call.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>How many rows were deleted.</returns>
    /// <remarks>
    /// <para>
    /// <b>Bounded, because an unbounded delete is an outage.</b>
    /// <c>DELETE FROM ... WHERE SentAtUtc &lt; @cutoff</c> against a table that has been
    /// accumulating for a year is one transaction removing millions of rows: it holds locks for
    /// minutes, blocks the queue it is meant to be tidying, and can escalate to a table lock.
    /// A slice per hour reaches the same state without anybody noticing.
    /// </para>
    /// <para>
    /// <b>Only delivered rows.</b> Abandoned ones stay forever: a message a customer should have
    /// received and did not is exactly what somebody will ask about, and a cleanup job that
    /// erases the evidence of its own failures is worse than none.
    /// </para>
    /// </remarks>
    public static async Task<int> DeleteDeliveredBeforeAsync(
        LogiFlowDbContext context,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        const string sql = """
            DELETE TOP (@batchSize) FROM logiflow.QueuedEmails
            WHERE SentAtUtc IS NOT NULL AND SentAtUtc < @cutoff;
            """;

        DatabaseFacade database = context.Database;

        await using DbCommand command = database.GetDbConnection().CreateCommand();

        command.CommandText = sql;
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();

        AddParameter(command, "@batchSize", DbType.Int32, batchSize);
        AddParameter(command, "@cutoff", DbType.DateTimeOffset, cutoff);

        bool opened = await OpenIfClosedAsync(database, command, cancellationToken).ConfigureAwait(false);

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (opened)
            {
                await database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<Guid>> ExecuteAsync(
        LogiFlowDbContext context,
        string sql,
        Action<DbCommand> configure,
        CancellationToken cancellationToken)
    {
        DatabaseFacade database = context.Database;

        await using DbCommand command = database.GetDbConnection().CreateCommand();

        command.CommandText = sql;

        // A DbCommand created from the connection is NOT automatically enlisted in EF's ambient
        // transaction. Omit this and SQL Server refuses the command outright when one is open.
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();

        configure(command);

        bool opened = await OpenIfClosedAsync(database, command, cancellationToken).ConfigureAwait(false);

        try
        {
            List<Guid> ids = [];

            await using DbDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetGuid(0));
            }

            return ids;
        }
        finally
        {
            // Close only what we opened. Closing a connection the DbContext was already using
            // would break the rest of the unit of work.
            if (opened)
            {
                await database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> OpenIfClosedAsync(
        DatabaseFacade database,
        DbCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Connection!.State == ConnectionState.Open)
        {
            return false;
        }

        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        DbParameter parameter = command.CreateParameter();

        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;

        command.Parameters.Add(parameter);
    }
}
