using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.ValueObjects;

/// <summary>
/// A postal address. A value object: two addresses with the same fields <i>are</i> the same address.
/// </summary>
/// <remarks>
/// <para>
/// Modelled as a positional <c>record</c>, which gives value equality, <c>GetHashCode</c>,
/// <c>ToString</c>, <c>Deconstruct</c>, and non-destructive mutation (<c>with</c>) in one line.
/// This is the single best argument for records in a domain model.
/// </para>
/// <para>
/// In the database this is <b>not</b> a separate table. EF Core maps it as a
/// <c>ComplexProperty</c>, so the columns land inline on the owner:
/// <c>ShippingAddress_Line1</c>, <c>ShippingAddress_City</c>, and so on. No join, no
/// <c>AddressId</c>, no orphan rows. See <c>OrderConfiguration</c>.
/// </para>
/// <para>
/// <b>A note on real-world address validation:</b> the constructor below enforces only
/// structural rules. Do not be tempted to add regexes for postcodes — address formats vary
/// enormously by country (Ireland had no postcodes at all until 2015), and every "clever"
/// validation rule eventually rejects a real customer's real address. Validate structure here;
/// validate deliverability with a proper address-verification service.
/// </para>
/// Covered in: <c>course/module-05-clean-architecture/02-entities-and-value-objects.md</c>
/// </remarks>
/// <param name="Line1">Street and number. Required.</param>
/// <param name="Line2">Apartment, suite, floor. Optional.</param>
/// <param name="City">City or town. Required.</param>
/// <param name="Region">State, province or county. Optional — many countries have none.</param>
/// <param name="PostalCode">Postal or ZIP code. Required.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 country code, e.g. <c>IT</c>.</param>
public sealed record Address(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode)
{
    /// <summary>Maximum stored length of an address line. Mirrored by the EF configuration.</summary>
    public const int MaxLineLength = 200;

    /// <summary>Validating factory. Trims input and normalises the country code to upper case.</summary>
    public static Result<Address> Create(
        string? line1,
        string? line2,
        string? city,
        string? region,
        string? postalCode,
        string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(line1))
        {
            return Error.Validation("Address.Line1Required", "Street address is required.");
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return Error.Validation("Address.CityRequired", "City is required.");
        }

        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return Error.Validation("Address.PostalCodeRequired", "Postal code is required.");
        }

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            return Error.Validation(
                "Address.InvalidCountry",
                "Country must be a two-letter ISO 3166-1 alpha-2 code, e.g. 'IT'.");
        }

        if (line1.Length > MaxLineLength || (line2?.Length ?? 0) > MaxLineLength)
        {
            return Error.Validation(
                "Address.LineTooLong",
                $"Address lines cannot exceed {MaxLineLength} characters.");
        }

        return new Address(
            line1.Trim(),
            string.IsNullOrWhiteSpace(line2) ? null : line2.Trim(),
            city.Trim(),
            string.IsNullOrWhiteSpace(region) ? null : region.Trim(),
            postalCode.Trim(),
            countryCode.Trim().ToUpperInvariant());
    }

    /// <summary>True when the address is outside the given country — drives customs and pricing rules.</summary>
    public bool IsInternationalFrom(string originCountryCode) =>
        !string.Equals(CountryCode, originCountryCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Renders the address as a single line for logs and labels.</summary>
    public override string ToString()
    {
        // Filtering nulls then joining beats six string concatenations with conditional
        // separators — and it is the pattern you want in review, not a StringBuilder.
        string[] parts = [Line1, Line2 ?? string.Empty, City, Region ?? string.Empty, PostalCode, CountryCode];
        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
