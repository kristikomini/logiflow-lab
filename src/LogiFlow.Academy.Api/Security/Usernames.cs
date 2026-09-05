using System.Text.RegularExpressions;

namespace LogiFlow.Academy.Api.Security;

/// <summary>
/// What counts as a username here, and how one is normalised before it touches the database.
/// </summary>
/// <remarks>
/// <b>Why a username and not an email.</b> An email address is a terrible primary credential
/// for a service that cannot send email: it cannot be verified, so it is an unchecked claim;
/// it is personal data with no purpose, so it is a GDPR liability; and it invites the user to
/// assume a reset link is coming when none ever will. A username is none of those things —
/// it is an identifier the learner chooses and the service can actually check.
/// </remarks>
internal static partial class Usernames
{
    /// <summary>Shortest username accepted.</summary>
    internal const int MinimumLength = 3;

    /// <summary>Longest username accepted. Matches the column width.</summary>
    internal const int MaximumLength = 32;

    /// <summary>
    /// Letters, digits, and the three separators, starting and ending on a letter or digit.
    /// </summary>
    /// <remarks>
    /// ASCII only, and no <c>@</c>. Both are deliberate. <c>@</c> is excluded so that nobody
    /// can register a username shaped like an email address and turn the sign-in form into a
    /// guessing game about which one it wants. Restricting to ASCII avoids the confusable
    /// -characters problem: Cyrillic <c>а</c> and Latin <c>a</c> render identically, so allowing
    /// both would let somebody register a username visually indistinguishable from yours.
    /// </remarks>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{1,30}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    /// <summary>Lower-cases and trims, so the stored value is directly seekable by index.</summary>
    /// <param name="candidate">The username as typed.</param>
    internal static string Normalise(string? candidate) =>
        (candidate ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>True when a normalised username is one this service will store.</summary>
    /// <param name="normalised">The output of <see cref="Normalise"/>.</param>
    internal static bool IsValid(string normalised) =>
        normalised.Length is >= MinimumLength and <= MaximumLength && Shape().IsMatch(normalised);

    /// <summary>The message shown when it is not.</summary>
    internal const string Rule =
        "Use 3 to 32 characters: letters, digits, and . _ - in the middle.";
}
