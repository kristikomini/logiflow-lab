using System.Diagnostics;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Results;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Application.Behaviors;

/// <summary>
/// Logs the start, outcome and duration of every request, and flags slow ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structured logging, not string concatenation.</b> Note the message templates below use
/// named placeholders — <c>{RequestName}</c>, not <c>$"{requestName}"</c>. That distinction is
/// the entire point:
/// </para>
/// <code>
/// logger.LogInformation($"Handled {name} in {ms}ms");     // ✗ one opaque string
/// logger.LogInformation("Handled {Name} in {Ms}ms", ...); // ✓ a message plus queryable fields
/// </code>
/// <para>
/// The second form reaches Seq or Application Insights with <c>Name</c> and <c>Ms</c> as
/// separate indexed properties, so you can ask "show me every request over 500ms, grouped by
/// name" without parsing text. With interpolation you get a million unique strings and no way
/// to aggregate them. The Roslyn analyser CA2254 exists to catch exactly this mistake.
/// </para>
/// <para>
/// <b>The other half is the scope.</b> <see cref="ILogger.BeginScope"/> attaches the correlation
/// id to every log line written anywhere inside the handler, including deep in EF Core. When a
/// customer reports a failure at 14:32, you filter on one id and see the whole request.
/// </para>
/// Covered in: <c>course/module-11-observability/02-structured-logging.md</c>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="logger">Logger for this behaviour.</param>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Requests slower than this are logged at Warning.</summary>
    private const int SlowRequestThresholdMs = 500;

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;
        string correlationId = Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString();

        // The scope is disposed at the end of the using block, which pops these properties back
        // off. Every log line written by anything downstream carries them until then.
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestName"] = requestName,
            ["CorrelationId"] = correlationId,
        });

        logger.LogInformation("Handling {RequestName}", requestName);

        // Stopwatch.GetTimestamp() is allocation-free, unlike `new Stopwatch()`. On a path that
        // runs for literally every request, that matters more than it looks.
        long start = Stopwatch.GetTimestamp();

        try
        {
            TResponse response = await next().ConfigureAwait(false);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

            // A business failure is NOT an error-level event. Logging every rejected order at
            // Error is how teams end up ignoring their error logs entirely.
            if (response is Result { IsFailure: true } failure)
            {
                logger.LogWarning(
                    "{RequestName} failed with {ErrorCode}: {ErrorDescription} ({ElapsedMs}ms)",
                    requestName,
                    failure.Error.Code,
                    failure.Error.Description,
                    elapsed.TotalMilliseconds);
            }
            else if (elapsed.TotalMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "{RequestName} completed but took {ElapsedMs}ms, over the {ThresholdMs}ms threshold",
                    requestName,
                    elapsed.TotalMilliseconds,
                    SlowRequestThresholdMs);
            }
            else
            {
                logger.LogInformation(
                    "{RequestName} completed in {ElapsedMs}ms",
                    requestName,
                    elapsed.TotalMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            // Log and rethrow. The exception still propagates to the global handler, which owns
            // turning it into an HTTP response - this is only here so the log line carries the
            // request name and timing, which the global handler no longer has.
            //
            // Never `catch { log; }` without rethrowing. That converts a crash into a silent
            // wrong answer, which is strictly worse.
            logger.LogError(
                ex,
                "{RequestName} threw {ExceptionType} after {ElapsedMs}ms",
                requestName,
                ex.GetType().Name,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            throw;
        }
    }
}
