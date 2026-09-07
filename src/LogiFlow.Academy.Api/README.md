# LogiFlow.Academy.Api

Accounts and progress storage for the tutorial site in [`site/`](../../site), plus a static
host for the site itself. One process, one URL, several people.

```bash
dotnet run --project src/LogiFlow.Academy.Api
# then open http://localhost:5280
```

No database to install, no Docker, no configuration. It creates a SQLite file under
`App_Data/` on first run and serves `site/` straight from the repository.

---

## Why it exists twice over

**For the learner.** The tutorial saves everything in the browser, which is fine until you
want it on your phone too, or until two people share one machine. An account carries the
same progress document between devices, and the merge in
[`site/assets/account.js`](../../site/assets/account.js) means editing on both does not
lose either.

**For the portfolio.** It is deliberately small enough to read in one sitting and it
contains, in about six files, the entire list an Italian junior `.NET` advert asks about:
minimal APIs, dependency injection, EF Core, JWT authentication, refresh-token rotation,
password hashing, middleware order, CORS, rate limiting and optimistic concurrency. Every
non-obvious decision is commented with *why*, which is the part an interviewer actually
probes.

It references **none** of the LogiFlow layers, and they reference nothing here. Two systems
sharing a repository and nothing else.

---

## The endpoints

| Method | Route | Auth | Purpose |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | — | Create an account, sign in, and return the one-time recovery code |
| `POST` | `/api/auth/login` | — | Sign in with a **username** and password |
| `POST` | `/api/auth/refresh` | — | Exchange a refresh token for a new pair |
| `POST` | `/api/auth/logout` | — | Revoke one refresh token |
| `POST` | `/api/auth/forgot-password` | — | Username + recovery code → a single-use reset ticket |
| `POST` | `/api/auth/reset-password` | — | Spend the ticket, set the password, get a fresh code |
| `POST` | `/api/auth/recovery-code` | Bearer | Replace the recovery code with a new one |
| `GET`  | `/api/auth/me` | Bearer | The signed-in user |
| `GET`  | `/api/profile` | Bearer | The stored progress document (`204` when there is none yet) |
| `PUT`  | `/api/profile` | Bearer | Store it (`409` when the revision is stale) |
| `GET`  | `/api/leaderboard` | Bearer | Top fifty learners who have not opted out |
| `PUT`  | `/api/me/leaderboard?visible=` | Bearer | Show or hide yourself |
| `DELETE` | `/api/me` | Bearer | Delete the account, its progress and its sessions |
| `GET`  | `/api/health` | — | Liveness. Deliberately does not touch the database |

In Development the whole thing is browsable at `/scalar/v1`.

---

## No email address, anywhere

The login is a **username**. There is no `Email` column and no mail transport, and the two
facts are the same decision: an address this service can never send to could never be
verified, never be written to, and never recover an account, so storing one would be personal
data collected for no purpose — the thing GDPR's data-minimisation principle exists to stop.

Which leaves the obvious question. **How do you reset a password with no email?**

Not by asking for the username and believing the answer; that is an account takeover with a
form around it. Instead every account is issued a **recovery code** at registration — 100 bits
of Crockford base32, shown exactly once, stored only as a SHA-256 hash — and presenting it is
what mints the reset ticket that an emailed link would otherwise have carried. It is spent on
use and replaced, and a learner who still has access can swap it for a new one at any time.

What it is honestly *not* is proof that anyone can still reach you: an email loop also confirms
the mailbox works. A lost code here is a lost account, and the site says so in those words
rather than pretending there is a way round it.

---

## The five decisions worth reading

### 1. Passwords: PBKDF2, salted, with the cost stored per user

[`Security/PasswordHasher.cs`](Security/PasswordHasher.cs). SHA-256 alone is the trap —
strong *and fast*, and fast is the wrong property when an attacker has a GPU. The iteration
count lives in the user row so it can be raised later without invalidating existing
accounts; an old hash still verifies against its own count and is silently re-hashed on the
next successful sign-in.

Argon2id would be the better modern choice and needs a third-party package. PBKDF2 is in the
base class library, is what ASP.NET Core Identity itself uses, and can be read end to end —
which matters more in a repository whose point is that you can read it.

### 2. Refresh tokens: hashed at rest, rotated, and a replay kills the family

[`Security/TokenService.cs`](Security/TokenService.cs) and
[`Endpoints/AuthEndpoints.cs`](Endpoints/AuthEndpoints.cs). A refresh token is a bearer
credential, so only its SHA-256 is stored. Every refresh issues a new one and marks the old
one used. If a used token turns up again the server cannot tell a thief from a client that
lost a response — so it revokes every session for that user and makes them sign in again.

A plain SHA-256 here where the password needed PBKDF2, and the reason is entropy: this token
is 256 random bits, so a fast hash costs nothing. Passwords are low-entropy and chosen by
people; that is the only reason they need a slow one.

### 3. The profile is a JSON blob with a revision number

