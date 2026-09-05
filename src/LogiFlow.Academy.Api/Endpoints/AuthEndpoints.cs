using System.Security.Claims;
using LogiFlow.Academy.Api.Domain;
using LogiFlow.Academy.Api.Persistence;
using LogiFlow.Academy.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace LogiFlow.Academy.Api.Endpoints;

/// <summary>Register, sign in, refresh, sign out, and recover a forgotten password.</summary>
public static class AuthEndpoints
{
    /// <summary>Failed attempts before an account is locked.</summary>
    private const int LockoutThreshold = 8;

    /// <summary>How long the lock lasts.</summary>
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a password-reset ticket is good for.
    /// </summary>
    /// <remarks>
    /// Short, because a reset ticket is a password that has not been chosen yet. The usual
    /// argument for an hour is that people go and make a cup of tea between asking for the
    /// reset and doing it — which applies when the ticket arrives by email. Here both steps
    /// happen on the same screen, so there is nothing to wait for.
    /// </remarks>
    private static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The one message returned for every failed sign-in.
    /// </summary>
    /// <remarks>
    /// Not "no account with that username" and not "wrong password". Either of those turns the
    /// login form into an account-enumeration oracle: an attacker learns which names are
    /// registered, which is the first half of credential stuffing and is itself a personal-data
    /// disclosure. The same reasoning is why registration cannot say "that username is taken"
    /// either — see <see cref="MapAuthEndpoints"/>.
    /// </remarks>
    private const string SignInFailed = "That username and password do not match an account.";

    /// <summary>
    /// The one message returned for every failed recovery attempt.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <see cref="SignInFailed"/>, and it matters more here: the reset form
    /// is the one place an attacker can probe with only a username and no password at all. If
    /// "no such account" and "wrong code" read differently, the form becomes a free membership
    /// list.
    /// </remarks>
    private const string RecoveryFailed =
        "That username and recovery code do not match an account.";

