# Module 24 — Security for a .NET API

> Not a compliance checklist. The eight or nine things that actually go wrong in a .NET web API,
> what the framework already does for you, and — the part that separates a senior candidate —
> *why* each defence works, so you can tell when it does not apply.

Module 15 covers authentication and authorization as parts of the pipeline. This module is about
the threat on the other side of each one.

---

## 1. Authentication vs authorization, and the order that breaks everything

```
   AUTHENTICATION   who are you?            produces a ClaimsPrincipal
   AUTHORIZATION    may you do this?        reads that principal, applies a policy
```

They are two middlewares, and **the order is not a style choice**:

```csharp
app.UseAuthentication();   // sets HttpContext.User
app.UseAuthorization();    // reads HttpContext.User
```

Swap them and `User` is an empty, unauthenticated principal when the policy runs — so every
`[Authorize]` endpoint returns 401 and every `[AllowAnonymous]` one works. It looks like a broken
token, and it is a broken pipeline. 📂 [`Api/Program.cs`](../../src/LogiFlow.Api/Program.cs) has
the comment on exactly this line, which is where the comment belongs.

**Policies, not roles, at the endpoint.** 📂 [`Api/Infrastructure/Authentication.cs`](../../src/LogiFlow.Api/Infrastructure/Authentication.cs)
defines `CatalogManager`, `WarehouseStaff` and `Analyst` as named policies. The reason is
maintenance: `RequireAuthorization(Roles = "Admin,Manager")` scatters the rule across every
endpoint that uses it, so *changing who may manage the catalogue* means finding all of them. A
named policy is defined once — and its definition can later become "has the `catalog:write`
scope" or "is in this Entra ID group" without touching a single endpoint.

**Roles are a claim, not a concept.** A role is just `ClaimTypes.Role` in the token. Once you
internalise that, "role-based" versus "claims-based" stops being two systems and becomes one.

---

## 2. What a JWT actually is

```
   eyJhbGciOiJIUzI1NiJ9  .  eyJzdWIiOiIxMjMiLCJyb2xlIjoiQWRtaW4ifQ  .  dBjftJeZ4CVP...
   └─ header ───────────┘   └─ payload ─────────────────────────┘     └─ signature ┘
      alg, typ                sub, iss, aud, exp, nbf, iat, roles       HMAC or RSA
```

The two facts that matter, and candidates get them wrong constantly:

> **A JWT is signed, not encrypted.** The payload is base64url — *not* encryption. Anyone holding
> the token can read every claim in it. Never put anything private in a JWT: no personal data,
> no internal ids you would not publish, certainly no secrets.

> **The signature proves it was issued by someone holding the key, and has not been altered.**
> That is all it proves. It says nothing about whether the token has since been revoked.

**Validation is not optional and not automatic.** `TokenValidationParameters` must check the
issuer, the audience, the lifetime **and** the signing key. The historic CVE class here is
accepting `"alg": "none"`, or accepting an HMAC token signed with the *public* RSA key. Modern
`Microsoft.IdentityModel` refuses both, but the rule stands: **validate the algorithm, never
accept the one the token asks for.**

**Symmetric (HMAC) vs asymmetric (RSA/ECDSA):** with HMAC, the same key signs and verifies — so
every service that *validates* can also *mint*. For anything beyond a single API, use asymmetric
signing and publish the public key via JWKS, so a verifier cannot forge.

**Expiry is the revocation story, and short expiry is the answer.** A JWT cannot be un-issued.
Fifteen minutes plus a refresh token that *is* stored server-side and *can* be revoked is the
standard shape. "Log out" on a pure-JWT system does not invalidate anything; it deletes the
client's copy.

**Clock skew.** The default `ClockSkew` is five minutes, so a "15-minute" token is valid for
twenty. Set it explicitly, and be aware of it when reasoning about expiry windows.

**Store the signing key nowhere near the repository.** The `appsettings.Development.json` value in
this codebase is a throwaway, and the XML doc on `JwtOptions.SigningKey` says so in as many words:
*anyone holding this key can mint a token for any user with any role.*

