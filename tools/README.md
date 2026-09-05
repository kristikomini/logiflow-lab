# tools/

Repository tools. Not products, not course material, and deliberately **not in the
solution** — nothing here is built by `dotnet build`, referenced by a project, or shipped.

Each one is a **.NET 10 file-based app**: a single `.cs` file with top-level statements and
no `.csproj`, run directly.

```bash
dotnet run tools/viva-deck.cs
```

That is a real language feature, not a script runner — the file is compiled, it picks up the
root `Directory.Build.props` (so it is `net10.0` with nullable and implicit usings like
everything else here), and it can take `#:package` directives if one ever needs a dependency.
The first run compiles and is slow; after that it is cached.

One consequence worth knowing, because it fails at *runtime* rather than at build:
a file-based app is compiled with `JsonSerializerIsReflectionEnabled=false`, so
`JsonSerializer.Serialize(value, options)` throws *"Reflection-based serialization has been
disabled for this application"*. Use a source-generated `JsonSerializerContext` — which is
what you want in a trimmed or AOT application anyway. `viva-deck.cs` shows the shape.

## viva-deck.cs

Turns [`course/GOLDEN-RULES.md`](../course/GOLDEN-RULES.md) into
[`site/assets/rules.js`](../site/assets/rules.js), the deck behind
[`site/viva.html`](../site/viva.html) — 362 cards, one per Golden rule.

```bash
dotnet run tools/viva-deck.cs            # regenerate the deck
dotnet run tools/viva-deck.cs --check    # parse and report, write nothing
```

**Rerun it whenever a Golden rules card changes.** The chain is: edit the module's card,
sync `course/GOLDEN-RULES.md`, run this. Skip the last step and the site quietly drills the
old wording.

Two things it will refuse or complain about, both deliberate:

- **Duplicate card ids fail the run.** Ids are spaced-repetition keys; a collision would
  merge two rules' review history without saying anything.
- **A count that is not twelve or six** in the two curated sections prints a warning. The
  headings say "The twelve" and "The six", so if the parser finds ten, either the file grew
  a rule or the parser lost one — and a quietly incomplete deck is the failure that would
  otherwise never be noticed.

Ids are derived from the claim text rather than from position, so reordering rules within a
card costs nothing and rewording a rule resets that one card's schedule.

Why generate a file at all, instead of reading the markdown at runtime: the site is
buildless on purpose — every page opens from `file://`, where `fetch` is blocked — and the
Academy host serves `site/` only, so `course/` is not reachable over HTTP either. A
`<script>` tag is the one loader that always works.

## doctor.cs

Every cross-reference in the repository, checked in one command.

```bash
dotnet run tools/doctor.cs            # ten checks, exit 1 on any error
dotnet run tools/doctor.cs --quiet    # print only what failed
dotnet run tools/doctor.cs --update   # rewrite tools/quiz-ids.lock, then check
```

| Check | What rots without it |
| --- | --- |
| `site/manifest` | a chapter file with no manifest entry is unreachable; an entry with no file 404s from every sidebar |
| `site/links` | `../../src/...` links are the reason the site lives inside the repo; a rename kills them silently |
| `site/quiz-bank` | a `c:` index past the end of `a:` makes a question nobody can answer, and caps that chapter's mastery forever |
| `site/quiz-ids` | ids are positional **and** are spaced-repetition keys — see below |
| `viva/deck` | `rules.js` is generated; editing a card without rerunning the generator drills last month's wording |
| `course/golden-rules` | `GOLDEN-RULES.md` is hand-synced from 28 module cards, and the viva is generated from the page, not the card |
| `course/links` | the course is a hypertext; a dead link reads as a chapter that was never written |
| `course/sections` | "module 13 section 2" survives a rename and dies on a renumber |
| `code/covered-in` | 82 `Covered in:` comments — the reason reading a class and reading its chapter is one gesture |
| `labs/demos` | modules say `dotnet run race`; a renamed demo turns an instruction into a wrong one |

Errors set the exit code. Warnings do not, because a gate people learn to ignore is worse
than no gate.

### quiz-ids.lock

A question's id is `<chapter-id>#<index>` — **positional** — and it is the key a learner's
review schedule is stored under. Appending a question is free. Reordering or deleting one
re-points existing review history at a *different* question: the schedule survives, attached
to the wrong thing, and nothing anywhere says so.

`tools/quiz-ids.lock` records the hash of every stem against its id, so that becomes a
failed build instead. Regenerate it deliberately — after a wording fix, or after appending —
and never just to make the red go away:

```bash
dotnet run tools/doctor.cs --update
```
