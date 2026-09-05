# Module 00 — Setup and tooling

> Fifteen minutes that will save you hours. Most people skip the build system and then spend a
> year confused by it.

---

## Deeper chapters

| | Chapter | |
|---|---|---|
| 1, 4–5 | [Running the stack](02-running-the-stack.md) | one command, the connection string, and what running without Redis costs |
| 2–3 | [The build system is code too](03-build-system.md) | the four invisible files, and the two real CVEs they caught |

---

## 1. Get it running

```bash
dotnet build
dotnet test LogiFlow.slnf
cd src/LogiFlow.Api && dotnet run
```

Then the UI, in a second terminal:

```bash
cd src/LogiFlow.Web && dotnet run
```

- The UI: <http://localhost:5280>
- API reference: <http://localhost:5199/scalar/v1>

The one prerequisite beyond the SDK is **SQL Server** — Developer or Express edition, or LocalDB.
The default connection string uses `Server=localhost` with Windows authentication, so there is no
password to configure and none in the repository. In Development the API creates its database,
applies migrations and seeds reference data on first run.

Redis and a telemetry collector are **not** prerequisites. `ConnectionStrings:Redis` and
`Otlp:Endpoint` are both empty by default, and the application reads each as a capability check:
no Redis means `AddDistributedMemoryCache`, no endpoint means the OTLP exporters are skipped.
Every line of application code is identical either way, which is the point.

### With Docker

The compose stack is still here and still works — it is now the alternative rather than the default:

```bash
docker compose up -d
docker compose ps                                          # wait for (healthy), not just Up
cd src/LogiFlow.Api && dotnet run --launch-profile docker
```

That profile overrides three settings with **environment variables** rather than editing any file
— which is worth pausing on, because it is the configuration-precedence rule from
[module 13](../module-13-deployment/02-configuration.md) doing real work: env vars beat
`appsettings.Development.json`, so one profile redirects the whole application without a diff.

What it buys you: a genuinely distributed cache, and the telemetry dashboard at
<http://localhost:18888>. Details in the root [`README.md`](../../README.md).

---

## 2. The build system is code too

Four files control every project. Learn them once.

```
                        ┌──────────────────────────────────────────────────┐
   global.json ────────►│  WHICH SDK builds this repo — 10.0.300           │
                        │  missing ⇒ your colleague's SDK behaves          │
                        │            differently and you both lose an      │
                        │            afternoon                             │
                        ├──────────────────────────────────────────────────┤
   Directory            │  HOW every project compiles                      │
     .Build.props ─────►│  TargetFramework · Nullable · analysers ·        │
                        │  TreatWarningsAsErrors (src/ only)               │
                        ├──────────────────────────────────────────────────┤
   Directory            │  WHICH VERSION of every package — declared once  │
     .Packages.props ──►│  + transitive pinning ⇒ a CVE fix is one line    │
                        ├──────────────────────────────────────────────────┤
   .editorconfig ──────►│  STYLE, checked at build time                    │
                        │  an unused using is a build error in src/        │
                        └──────────────────────────────────────────────────┘
                                              │
                                              ▼
                             every .csproj inherits all four,
                            and none of them says so explicitly
```

### `Directory.Build.props` — settings for everything

MSBuild imports the **nearest** one walking up from each `.csproj` and stops there. Nested files
must explicitly import their parent — see `src/Directory.Build.props`, which does exactly that.

```
/Directory.Build.props           TargetFramework, Nullable, analyzers
  /src/Directory.Build.props     + TreatWarningsAsErrors  ← production code is strict
  /tests/Directory.Build.props   - relaxed
  /labs/Directory.Build.props    - relaxed (labs are full of NotImplementedException stubs)
```

**Warnings are errors in `src/`.** That is not pedantry — it is what caught a real CVE in this
repository (below). A warning nobody reads is a warning that does nothing.

### `Directory.Packages.props` — central package management

Every NuGet version is declared **once**. Individual `.csproj` files reference packages with no
version:

```xml
<PackageReference Include="FluentValidation" />
```

Why: one place to bump a version, no "project A is on 8.0.1 and project B on 8.0.4" drift, and
security patching becomes a one-line diff.

### `.editorconfig` — style, enforced by the compiler

