# Module 13 — Configuration, security and deployment

> Getting it onto a server without leaking your signing key or deadlocking your migrations.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 1 | [Configuration precedence](02-configuration.md) | layering, `__`, options validation, secrets |
| 2 | [Supply-chain security](03-supply-chain.md) | two real CVEs the build caught, and why never to blanket-suppress |
| 3 | [Migrations in CI, not at startup](04-migrations-in-ci.md) | idempotent scripts, and expand-and-contract |

---

## 1. Configuration precedence

Later sources win:

```
appsettings.json
  → appsettings.{Environment}.json
    → User Secrets            (Development only)
      → Environment variables
        → Command-line arguments
```

**Environment variables use `__` for nesting**, because `:` is not portable:

```bash
ConnectionStrings__SqlServer="Server=..."
Jwt__SigningKey="..."
```

That is how you configure a container. It is why `appsettings.json` in this repo has **empty**
values for every secret — the file documents the shape; the environment supplies the value.

### Never commit a production secret

```bash
cd src/LogiFlow.Api
dotnet user-secrets set "Jwt:SigningKey" "a-real-key-at-least-32-bytes-long"
```

User Secrets live outside the repo (`%APPDATA%\Microsoft\UserSecrets\`) and only work in
Development. In production: environment variables, or Azure Key Vault / AWS Secrets Manager /
HashiCorp Vault.

The values in `appsettings.Development.json` **are** committed, deliberately — they only unlock a
container on your own machine, and the file says so.

**If a secret is ever committed, rotate it.** Removing it from the working tree does nothing; it
is still in the git history, and history gets cloned.

### Validate at startup

📂 [`Api/Infrastructure/Authentication.cs`](../../src/LogiFlow.Api/Infrastructure/Authentication.cs)

```csharp
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey is not configured.")
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "…must be at least 32 bytes.")
    .ValidateOnStart();
```

A misconfigured deployment should **fail to start**, loudly, not serve 500s an hour later when
the first user logs in.

---

## 2. Supply-chain security

📂 [`Directory.Packages.props`](../../Directory.Packages.props)

**NuGetAudit is on by default since .NET 8.** Combined with warnings-as-errors, a known
vulnerability in your dependency tree **fails the build**.

This repository contains a real example. `Microsoft.AspNetCore.OpenApi 10.0.10` pulls
`Microsoft.OpenApi >= 2.0.0`, and 2.0.0 carries CVE-2026-49451 (HIGH). We never reference it
directly — `CentralPackageTransitivePinningEnabled` is what makes it fixable.

And then the judgement call. The advisory lists **two** patched lines: 2.7.5+ and 3.5.4+. Taking
the newest (3.9.0) **breaks the build**, because 3.x made `IOpenApiMediaType.Example` read-only
and the OpenAPI source generator inside the framework package still assigns to it.

So we pin `2.7.6` — patched, on the major line the framework was built against.

> **Newest is not the goal. Patched and compatible is.**

Read the advisory before choosing a version. This one is availability-only (a stack overflow when
parsing a crafted document), which is exactly the sort of thing you need to know when justifying
a same-day patch versus a scheduled one.

### Other habits worth having

```bash
dotnet list package --vulnerable --include-transitive
dotnet list package --outdated
dotnet list package --deprecated
```

Run these in CI. Enable Dependabot or Renovate. Use a lock file
(`RestoreLockedMode` is already set for CI builds in `Directory.Build.props`) so a build cannot
silently pick up a different version than the one you tested.

---

## 3. Migrations in CI, not at startup

📂 [`Program.cs`](../../src/LogiFlow.Api/Program.cs) migrates on boot — **in Development only**, and
says why in a comment.

**Never in production:**

- Several instances starting at once **race** to migrate the same database.
- The application then needs schema-altering permissions it should not hold at runtime. A SQL
  injection that reaches `db_owner` is a much worse day than one that reaches `db_datawriter`.

Instead, generate a script and run it as its own pipeline step:

```bash
dotnet ef migrations script --idempotent --output migrate.sql \
  --project src/LogiFlow.Infrastructure

# review it — this is the step people skip
sqlcmd -S $SERVER -d LogiFlow -i migrate.sql
```

`--idempotent` wraps each migration in an existence check, so re-running is safe.

**Always read the generated migration.** EF sometimes decides a rename is a drop-and-recreate,
which silently deletes a column of data. It is a two-minute review that has saved entire tables.

### Expand/contract for zero-downtime

During a rolling deploy, old and new code run **simultaneously**. A migration that renames a
column breaks every instance that has not been updated yet.

```
   a rolling deploy, seen from the database
   ───────────────────────────────────────────────────────────────────────────
   v1  ████████████████░░░░░░░░                 both versions are talking to
   v2          ░░░░░░░░████████████████         ONE database, at the same time
               └── the overlap. It is minutes, and it is where the outage lives.

   rename in one step        v1 breaks the instant the old column disappears
   expand / contract         nothing ever breaks, and it costs three deploys