---

## 3. Passwords, if you ever store one

Prefer not to — an identity provider (Entra ID, Auth0, Keycloak) is nearly always the right
answer. When you must:

```
   NEVER   plain text; encryption (reversible is the wrong property); MD5; SHA-1
   NEVER   a bare SHA-256 — it is FAST, which is exactly what an attacker wants
   USE     a deliberately slow, salted, memory-hard KDF:
              Argon2id  (preferred today)
              PBKDF2    (in the box: Rfc2898DeriveBytes, ≥ 600,000 iterations for SHA-256)
              bcrypt / scrypt
```

The whole point is **slowness with a tuning parameter**, so that the cost rises with hardware.
A per-user random **salt** (stored alongside the hash) makes rainbow tables useless and stops two
users with the same password sharing a hash.

**Compare in constant time.** `CryptographicOperations.FixedTimeEquals` — a normal comparison
returns early on the first differing byte, which leaks how much of the value was right. The same
rule applies to comparing API keys and HMAC signatures.

**And the one people forget:** the login endpoint must return the same message and take
approximately the same time whether the user exists or not. "Unknown email" versus "wrong
password" is a free user-enumeration oracle.

---

## 4. Injection, and why EF Core mostly ends the argument

```csharp
// SAFE — a parameter, not a string. The value can never become SQL.
var orders = await db.Orders.Where(o => o.Number == input).ToListAsync(ct);
await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET Status = {status}");

// SAFE — FromSqlInterpolated parameterises the holes
db.Orders.FromSqlInterpolated($"SELECT * FROM Orders WHERE Number = {input}");

// UNSAFE — string concatenation. The classic, and it still ships.
db.Orders.FromSqlRaw("SELECT * FROM Orders WHERE Number = '" + input + "'");
```

**Why parameterisation works** is worth being able to say: the value is never part of the SQL
*text*. It is sent separately and bound by the server, so no content — quotes, semicolons,
`DROP TABLE` — can change the statement's structure. It is not escaping, and it is not a filter.
This is also why the folklore "escape the input" is the wrong mental model: escaping is a
best-effort transformation, parameterisation is a categorical separation.

**Where it still bites you in an EF codebase:** dynamic `ORDER BY` and dynamic table or column
names cannot be parameters. A column name is not a value. The only safe pattern is an **allow
list**:

```csharp
string column = sort switch
{
    "total"  => "TotalAmount",
    "date"   => "CreatedOnUtc",
    _        => "Id",              // never the user's string, ever
};
```

**Other injections in the same family:** command injection (`Process.Start` with concatenated
arguments), LDAP injection, path traversal (`Path.Combine(root, userInput)` where the input is
`../../etc/passwd` — always `Path.GetFullPath` and check it still starts with the root), and log
injection (a newline in user input forging a log line — structured logging solves this by not
building the message as text).

---

## 5. The API-shaped vulnerabilities

**Over-posting / mass assignment.** Bind to a **request DTO with only the fields the caller may
set**, never to a domain entity. If the endpoint binds `Order` directly, a JSON body containing
`"status": 4` or `"totalAmount": 0` sets them. 📂 The endpoints here bind to command records —
[`Api/Endpoints/OrderEndpoints.cs`](../../src/LogiFlow.Api/Endpoints/OrderEndpoints.cs) — which
makes over-posting structurally impossible rather than merely unlikely.

**Broken object-level authorization (IDOR).** The most common real vulnerability in business APIs
and the one automated scanners cannot find. `GET /orders/{id}` authenticates the caller, checks
they have the `Analyst` policy... and returns *anyone's* order. **The check "may this user see
*this row*" belongs in the handler, next to the data**, because only the handler knows whose
order it is. A policy on the endpoint cannot express it.

**Excessive data exposure.** Returning the entity instead of a DTO ships every column you later
add, including the ones you would not have chosen to publish. Module 06's rule — project to a DTO
— is a security rule as much as a performance one.

