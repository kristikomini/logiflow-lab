using LogiFlow.Domain.Orders;

namespace LogiFlow.Application.Abstractions.Services;

/// <summary>
/// Supplies the current time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never call <c>DateTime.UtcNow</c> directly in code you intend to test.</b> A rule like
/// "orders can be cancelled within 30 minutes" is untestable if the clock is a static call — you
/// would have to actually wait, or run the test at a specific time of day and watch it fail in CI
/// in another timezone.
/// </para>
/// <para>
/// .NET 8 added <see cref="TimeProvider"/> as the framework-blessed version of this, and the
/// implementation simply wraps it. The interface is kept because it keeps the Application layer
/// honest about what it depends on, and because <c>FakeTimeProvider</c> pulls in a test package
/// that production code should not reference.
/// </para>
/// </remarks>
public interface IDateTimeProvider
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Today's date, in UTC.</summary>
    DateOnly Today { get; }
}

/// <summary>
/// Who is making the current request.
/// </summary>
/// <remarks>
/// The Application layer needs the caller's identity for authorisation and audit, but must not
/// know that identity arrives in a JWT over HTTP. This interface is the seam: Infrastructure
/// implements it by reading <c>IHttpContextAccessor</c>, and a test implements it with a
/// two-line stub.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The authenticated user's id, or <c>null</c> for anonymous callers.</summary>
    Guid? UserId { get; }

    /// <summary>The authenticated user's email, or <c>null</c>.</summary>
    string? Email { get; }

    /// <summary>True when the caller presented valid credentials.</summary>
    bool IsAuthenticated { get; }

    /// <summary>True when the caller holds the named role.</summary>
    bool IsInRole(string role);
}

/// <summary>
/// Issues the next human-facing order reference.
/// </summary>
/// <remarks>
/// <para>
/// Its own service because generating a gapless per-year sequence is a database concern, not a
/// domain one. The implementation uses a SQL Server <c>SEQUENCE</c>, which is atomic and does
/// not block concurrent writers.
/// </para>
/// <para>
/// <b>The wrong way</b>, which appears in a lot of production code:
/// <c>SELECT MAX(Number) + 1 FROM Orders</c>. Two concurrent requests read the same maximum and
/// produce the same reference. It works in every test and fails on the first busy morning.
/// </para>
/// </remarks>
public interface IOrderNumberGenerator
{
    /// <summary>Reserves and returns the next reference for the current year.</summary>
    Task<OrderNumber> NextAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Distributed cache access, expressed in application terms.
/// </summary>
/// <remarks>
/// Wraps <c>IDistributedCache</c> so handlers deal in objects rather than byte arrays, and so
/// the JSON serialisation settings are configured in exactly one place.
/// </remarks>
public interface ICacheService
{
    /// <summary>Reads a cached value, or <c>null</c> when absent or expired.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>Writes a value with an absolute expiry.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Removes one entry.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached value, or computes, stores and returns it.
    /// </summary>
    /// <remarks>
    /// The cache-aside pattern. Note what it does <i>not</i> do: prevent two concurrent misses
    /// from both running <paramref name="factory"/>. That is a cache stampede, and solving it
    /// properly needs a per-key lock. Whether it matters depends entirely on how expensive the
    /// factory is — discussed in <c>course/module-10-cross-cutting/03-caching.md</c>.
    /// </remarks>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;
}

// IEmailSender used to live here, as a three-string method with a logging implementation and a
// comment saying a real provider was "left as an exercise". It grew into a subsystem — message
// type, templates, a durable queue, four transports, a delivery worker with backoff — and moved
// to LogiFlow.Application.Abstractions.Mailing.
//
// The move is itself the lesson about this file. "Services.cs" is a fine home for a handful of
// small, unrelated seams; the moment one of them acquires a queue and a background worker it has
// stopped being a service and become a feature, and it needs a folder with its own name. Files
// named after their layer rather than their subject are where that transition goes unnoticed.
