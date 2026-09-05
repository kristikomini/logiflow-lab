using System.Diagnostics.CodeAnalysis;

namespace LogiFlow.Domain.Results;

/// <summary>
/// The outcome of an operation that is allowed to fail: either success, or an <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not just throw?</b> Exceptions are for the <i>exceptional</i> — a dropped network
/// connection, a bug, a disk that filled up. "This customer tried to cancel an order that
/// already shipped" is none of those things. It is an ordinary, expected, fully-anticipated
/// business outcome that happens hundreds of times a day.
/// </para>
/// <para>Using exceptions for expected failures costs you three things:</para>
/// <list type="number">
///   <item><description>
///     <b>Honesty.</b> A method signature <c>Task&lt;Order&gt; Cancel(...)</c> is a lie: it
///     claims to always return an Order. <c>Task&lt;Result&lt;Order&gt;&gt;</c> tells the truth,
///     and the compiler makes callers acknowledge it.
///   </description></item>
///   <item><description>
///     <b>Performance.</b> A thrown exception costs microseconds — thousands of times a simple
///     return. Fine once; ruinous in a validation loop. Measured in <c>Labs.Benchmarks</c>.
///   </description></item>
///   <item><description>
///     <b>Debuggability.</b> When business rules throw, your logs fill with exceptions and real
///     bugs stop standing out. Alert fatigue is a production outage waiting to happen.
///   </description></item>
/// </list>
/// <para>
/// <b>The rule of thumb:</b> if a human could reasonably cause it, return a Result.
/// If only a bug or the infrastructure could cause it, throw.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/05-result-vs-exceptions.md</c>
/// </remarks>
public class Result
{
    /// <summary>Creates a result. Enforces that success and error states are consistent.</summary>
    protected Result(bool isSuccess, Error error)
    {
        // These two guards are the whole point of the type. Without them you could construct
        // a "successful failure" and every downstream check becomes untrustworthy.
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation completed as intended.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed. Always the negation of <see cref="IsSuccess"/>.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure, or <see cref="Error.None"/> on success.</summary>
    public Error Error { get; }

    /// <summary>A successful result carrying no value.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>A failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>A failed result for an operation that would have returned a value.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>
    /// Returns the first failure among <paramref name="results"/>, or success if all succeeded.
    /// </summary>
    /// <remarks>
    /// Handy for validating several independent things before mutating anything:
    /// <code>
    /// var check = Result.FirstFailureOrSuccess(
    ///     ValidateAddress(address),
    ///     ValidateQuantity(qty),
    ///     ValidatePaymentMethod(method));
    /// if (check.IsFailure) return check;
    /// </code>
    /// </remarks>
    public static Result FirstFailureOrSuccess(params ReadOnlySpan<Result> results)
    {
        // ReadOnlySpan<T> params (C# 13) — the caller's array is stack-allocated, so this
        // validation helper allocates nothing at all on the happy path.
        foreach (Result result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }

    /// <summary>Lets a method <c>return someError;</c> instead of <c>return Result.Failure(someError);</c>.</summary>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// The outcome of an operation that produces a value when it succeeds.
/// </summary>
/// <typeparam name="TValue">The type produced on success.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error) =>
        _value = value;

    /// <summary>
    /// The produced value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    /// <remarks>
    /// Throwing here is deliberate. Reading <c>.Value</c> without checking <c>.IsSuccess</c>
    /// is a programming error, and programming errors should be loud — a silent
    /// <c>default(TValue)</c> would let a null order flow three layers down before anything
    /// noticed. Where you want the compiler's null-flow analysis to help instead, use
    /// <see cref="TryGetValue"/>: its <see cref="NotNullWhenAttribute"/> tells Roslyn the
    /// out-parameter is non-null on the <c>true</c> branch.
    /// </remarks>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read .Value of a failed result. Error was '{Error}'. Check .IsSuccess first.");

    /// <summary>Attempts to read the value without throwing.</summary>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = _value;
        return IsSuccess && value is not null;
    }

    /// <summary>Lets a method <c>return order;</c> and get a successful result.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Lets a method <c>return OrderErrors.NotFound;</c> and get a failed result.</summary>
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