**Rate limiting.** 📂 [`Api/Program.cs`](../../src/LogiFlow.Api/Program.cs) partitions by user or
IP with a fixed window. It is the defence against credential stuffing and scraping, and it is also
the reason a bug in a client cannot take the service down. Rate-limit the *login* endpoint harder
than the rest, and always return `Retry-After`.

**Unrestricted resource consumption.** A `pageSize` the caller controls with no maximum is a
denial-of-service parameter. So is an unbounded upload, and an unbounded `$filter`. 📂
[`Application/Common/Pagination.cs`](../../src/LogiFlow.Application/Common/Pagination.cs) clamps
it, which is the entire fix.

**Verbose errors.** A stack trace in a 500 response tells an attacker your framework versions,
file paths and library list. 📂 [`GlobalExceptionHandler.cs`](../../src/LogiFlow.Api/Infrastructure/GlobalExceptionHandler.cs)
returns a `ProblemDetails` with a correlation id and logs the detail server-side. The user gets an
id to quote; the attacker gets nothing.

---

## 6. Transport, browsers, and configuration

**HTTPS everywhere, HSTS in production.** `UseHttpsRedirection` fixes the first request;
`UseHsts` tells the browser never to try HTTP again. Note it is `if (!IsDevelopment())` in this
codebase for a reason: HSTS on `localhost` is remarkably annoying to undo, because the browser
caches it.

**CORS is a browser policy, not a security boundary.** It stops *a page on another origin* from
reading your response. It stops nothing coming from curl, Postman, or a server. `AllowAnyOrigin`
combined with `AllowCredentials` is not merely bad practice — it is rejected by the framework,
because the combination is meaningless.

**Anti-forgery (CSRF) applies to cookie authentication, not to bearer tokens.** A browser attaches
cookies automatically, which is the vulnerability; it does not attach an `Authorization` header,
which is why a token API does not need anti-forgery. Blazor Server *does* use a cookie, so it
does — module 18.

**Secrets.** Never in the repository, never in `appsettings.json`. Locally: `dotnet user-secrets`.
In production: environment variables, or a key vault. Module 13 has the mechanics; the security
point is simply that a secret in git is compromised the moment it is pushed, and rotating it is
the only remedy — deleting the commit is not one.

**Security headers**, briefly: `Content-Security-Policy` (the one that actually stops XSS),
`X-Content-Type-Options: nosniff`, `Referrer-Policy`, and `X-Frame-Options`/`frame-ancestors`.
For a JSON API only the first two really apply; for anything serving HTML, all of them.

**Supply chain.** `dotnet list package --vulnerable --include-transitive` in CI, and
`<NuGetAudit>true</NuGetAudit>`. Module 00 notes the warning-as-error that caught a real CVE in
this repository. A transitive dependency you have never heard of runs with exactly the same
privileges as your code.

---

## 7. Do this

```bash
# 1. Look at your own token. It is not encrypted, and you can prove it in ten seconds.
cd requests   # get a token from logiflow.http, then:
```
Split it on `.`, base64url-decode the middle segment, and read your claims. Paste it into
<https://jwt.io> if you prefer — but only ever a throwaway development token, never a real one.

2. **Break the pipeline on purpose.** In `Api/Program.cs`, swap `UseAuthentication()` and
   `UseAuthorization()`. Every authenticated request now returns 401. Then put them back and read
   the comment again — it will mean something now.

3. **Find an IDOR.** Take any endpoint that fetches by id and ask: *where is the check that this
   user may see this row?* If the answer is only the endpoint's policy, you have found one.

4. **Run the vulnerability scan:**
   ```bash
   dotnet list package --vulnerable --include-transitive
   ```

5. **Try over-posting.** Add a field to a request DTO that the domain does not accept, send it,
   and watch nothing happen. Then imagine the endpoint had bound the entity.

---

## 8. Golden rules

1. **`UseAuthentication` before `UseAuthorization`.** Reversed, `User` is empty when the policy
   runs — every protected endpoint 401s and it looks like a token bug.
2. **A JWT is signed, not encrypted.** Anyone holding it can read every claim, so nothing private
   goes in a token.
