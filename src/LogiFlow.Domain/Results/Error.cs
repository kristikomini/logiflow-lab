namespace LogiFlow.Domain.Results;

/// <summary>
/// Classifies a failure so the API layer can map it to the right HTTP status
/// without the Domain ever mentioning HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>Unexpected failure. Maps to 500.</summary>
    Failure = 0,

    /// <summary>Input failed a rule. Maps to 400.</summary>
    Validation = 1,

    /// <summary>The thing you asked for does not exist. Maps to 404.</summary>
    NotFound = 2,

    /// <summary>State clash — duplicate, or a concurrent edit. Maps to 409.</summary>
    Conflict = 3,

    /// <summary>Not authenticated. Maps to 401.</summary>
    Unauthorized = 4,

    /// <summary>Authenticated but not allowed. Maps to 403.</summary>
    Forbidden = 5,
}

/// <summary>
/// A machine-readable failure. Never a raw string.
/// </summary>
/// <param name="Code">
/// Stable dotted identifier, e.g. <c>Order.AlreadyShipped</c>. Clients switch on this;
/// it must not change once released, and it must never be localised.
/// </param>
/// <param name="Description">Human-readable explanation. Safe to change, safe to translate.</param>
/// <param name="Type">Category that drives the HTTP status code.</param>
/// <remarks>
/// <para>
/// The <c>Code</c>/<c>Description</c> split matters more than it looks. A front-end that
/// does <c>if (error.message === "Order already shipped")</c> breaks the day someone fixes a
/// typo. A front-end that does <c>if (error.code === "Order.AlreadyShipped")</c> does not.
/// </para>
/// <para>
/// Errors are declared as <c>static readonly</c> fields next to the aggregate that produces
/// them (see <c>OrderErrors</c>), which gives you one greppable list of everything that can
/// go wrong with an order — and makes it obvious in review when someone invents a new failure
/// mode.
/// </para>
/// </remarks>
/// <remarks>
/// Not sealed: <c>ValidationError</c> in the Application layer derives from it to carry
/// per-field failures. Because records generate a structural <c>Equals</c> that includes an
/// <c>EqualityContract</c> type check, a <c>ValidationError</c> never compares equal to a plain
/// <c>Error</c> with the same code — which is exactly the behaviour you want.
/// </remarks>
public record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>The absence of an error. Carried by every successful <see cref="Result"/>.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>Creates a 400-class error.</summary>
    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    /// <summary>Creates a 404-class error.</summary>
    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    /// <summary>Creates a 409-class error.</summary>
    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    /// <summary>Creates a 500-class error.</summary>
    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    /// <summary>Creates a 401-class error.</summary>
    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    /// <summary>Creates a 403-class error.</summary>
    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Description}";
}
