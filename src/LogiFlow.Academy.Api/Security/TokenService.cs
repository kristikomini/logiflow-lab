using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LogiFlow.Academy.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LogiFlow.Academy.Api.Security;

/// <summary>JWT and refresh-token settings, bound from the <c>Academy:Jwt</c> section.</summary>
public sealed class AcademyJwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Academy:Jwt";

    /// <summary>Token issuer.</summary>
    public string Issuer { get; init; } = "logiflow-academy";

    /// <summary>Intended audience.</summary>
    public string Audience { get; init; } = "logiflow-academy-site";

    /// <summary>
    /// HMAC-SHA256 signing key. Must be at least 32 bytes.
    /// </summary>
    /// <remarks>
    /// In Development this is generated on first run and written to
    /// <c>App_Data/signing.key</c> when it is not configured, so that a fresh clone starts
    /// without a setup step and does NOT ship a committed key that everybody shares. In any
    /// other environment a missing key stops the process at startup, loudly, rather than
    /// producing a service that mints tokens anyone can forge.
    /// </remarks>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Access-token lifetime in minutes.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A JWT cannot be revoked before it expires — that is the price of a
    /// stateless token — so its lifetime IS the revocation window. Fifteen minutes is the
    /// usual compromise; the refresh token, which is stateful and revocable, carries the
    /// long-lived session.
    /// </remarks>
    public int AccessMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshDays { get; init; } = 30;
}

/// <summary>Mints access tokens and refresh tokens.</summary>
/// <param name="options">Bound and validated <see cref="AcademyJwtOptions"/>.</param>
/// <param name="timeProvider">Injected clock, so tests do not depend on the wall clock.</param>
internal sealed class TokenService(IOptions<AcademyJwtOptions> options, TimeProvider timeProvider)
{
    private readonly AcademyJwtOptions _options = options.Value;

    /// <summary>An issued pair, plus what the client needs to know about it.</summary>
    /// <param name="AccessToken">The signed JWT.</param>
    /// <param name="RefreshToken">The opaque refresh token. Shown to the client once.</param>
    /// <param name="ExpiresInSeconds">Access-token lifetime.</param>
    /// <param name="RefreshExpiresAt">When the refresh token stops working.</param>
    internal readonly record struct IssuedTokens(
        string AccessToken, string RefreshToken, int ExpiresInSeconds, DateTimeOffset RefreshExpiresAt);

    /// <summary>Issues an access token and a refresh token for a user.</summary>
    /// <param name="user">The signed-in user.</param>
    internal IssuedTokens Issue(AcademyUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        DateTimeOffset now = timeProvider.GetUtcNow();

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        // Only what the API itself needs. A JWT payload is base64, not encrypted — anyone
        // holding the token can read every claim in it — so nothing goes in here that would
        // matter if it were printed in a log or pasted into jwt.io.
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
        ];

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.UtcDateTime.AddMinutes(_options.AccessMinutes),
            signingCredentials: credentials);

        return new IssuedTokens(
            new JwtSecurityTokenHandler().WriteToken(token),
            CreateOpaqueToken(),
            _options.AccessMinutes * 60,
            now.AddDays(_options.RefreshDays));
    }

    /// <summary>
    /// An opaque bearer token: 256 bits from a cryptographic RNG, url-safe base64.
    /// </summary>
    /// <remarks>
    /// Used for both kinds of opaque token this service issues — the refresh token that carries
    /// a session, and the single-use ticket that authorises one password reset. They have
    /// different lifetimes and different tables, but identical requirements: unguessable, and
    /// never stored in the clear.
    ///
    /// <see cref="RandomNumberGenerator"/> and not <see cref="System.Random"/>. <c>Random</c> is
    /// seeded predictably enough that an attacker who observes a few outputs can predict the
    /// rest — fine for shuffling a quiz, catastrophic for a bearer credential. This is one of
    /// the few places in ordinary application code where the distinction is not academic.
    /// </remarks>
    internal static string CreateOpaqueToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Hashes an opaque token for storage.
    /// </summary>
    /// <param name="token">The raw token.</param>
    /// <remarks>
    /// A plain SHA-256 here, deliberately, where a password would need PBKDF2. The reason is
    /// entropy: this token is 256 random bits, so brute-forcing it is infeasible regardless of
    /// how fast the hash is. Passwords are low-entropy and chosen by humans, which is the only
    /// reason they need a slow hash. Using PBKDF2 here would cost 200 ms per API call and buy
    /// nothing.
    /// </remarks>
    internal static string HashOpaqueToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
