using System.Text.Json.Serialization;

namespace LogiFlow.Web.Contracts;

/// <summary>
/// An RFC 9457 problem document, as returned by every failing LogiFlow endpoint.
/// </summary>
/// <remarks>
/// <b>Branch on <see cref="Code"/>, never on <see cref="Detail"/>.</b> <c>code</c> is a
/// non-standard extension member the API adds deliberately (see <c>ResultExtensions</c>) because
/// it is stable: <c>Order.InvalidTransition</c> means the same thing in six months.
/// <c>detail</c> is human-readable prose and is free to be reworded, translated, or improved by
/// anyone at any time. A client that matches on the message string breaks silently when someone
/// fixes a typo.
/// </remarks>
/// <param name="Title">Short summary of the problem type.</param>
/// <param name="Status">The HTTP status code, repeated in the body.</param>
/// <param name="Detail">Human-readable explanation. For display, not for logic.</param>
/// <param name="Code">Stable machine-readable code, e.g. <c>Order.InvalidTransition</c>.</param>
/// <param name="Errors">Per-field messages, present only on a 400 from validation.</param>
public sealed record ProblemDocument(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("errors")] IReadOnlyDictionary<string, string[]>? Errors);

/// <summary>
/// Why a call failed, in a form the UI can render.
/// </summary>
/// <param name="Code">Stable machine-readable code, or a transport-level code we invented.</param>
/// <param name="Message">What to show the user.</param>
/// <param name="Status">HTTP status, or 0 when the request never got a response.</param>
/// <param name="FieldErrors">Per-field validation messages, if any.</param>
public sealed record ApiError(
    string Code,
    string Message,
    int Status,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    /// <summary>The request never reached the API, or the response was unreadable.</summary>
    /// <param name="message">What to show the user.</param>
    public static ApiError Transport(string message) => new("Api.Unreachable", message, 0);
}

/// <summary>
/// The outcome of an API call that returns no payload.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same pattern as the server's <c>Result</c>, for the same reason.</b> A 409 from the API
/// is not an exceptional condition - it is one of the two documented outcomes of pressing
/// "Submit", and the UI must render it either way. Modelling it as a returned value means every
/// call site is forced by the compiler to consider the failure, instead of an unhandled
/// <c>HttpRequestException</c> tearing down a Blazor circuit and blanking the user's screen.
/// </para>
/// <para>
/// Exceptions are still thrown for genuinely exceptional things - a bug in this class, an
/// <c>OperationCanceledException</c> from navigation - and those are what the error boundary and
/// the circuit handler are for.
/// </para>
/// <para>
/// <b>A class, not a record, and that is not arbitrary.</b> As a <c>record</c> this type fails to
/// compile: records generate a copy constructor <c>ApiResult(ApiResult)</c>, which makes
/// <c>new(null)</c> ambiguous with the private <c>ApiResult(ApiError?)</c> below - CS0121. It is
/// a genuinely surprising interaction between two features that are individually obvious, and it
/// is worth recognising the error message once so you do not lose ten minutes to it later.
/// </para>
/// </remarks>
public sealed class ApiResult
{
    private ApiResult(ApiError? error) => Error = error;

    /// <summary>The failure, or <c>null</c> when the call succeeded.</summary>
    public ApiError? Error { get; }

    /// <summary>True when the call succeeded.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>A successful outcome.</summary>
    public static ApiResult Success() => new(null);

    /// <summary>A failed outcome.</summary>
    /// <param name="error">Why it failed.</param>
    public static ApiResult Failure(ApiError error) => new(error);
}

/// <summary>
/// The outcome of an API call that returns a payload.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public sealed class ApiResult<T>
{
    private ApiResult(T? value, ApiError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>The payload, or <c>null</c> when the call failed.</summary>
    public T? Value { get; }

    /// <summary>The failure, or <c>null</c> when the call succeeded.</summary>
    public ApiError? Error { get; }

    /// <summary>True when the call succeeded and <see cref="Value"/> is populated.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>A successful outcome.</summary>
    /// <param name="value">The payload.</param>
    public static ApiResult<T> Success(T value) => new(value, null);

    /// <summary>A failed outcome.</summary>
    /// <param name="error">Why it failed.</param>
    public static ApiResult<T> Failure(ApiError error) => new(default, error);
}