3. **Validate issuer, audience, lifetime, signature and algorithm.** Never trust the `alg` the
   token names.
4. **A JWT cannot be revoked, so keep it short-lived** and put the revocable state in a refresh
   token you store server-side.
5. **Use asymmetric signing beyond a single service.** With HMAC, everything that can verify can
   also mint.
6. **Named policies at the endpoint, never inline role lists.** The rule is then defined once and
   can change shape without touching the endpoints.
7. **Hash passwords with a slow, salted KDF** — Argon2id or PBKDF2 with a high iteration count.
   A fast hash is the attacker's dream, so SHA-256 alone is a mistake.
8. **Compare secrets in constant time**, and return the same message and timing for "no such user"
   as for "wrong password".
9. **Parameterise every query.** The value is transmitted separately from the SQL text, so its
   content can never change the statement's structure. Escaping is not the same thing.
10. **A column or table name cannot be a parameter — use an allow list.** Dynamic `ORDER BY` is
    where injection survives in an EF codebase.
11. **Bind to a request DTO, never to an entity.** Over-posting becomes structurally impossible
    rather than merely unlikely.
12. **Object-level authorization belongs in the handler, next to the data.** An endpoint policy
    cannot express "may this user see *this* row", and IDOR is the vulnerability scanners miss.
13. **Every list endpoint has a maximum page size.** An unbounded one is a denial-of-service
    parameter with a friendly name.
14. **Return a `ProblemDetails` with a correlation id; log the detail server-side.** A stack trace
    in a response is reconnaissance.
15. **CORS is a browser policy, not a security boundary.** It does nothing against a non-browser
    client.
16. **Anti-forgery is for cookie auth, not bearer tokens** — because the browser attaches cookies
    for you and does not attach an `Authorization` header.
17. **A secret committed to git is compromised; rotate it.** Deleting the commit is not a remedy.
18. **Scan dependencies in CI.** A transitive package runs with your privileges.

---

## 9. Interview questions

**"Authentication vs authorization, and does the middleware order matter?"**
Authentication establishes who the caller is and populates `HttpContext.User`; authorization
decides what they may do by reading it. The order matters absolutely — reversed, the principal is
empty when policies evaluate, so everything protected returns 401.

**"Is a JWT encrypted?"**
No. It is signed. The payload is base64url-encoded and anyone holding the token can read it. The
signature guarantees integrity and origin, not confidentiality — so no private data goes inside.

**"How do you revoke a JWT?"**
You cannot, which is the design. You keep the access token short-lived — 15 minutes is typical —
and pair it with a refresh token held server-side that can be revoked. Anything else means
maintaining a denylist, which gives up the statelessness that made JWTs attractive.

**"How does parameterisation prevent SQL injection?"**
The value never becomes part of the SQL text. It is sent separately and bound by the server, so no
character in it can alter the statement's structure. It is a categorical separation, not escaping
— which is why the advice "sanitise the input" is the weaker, older idea.

**"Where can injection still happen with EF Core?"**
`FromSqlRaw`/`ExecuteSqlRaw` with concatenation, and anywhere a column or table name is dynamic —
identifiers cannot be parameters. The fix for the second is an allow list mapping user input to
known-safe names.

**"What is over-posting and how do you prevent it?"**
The caller sends fields the endpoint never intended to accept, and the model binder sets them —
a status, a price, an `IsAdmin`. You prevent it by binding to a request DTO that contains only the
settable fields, never to a domain entity.

**"What is IDOR, and why do scanners miss it?"**
Broken object-level authorization: an authenticated, correctly-authorized user reads or edits
another user's row by changing an id. Scanners miss it because every response is a legitimate 200
— only the application knows whose data that row is, so the check has to live in the handler.

**"Do you need CSRF protection on a bearer-token API?"**
No. CSRF exploits the browser attaching credentials automatically, which it does for cookies and
does not for an `Authorization` header. A cookie-authenticated app — including Blazor Server —
does need it.

---

## Next

→ [Module 25 — Distributed systems and integration](../module-25-distributed-systems/)
