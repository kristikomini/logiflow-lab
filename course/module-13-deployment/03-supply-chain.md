# 2. Supply-chain security

> Part of [Module 13 — Configuration, security and deployment](README.md), section 2.
> Previous: [1. Configuration precedence](02-configuration.md) ·
> Next: [3. Migrations in CI, not at startup](04-migrations-in-ci.md)

---

You did not write most of the code you ship. A mid-sized ASP.NET Core service pulls in several
hundred assemblies transitively, and each is a place somebody else's mistake becomes your incident.

The good news is that .NET now checks for you, and this repository contains **three real
vulnerabilities that the build caught** — not hypotheticals, not seeded examples. All three are
worth reading, because they fail in different ways and each ends in a different fix: take a
different version, pin a whole family, or delete the dependency outright.

## The mechanism

**NuGetAudit is on by default from .NET 8.** On every restore, the SDK checks your resolved
dependency graph — direct *and* transitive — against the GitHub Advisory Database, and emits
`NU1901`–`NU1904` by severity.

On its own that is a warning nobody reads. Combined with warnings-as-errors it becomes a gate:

```
/Directory.Build.props           TargetFramework, Nullable, analyzers
  /src/Directory.Build.props     + TreatWarningsAsErrors  ← production code is strict
  /tests/Directory.Build.props   - relaxed
  /labs/Directory.Build.props    - relaxed
```

A known vulnerability in anything `src/` depends on **fails the build**. That is the whole design:
the check is only as good as the thing that refuses to continue.

And `CentralPackageTransitivePinningEnabled` is what makes a fix *possible*. None of the packages
below is referenced directly by any project — they all arrive several levels down — and without
transitive pinning your only options would be to wait for the intermediate package to update, or to
suppress the warning.

## Case one: a build failure, and a version you must not take

`Microsoft.AspNetCore.OpenApi 10.0.10` pulls `Microsoft.OpenApi >= 2.0.0`, and 2.0.0 carries
**GHSA-v5pm-xwqc-g5wc**, catalogued as **CVE-2026-49451** (HIGH, CVSS 7.5) — a circular schema
reference that terminates parsing through a stack overflow.

**Record both identifiers, always.** NuGet and the GitHub Advisory Database speak GHSA; almost
everything else speaks CVE. A file that names only one sends the next reader looking for a second
problem that does not exist.

The advisory lists **two** patched lines: 2.7.5+ and 3.5.4+. The instinct is to take the newest.

Taking 3.9.0 **breaks the build**: 3.x made `IOpenApiMediaType.Example` read-only, and the OpenAPI
source generator inside the framework package still assigns to it. So the newest patched version is
incompatible with the framework you are on.

The fix is `2.7.6` — patched, on the major line the framework was built against.

> **Newest is not the goal. Patched and compatible is.**

Reading the advisory also tells you *how urgent* it is. This one is availability-only — process
termination, no data exposure and no code execution — which is exactly what you need to know when
arguing for a same-day patch versus a scheduled one. A CVSS of 7.5 looks alarming until you read
what it is 7.5 *of*.

## Case two: a restore failure, and no patched version at all

Adding `Microsoft.EntityFrameworkCore.Sqlite 10.0.10` failed the **restore**, not the build:

```
error NU1903: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known
high severity vulnerability, GHSA-2m69-gcr7-jv3q
```

That is **CVE-2025-6965**, a memory-corruption flaw in SQLite itself, fixed upstream in SQLite
3.50.2.

Three things make this the more instructive case:

**It failed restore, not compilation.** There was no build to fail — the dependency graph could not
even be resolved cleanly. Audit runs earlier than most people expect.

**The advisory listed no patched NuGet version when it was published.** This is the situation that
tempts everyone:

```xml
<!-- Do not do this. -->
<NoWarn>NU1903</NoWarn>
```

That does not silence *this* advisory. It silences **every future high-severity advisory**, in every
package, forever — and it does so invisibly, because the whole point of the check is that nobody is
looking. If you must ship before a fix exists, suppress it for the *one* package with
`<NoWarn>` scoped to that `PackageReference`, write down why, and put a date on it.

**The fix had to pin the whole family:**

```xml
<PackageVersion Include="SQLitePCLRaw.bundle_e_sqlite3"   Version="2.1.13" />
<PackageVersion Include="SQLitePCLRaw.core"              Version="2.1.13" />
<PackageVersion Include="SQLitePCLRaw.lib.e_sqlite3"     Version="2.1.13" />
<PackageVersion Include="SQLitePCLRaw.provider.e_sqlite3" Version="2.1.13" />
```

The family had since shipped 2.1.12 and 2.1.13 carrying a newer embedded SQLite. All four version in
**lockstep**, and pinning only the flagged one gives you a bundle loading a native library it was not
built against — which is a crash at runtime rather than an error at build time, and therefore worse
than the vulnerability you were fixing.

