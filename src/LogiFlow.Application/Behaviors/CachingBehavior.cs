using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Behaviors;

/// <summary>
/// A query that opts into caching by declaring its key and lifetime.
/// </summary>
/// <remarks>
/// Opt-in, never automatic. A behaviour that cached every query would eventually cache one that
/// must always be fresh, and that bug is invisible until a customer sees stale data. Making each
/// query state its own policy keeps the decision next to the query, where the person who
/// understands the data can see it.
/// </remarks>
public interface ICacheableQuery
{
    /// <summary>
    /// Unique key for this query's parameters.
    /// </summary>
    /// <remarks>
    /// <b>Must include every parameter that changes the result.</b> A key of <c>"orders"</c> for
    /// a paged, filtered query serves page 1 to someone asking for page 3. Include the tenant or
    /// user id too whenever results are scoped to a caller — leaking one customer's data to
    /// another through a shared cache key is a genuine and regularly-shipped security bug.
    /// </remarks>
    string CacheKey { get; }

    /// <summary>How long the entry stays valid.</summary>
    TimeSpan? CacheDuration { get; }
}

/// <summary>
/// Serves cacheable queries from the distributed cache, falling back to the handler on a miss.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only successes are cached.</b> Caching a failure means a transient database blip gets
/// served back to every caller for the next ten minutes — turning a one-second outage into a
/// ten-minute one.
/// </para>
/// <para>
/// <b>Why distributed rather than in-memory?</b> <c>IMemoryCache</c> is faster and simpler, and
/// it breaks the moment you run two instances: each has its own copy, they disagree, and an
/// invalidation on one does nothing to the other. Users see data flicker between old and new
/// depending on which pod answers. If you are ever going to scale out, start with Redis.
/// </para>
/// <para>
/// <b>What this does not solve:</b> invalidation. An order cached for five minutes stays stale
/// for up to five minutes after an update. Short TTLs are the crude answer; explicit
/// invalidation from the command side is the precise one. Discussed in
/// <c>course/module-10-cross-cutting/03-caching.md</c>.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="cache">The distributed cache.</param>
/// <param name="logger">Logger.</param>
public sealed class CachingBehavior<TRequest, TResponse>(
    ICacheService cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
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
        if (request is not ICacheableQuery cacheable)
        {
            return await next().ConfigureAwait(false);
        }

        string key = cacheable.CacheKey;

        TResponse? hit = await cache.GetAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
        if (hit is not null)
        {
            logger.LogDebug("Cache hit for {CacheKey}", key);
            return hit;
        }

        logger.LogDebug("Cache miss for {CacheKey}", key);

        TResponse response = await next().ConfigureAwait(false);

        if (response.IsSuccess)
        {
            await cache.SetAsync(key, response, cacheable.CacheDuration, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
