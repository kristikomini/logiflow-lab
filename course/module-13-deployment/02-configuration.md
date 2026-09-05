# 1. Configuration precedence

> Part of [Module 13 — Configuration, security and deployment](README.md), section 1.
> Next: [3. Migrations in CI, not at startup](04-migrations-in-ci.md)

---

.NET builds configuration by **layering sources, last one wins**. Knowing the order is the difference
between "the setting is being ignored" being a five-minute fix and an afternoon.

The default order, lowest priority first:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User secrets — **Development only**
4. Environment variables
5. Command-line arguments

So a value in `appsettings.json` is overridden by `appsettings.Production.json`, which is overridden
by an environment variable, which is overridden by a command-line flag. Azure App Service application
settings arrive as environment variables, which is why they beat anything in a file — and why "I
changed the JSON and nothing happened" is nearly always this.

## The double underscore

Nested keys become environment variables with `__`:

```jsonc
{ "ConnectionStrings": { "Default": "Server=…" },
  "Jwt": { "ExpiryMinutes": 60 } }
```

```bash
ConnectionStrings__Default=Server=sql,1433;Database=LogiFlow;…
Jwt__ExpiryMinutes=30
```

Every container platform configures a .NET application this way — Docker, Compose, Kubernetes, App
Service, Container Apps — so this is the single most useful thing in the chapter. A colon works on
Linux too, but `__` works everywhere, so use it always.

## The options pattern

Do not inject `IConfiguration` and read strings. Bind to a class:

```csharp
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "logiflow";
    public string SigningKey { get; init; } = string.Empty;
    public int ExpiryMinutes { get; init; } = 60;
}

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();          // ← fail at startup, not on the first request
```

**`ValidateOnStart` is the part people miss.** Without it, a missing signing key is discovered by the
first user who tries to log in, at 09:00 on Monday, as a 500. With it, the deployment fails
immediately and the previous version keeps serving — which is what you want. **Fail fast, at
startup**, is the whole point of validating configuration at all.

Three flavours, and choosing wrongly is a real bug:

| Interface | Lifetime | Re-reads |
|---|---|---|
| `IOptions<T>` | singleton | never |
| `IOptionsSnapshot<T>` | scoped | per request |
| `IOptionsMonitor<T>` | singleton | on change, with a callback |

A **singleton** cannot inject `IOptionsSnapshot<T>` — that is the
[captive dependency](../module-10-cross-cutting/01-dependency-injection.md) trap wearing a different
hat. If a singleton needs a value that can change, it needs `IOptionsMonitor<T>`.

## Secrets

**Never in `appsettings.json`.** A signing key or a connection string with a password in a committed
file is in your git history forever, and rotating it — not deleting the line — is the only fix.

| Where | How |
|---|---|
| Local development | `dotnet user-secrets set "Jwt:SigningKey" "…"` — stored outside the repo |
| CI | the pipeline's secret store, injected as environment variables |
| Azure | Key Vault, read with a **managed identity** |

The managed-identity part is the one worth understanding: the application authenticates to Key Vault
as *itself*, using an identity the platform issues and rotates. There is no credential in
configuration at all, which means there is no credential to leak.

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{vaultName}.vault.azure.net/"),
    new DefaultAzureCredential());
```

`DefaultAzureCredential` tries a chain — environment variables, managed identity, Azure CLI, Visual
Studio — so the same line works on a developer's machine and in production without an `if`.

Note that this line comes **after** the JSON sources, so Key Vault values override them. Order is
everything.

`appsettings.Development.json` in this repository does contain a throwaway signing key, marked as
such. That is deliberate — a local-only value that grants nothing — and it is exactly the file people
copy into `appsettings.json` by accident.

## Environments

`ASPNETCORE_ENVIRONMENT` selects which `appsettings.{Environment}.json` loads and drives
`IHostEnvironment`. The three conventional values are `Development`, `Staging` and `Production`, and
custom ones are legal.

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
}
```

Two rules worth stating:

**Never `IsDevelopment()` for business logic.** Configuration differing by environment is correct;
*behaviour* differing means production runs code nobody tested. If a feature should be off, that is a
feature flag with a configuration value — same mechanism, testable in both states.

**Default to secure.** `if (IsDevelopment()) { relax }` is safe, because a missing or misspelled
environment variable leaves you in the strict branch. `if (IsProduction()) { tighten }` is not: a typo
in the variable name silently gives you the development configuration in production.

## Reloading

`reloadOnChange: true` is the default for the JSON sources, so editing a file updates
`IConfiguration` — and `IOptionsMonitor<T>` sees it, while `IOptions<T>` does not.

In a container this rarely matters: the file cannot change without a redeploy. Where it matters is
feature flags, and there a purpose-built store beats a JSON file.

## The mistakes

**Editing the JSON when an environment variable is set.** The commonest confusion, and precedence
explains it.

**A single underscore instead of double.** `ConnectionStrings_Default` binds to nothing, silently.

**`IConfiguration["Some:Key"]` scattered through the code.** No validation, no type safety, and a
typo returns `null` rather than failing.

**No `ValidateOnStart`.** A misconfiguration discovered by a user rather than by the deployment.

**A singleton taking `IOptionsSnapshot<T>`.** Captive dependency.

**Secrets in a committed file.** And if it has happened: rotate, do not just delete.

## Try it

```bash
dotnet run --project src/LogiFlow.Api
Jwt__ExpiryMinutes=5 dotnet run --project src/LogiFlow.Api
```

The second overrides the file. Then delete the signing key from `appsettings.Development.json` and
start again: with `ValidateOnStart` the application refuses to boot and names the missing setting —
which is exactly the failure you want, at exactly the moment you want it.

## What to remember

- Last source wins: JSON → environment JSON → user secrets → environment variables → command line.
- Nested keys use `__` in environment variables. That is how every container platform configures .NET.
- Bind to an options class; never scatter `IConfiguration["..."]`.
- `ValidateOnStart` so a misconfiguration fails the deployment, not the first user.
- `IOptions` never reloads, `IOptionsSnapshot` is scoped, `IOptionsMonitor` is for singletons.
- Secrets: user-secrets locally, the platform's store in CI, Key Vault with a managed identity in Azure.
- Add Key Vault after the JSON sources so it wins.
- Vary configuration by environment, never behaviour. Default to the secure branch.

**Code:** [`Program.cs`](../../src/LogiFlow.Api/Program.cs) ·
[`Authentication.cs`](../../src/LogiFlow.Api/Infrastructure/Authentication.cs) ·
[`appsettings.Development.json`](../../src/LogiFlow.Api/appsettings.Development.json)

**Next:** [3. Migrations in CI, not at startup](04-migrations-in-ci.md)
