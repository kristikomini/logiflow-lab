# 2–3. The build system is code too

> Part of [Module 00 — Setup and tooling](README.md), sections 2 and 3.
> Previous: [1. Get it running](02-running-the-stack.md) ·
> Next: [4. The optional Docker stack](02-running-the-stack.md#the-stack)

---

Four files control how every project in this repository compiles, and **none of the `.csproj` files
mentions any of them**. That invisibility is the point — and it is also why, when a build behaves
differently on somebody else's machine, most developers have no idea where to look.

```
   global.json ─────────►  WHICH SDK builds this repo — 10.0.300
   Directory.Build.props►  HOW every project compiles
   Directory.Packages   ►  WHICH VERSION of every package
     .props
   .editorconfig ───────►  STYLE, checked at build time
```

## `global.json` — pin the SDK

```json
{ "sdk": { "version": "10.0.300", "rollForward": "latestFeature" } }
```

Without it, the build uses whatever SDK happens to be newest on the machine. Your colleague installs
a preview, a source generator behaves differently, and you both lose an afternoon to a difference
neither of you can see.

`rollForward: latestFeature` is the pragmatic setting: it accepts 10.0.3xx patches — so a security
update to the SDK does not break the repo — while refusing 10.1 and above.

The failure mode is loud, which is a mercy: a missing SDK is an immediate, explicit error naming the
version it wanted.

## `Directory.Build.props` — settings for everything

MSBuild imports the **nearest** one walking up from each `.csproj`, and **stops there**. That last
part catches people: a nested file does not automatically add to its parent, it *replaces* it. A
nested file that wants both must import the parent explicitly, which `src/Directory.Build.props`
does:

```xml
<Import Project="$([MSBuild]::GetPathOfFileAbove($(MSBuildThisFile), $(MSBuildThisFileDirectory)..))" />
```

The layering that produces:

```
/Directory.Build.props           TargetFramework, Nullable, analyzers, GenerateDocumentationFile
  /src/Directory.Build.props     + TreatWarningsAsErrors  ← production code is strict
  /tests/Directory.Build.props   - relaxed
  /labs/Directory.Build.props    - relaxed (labs are full of NotImplementedException stubs)
```

**Warnings are errors in `src/` and nowhere else,** and that asymmetry is deliberate. Production code
gets no leeway. Test and lab projects would otherwise fail over deliberate stubs and unused
variables that exist to make a teaching point.

This is also the setting that makes the whole [supply-chain](../module-13-deployment/03-supply-chain.md)
story work: NuGetAudit emits a *warning*, and a warning nobody reads does nothing. Warnings-as-errors
is what converts it into a gate.

**Anything you set here, a new project inherits silently.** `GenerateDocumentationFile=true` means a
project added next year needs XML doc comments on every public member or it will not build — which is
a genuine surprise for whoever adds it. That is the cost of the pattern, and it is worth less than
the benefit.

## `Directory.Packages.props` — central package management

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
```

With this on, a `.csproj` references a package **with no version at all**:

```xml
<PackageReference Include="FluentValidation" />
```

…and the version lives once, centrally. Three things follow:

**No drift.** "Project A is on 8.0.1 and project B on 8.0.4" cannot happen. In a five-project
solution that is worth having; in a fifty-project one it is the difference between a maintainable
repo and a mystery.

**Patching is a one-line diff**, reviewable at a glance.

**Transitive pinning makes indirect dependencies fixable.** This is the important one. Without it,
a vulnerability in a package you never referenced can only be fixed by waiting for the package that
*does* reference it to update. With it, you declare the version and the graph resolves to it.

A practical consequence for anyone adding a project here: you **cannot** put a version in your own
`.csproj`. It is an error. Add a `<PackageVersion>` entry to the shared file instead.

## The two CVEs this caught

Section 3 of the module is not a hypothetical. The build has caught two real vulnerabilities, both
transitive, both fixable only because of the two settings above:

- **`Microsoft.OpenApi` 2.0.0** — CVE-2026-49451, where the *newest* patched version breaks the build
  and the right answer is the patched version on the major line the framework was built against.
- **`SQLitePCLRaw.lib.e_sqlite3` 2.1.11** — CVE-2025-6965, which failed *restore*, had no patched
  version listed when published, and required pinning all four packages in the family together.

Both are worked through in
[module 13 section 2](../module-13-deployment/03-supply-chain.md). Read that one; it is where the
judgement lives.

## `.editorconfig` — style, enforced by the compiler

```xml
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

With that set, the `IDExxxx` rules in `.editorconfig` are checked at build time rather than merely
underlined in the editor. In `src/`, where warnings are errors, **an unused `using` is a build
error**.

That sounds severe and it removes an entire category of review comment. Nobody has to say "please
remove the unused import" ever again, and the diff never contains a formatting change nobody asked
for.

Note the section for `**/Migrations/*.cs` with `generated_code = true`. EF Core writes those files
and does not write them to your style. Without the exemption, every migration would fail the build
for formatting — which would end with somebody turning the whole feature off.

**That is the general lesson about strict tooling: it survives only if the exemptions are
principled.** `generated_code = true` on generated code is principled. A growing `NoWarn` list is
not.

## Try it

```bash
dotnet build LogiFlow.slnx
```

Now add an unused `using System.Text.Json;` to any file in `src/` and build again — a build error,
not a warning. Add the same line to a file under `labs/` and it builds fine. That difference is
four lines of `.props` and it is the whole philosophy.

Then try to add a version to a `PackageReference` in any `.csproj` and watch restore refuse it,
which is central package management enforcing itself.

## What to remember

- Four files govern every project and no `.csproj` mentions them.
- `global.json` pins the SDK; without it your machine and CI can compile differently.
- MSBuild takes the **nearest** `Directory.Build.props` and stops — nested files must import the parent.
- Warnings-as-errors in `src/` only. Tests and labs are deliberately relaxed.
- That setting is what turns NuGetAudit from a warning into a gate.
- Central package management removes version drift and makes patching a one-line diff.
- Transitive pinning is what makes an indirect vulnerability fixable at all.
- With central management, a version in your own `.csproj` is an error.
- Strict style survives only with principled exemptions — generated code, and nothing else.

**Code:** [`Directory.Build.props`](../../Directory.Build.props) ·
[`Directory.Packages.props`](../../Directory.Packages.props) ·
[`global.json`](../../global.json) ·
[module 13 section 2](../module-13-deployment/03-supply-chain.md)

**Next:** [The optional Docker stack](02-running-the-stack.md#the-stack)
