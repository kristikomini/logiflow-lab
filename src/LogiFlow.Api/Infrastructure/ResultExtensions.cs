using LogiFlow.Application.Behaviors;
using LogiFlow.Domain.Results;

namespace LogiFlow.Api.Infrastructure;

/// <summary>
/// Translates a domain <see cref="Result"/> into an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file is the only place in the solution that knows about HTTP status codes.</b> The
/// Domain says "Order.AlreadyShipped, and that's a Conflict"; this decides that a Conflict is
/// 409. Nothing below the API layer imports <c>Microsoft.AspNetCore</c>, which is what makes the
/// Application layer testable without a web host and reusable from a worker or a CLI.
/// </para>
/// <para>
/// <b>The response body is RFC 9457 Problem Details</b> (which obsoleted RFC 7807). It is the
/// standard machine-readable error format for HTTP APIs, and using it means clients, gateways
/// and tooling already know how to parse your errors. Inventing
/// <c>{ "success": false, "msg": "..." }</c> means every consumer writes bespoke handling.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/04-error-handling.md</c>
/// </remarks>
public static class ResultExtensions
{
    /// <summary>Maps a payload-free result: 204 on success, a problem document on failure.</summary>
    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // 204 No Content, not 200 with an empty body. The command succeeded and has nothing to
        // say; a 200 implies a representation the client should read.
        return result.IsSuccess ? Results.NoContent() : Problem(result.Error);
    }

    /// <summary>Maps a value-returning result: 200 with the payload, or a problem document.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Maps a creation result to 201 Created with a Location header.</summary>
    /// <param name="result">The result carrying the new resource's id.</param>
    /// <param name="locationFactory">Builds the URI of the created resource.</param>
    /// <remarks>
    /// <b>201 must carry a Location header.</b> It is the part everyone omits, and it is what
    /// lets a client follow the response to the thing it just made without guessing your URL
    /// scheme. Hypermedia in the smallest useful dose.
    /// </remarks>
    public static IResult ToCreatedResult<T>(this Result<T> result, Func<T, string> locationFactory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(locationFactory);

        return result.IsSuccess
            ? Results.Created(locationFactory(result.Value), result.Value)
            : Problem(result.Error);
    }

    /// <summary>Builds an RFC 9457 problem document from a domain error.</summary>
    private static IResult Problem(Error error)
    {
        // A ValidationError carries per-field detail, which belongs in the standard `errors`
        // dictionary rather than mashed into one string. This is the shape ASP.NET Core's own
        // model validation produces, so clients that already handle it need no changes.
        if (error is ValidationError validation)
        {
            Dictionary<string, string[]> errors = validation.Errors
                .GroupBy(e => e.Field, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.Message).ToArray(),
                    StringComparer.Ordinal);

            return Results.ValidationProblem(
                errors,
                detail: error.Description,
                statusCode: StatusCodes.Status400BadRequest,
                title: "One or more validation errors occurred.",
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        int status = StatusFor(error.Type);

        return Results.Problem(
            detail: error.Description,
            statusCode: status,
            title: TitleFor(error.Type),
            // A non-standard member carrying the stable machine-readable code. RFC 9457
            // explicitly allows extension members, and this is what clients should branch on -
            // never on the human-readable `detail`, which is free to change.
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    /// <summary>
    /// Maps an error category to a status code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinctions people get wrong, worth memorising:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>401 vs 403.</b> 401 means "I do not know who you are" — send credentials. 403 means
    ///     "I know exactly who you are and you still cannot" — sending credentials again will not
    ///     help. Returning 401 for an authorisation failure sends clients into a login loop.
    ///   </description></item>
    ///   <item><description>
    ///     <b>400 vs 409.</b> 400 means the request is malformed — fix the request. 409 means the
    ///     request is well-formed but conflicts with current state — the client may need to
    ///     reload and retry.
    ///   </description></item>
    ///   <item><description>
    ///     <b>404 on a collection.</b> An empty list is a successful query that matched nothing:
    ///     200 with <c>[]</c>. Reserve 404 for a specific resource that does not exist.
    ///   </description></item>
    /// </list>
    /// </remarks>
    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Bad request",
        ErrorType.Unauthorized => "Authentication required",
        ErrorType.Forbidden => "Access denied",
        ErrorType.NotFound => "Resource not found",
        ErrorType.Conflict => "Conflict with current state",
        ErrorType.Failure => "An unexpected error occurred",
        _ => "An unexpected error occurred",
    };
}
