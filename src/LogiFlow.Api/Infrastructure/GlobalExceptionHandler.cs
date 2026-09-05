using LogiFlow.Application.Features.Orders.EventHandlers;
using LogiFlow.Domain.Orders;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Api.Infrastructure;

/// <summary>
/// Catches anything that escapes a handler and turns it into a Problem Details response.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="IExceptionHandler"/> (.NET 8+) replaces the old middleware approach.</b> Several
/// can be registered and they run in order until one returns <c>true</c>, so you can have a
/// specific handler for your own exception types and a catch-all behind it.
/// </para>
/// <para>
/// <b>The security rule this file exists to enforce:</b> never let an exception message or stack
/// trace reach a client in production. Stack traces disclose your namespaces, file paths,
/// library versions, and sometimes connection strings — a genuinely useful map for an attacker.
/// The detail goes to the log; the client gets a trace id to quote at support.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/04-error-handling.md</c>
/// </remarks>
/// <param name="logger">Logger.</param>
/// <param name="environment">Used to decide whether detail is safe to return.</param>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // The trace id ties this response to the log entry and to the distributed trace. Give it
        // to the user; they quote it to support; support finds the exact request in seconds.
        string traceId = httpContext.TraceIdentifier;

        (int status, string title, string detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled {ExceptionType} on {Method} {Path} (trace {TraceId})",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId);
        }
        else
        {
            // A 409 from a concurrency clash is expected traffic, not a defect. Logging it at
            // Error would bury the real failures.
            logger.LogWarning(
                "Handled {ExceptionType} as {StatusCode} on {Method} {Path} (trace {TraceId})",
                exception.GetType().Name,
                status,
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,

            // In Development the real message is far more useful than a generic one. In any
            // other environment it is an information leak. This ternary is the whole control.
            Detail = environment.IsDevelopment() ? $"{detail} — {exception.Message}" : detail,

            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
            Extensions =
            {
                ["traceId"] = traceId,
            },
        };

        if (environment.IsDevelopment())
        {
            problem.Extensions["stackTrace"] = exception.StackTrace;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        // true = handled, stop the pipeline. Returning false would let the next handler try,
        // and eventually surface the default developer exception page.
        return true;
    }

    private static (int Status, string Title, string Detail) Map(Exception exception) => exception switch
    {
        // ── Malformed request ────────────────────────────────────────────────────────────
        // Thrown by minimal-API parameter binding when a query value or body cannot be parsed -
        // e.g. `?from=not-a-date`, or a DateTimeOffset whose `+02:00` offset was not URL-encoded
        // (a raw `+` in a query string means a space, so the value arrives mangled).
        //
        // This MUST be a 400. Without this arm it falls through to the catch-all and becomes a
        // 500, telling the caller "we broke" when in fact their request was invalid - and
        // polluting your error-rate alerting with other people's typos.
        //
        // BadHttpRequestException carries its own StatusCode, so it is honoured rather than
        // hard-coded.
        BadHttpRequestException badRequest => (
            badRequest.StatusCode,
            "Bad request",
            "One or more parameters could not be read. Check types, formats, and URL encoding "
            + "(a '+' in a query string must be encoded as %2B)."),

        // ── Optimistic concurrency ───────────────────────────────────────────────────────
        // Someone else saved first. That is a 409 the client can recover from by reloading -
        // emphatically not a 500, which would tell them to give up and file a bug.
        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            "Conflict with current state",
            OrderErrors.ConcurrencyConflict.Description),

        // A unique index rejected the write. Distinguished from other DbUpdateExceptions by the
        // SQL Server error numbers for duplicate key (2601) and unique constraint (2627).
        DbUpdateException dbEx when IsUniqueViolation(dbEx) => (
            StatusCodes.Status409Conflict,
            "Conflict with current state",
            "A record with the same unique value already exists."),

        DbUpdateException => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            "The change could not be saved."),

        StockReservationFailedException => (
            StatusCodes.Status409Conflict,
            "Conflict with current state",
            "There is not enough stock to fulfil this order."),

        // ── Cancellation is not a failure ────────────────────────────────────────────────
        // The client closed the connection or the request timed out. 499 is nginx's convention
        // for "client closed request"; nothing will read the body, but logging it as a 5xx would
        // pollute your error rate with events you did not cause and cannot fix.
        OperationCanceledException or TaskCanceledException => (
            499,
            "Request cancelled",
            "The request was cancelled before it completed."),

        // ── Guard-clause failures are bugs ───────────────────────────────────────────────
        // ArgumentNullException reaching here means validation missed something. It is a 500 on
        // purpose: it should show up in your error budget and get fixed, not be quietly
        // rebranded as a client error.
        ArgumentException or InvalidOperationException => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            "The server encountered an internal error."),

        _ => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            "The server encountered an internal error."),
    };

    /// <summary>Detects a unique-index violation by SQL Server error number.</summary>
    /// <remarks>
    /// String-matching the message ("Cannot insert duplicate key...") would break under a
    /// non-English SQL Server. Error numbers are stable and localisation-independent — always
    /// branch on those.
    /// </remarks>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