    /// <summary>Registers the authentication endpoints.</summary>
    /// <param name="app">The route builder.</param>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/api/auth")
            .WithTags("Authentication")
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        // ── Register ────────────────────────────────────────────────────────────────────
        group.MapPost("/register", async (
            RegisterRequest request,
            AcademyDbContext db,
            TokenService tokens,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string username = Usernames.Normalise(request.Username);
            if (!Usernames.IsValid(username))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["username"] = [Usernames.Rule],
                });
            }

            if ((request.Password ?? string.Empty).Length < PasswordHasher.MinimumPasswordLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = [$"Use at least {PasswordHasher.MinimumPasswordLength} characters."],
                });
            }

            if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken))
            {
                // A username, unlike an email address, is public by design — it is printed on
                // the leaderboard — so "that name is taken" discloses nothing an attacker could
                // not read off the board, and refusing to say it would only leave people
                // guessing why registration failed. This is the one place the enumeration
                // argument genuinely does not apply, and saying so is worth more than a
                // reflexive vague message.
                return Results.Problem(
                    detail: "That username is already taken. Pick another one.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            PasswordHasher.HashResult hashed = PasswordHasher.Hash(request.Password!);
            string recoveryCode = RecoveryCode.Create();

            var user = new AcademyUser
            {
                Username = username,
                DisplayName = CleanDisplayName(request.DisplayName, username),
                PasswordHash = hashed.Hash,
                PasswordSalt = hashed.Salt,
                PasswordIterations = hashed.Iterations,
                RecoveryCodeHash = RecoveryCode.Hash(recoveryCode),
                RecoveryCodeIssuedAt = clock.GetUtcNow(),
                CreatedAt = clock.GetUtcNow(),
                LastSignInAt = clock.GetUtcNow(),
            };

            db.Users.Add(user);
            AuthResponse response = await IssueAsync(db, tokens, clock, user, cancellationToken, recoveryCode);

            return Results.Ok(response);
        })
        .WithSummary("Creates an account, signs in, and returns the one-time recovery code.");

        // ── Sign in ─────────────────────────────────────────────────────────────────────
        group.MapPost("/login", async (
            LoginRequest request,
            AcademyDbContext db,
            TokenService tokens,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string username = Usernames.Normalise(request.Username);
            AcademyUser? user = await db.Users
                .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

            if (user is null)
            {
                // Spend the same CPU a real check would, so response time does not reveal
                // whether the account exists. Constant message plus constant work.
                PasswordHasher.BurnEquivalentWork();
                return Results.Problem(detail: SignInFailed, statusCode: StatusCodes.Status401Unauthorized);
            }

            DateTimeOffset now = clock.GetUtcNow();
            if (user.LockedUntil is { } locked && locked > now)
            {
                return Results.Problem(
                    detail: $"Too many failed attempts. Try again in {Math.Ceiling((locked - now).TotalMinutes)} minutes.",
                    statusCode: StatusCodes.Status423Locked);
            }

            bool ok = PasswordHasher.Verify(
                request.Password ?? string.Empty, user.PasswordHash, user.PasswordSalt, user.PasswordIterations);

            if (!ok)
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= LockoutThreshold)
                {
                    user.LockedUntil = now.Add(LockoutWindow);
                    user.FailedAttempts = 0;
                }
                await db.SaveChangesAsync(cancellationToken);
                return Results.Problem(detail: SignInFailed, statusCode: StatusCodes.Status401Unauthorized);
            }

            user.FailedAttempts = 0;
            user.LockedUntil = null;
            user.LastSignInAt = now;

            // Transparent cost upgrade: the account was created when the iteration count was
            // lower, and the only moment the plaintext is legitimately available to re-hash it
            // is right now, during a successful sign-in.
            if (PasswordHasher.NeedsUpgrade(user.PasswordIterations))
            {
                PasswordHasher.HashResult upgraded = PasswordHasher.Hash(request.Password!);
                user.PasswordHash = upgraded.Hash;
                user.PasswordSalt = upgraded.Salt;
                user.PasswordIterations = upgraded.Iterations;
            }

            AuthResponse response = await IssueAsync(db, tokens, clock, user, cancellationToken);
            return Results.Ok(response);
        })
        .WithSummary("Signs in and returns an access token and a refresh token.");

        // ── Forgotten password, step one: prove the account is yours ────────────────────
        //
        //  This is where an ordinary service would send an email and answer 202 regardless of
        //  whether the account exists. With no mail transport there is nothing to send, so the
        //  proof has to travel the other way: the learner presents the recovery code they were
        //  given at registration, and gets back the ticket that an emailed link would have
        //  carried.
        //
        //  It is genuinely a proof of ownership rather than security theatre, because the code
        //  is 100 random bits and the server stores only its hash. What it is NOT is a proof
        //  that anyone can still reach you — an email loop also tells the user "your mailbox
        //  still works". A lost code here is a lost account, and the pages say so plainly
        //  rather than pretending otherwise.
        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            AcademyDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string username = Usernames.Normalise(request.Username);
            AcademyUser? user = await db.Users
                .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

            if (user is null || !RecoveryCode.Verify(request.RecoveryCode, user.RecoveryCodeHash))
            {
                return Results.Problem(detail: RecoveryFailed, statusCode: StatusCodes.Status401Unauthorized);
            }

            DateTimeOffset now = clock.GetUtcNow();

            // Asking for a second ticket kills the first. Otherwise every abandoned attempt
            // leaves a live password-change credential behind for its full lifetime, and the
            // window an attacker has to find one grows with every click of the button.
            await db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

            string ticket = TokenService.CreateOpaqueToken();

            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = TokenService.HashOpaqueToken(ticket),
                ExpiresAt = now.Add(ResetWindow),
                CreatedAt = now,
            });

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new ForgotPasswordResponse(ticket, (int)ResetWindow.TotalSeconds));
        })
        .WithSummary("Exchanges a username and recovery code for a single-use reset ticket.");

        // ── Forgotten password, step two: spend the ticket ──────────────────────────────
        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            AcademyDbContext db,
            TokenService tokens,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            if ((request.NewPassword ?? string.Empty).Length < PasswordHasher.MinimumPasswordLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["newPassword"] = [$"Use at least {PasswordHasher.MinimumPasswordLength} characters."],
                });
            }

            string hash = TokenService.HashOpaqueToken(request.ResetToken ?? string.Empty);
            PasswordResetToken? ticket = await db.PasswordResetTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

            DateTimeOffset now = clock.GetUtcNow();

            if (ticket?.User is null || !ticket.IsRedeemable(now))
            {
                return Results.Problem(
                    detail: "That reset has expired or has already been used. Start again.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            AcademyUser user = ticket.User;

            PasswordHasher.HashResult hashed = PasswordHasher.Hash(request.NewPassword!);
            user.PasswordHash = hashed.Hash;
            user.PasswordSalt = hashed.Salt;
            user.PasswordIterations = hashed.Iterations;

            // A completed reset clears the lockout. The person who just proved they own the
            // account should not then be told to wait fifteen minutes because whoever was
            // guessing at their password tripped the counter.
            user.FailedAttempts = 0;
            user.LockedUntil = null;
            user.LastSignInAt = now;

            // The code that just recovered the account is spent. A recovery code that keeps
            // working is a permanent second password, and the whole point of the ceremony is
            // that using it costs you it.
            string recoveryCode = RecoveryCode.Create();
            user.RecoveryCodeHash = RecoveryCode.Hash(recoveryCode);
            user.RecoveryCodeIssuedAt = now;

            ticket.UsedAt = now;

            // Every other session dies. If the reset happened because somebody else had got
            // in, leaving their refresh token alive would make the whole exercise pointless —
            // they would simply carry on. The cost is signing yourself out on your other
            // devices, which is the correct thing to make somebody do after a reset.
            await db.RefreshTokens
                .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

            AuthResponse response = await IssueAsync(db, tokens, clock, user, cancellationToken, recoveryCode);
            return Results.Ok(response);
        })
        .WithSummary("Spends a reset ticket, sets the new password, and issues a fresh recovery code.");

        // ── Refresh ─────────────────────────────────────────────────────────────────────
        group.MapPost("/refresh", async (
            RefreshRequest request,
            AcademyDbContext db,
            TokenService tokens,
            TimeProvider clock,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string hash = TokenService.HashOpaqueToken(request.RefreshToken ?? string.Empty);
            RefreshToken? stored = await db.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

            DateTimeOffset now = clock.GetUtcNow();

            if (stored is null || stored.User is null)
            {
                return Results.Problem(detail: "That session is no longer valid.", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!stored.IsActive(now))
            {
                // A token that has already been exchanged is being presented again. Either it
                // was stolen and replayed, or a legitimate client lost the response to its own
                // refresh — and the server cannot tell which. Revoking the whole family is the
                // conservative choice: the honest user signs in again, the attacker gets
                // nothing, and the alternative silently accepts a stolen credential.
                if (stored.RevokedAt is not null)
                {
                    ILogger logger = loggerFactory.CreateLogger("LogiFlow.Academy.Api.Auth");
                    logger.LogWarning(
                        "Reused refresh token for user {UserId}; revoking all sessions.", stored.UserId);

                    await db.RefreshTokens
                        .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
                }

                return Results.Problem(detail: "That session has expired. Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
            }

            TokenService.IssuedTokens issued = tokens.Issue(stored.User);
            string newHash = TokenService.HashOpaqueToken(issued.RefreshToken);

            stored.RevokedAt = now;
            stored.ReplacedByTokenHash = newHash;

            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = stored.UserId,
                TokenHash = newHash,
                ExpiresAt = issued.RefreshExpiresAt,
                CreatedAt = now,
            });

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new AuthResponse(
                issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds, ToDto(stored.User)));
        })
        .WithSummary("Exchanges a refresh token for a new pair. The old token stops working.");

        // ── Sign out ────────────────────────────────────────────────────────────────────
        group.MapPost("/logout", async (
            RefreshRequest request,
            AcademyDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string hash = TokenService.HashOpaqueToken(request.RefreshToken ?? string.Empty);
            await db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), cancellationToken);

            // 204 whether or not anything matched. "That token did not exist" is information
            // an unauthenticated caller has no need for.
            return Results.NoContent();
        })
        .WithSummary("Revokes a refresh token. The access token remains valid until it expires.");

        // ── Who am I ────────────────────────────────────────────────────────────────────
        app.MapGet("/api/auth/me", async (
            ClaimsPrincipal principal,
            AcademyDbContext db,
            CancellationToken cancellationToken) =>
        {
            Guid id = principal.UserId();
            AcademyUser? user = await db.Users.FindAsync([id], cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(ToDto(user));
        })
        .RequireAuthorization()
        .WithTags("Authentication")
        .WithSummary("Returns the signed-in user.");

        // ── A replacement recovery code ─────────────────────────────────────────────────
        //
        //  Authenticated, because being signed in is itself the proof of ownership — and
        //  because the person most likely to need this is somebody who still has access and
        //  has just realised they never wrote the first code down.
        //
        //  It replaces rather than adds. Two live codes would double the number of secrets
        //  that can take over the account, and a learner who has lost one has no way to tell
        //  you which of the two is still out there.
        app.MapPost("/api/auth/recovery-code", async (
            ClaimsPrincipal principal,
            AcademyDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            Guid id = principal.UserId();
            AcademyUser? user = await db.Users.FindAsync([id], cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            string code = RecoveryCode.Create();
            user.RecoveryCodeHash = RecoveryCode.Hash(code);
            user.RecoveryCodeIssuedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new RecoveryCodeResponse(code));
        })
        .RequireAuthorization()
        .WithTags("Authentication")
        .WithSummary("Issues a new recovery code and invalidates the previous one.");

        return app;
    }

    /// <summary>Issues a token pair, stores the refresh token's hash, and saves.</summary>
    private static async Task<AuthResponse> IssueAsync(
        AcademyDbContext db,
        TokenService tokens,
        TimeProvider clock,
        AcademyUser user,
        CancellationToken cancellationToken,
        string? recoveryCode = null)
    {
        TokenService.IssuedTokens issued = tokens.Issue(user);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashOpaqueToken(issued.RefreshToken),
            ExpiresAt = issued.RefreshExpiresAt,
            CreatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds, ToDto(user), recoveryCode);
    }

    private static UserDto ToDto(AcademyUser user) => new(user.Id, user.Username, user.DisplayName);

    private static string CleanDisplayName(string? candidate, string username)
    {
        string name = (candidate ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = username;
        }
        return name.Length > 60 ? name[..60] : name;
    }
}

/// <summary>Reads the user id out of the token's claims.</summary>
internal static class PrincipalExtensions
{
    /// <summary>The signed-in user's id, or <see cref="Guid.Empty"/> when there is not one.</summary>
    /// <param name="principal">The caller.</param>
    internal static Guid UserId(this ClaimsPrincipal principal)
    {
        string? value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
    }
}
