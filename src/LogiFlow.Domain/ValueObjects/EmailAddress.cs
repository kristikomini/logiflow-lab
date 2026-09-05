using System.Text.RegularExpressions;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.ValueObjects;

/// <summary>
/// A syntactically valid email address, stored normalised to lower case.
/// </summary>
/// <remarks>
/// <para>
/// <b>On email regexes.</b> The fully RFC 5322-compliant pattern is roughly 6,000 characters
/// long, and it still accepts addresses no mail server will deliver to. Chasing it is a waste
/// of time. The pragmatic rule used here — "something, an @, something with a dot" — rejects
/// obvious typos and accepts everything real. The only way to truly verify an address is to
/// send it a confirmation link, which is what production systems actually do.
/// </para>
/// <para>
/// <b>Why normalise to lower case?</b> The local part of an address is technically
/// case-sensitive per RFC, but no significant mail provider treats it that way. Normalising
/// prevents <c>Mario@example.com</c> and <c>mario@example.com</c> registering as two accounts,
/// and lets the database use a case-insensitive unique index without surprises.
/// </para>
/// </remarks>
public readonly partial record struct EmailAddress
{
    /// <summary>Maximum length per RFC 5321. Mirrored by the EF configuration.</summary>
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    /// <summary>The normalised address.</summary>
    public string Value { get; }

    /// <summary>The part after the <c>@</c>, e.g. <c>example.com</c>.</summary>
    public string Domain => Value[(Value.IndexOf('@', StringComparison.Ordinal) + 1)..];

    /// <summary>Validating factory.</summary>
    public static Result<EmailAddress> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation("Email.Empty", "Email address is required.");
        }

        string normalised = value.Trim().ToLowerInvariant();

        if (normalised.Length > MaxLength)
        {
            return Error.Validation("Email.TooLong", $"Email address cannot exceed {MaxLength} characters.");
        }

        if (!EmailPattern().IsMatch(normalised))
        {
            return Error.Validation("Email.InvalidFormat", $"'{value}' is not a valid email address.");
        }

        return new EmailAddress(normalised);
    }

    /// <summary>Rehydrates from trusted storage without re-validating.</summary>
    public static EmailAddress FromTrusted(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    // Deliberately permissive. See the remarks on the type.
    // The 250ms timeout is belt-and-braces against catastrophic backtracking; this pattern has
    // no nested quantifiers so it cannot backtrack pathologically, but making the habit
    // automatic costs nothing and one day saves you from a ReDoS.
    [GeneratedRegex(
        @"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex EmailPattern();
}
