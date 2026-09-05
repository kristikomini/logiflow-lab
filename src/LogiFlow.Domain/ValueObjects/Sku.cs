using System.Text.RegularExpressions;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.ValueObjects;

/// <summary>
/// A Stock Keeping Unit code: the business-facing identifier for a product.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why wrap a string?</b> This is "primitive obsession" — the most common design smell in
/// enterprise C#. A method taking <c>(string sku, string name, string description)</c> accepts
/// them in any order, and no compiler will save you. It also means the validation rule below
/// has to be re-applied at every entry point, and someone will eventually forget one.
/// </para>
/// <para>
/// With a <c>Sku</c> type the rule is enforced exactly once, at construction, and the type
/// system carries the guarantee everywhere afterwards. Any <c>Sku</c> you are handed is, by
/// construction, a valid SKU. That is called <i>parse, don't validate</i>.
/// </para>
/// <para>
/// <b>Format:</b> three uppercase letters, a hyphen, then four to eight digits.
/// For example <c>ELE-100234</c>.
/// </para>
/// </remarks>
public readonly partial record struct Sku
{
    private Sku(string value) => Value = value;

    /// <summary>The validated code.</summary>
    public string Value { get; }

    /// <summary>The category prefix, e.g. <c>ELE</c> for <c>ELE-100234</c>.</summary>
    public string CategoryPrefix => Value[..3];

    /// <summary>Validating factory.</summary>
    public static Result<Sku> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation("Sku.Empty", "SKU is required.");
        }

        string normalised = value.Trim().ToUpperInvariant();

        if (!SkuPattern().IsMatch(normalised))
        {
            return Error.Validation(
                "Sku.InvalidFormat",
                $"SKU '{value}' is invalid. Expected three letters, a hyphen, then 4-8 digits (e.g. ELE-100234).");
        }

        return new Sku(normalised);
    }

    /// <summary>Rehydrates from trusted storage without re-validating.</summary>
    public static Sku FromTrusted(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Source-generated regex.
    /// </summary>
    /// <remarks>
    /// <c>[GeneratedRegex]</c> (.NET 7+) makes the Roslyn source generator emit a purpose-built
    /// matcher at compile time instead of the runtime engine interpreting the pattern. It is
    /// typically 3-10x faster, allocates nothing, has no first-call JIT cost, and — the part
    /// people miss — an invalid pattern becomes a <i>compile</i> error rather than a
    /// <c>RegexParseException</c> in production. This is why the record is declared
    /// <c>partial</c>. Benchmarked in <c>Labs.Benchmarks</c>.
    /// </remarks>
    [GeneratedRegex(@"^[A-Z]{3}-\d{4,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