That generalises: **a native-interop package family is one unit.** `bundle`, `core`, `provider` and
`lib` move together, and so do most families that wrap a native binary.

## Case three: the cheapest fix is deleting the dependency

For a long time every single build of this solution emitted the same warning:

```
warning NU1903: Package 'SSH.NET' 2025.1.0 has a known high severity
vulnerability, GHSA-q939-rpr3-3284
```

Nothing in the repository references SSH.NET. It arrived under `Testcontainers.MsSql`, which uses it
for SSH port forwarding, which the integration tests were not using and could not have used.

The obvious moves are the two from the cases above: pin the family forward, or scope a `NoWarn` to
the one package. Both would have worked. Neither was right, because there was a third option nobody
reaches for first — **stop depending on it.**

When the repository moved off Docker, the integration tests were retargeted at a locally installed
SQL Server with a throwaway per-run database (see
[module 12 section 4](../module-12-testing/03-integration-testing.md)). Testcontainers became dead
weight, the `PackageVersion` entry was deleted, and the advisory went with it. Not patched — gone.
There is no version to track, no pin to revisit, and no comment to keep accurate.

That is worth internalising because the instinct runs the other way. A vulnerability report feels
like a request to *upgrade something*, and upgrading is the move that keeps the dependency and the
maintenance. Before you pin, ask what the package is actually doing for you. If the answer is
"nothing, since we changed approach", the fix is a deletion — and a pin on a package nothing uses is
maintenance you have volunteered for permanently.

The counterpart habit is the one this file already argues for at the bottom of *The habits*: the
cheapest supply-chain fix is **the package you did not add**. This is the same rule applied late.

## The habits

```bash
dotnet list package --vulnerable --include-transitive   # the one that matters
dotnet list package --outdated
dotnet list package --deprecated
```

`--include-transitive` is not optional. All three cases above are transitive; none appears without
it.

Beyond the commands:

- **Run them in CI**, so the answer changes when the advisory database does, not when somebody
  remembers. A vulnerability disclosed tomorrow affects the code you shipped today — your build was
  green because nobody knew yet.
- **Dependabot or Renovate** to raise the pull request for you.
- **A lock file.** `RestoreLockedMode` is already set for CI builds in `Directory.Build.props`, so a
  build cannot silently resolve a different version than the one you tested. Without it, "works in CI,
  fails in production" has a whole extra cause.
- **Fewer dependencies.** The cheapest supply-chain fix is the package you did not add. A
  three-function helper is not worth a transitive graph you will be patching for five years.

## What audit does not cover

Worth saying, because it is the follow-up question:

- **Only *known* vulnerabilities.** A green audit means nothing has been disclosed yet.
- **Not malicious packages.** Typosquatting and a compromised maintainer account produce no
  advisory. Check the name character by character when you add something.
- **Not your own code.** That is analysers, code review, and [module 24](../module-24-security/).
- **Not the base image.** `mcr.microsoft.com/dotnet/aspnet:10.0` has its own CVE stream — scan it
  separately, with Trivy or the registry's own scanning.

## Try it

```bash
dotnet list package --vulnerable --include-transitive
```

Clean today. Now make it fail on purpose: comment out the four `SQLitePCLRaw` pins in
`Directory.Packages.props` and run `dotnet restore`. `NU1903` comes back, restore fails, and the
error names a package no project references directly. Put them back.

Then read the three comment blocks in that file. They are longer than the XML they explain, which
is the correct ratio — the version numbers are obvious and the *reasoning* is what somebody needs in
eighteen months when they wonder why the family is pinned, or why a package that is no longer there
still has a paragraph about it.

## What to remember

- NuGetAudit is on by default; warnings-as-errors is what turns it into a gate.
- Transitive pinning is what makes a transitive vulnerability fixable at all.
- Patched **and compatible** — the newest patched version may not build against your framework.
- Read the advisory: severity and impact decide urgency, not the colour of the warning.
- Record **both** identifiers. NuGet speaks GHSA, everything else speaks CVE, and naming one makes
  the next reader hunt for a second problem that does not exist.
- Never blanket-suppress `NU1903`. It silences every future advisory too.
- Native-interop package families version in lockstep. Pin all of them or none.
- Before pinning, ask whether you need the package at all. A deletion needs no maintenance.
- `--include-transitive`, always. All three real cases here were transitive.
- Run it in CI, use a lock file, and prefer fewer dependencies.
- Audit covers known vulnerabilities only — not malice, not your code, not the base image.

**Code:** [`Directory.Packages.props`](../../Directory.Packages.props) ·
[`Directory.Build.props`](../../Directory.Build.props) ·
[module 00 section 3](../module-00-setup/03-build-system.md)

**Next:** [3. Migrations in CI, not at startup](04-migrations-in-ci.md)
