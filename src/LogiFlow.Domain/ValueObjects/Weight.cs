using System.Globalization;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.ValueObjects;

/// <summary>
/// A physical weight, always stored in grams internally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Store one canonical unit; convert at the edges.</b> The alternative — letting each record
/// carry its own unit — means every comparison and every sum has to convert first, and one
/// missed conversion is a shipment quoted at 1/1000th of its real weight.
/// </para>
/// <para>
/// This is not a hypothetical failure mode. In 1999 NASA lost the $125M Mars Climate Orbiter
/// because one team worked in pound-force-seconds and another in newton-seconds. A type like
/// this one, with a private constructor and named factories, is the cheapest possible
/// insurance against that class of mistake.
/// </para>
/// <para>
/// Grams as <c>int</c> rather than kilograms as <c>decimal</c> keeps all arithmetic exact and
/// makes SQL <c>SUM</c> and index range scans cheap. Max value is ~2,147 tonnes, comfortably
/// more than any parcel.
/// </para>
/// </remarks>
public readonly record struct Weight : IComparable<Weight>
{
    private Weight(int grams) => Grams = grams;

    /// <summary>The canonical value, in grams.</summary>
    public int Grams { get; }

    /// <summary>The value expressed in kilograms.</summary>
    public decimal Kilograms => Grams / 1000m;

    /// <summary>Nothing.</summary>
    public static Weight Zero => new(0);

    /// <summary>Creates a weight from grams.</summary>
    public static Result<Weight> FromGrams(int grams) =>
        grams < 0
            ? Error.Validation("Weight.Negative", "Weight cannot be negative.")
            : new Weight(grams);

    /// <summary>Creates a weight from kilograms, rounding to the nearest gram.</summary>
    public static Result<Weight> FromKilograms(decimal kilograms) =>
        FromGrams((int)Math.Round(kilograms * 1000m, 0, MidpointRounding.ToEven));

    /// <summary>Rehydrates from trusted storage.</summary>
    public static Weight FromTrusted(int grams) => new(grams);

    /// <summary>Adds two weights.</summary>
    public static Weight operator +(Weight left, Weight right) => new(left.Grams + right.Grams);

    /// <summary>Scales a weight by a whole-number quantity.</summary>
    public static Weight operator *(Weight weight, int quantity) => new(weight.Grams * quantity);

    /// <summary>Compares two weights.</summary>
    public static bool operator >(Weight left, Weight right) => left.Grams > right.Grams;

    /// <summary>Compares two weights.</summary>
    public static bool operator <(Weight left, Weight right) => left.Grams < right.Grams;

    /// <summary>Compares two weights.</summary>
    public static bool operator >=(Weight left, Weight right) => left.Grams >= right.Grams;

    /// <summary>Compares two weights.</summary>
    public static bool operator <=(Weight left, Weight right) => left.Grams <= right.Grams;

    /// <inheritdoc />
    public int CompareTo(Weight other) => Grams.CompareTo(other.Grams);

    /// <inheritdoc />
    public override string ToString() =>
        Grams >= 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{Kilograms:0.###} kg")
            : string.Create(CultureInfo.InvariantCulture, $"{Grams} g");
}
