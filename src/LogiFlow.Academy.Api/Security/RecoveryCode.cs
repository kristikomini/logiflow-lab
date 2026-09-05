using System.Security.Cryptography;
using System.Text;

namespace LogiFlow.Academy.Api.Security;

/// <summary>
/// The one-time code that lets somebody prove an account is theirs without a mail server.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> "Forgotten password" normally means "prove you can read the
/// mailbox we have on file". This service has no mail transport, and a reset that asks only for
/// a username is not a reset — it is an unauthenticated account takeover with a friendly form
/// around it. So the proof has to be something the learner already holds: a second secret,
/// issued once at registration, shown once, and stored only as a hash. It is the same idea as
/// the backup codes GitHub and Google hand out, and it is the honest answer to "how do you do
/// recovery with no third party".
///
/// <b>Shape.</b> 20 characters of Crockford base32 — <c>K7M2Q-3XZ9F-P4WRT-8NBHV</c> — which is
/// 100 bits of entropy. The alphabet leaves out <c>I</c>, <c>L</c>, <c>O</c> and <c>U</c>: the
/// first three because they are indistinguishable from <c>1</c> and <c>0</c> in most fonts and
/// this is a string humans copy off a screen and type into a phone, and <c>U</c> because
/// dropping it keeps accidental profanity out of a code you might have to read aloud to
/// support. <see cref="Normalise"/> then accepts what people actually type: any casing, any
/// spacing, any dashes, and <c>I</c>/<c>l</c>/<c>O</c> forgiven back to <c>1</c>/<c>0</c>.
///
/// <b>Hashing.</b> Plain SHA-256, where a password would need PBKDF2 — for the same reason
/// refresh tokens use it. Slow hashing exists to make guessing a low-entropy, human-chosen
/// secret expensive. Nothing guesses 100 random bits, so the slowness would cost every reset
/// 200 ms and buy exactly nothing.
/// </remarks>
internal static class RecoveryCode
{
    /// <summary>Crockford base32: the digits and the letters, less I, L, O and U.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>20 characters at 5 bits each — 100 bits.</summary>
    private const int Characters = 20;

    /// <summary>Characters between dashes, for something a person can read off a screen.</summary>
    private const int GroupSize = 5;

    /// <summary>Mints a fresh code, formatted for a human to write down.</summary>
    /// <returns>Something of the form <c>K7M2Q-3XZ9F-P4WRT-8NBHV</c>.</returns>
    /// <remarks>
    /// <see cref="RandomNumberGenerator.GetItems{T}(ReadOnlySpan{T}, int)"/> rather than
    /// <c>Random.Shared</c>: this is a bearer credential, and <c>Random</c> is predictable
    /// enough from a handful of outputs that it would hand an attacker everybody else's codes.
    /// It is also unbiased, which a naive <c>rng.Next() % 32</c> would not be.
    /// </remarks>
    internal static string Create()
    {
        char[] raw = RandomNumberGenerator.GetItems<char>(Alphabet, Characters);

        var formatted = new StringBuilder(Characters + (Characters / GroupSize) - 1);
        for (int i = 0; i < raw.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                formatted.Append('-');
            }
            formatted.Append(raw[i]);
        }

        return formatted.ToString();
    }

    /// <summary>
    /// Reduces whatever somebody typed to the canonical form, or to empty when it cannot be one.
    /// </summary>
    /// <param name="candidate">The code as typed, pasted, or dictated over the phone.</param>
    /// <returns>The 20-character canonical code, or an empty string when it is not one.</returns>
    /// <remarks>
    /// Deliberately forgiving about presentation and completely unforgiving about content.
    /// Spaces, dashes and casing carry no information, so rejecting a code over them only ever
    /// punishes the honest user; a character outside the alphabet, or the wrong length, means
    /// this is not a code this service ever issued.
    /// </remarks>
    internal static string Normalise(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Characters);
        foreach (char raw in candidate)
        {
            if (raw is ' ' or '-' or '_' or '\t')
            {
                continue;
            }

            char c = char.ToUpperInvariant(raw);

            // Crockford's decoding rules: the letters that were left out of the alphabet
            // because they look like digits are read back as those digits.
            c = c switch { 'I' or 'L' => '1', 'O' => '0', _ => c };

            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            builder.Append(c);

            if (builder.Length > Characters)
            {
                return string.Empty;
            }
        }

        return builder.Length == Characters ? builder.ToString() : string.Empty;
    }

    /// <summary>Hashes a canonical code for storage.</summary>
    /// <param name="canonical">The output of <see cref="Normalise"/>, or of <see cref="Create"/>.</param>
    internal static string Hash(string canonical) =>
        Convert.ToBase64String(SHA256.HashData(
            Encoding.UTF8.GetBytes(Normalise(canonical))));

    /// <summary>Checks a typed code against the stored hash.</summary>
    /// <param name="candidate">The code as typed.</param>
    /// <param name="storedHash">Base64 SHA-256 from the database.</param>
    /// <remarks>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> rather than <c>==</c>. String
    /// comparison stops at the first differing byte, so how long it takes leaks how much of the
    /// hash was right — the same timing side channel <c>PasswordHasher.Verify</c> closes, and
    /// the same one-line fix.
    /// </remarks>
    internal static bool Verify(string? candidate, string storedHash)
    {
        string canonical = Normalise(candidate);
        if (canonical.Length == 0 || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            // A corrupted row fails closed rather than throwing a 500 that tells the caller
            // this particular account exists and is broken.
            return false;
        }

        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
