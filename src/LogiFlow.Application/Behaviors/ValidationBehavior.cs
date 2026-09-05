using FluentValidation;
using FluentValidation.Results;
using LogiFlow.Application.Abstractions.Messaging;
using LogiFlow.Domain.Results;

namespace LogiFlow.Application.Behaviors;

/// <summary>
/// Runs every registered <see cref="IValidator{T}"/> for a request before its handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>The payoff:</b> not one handler in this codebase begins with a wall of null checks and
/// range checks. Validation is declared once per request type as a validator class, and this
/// behaviour guarantees it runs. A developer who forgets to write a validator gets no
/// validation; a developer who forgets to <i>call</i> one is impossible.
/// </para>
/// <para>
/// <b>Two kinds of validation, and the line between them.</b> This handles <i>input</i>
/// validation — shape, ranges, required fields, things checkable by looking at the request
/// alone. It must never contain business rules. "Quantity must be positive" belongs here;
/// "this customer's credit limit allows this order" does not, because that requires loading
/// state and is the domain's job. Blur this line and your business rules end up split across
/// two layers with no clear owner.
/// </para>
/// <para>
/// <b>The generic constraint is the interesting bit.</b> <c>where TResponse : Result</c> means
/// this behaviour only applies to requests whose response derives from <see cref="Result"/>.
/// DI's open-generic registration honours the constraint, so the container simply will not
/// insert this behaviour into a pipeline where it could not build a failure value.
/// </para>
/// Covered in: <c>course/module-08-cqrs/05-pipeline-behaviors.md</c>
/// </remarks>
/// <typeparam name="TRequest">The request being validated.</typeparam>
/// <typeparam name="TResponse">The response, which must be a <see cref="Result"/>.</typeparam>
/// <param name="validators">Every validator registered for <typeparamref name="TRequest"/>. Often empty.</param>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
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
        IValidator<TRequest>[] applicable = [.. validators];

        if (applicable.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);

        // Every validator runs, and every failure is collected. Returning only the first error
        // would make a user with three bad fields submit the form three times.
        ValidationResult[] results = await Task.WhenAll(
            applicable.Select(v => v.ValidateAsync(context, cancellationToken))).ConfigureAwait(false);

        ValidationFailure[] failures = [.. results.SelectMany(r => r.Errors).Where(f => f is not null)];

        if (failures.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        // Short-circuit: `next()` is never called, so the handler never runs and - crucially -
        // TransactionBehavior (registered inside this one) never opens a transaction.
        return CreateValidationResult(failures);
    }

    /// <summary>
    /// Builds a failed <typeparamref name="TResponse"/>, which may be <see cref="Result"/> or
    /// <see cref="Result{TValue}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflection is needed here for the same reason the dispatcher needs it: this method must
    /// produce a <c>Result&lt;OrderId&gt;</c> when <c>TResponse</c> is <c>Result&lt;OrderId&gt;</c>,
    /// but the generic <c>Result.Failure&lt;T&gt;</c> cannot be called with a type only known at
    /// runtime.
    /// </para>
    /// <para>
    /// This runs only on the failure path, where a human is about to read an error message, so
    /// the reflection cost is irrelevant. Putting it on the success path would be a different
    /// conversation.
    /// </para>
    /// </remarks>
    private static TResponse CreateValidationResult(ValidationFailure[] failures)
    {
        Error error = ValidationError.FromFailures(failures);

        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Failure(error);
        }

        // TResponse is Result<TValue>; build Result.Failure<TValue>(error) reflectively.
        Type valueType = typeof(TResponse).GetGenericArguments()[0];

        object failureResult = typeof(Result)
            .GetMethod(nameof(Result.Failure), 1, [typeof(Error)])!
            .MakeGenericMethod(valueType)
            .Invoke(null, [error])!;

        return (TResponse)failureResult;
    }
}

/// <summary>
/// An <see cref="Error"/> carrying every individual field failure.
/// </summary>
/// <remarks>
/// A plain <c>Error</c> holds one code and one message, which is not enough for a form: the
/// client needs to know <i>which field</i> failed. This subtype carries the detail through to
/// the API layer, where <c>ProblemDetails</c> renders it as an <c>errors</c> dictionary in the
/// shape RFC 9457 specifies.
/// </remarks>
public sealed record ValidationError : Error
{
    private ValidationError(IReadOnlyList<FieldError> errors)
        : base("Validation.Failed", "One or more validation errors occurred.", ErrorType.Validation) =>
        Errors = errors;

    /// <summary>The individual field failures.</summary>
    public IReadOnlyList<FieldError> Errors { get; }

    /// <summary>Groups FluentValidation failures into a single error.</summary>
    public static ValidationError FromFailures(IEnumerable<ValidationFailure> failures) =>
        new([.. failures.Select(f => new FieldError(f.PropertyName, f.ErrorMessage))]);
}

/// <summary>One field-level validation failure.</summary>
/// <param name="Field">The property that failed, e.g. <c>Lines[0].Quantity</c>.</param>
/// <param name="Message">What was wrong with it.</param>
public sealed record FieldError(string Field, string Message);
