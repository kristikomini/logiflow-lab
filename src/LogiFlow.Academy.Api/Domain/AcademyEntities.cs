namespace LogiFlow.Academy.Api.Domain;

/// <summary>
/// Somebody using the tutorial site.
/// </summary>
/// <remarks>
/// There is no <c>Password</c> property and there never will be. What is stored is a
/// PBKDF2 derivation of it, plus the salt and iteration count needed to check a candidate
/// against it — see <c>PasswordHasher</c>. Storing the parameters alongside the hash is
/// what makes it possible to raise the iteration count later without invalidating every
/// existing account: an old hash still verifies with its own parameters, and is re-hashed
/// with the new ones on the next successful sign-in.
/// </remarks>
public sealed class AcademyUser
{
    /// <summary>Primary key. A v7 GUID, so it is time-ordered and does not fragment the index.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// Normalised (lower-cased, trimmed) username. Unique, and the only sign-in credential.
    /// </summary>
    /// <remarks>
    /// <b>There is deliberately no email column.</b> This service has no mail transport, so an
    /// address could never be verified, never be written to, and never be used to recover an
    /// account — it would be personal data collected for no purpose, which is precisely what
    /// GDPR's data-minimisation principle exists to stop. Recovery is handled by
    /// <see cref="RecoveryCodeHash"/> instead, which needs no third party at all.
    /// </remarks>
    public required string Username { get; set; }

    /// <summary>What other learners see on the leaderboard.</summary>
    public required string DisplayName { get; set; }

    /// <summary>PBKDF2 output, base64.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Per-user random salt, base64. Never shared between users.</summary>
    public required string PasswordSalt { get; set; }

    /// <summary>Iteration count used to produce <see cref="PasswordHash"/>.</summary>
    public int PasswordIterations { get; set; }

    /// <summary>
    /// SHA-256 of the account's current recovery code, base64. The code itself is shown once
    /// and never stored.
    /// </summary>
    /// <remarks>
    /// <b>Why a fast hash here and a deliberately slow one for the password.</b> The recovery
    /// code is 100 bits from a cryptographic RNG, so guessing it is infeasible however fast the
    /// hash is. PBKDF2's slowness only ever buys anything against a <i>low-entropy,
    /// human-chosen</i> secret. Same reasoning as <see cref="RefreshToken.TokenHash"/>, and the
    /// mirror image of <c>PasswordHasher</c>'s.
    ///
    /// It is the one thing that makes a password reset possible without an email server: a
    /// second secret, held only by the learner, which proves the account is theirs. Rotated
    /// every time it is used, so a code that has recovered an account once cannot do it twice.
    /// </remarks>
    public required string RecoveryCodeHash { get; set; }

    /// <summary>When the current recovery code was issued.</summary>
    public DateTimeOffset RecoveryCodeIssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last successful sign-in, for the "inactive account" question nobody asks until it matters.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>
    /// Consecutive failed sign-in attempts. Reset on success.
    /// </summary>
    /// <remarks>
    /// With <see cref="LockedUntil"/> this is the smallest honest defence against credential
    /// stuffing that does not need an external service. It is per-account, so it does not stop
    /// a spray across many accounts — that is what the per-IP rate limiter in <c>Program.cs</c>
    /// is for. Neither is a substitute for the other.
    /// </remarks>
    public int FailedAttempts { get; set; }

    /// <summary>Set when <see cref="FailedAttempts"/> crosses the threshold.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Whether this learner appears on the leaderboard. Opt-out, not opt-in.</summary>
    public bool ShowOnLeaderboard { get; set; } = true;

    /// <summary>The learner's stored progress. One per user.</summary>
    public LearnerProfile? Profile { get; set; }

    /// <summary>Issued refresh tokens, live and revoked.</summary>
    public List<RefreshToken> RefreshTokens { get; } = [];

    /// <summary>Issued password-reset tokens, live, spent and expired.</summary>
    public List<PasswordResetToken> PasswordResetTokens { get; } = [];
}

/// <summary>
/// One learner's whole progress document, stored as the JSON the browser produced.
/// </summary>
/// <remarks>
/// <b>Why a blob and not a schema.</b> The shape of this document is owned by
/// <c>site/assets/store.js</c> and changes whenever a learning feature is added — a new badge,
/// a new counter, a different card field. Modelling it relationally would mean a migration for
/// every one of those, for data that is never queried by its parts: the server only ever reads
/// and writes the whole thing.
///
/// The columns that ARE promoted out of the JSON (<see cref="Xp"/>, <see cref="MasteryPercent"/>,
/// <see cref="StreakDays"/>) exist because the leaderboard queries them, and querying inside a
/// JSON column across two different database providers is exactly the sort of cleverness that
/// stops working when somebody switches provider. They are denormalised copies, written on save,
/// and the document remains the source of truth.
/// </remarks>
public sealed class LearnerProfile
{
    /// <summary>Primary key, and the foreign key to <see cref="AcademyUser"/>.</summary>
    public Guid UserId { get; init; }

