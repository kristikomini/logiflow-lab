using System.Globalization;
using System.Numerics;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.ValueObjects;

/// <summary>
/// An amount of money in a specific currency. Immutable, currency-safe, and never a <c>double</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule one: never store money in a floating-point type.</b> <c>0.1 + 0.2</c> is
/// <c>0.30000000000000004</c> in <c>double</c>, because binary floating point cannot represent
/// tenths exactly — the same reason decimal cannot represent 1/3. Over a million invoice lines
/// that drift becomes a real, auditable discrepancy that an accountant will find and you will
/// have to explain. <c>decimal</c> is base-10 and stores 0.1 exactly. In SQL Server this maps to
/// <c>decimal(19,4)</c>; see <c>MoneyConfiguration</c>.
/// </para>
/// <para>
/// <b>Rule two: an amount without a currency is meaningless.</b> Bundling the two into one type
/// makes <c>euros + dollars</c> a runtime guard rather than a silent wrong answer — the class of
/// bug that only shows up after you expand to a second market.
/// </para>
/// <para>
/// <b>The C# being taught here:</b> operator overloading, <see cref="IComparable{T}"/>,
/// <see cref="IParsable{TSelf}"/>, and generic-math interfaces
/// (<see cref="IAdditionOperators{TSelf,TOther,TResult}"/> and friends, C# 11) that let this
/// type be used from generic algorithms constrained on arithmetic.
/// </para>
/// Covered in: <c>course/module-01-csharp-advanced/06-operator-overloading.md</c>
/// </remarks>
public readonly record struct Money :
    IComparable<Money>,
    IAdditionOperators<Money, Money, Money>,
    ISubtractionOperators<Money, Money, Money>,
    IMultiplyOperators<Money, decimal, Money>,
    IUnaryNegationOperators<Money, Money>
{
    /// <summary>Creates an amount. Prefer <see cref="Create"/> for untrusted input.</summary>
    /// <param name="amount">The value, rounded to the currency's precision.</param>
    /// <param name="currency">The currency. Required.</param>
    public Money(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        Currency = currency;

        // Banker's rounding (MidpointRounding.ToEven) is the default in .NET and the correct
        // choice for financial totals: rounding 0.5 always *up* introduces a systematic upward
        // bias across many transactions. Rounding to even cancels out over a large sample.
        // This is what IEEE 754 and most accounting standards specify.
        Amount = Math.Round(amount, currency.DecimalPlaces, MidpointRounding.ToEven);
    }

    /// <summary>The numeric value, already rounded to <see cref="Currency"/>'s precision.</summary>
    public decimal Amount { get; }

    /// <summary>The currency this amount is denominated in.</summary>
    public Currency Currency { get; }

    /// <summary>Zero euros. A useful <see cref="Enumerable.Aggregate{T}(IEnumerable{T},Func{T,T,T})"/> seed.</summary>
    public static Money ZeroEur => new(0m, Currency.Eur);

    /// <summary>Zero in the given currency.</summary>
    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>True when the amount is below zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>Validating factory for untrusted input.</summary>
    public static Result<Money> Create(decimal amount, string currencyCode)
    {
        Result<Currency> currency = Currency.FromCode(currencyCode);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        return new Money(amount, currency.Value);
    }

    /// <summary>Validating factory that also rejects negative amounts.</summary>
    public static Result<Money> CreateNonNegative(decimal amount, string currencyCode)
    {
        if (amount < 0m)
        {
            return Error.Validation("Money.Negative", "Amount cannot be negative.");
        }

        return Create(amount, currencyCode);
    }

    /// <summary>
    /// Adds two amounts.
    /// </summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    /// <remarks>
    /// This <i>throws</i> rather than returning a <c>Result</c>, which looks inconsistent with the
    /// rest of the domain until you ask who could cause it. A user cannot make the system add
    /// euros to yen; only a developer wiring up the wrong field can. That is a bug, not a
    /// business outcome — so it gets an exception. Converting currencies needs an exchange rate
    /// and a date, so it is a deliberate call to a domain service, never an implicit coercion
    /// hidden inside <c>operator +</c>.
    /// </remarks>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>Subtracts two amounts of the same currency.</summary>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>Negates an amount (e.g. to express a refund).</summary>
    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    /// <summary>Scales an amount, e.g. unit price × quantity.</summary>
    public static Money operator *(Money left, decimal multiplier) =>
        new(left.Amount * multiplier, left.Currency);

    /// <summary>Scales an amount.</summary>
    public static Money operator *(decimal multiplier, Money right) => right * multiplier;

    /// <summary>Divides an amount, e.g. splitting a total across instalments.</summary>
    public static Money operator /(Money left, decimal divisor) =>
        divisor == 0m
            ? throw new DivideByZeroException("Cannot divide money by zero.")
            : new Money(left.Amount / divisor, left.Currency);

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator >=(Money left, Money right) => !(left < right);

    /// <summary>Compares two amounts of the same currency.</summary>
    public static bool operator <=(Money left, Money right) => !(left > right);

    /// <summary>
    /// Splits an amount into <paramref name="parts"/> pieces that sum <i>exactly</i> back to the original.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The naive <c>total / parts</c> loses money. €10.00 split three ways gives €3.33 each and
    /// leaves a stranded cent — over enough transactions, a reconciliation failure. This
    /// distributes the remainder one minor unit at a time across the leading parts, so
    /// €10.00 / 3 yields €3.34, €3.33, €3.33.
    /// </para>
    /// <para>This exact problem is a common technical-interview question. Now you have the answer.</para>
    /// </remarks>
    public Money[] Allocate(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);

        // Work in minor units (cents) so all arithmetic is integral and cannot drift.
        decimal factor = Pow10(Currency.DecimalPlaces);
        long totalMinorUnits = (long)Math.Round(Amount * factor, 0, MidpointRounding.ToEven);

        long baseShare = totalMinorUnits / parts;
        long remainder = Math.Abs(totalMinorUnits % parts);
        long sign = totalMinorUnits < 0 ? -1 : 1;

        var results = new Money[parts];
        for (int i = 0; i < parts; i++)
        {
            long share = baseShare + (i < remainder ? sign : 0);
            results[i] = new Money(share / factor, Currency);
        }

        return results;
    }

    /// <summary>Applies a percentage discount, e.g. <c>0.10m</c> for 10% off.</summary>
    public Money ApplyDiscount(decimal rate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rate, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rate, 1m);
        return this * (1m - rate);
    }

    /// <inheritdoc />
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Currency.Symbol}{Amount.ToString($"N{Currency.DecimalPlaces}", CultureInfo.InvariantCulture)}");

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot combine {left.Currency.Code} and {right.Currency.Code}. " +
                "Convert explicitly through an exchange-rate service first.");
        }
    }

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