[`Domain/AcademyEntities.cs`](Domain/AcademyEntities.cs) and
[`Endpoints/ProfileEndpoints.cs`](Endpoints/ProfileEndpoints.cs). The shape of the document
is owned by the browser and changes with every learning feature; modelling it relationally
would mean a migration per badge. Only the four figures the leaderboard orders by are
promoted into indexed columns, and the server derives those from the document rather than
trusting a summary the client supplied.

`Revision` is optimistic concurrency written by hand. The client sends the revision it read
before editing; a mismatch is a `409` carrying the current document, and the client merges
and retries. Without it, a phone that has been offline for a week silently overwrites a
week of work done on a laptop.

### 4. Two clocks are one clock

`TimeProvider` is injected rather than `DateTimeOffset.UtcNow` being called in six places.
Token expiry, lockout windows and streaks are all time-dependent, and a test that has to
wait fifteen real minutes to prove a token expired is a test nobody runs.

### 5. Static files above an explicit `UseRouting`

[`Program.cs`](Program.cs). This one was a bug before it was a decision. Static-file
middleware steps aside when an endpoint has already been selected, and `WebApplication`
inserts `UseRouting` at the *top* of the pipeline when you do not call it yourself — so the
`MapFallback` that turns unknown paths into `404`s was matching `/` first, and the home page
404ed while every other page worked.

Calling `app.UseRouting()` explicitly, below the static files, fixes it and is better
anyway: the tutorial is public, so a stylesheet should never pay for route matching, token
validation or a rate-limit permit. `AuthFlowTests.Every_public_page_and_asset_is_served_anonymously`
keeps it that way.

---

## Configuration

Everything has a working default. These are the keys that exist.

| Key | Default | Notes |
| --- | --- | --- |
| `Academy:Provider` | `Sqlite` | Or `SqlServer` |
| `ConnectionStrings:Academy` | a file in `App_Data/` | Required when the provider is `SqlServer` |
| `Academy:Jwt:SigningKey` | generated in Development | **Required** anywhere else; startup fails without it |
| `Academy:Jwt:AccessMinutes` | `15` | Access tokens are short because a JWT cannot be revoked |
| `Academy:Jwt:RefreshDays` | `30` | Refresh tokens are stateful and revocable |
| `Academy:ChapterCount` | `47` | Denominator for the mastery percentage. Must equal the site's chapter count — `doctor.cs` checks it |
| `Academy:AuthRequestsPerMinute` | `10` | Per IP, on `/api/auth/*` |
| `Academy:AllowedOrigins` | localhost + `null` | `null` is the origin a `file://` page sends |

To run it against SQL Server instead:

```bash
dotnet user-secrets --project src/LogiFlow.Academy.Api set "Academy:Provider" "SqlServer"
dotnet user-secrets --project src/LogiFlow.Academy.Api set "ConnectionStrings:Academy" \
  "Server=localhost,1433;Database=LogiFlowAcademy;User Id=sa;Password=...;TrustServerCertificate=True"
```

Nothing else changes. That switch is the whole argument for provider abstraction, made
concrete: no other file in the project mentions either provider.

### The signing key

In Development, one is generated on first run and kept in `App_Data/signing.key` (which is
git-ignored) so that restarting does not sign everybody out. **No key is committed**, because
anyone holding it can mint a token for any user. Outside Development a missing key stops the
process at startup rather than producing a service that boots green and fails at the first
sign-in.

---

## Schema

`EnsureCreatedAsync()`, not migrations — and not because migrations are hard.

This is a single instance with a small schema and no upgrade history to preserve, so
`EnsureCreated` gives a running database on first launch with no `dotnet ef` install and no
`Migrations/` folder to keep in step. That trade is right *here* and wrong almost everywhere
else: the same shortcut in a multi-instance deployment races on startup and forces the
runtime account to hold schema-altering permissions. See
`course/module-13-deployment/README.md`.

If this ever grows a second instance, the fix is real migrations run as a separate pipeline
step, not a bigger `EnsureCreated`.

---

## Tests

```bash
dotnet test tests/LogiFlow.Academy.Api.Tests
```

81 tests, all green. They boot the real application in-process against SQLite `:memory:` —
a real relational engine, not the EF Core in-memory provider, which is not a database and
happily passes tests that fail against anything you would deploy.

What they actually assert, beyond the happy paths:

- a wrong password and an unknown account produce the **same** message, so the login form is
  not an account-enumeration oracle
- a forged JWT signature is rejected
- a used refresh token cannot be replayed, and replaying one revokes the whole family
- a stale `baseRevision` gets a `409` carrying the current document
- one learner cannot read another's profile
- a malformed progress document produces zeroes rather than a `500`
- a mastery percentage cannot be inflated past 100 by a hand-edited profile
- the rate limiter throttles repeated sign-ins **and** leaves `/api/health` alone
- every public page is served anonymously and every unknown path is a `404`, not a `401`
