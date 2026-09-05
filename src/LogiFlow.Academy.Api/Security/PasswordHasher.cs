using System.Security.Cryptography;

namespace LogiFlow.Academy.Api.Security;

/// <summary>
/// Turns a password into something safe to store, and checks a candidate against it.
/// </summary>
/// <remarks>
/// <b>This is the interview question, written out.</b> "How do you store a password?" has one
/// acceptable answer and three common wrong ones:
///
/// <list type="bullet">
///   <item><description>
///     <b>Encryption is wrong.</b> Encryption is reversible by design; if your service can
///     decrypt it, so can anyone who takes the key. There is no legitimate reason to ever
///     recover a user's password — if you can email it to them, you have already lost.
///   </description></item>
///   <item><description>
///     <b>SHA-256 alone is wrong,</b> and it is the trap, because SHA-256 is genuinely strong.
///     It is also genuinely <i>fast</i> — billions of guesses per second on a GPU — and speed is
///     the wrong property here. A password hash must be deliberately slow.
///   </description></item>
///   <item><description>
///     <b>An unsalted hash is wrong.</b> Without a per-user salt, identical passwords produce
///     identical hashes, so one rainbow table breaks every account at once and the dump tells
///     the attacker which users to target first.
///   </description></item>
/// </list>
///
/// What is here is <b>PBKDF2-HMAC-SHA256</b> with a 128-bit random salt and an iteration count
/// at OWASP's current floor. Argon2id is the better modern choice and would be the answer in a
/// greenfield service, but it needs a third-party package; PBKDF2 is in the base class library,
/// is FIPS-approved, and is what <c>ASP.NET Core Identity</c> itself uses by default. Given a
/// repository whose point is that you can read every line of it, the BCL version wins.
///
/// The iteration count is stored per user, which is the part people leave out. It means the
/// cost can be raised as hardware improves: an old hash still verifies against its own recorded
/// count, and is transparently upgraded the next time that user signs in successfully.
/// </remarks>
internal static class PasswordHasher
{
    /// <summary>OWASP's recommended minimum for PBKDF2-HMAC-SHA256 (2023 onward).</summary>
    internal const int DefaultIterations = 210_000;

    /// <summary>128 bits. More would not hurt; less starts to allow collisions across users.</summary>
    private const int SaltBytes = 16;

    /// <summary>256 bits, matching the underlying hash's output size. Asking for more buys nothing.</summary>
    private const int HashBytes = 32;

    /// <summary>The shortest password this service will accept.</summary>
    /// <remarks>
    /// Length beats composition rules. "At least one uppercase, one digit and one symbol" mostly
    /// produces <c>Password1!</c>; a longer minimum with no composition rule produces better
    /// passwords and fewer sticky notes on monitors. This matches NIST SP 800-63B's advice.
    /// </remarks>
    internal const int MinimumPasswordLength = 10;

    /// <summary>The result of hashing a new password.</summary>
    /// <param name="Hash">Base64 PBKDF2 output.</param>
    /// <param name="Salt">Base64 salt.</param>
    /// <param name="Iterations">Iteration count used.</param>
    internal readonly record struct HashResult(string Hash, string Salt, int Iterations);

    /// <summary>Hashes a new or changed password.</summary>
    /// <param name="password">The plaintext password. Never stored, never logged.</param>
    /// <param name="iterations">Iteration count; defaults to <see cref="DefaultIterations"/>.</param>
    /// <returns>The hash, its salt, and the cost used to produce it.</returns>
    internal static HashResult Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1000);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);

        return new HashResult(Convert.ToBase64String(hash), Convert.ToBase64String(salt), iterations);
    }

    /// <summary>Checks a candidate password against a stored hash.</summary>
    /// <param name="password">The candidate.</param>
    /// <param name="storedHash">Base64 hash from the database.</param>
    /// <param name="storedSalt">Base64 salt from the database.</param>
    /// <param name="iterations">The iteration count recorded with that hash.</param>
    /// <returns><see langword="true"/> when the password matches.</returns>
    /// <remarks>
    /// The comparison is <see cref="CryptographicOperations.FixedTimeEquals"/> and not
    /// <c>SequenceEqual</c>. An ordinary comparison returns as soon as two bytes differ, so how
    /// long it takes leaks how much of the hash was correct — a timing side channel. It is a
    /// small risk over a network and it costs one method call to remove.
    /// </remarks>
    internal static bool Verify(string password, string storedHash, string storedSalt, int iterations)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(storedSalt);
            expected = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            // A corrupted row must fail closed, not throw a 500 that tells the caller
            // this particular account exists and is broken.
            return false;
        }

        if (iterations < 1000 || expected.Length == 0)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>True when a stored hash was produced with a now-too-low cost.</summary>
    /// <param name="iterations">The iteration count recorded with the hash.</param>
    internal static bool NeedsUpgrade(int iterations) => iterations < DefaultIterations;

    /// <summary>
    /// Burns roughly the same CPU as a real verification, for accounts that do not exist.
    /// </summary>
    /// <remarks>
    /// Without this, "no such user" returns in a millisecond and "wrong password" takes a
    /// hundred, so anyone can enumerate which usernames have accounts by timing the
    /// responses. Returning the same message for both is only half the fix; taking the same
    /// amount of time is the other half.
    /// </remarks>
    internal static void BurnEquivalentWork()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        _ = Rfc2898DeriveBytes.Pbkdf2(
            "not-a-real-password", salt, DefaultIterations, HashAlgorithmName.SHA256, HashBytes);
    }
}
