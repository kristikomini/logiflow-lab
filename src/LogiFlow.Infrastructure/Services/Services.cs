using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Domain.Orders;
using LogiFlow.Domain.Results;
using LogiFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Services;

/// <summary>Clock backed by <see cref="TimeProvider"/>.</summary>
/// <remarks>
/// Wrapping <see cref="TimeProvider"/> (.NET 8+) rather than calling
/// <c>DateTimeOffset.UtcNow</c> directly means tests can substitute
/// <c>Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider</c> and control the clock —
/// including advancing it instantly rather than sleeping.
/// </remarks>
/// <param name="timeProvider">The underlying clock. <see cref="TimeProvider.System"/> in production.</param>
public sealed class DateTimeProvider(TimeProvider timeProvider) : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}

/// <summary>
/// Issues order references from a SQL Server sequence.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>NEXT VALUE FOR</c> is atomic and does not block.</b> Two concurrent callers get two
/// different numbers with no lock contention, because a sequence is a dedicated allocator rather
/// than a row anyone has to lock.
/// </para>
/// <para>
/// <b>What it is not:</b> gapless. A sequence value consumed by a transaction that then rolls
/// back is simply lost, so order numbers can skip. If your accountants require a gapless series
/// — and in some jurisdictions invoice numbering legally must be — a sequence is the wrong tool
/// and you need a dedicated counter table with a real lock, accepting the contention that
/// implies. Knowing which constraint you are under before choosing is the whole point.
/// </para>
/// </remarks>
/// <param name="context">The scoped session.</param>
/// <param name="clock">Supplies the current year.</param>
public sealed class OrderNumberGenerator(LogiFlowDbContext context, IDateTimeProvider clock) : IOrderNumberGenerator
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Why a raw <see cref="DbCommand"/> and not <c>Database.SqlQuery&lt;int&gt;</c>?</b>
    /// The obvious version looks like this and fails at runtime:
    /// </para>
    /// <code>
    /// context.Database.SqlQuery&lt;int&gt;($"SELECT NEXT VALUE FOR logiflow.OrderNumbers")
    /// </code>
    /// <code>
    /// Msg 11719: NEXT VALUE FOR function is not allowed in check constraints, default objects,
    /// computed columns, views, ... sub-queries, common table expressions, derived tables ...
    /// </code>
    /// <para>
    /// <c>SqlQuery&lt;T&gt;</c> composes: EF wraps whatever you give it as a derived table so it
    /// can apply <c>Where</c>, <c>OrderBy</c> and the rest — turning the statement into
    /// <c>SELECT [v].[Value] FROM (SELECT NEXT VALUE FOR ...) AS [v]</c>. SQL Server forbids
    /// <c>NEXT VALUE FOR</c> inside a derived table, so it rejects the whole thing.
    /// </para>
    /// <para>
    /// Dropping to ADO.NET runs the statement exactly as written. The lesson generalises: EF's
    /// raw-SQL helpers are still <i>composable</i> query builders, and any statement that must
    /// be executed verbatim needs a real command.
    /// </para>
    /// <para>
    /// <b>The transaction line is not optional.</b> A <c>DbCommand</c> created from the
    /// connection is not automatically enlisted in the ambient EF transaction. Omit it and SQL
    /// Server throws "ExecuteScalar requires the command to have a transaction when the
    /// connection assigned to the command is in a pending local transaction" — a genuinely
    /// confusing error the first time you meet it.
    /// </para>
    /// </remarks>
    public async Task<OrderNumber> NextAsync(CancellationToken cancellationToken = default)
    {
        DatabaseFacade database = context.Database;

        await using DbCommand command = database.GetDbConnection().CreateCommand();

        // A compile-time constant, never user input. Interpolating anything user-supplied into
        // a CommandText is precisely how SQL injection happens.
        command.CommandText = "SELECT NEXT VALUE FOR logiflow.OrderNumbers";

        // Enlist in the ambient transaction, if one is open. Without this the sequence read
        // would run outside the transaction that is about to insert the order.
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();

        // The connection may already be open (inside a transaction) or not (first use in the
        // request). Opening an open connection throws, so check first.
        bool opened = false;
        if (command.Connection!.State != ConnectionState.Open)
        {
            await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        try
        {
            object? scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            int next = scalar is null or DBNull
                ? throw new InvalidOperationException("The order-number sequence returned no value.")
                : Convert.ToInt32(scalar, CultureInfo.InvariantCulture);

            Result<OrderNumber> number = OrderNumber.Create(clock.UtcNow.Year, next);

            return number.IsSuccess
                ? number.Value
                : throw new InvalidOperationException(
                    $"Order number sequence produced an invalid value: {number.Error.Description}");
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
}

/// <summary>Reads the caller's identity from the current HTTP request's claims.</summary>
/// <remarks>
/// The concrete <c>ClaimsPrincipal</c> is supplied by the API layer, so Infrastructure needs no
/// reference to ASP.NET Core hosting types. See <c>HttpContextCurrentUser</c> in the API project.
/// </remarks>
public sealed class CurrentUser : ICurrentUser
{
    private ClaimsPrincipal? _principal;

    /// <inheritdoc />
    /// <remarks>
    /// <c>FindFirst(...)?.Value</c> rather than the <c>FindFirstValue</c> extension, which lives
    /// in an ASP.NET Core package this layer deliberately does not reference.
    /// </remarks>
    public Guid? UserId =>
        Guid.TryParse(_principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out Guid id) ? id : null;

    /// <inheritdoc />
    public string? Email => _principal?.FindFirst(ClaimTypes.Email)?.Value;

    /// <inheritdoc />
    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public bool IsInRole(string role) => _principal?.IsInRole(role) ?? false;

    /// <summary>Called once per request by the API layer's middleware.</summary>
    public void SetPrincipal(ClaimsPrincipal? principal) => _principal = principal;
}

/// <summary>Object-friendly wrapper over <see cref="IDistributedCache"/>.</summary>
/// <param name="cache">The distributed cache.</param>
/// <param name="logger">Logger.</param>
public sealed class CacheService(IDistributedCache cache, ILogger<CacheService> logger) : ICacheService
{
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    /// <remarks>
    /// <b>A cache failure must never fail the request.</b> If Redis is down, the correct
    /// behaviour is a cache miss and a slightly slower response — not a 500. Catching broadly
    /// here is one of the few places where it is right, and it is why the exception is logged
    /// rather than swallowed silently.
    /// </remarks>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            byte[]? bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

            return bytes is null or { Length: 0 }
                ? null
                : JsonSerializer.Deserialize<T>(bytes, SerializerOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache read failed for {CacheKey}; treating as a miss", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);

            var options = new DistributedCacheEntryOptions
            {
                // Absolute, not sliding. A sliding expiry on a frequently-read key means it is
                // never evicted and can serve stale data indefinitely - which is precisely the
                // failure mode people blame on "the cache being broken".
                AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration,
            };

            await cache.SetAsync(key, bytes, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache write failed for {CacheKey}; continuing without caching", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache eviction failed for {CacheKey}", key);
        }
    }

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        T? cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        T created = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, created, expiration, cancellationToken).ConfigureAwait(false);

        return created;
    }
}

// The email transport that used to sit here - a null object that logged instead of sending -
// now lives in LogiFlow.Infrastructure.Mailing, beside the three other transports, the durable
// queue, the delivery worker and the options that choose between them. Start at
// Mailing/MailingServiceCollectionExtensions.cs; it is the entry point and says why each piece
// is registered the way it is.