With `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, the `IDExxxx` rules in there are
checked at build time. An unused `using` is a build error in `src/`.

Note the section for `**/Migrations/*.cs` with `generated_code = true`. EF writes those files in
its own style; holding generated code to hand-written standards means
`dotnet ef migrations add` produces a repo that will not compile. **Do not solve that by turning
the rules off globally** — they are valuable on code you own.

### `global.json` — pins the SDK

```json
{ "sdk": { "version": "10.0.300", "rollForward": "latestFeature" } }
```

Without it, a colleague with a different SDK gets different behaviour and you both waste an
afternoon.

---

## 3. A real CVE, caught by the build

Open [`Directory.Packages.props`](../../Directory.Packages.props) and read the long comment.

`Microsoft.AspNetCore.OpenApi 10.0.10` depends on `Microsoft.OpenApi >= 2.0.0`, and version
2.0.0 carries a HIGH severity advisory — **GHSA-v5pm-xwqc-g5wc**, also catalogued as
**CVE-2026-49451**. (One advisory, two identifiers: NuGet and the GitHub Advisory Database use the
GHSA id, most other tooling uses the CVE. Always record both, or the next person searching for one
concludes there are two problems.) We never reference that package directly, so there is no
`.csproj` line to fix.

Two things made it fixable:

1. **NuGetAudit** (on by default since .NET 8) reported it as `NU1903`, and because warnings are
   errors, **restore failed** instead of quietly shipping a vulnerable dependency.
2. **`CentralPackageTransitivePinningEnabled`** lets a `PackageVersion` entry override a version
   nobody asked for directly.

And then the part that takes judgement. The advisory lists **two** patched lines: `2.7.5+` and
`3.5.4+`. Jumping to the newest (3.9.0) looks obviously right and **breaks the build** — 3.x made
`IOpenApiMediaType.Example` read-only and the OpenAPI source generator inside
`Microsoft.AspNetCore.OpenApi` still assigns to it, producing `CS0200` in generated code you
cannot edit.

So we pin `2.7.6`: patched, and on the major line the framework was built against.

**Newest is not the goal. Patched and compatible is.** Read the advisory before choosing — this
one is availability-only, which matters when justifying a same-day patch versus a scheduled one.

---

## 4. The optional Docker stack

| Service | Port | Why |
|---|---|---|
| SQL Server 2022 | 1433 | the database — only if you have not installed one |
| Redis | **6380** | a genuinely distributed cache |
| Aspire Dashboard | 18888 / 18889 | logs, traces and metrics over OTLP |

**Redis is on 6380, not 6379, deliberately.** A locally installed Redis or another project's
container very often already owns 6379, and the resulting "port is already allocated" blocks the
whole stack. Inside the compose network it is still 6379; only the host mapping shifts.

The same clash applies to SQL Server: if you already have one installed it owns 1433, and the
container cannot bind it. Pick one or the other, not both.

---

## 5. Turn on the SQL log

The single most useful switch in the repository. In `appsettings.Development.json`:

```json
"Microsoft.EntityFrameworkCore.Database.Command": "Information"
```

Every query now prints with its parameters. Leave it on for modules 06, 07 and 09 — reading the
generated SQL is the fastest way to build an accurate model of what your LINQ costs.

---

## 6. Useful commands

```bash
dotnet test tests/LogiFlow.Domain.Tests     # fast loop: 30 tests, ~400ms, no I/O
dotnet test tests/LogiFlow.ArchitectureTests # is the layering still intact?
dotnet test labs/Labs.Exercises              # your homework

cd labs/Labs.Playground && dotnet run        # list demos
cd labs/Labs.Benchmarks && dotnet run -c Release --filter '*Linq*'

# Only on the optional container path
docker compose down -v                       # wipe the database and start over
```

---

## 7. Golden rules

> The card. Reread it before you touch anyone else's build.

1. **Warnings are errors in `src/`.** A warning nobody reads is a warning that does nothing — and
   the one it caught here was a real CVE.
2. **One version per package, declared once.** Central package management turns "which project is
   on which version?" into a question nobody has to ask.
3. **Pin the SDK.** `global.json` is the difference between "works on my machine" being a joke and
   being a bug report.
4. **Newest is not the goal. Patched and compatible is.** Read the advisory, then pick the version
   on the major line your framework was built against.
5. **Scope an exception, never turn a rule off globally.** Generated code gets its own
   `.editorconfig` section; the code you own keeps the rules.
6. **Turn the SQL log on and leave it on** for modules 06, 07 and 09. Reading the generated SQL is
   the fastest way to learn what your LINQ costs.
7. **Check capability, not environment.** "Is a collector configured?" keeps working when someone
   runs one locally; "is this Development?" does not. It is why Redis and OTLP are optional here.

---

## Next

→ [Module 01 — Advanced C#](../module-01-csharp-advanced/)