    /// <summary>Navigation back to the owner.</summary>
    public AcademyUser? User { get; set; }

    /// <summary>The whole profile document, exactly as the browser serialised it.</summary>
    public required string Document { get; set; }

    /// <summary>
    /// Incremented on every write. The client sends the revision it based its edit on, and a
    /// mismatch is a 409 rather than a silent overwrite.
    /// </summary>
    /// <remarks>
    /// This is optimistic concurrency done by hand, and it is the thing that makes two devices
    /// safe. Without it, a phone that has been offline for a week would happily replace a week
    /// of work done on a laptop, and nobody would find out until the XP went backwards.
    /// </remarks>
    public int Revision { get; set; }

    /// <summary>The client's own last-modified stamp, used by its merge logic.</summary>
    public DateTimeOffset ClientUpdatedAt { get; set; }

    /// <summary>When the server last stored it.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Denormalised from the document for the leaderboard.</summary>
    public int Xp { get; set; }

    /// <summary>Denormalised from the document for the leaderboard. 0–100.</summary>
    public double MasteryPercent { get; set; }

    /// <summary>Denormalised from the document for the leaderboard.</summary>
    public int StreakDays { get; set; }

    /// <summary>Denormalised from the document. How many chapter tests are passed.</summary>
    public int ChaptersPassed { get; set; }
}

/// <summary>
/// A long-lived token that can be exchanged for a new access token.
/// </summary>
/// <remarks>
/// <b>The token itself is not stored.</b> Only a SHA-256 hash of it is, for the same reason
/// passwords are not stored: a stolen database dump should not hand the attacker working
/// credentials. A refresh token is a bearer credential — anyone holding one IS the user —
/// so it deserves the same treatment.
///
/// Rotation is the other half. Every refresh issues a new token and marks the old one used,
/// recording which token replaced it. If a used token is ever presented again, either it was
/// replayed by an attacker or the legitimate client lost the response — and since the server
/// cannot tell those apart, the safe response is to revoke the whole chain and force a
/// sign-in. That is <c>ReplacedByTokenHash</c>'s only purpose.
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Owner.</summary>
    public Guid UserId { get; init; }

    /// <summary>Navigation to the owner.</summary>
    public AcademyUser? User { get; set; }

    /// <summary>SHA-256 of the token string, base64. The token itself is never persisted.</summary>
    public required string TokenHash { get; set; }

    /// <summary>When it stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Set when it is exchanged or explicitly revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The hash of the token that replaced this one, when it was rotated.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>True while the token may still be exchanged.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>
/// A short-lived, single-use ticket that authorises one password change.
/// </summary>
/// <remarks>
/// <b>This is the thing an emailed reset link carries.</b> Splitting the reset in two —
/// prove who you are, then choose a password — is not ceremony: it is what lets the proof
/// arrive by one channel and the new password by another, and it is the shape every real
/// reset flow has. Here the proof is the recovery code rather than possession of a mailbox,
/// but the ticket, its expiry and its single use are identical.
///
/// Three properties do the work, and leaving any one out is a known way to be breached:
/// <list type="number">
///   <item><description>
///     <b>Hashed at rest.</b> Only SHA-256 of the token is stored, so a database dump does
///     not hand the attacker a pending reset for every account that has one open.
///   </description></item>
///   <item><description>
///     <b>Short expiry.</b> Fifteen minutes. A reset link that works next year is a
///     permanent second password sitting in somebody's inbox.
///   </description></item>
///   <item><description>
///     <b>Single use.</b> <see cref="UsedAt"/> is set the moment it changes a password, and
///     issuing a new one invalidates the account's earlier unspent tickets.
///   </description></item>
/// </list>
/// </remarks>
public sealed class PasswordResetToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Owner.</summary>
    public Guid UserId { get; init; }

    /// <summary>Navigation to the owner.</summary>
    public AcademyUser? User { get; set; }

    /// <summary>SHA-256 of the token string, base64. The token itself is never persisted.</summary>
    public required string TokenHash { get; set; }

    /// <summary>When it stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When it was issued.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Set the moment it is spent, or when a newer ticket supersedes it.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>True while the ticket may still be redeemed.</summary>
    public bool IsRedeemable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
}