```

```
Deploy 1 (expand):    add the new column, write to BOTH, read from the old
Deploy 2 (migrate):   backfill, switch reads to the new column
Deploy 3 (contract):  stop writing the old, drop it
```

Three deploys instead of one. That is the actual price of zero downtime, and it is worth knowing
before you promise it.

---

## 4. Containerising

A `Dockerfile` for this API would use a multi-stage build:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy manifests FIRST and restore — this layer is cached until a .csproj changes,
# so an ordinary code change skips the restore entirely.
COPY *.sln Directory.*.props global.json ./
COPY src/*/*.csproj ./
RUN for f in *.csproj; do mkdir -p src/${f%.csproj} && mv $f src/${f%.csproj}/; done
RUN dotnet restore

COPY . .
RUN dotnet publish src/LogiFlow.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Run as non-root. The default in the image is root, and a container escape from root
# is a much worse day.
USER $APP_UID

ENTRYPOINT ["dotnet", "LogiFlow.Api.dll"]
```

Points that matter:

- **`aspnet` runtime image, not `sdk`** for the final stage — ~100 MB instead of ~800 MB, and no
  compiler on your production host.
- **Restore before copying source** so the layer cache actually works.
- **Non-root user.**
- **`InvariantGlobalization`** would shrink it further by dropping ICU — but
  `Microsoft.Data.SqlClient` does not support it, and the failure is a runtime crash reading
  *"Globalization Invariant Mode is not supported."* See the comment in
  [`Directory.Build.props`](../../Directory.Build.props).

### Health checks and orchestration

```yaml
livenessProbe:  { httpGet: { path: /health/live,  port: 8080 } }
readinessProbe: { httpGet: { path: /health/ready, port: 8080 } }
```

Liveness must **not** check the database (module 11) — a brief database blip would restart every
pod at once, turning a 10-second degradation into a full outage.

---

## 5. Production checklist

**Configuration**
- [ ] No secrets in the repository; production values from environment or a vault
- [ ] `ValidateOnStart` on everything the app cannot run without
- [ ] `ASPNETCORE_ENVIRONMENT=Production` actually set

**Security**
- [ ] HTTPS enforced; HSTS on (`app.UseHsts()`, excluded in Development for a reason)
- [ ] `EnableSensitiveDataLogging` **off** — it logs parameter values
- [ ] Stack traces never returned to clients
- [ ] Rate limiting on
- [ ] `dotnet list package --vulnerable` clean
- [ ] Database user is `db_datareader`/`db_datawriter`, **not** `db_owner`

**Operations**
- [ ] Migrations run as a pipeline step, not at startup
- [ ] Liveness and readiness probes wired, liveness dependency-free
- [ ] OTLP endpoint pointed at a real collector
- [ ] Alerts on error rate, p99 latency and outbox backlog
- [ ] A tested backup restore — an untested backup is a hope, not a backup

---

## 6. Golden rules

> The card. Print it and work down it before a first production deploy.

1. **No secret in the repository, ever — and if one gets in, rotate it.** Deleting it from the
   working tree leaves it in the history, and history gets cloned.
2. **The JSON file documents the shape; the environment supplies the value.** Environment
   variables win over JSON, with `__` standing in for `:`.
3. **`ValidateOnStart` on everything the app cannot run without.** A misconfigured deploy should
   fail to start, loudly, not serve 500s an hour later.
4. **Let a known CVE fail the build.** NuGetAudit plus warnings-as-errors plus central package
   management makes the fix a one-line diff.
5. **Newest is not the goal. Patched and compatible is.** Read the advisory, and check what the
   framework package was built against before jumping a major version.
6. **Lock the restore in CI.** A build that can silently pick up a version you never tested is not
   reproducible.
7. **Migrations are a pipeline step, not a startup step.** Instances race, and the application
   should not hold schema-altering permissions at runtime.
8. **Generate the script with `--idempotent`, and have a human read it.** EF sometimes decides a
   rename is a drop-and-recreate.
9. **Expand, migrate, contract.** Three deploys is the actual price of zero downtime — know that
   before you promise it.
10. **`aspnet` runtime image, not `sdk`; restore before copying source; run as non-root.** 100 MB
    instead of 800, a cache that actually works, and no compiler on a production host.
11. **Liveness dependency-free, readiness not.** Same rule as module 11, this time in the
    orchestrator's YAML.
12. **An untested backup is a hope, not a backup.** Restore one before you need to.

---

## 7. Interview questions

**"How do you manage secrets?"**
Never in source control. User Secrets locally, environment variables or a managed vault in
production. Validate at startup so a misconfigured deploy fails fast. If a secret is ever
committed, rotate it — deleting it from the working tree leaves it in history.

**"How do you run migrations safely?"**
As a separate, single-instance pipeline step, using an idempotent script that a human has read.
Not at application startup: instances race, and the app would need schema permissions it should
not hold. For zero downtime, use expand/contract across several deploys.

**"How do you keep dependencies secure?"**
NuGetAudit with warnings-as-errors so a known CVE fails the build, central package management so
a fix is one line, automated update PRs, and a lock file so builds are reproducible. Add that you
read the advisory before picking a version — the newest patched version is not always compatible.

**"What is in your production checklist?"**
Cover configuration, security and operations — the list above. The signal is that you have one at
all.

---

## Next

→ [Module 14 — Performance](../module-14-performance/)
