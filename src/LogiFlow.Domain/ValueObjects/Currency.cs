using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.ValueObjects;

/// <summary>
/// A supported ISO-4217 currency, modelled as a "smart enum".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>enum Currency { EUR, USD, GBP }</c>?</b> Because a C# enum is just a named
/// integer, and integers cannot carry behaviour or data. The moment you need "how many decimal
/// places does this currency use?" you end up with a <c>switch</c> in a static helper class —
/// and then a second one somewhere else for the symbol, and a third for the display name.
/// Each is a place to forget a case when a new currency is added.
/// </para>
/// <para>
/// A smart enum keeps the data with the instance. Adding a currency is one line here and
/// nothing anywhere else. Also, a real enum has a nasty property: <c>(Currency)999</c> is a
/// perfectly legal value that passes every <c>switch</c> and matches no case.
/// </para>
/// <para>
/// The trade-off is that this is a reference type, so it needs an EF Core value converter
/// (see <c>MoneyConfiguration</c>) and equality by <see cref="Code"/> rather than by reference.
/// </para>
/// Covered in: <c>course/module-01-csharp-advanced/05-smart-enums.md</c>
/// </remarks>
public sealed class Currency : IEquatable<Currency>
{
    /// <summary>Euro.</summary>
    public static readonly Currency Eur = new("EUR", "€", 2);

    /// <summary>United States dollar.</summary>
    public static readonly Currency Usd = new("USD", "$", 2);

    /// <summary>Pound sterling.</summary>
    public static readonly Currency Gbp = new("GBP", "£", 2);

    /// <summary>Japanese yen — note zero decimal places. This is exactly the data a plain enum cannot hold.</summary>
    public static readonly Currency Jpy = new("JPY", "¥", 0);

    /// <summary>Every currency the system understands.</summary>
    public static readonly IReadOnlyList<Currency> All = [Eur, Usd, Gbp, Jpy];

    private static readonly Dictionary<string, Currency> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    private Currency(string code, string symbol, int decimalPlaces)
    {
        Code = code;
        Symbol = symbol;
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>ISO-4217 three-letter code, e.g. <c>EUR</c>.</summary>
    public string Code { get; }

    /// <summary>Display symbol, e.g. <c>€</c>.</summary>
    public string Symbol { get; }

    /// <summary>Minor-unit precision. 2 for most currencies, 0 for yen.</summary>
    public int DecimalPlaces { get; }

    /// <summary>Looks up a currency by ISO code.</summary>
    public static Result<Currency> FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation("Currency.Empty", "Currency code is required.");
        }

        return ByCode.TryGetValue(code.Trim(), out Currency? currency)
            ? currency
            : Error.Validation("Currency.Unsupported", $"Currency '{code}' is not supported.");
    }

    /// <summary>
    /// Looks up a currency by ISO code, throwing if unknown. For trusted input only
    /// (database rows, hard-coded seed data) — never for user input.
    /// </summary>
    public static Currency FromCodeOrThrow(string code) =>
        ByCode.TryGetValue(code, out Currency? currency)
            ? currency
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported currency code.");

    /// <inheritdoc />
    public bool Equals(Currency? other) =>
        other is not null && string.Equals(Code, other.Code, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Currency other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Code;

    /// <summary>Compares two currencies by code.</summary>
    public static bool operator ==(Currency? left, Currency? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>Compares two currencies by code.</summary>
    public static bool operator !=(Currency? left, Currency? right) => !(left == right);
}
