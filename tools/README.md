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
[`site/viva.html`](../site/viva.html) — 378 cards, one per Golden rule.

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
dotnet run tools/doctor.cs            # fourteen checks, exit 1 on any error
dotnet run tools/doctor.cs --quiet    # print only what failed
dotnet run tools/doctor.cs --update   # rewrite tools/quiz-ids.lock, then check
```

| Check | What rots without it |
| --- | --- |
| `site/manifest` | a chapter file with no manifest entry is unreachable; an entry with no file 404s from every sidebar |
| `site/links` | links into `src/` and `course/` are the reason the site lives inside the repo. They are absolute GitHub URLs, because every host serves `site/` alone and relative ones 404 there while passing locally — so the URL is decoded back to a repo path and checked against the working tree |
| `site/quiz-bank` | a `c:` index past the end of `a:` makes a question nobody can answer, and caps that chapter's mastery forever |
| `site/quiz-ids` | ids are positional **and** are spaced-repetition keys — see below |
| `viva/deck` | `rules.js` is generated; editing a card without rerunning the generator drills last month's wording |
| `course/golden-rules` | `GOLDEN-RULES.md` is hand-synced from 29 module cards, and the viva is generated from the page, not the card |
| `course/links` | the course is a hypertext; a dead link reads as a chapter that was never written |
| `course/sections` | "module 13 section 2" survives a rename and dies on a renumber |
| `code/covered-in` | 100 `Covered in:` comments — the reason reading a class and reading its chapter is one gesture |
| `labs/demos` | modules say `dotnet run race`; a renamed demo turns an instruction into a wrong one |
| `docs/counts` | "53 chapters", "464 questions" — a number in a sentence has no other end to compare against, so it just quietly stops being true |
| `site/chapter-count` | the mastery formula is written twice, and the two copies divide by different numbers the moment somebody adds a chapter |

Errors set the exit code. Warnings do not, because a gate people learn to ignore is worse
than no gate.

### docs/counts

The only check here that reads prose rather than a cross-reference, and the one the repository
needed most: when it was written, **twenty-three** stated counts were wrong across eleven files.
The home page advertised thirty-nine chapters above a sidebar listing forty-seven, and the table
you are reading was out by eleven on its own row.

It works from a table of quantities — chapters, questions, Golden rules, modules, demos — each
derived from the thing itself, and each with a list of the **sentences** that state it. The
patterns are anchored on the words around the number rather than on the noun, because `30
questions` in `exam.html` is the length of a paper and `400 cards` in `store.js` is an argument
about pacing. A check that shouts about those is a check somebody turns off.

The cost of being that specific is that a pattern can quietly stop matching, which would
reintroduce the exact failure one level up. So **a pattern that matches nothing is itself an
error**. Rewording a guarded sentence means editing its pattern in `CheckProseCounts`, on
purpose — that is the bargain, and it is the same one `quiz-ids.lock` makes below.

#### The scan, and why the patterns were not enough

That bargain covers a pattern that stops matching. For a while it covered nothing at all about
prose written *afterwards*: a sentence nobody had registered a pattern for was not guarded, and
looked exactly like one that passed. Six sentences drifted that way — the site grew from 39
chapters to 47 and from 312 questions to 432 while four files went on stating the old figures, in
`site/`, in `src/` and in this repository's own README, with this check green the whole time. They
were found by eye, which is what the check exists to make unnecessary.

So the patterns are now only half of it. After them, the check **scans**: every number sitting
next to one of seven nouns — chapters, questions, Golden rules, modules, terms, demos, checks — is
a claim, and has to be right. The default is inverted. Prose is checked unless excused, rather
than unchecked until registered.

Three things make that survivable rather than a wall of false alarms:

- **A floor.** A number below a third of the true figure is ignored. English uses small cardinals
  as ordinary quantifiers constantly — *"two questions decide where a thing lives"*, *"three
  rules, and everyone knows the first"* — and none of them are counts. A third is not arbitrary:
  this check catches counts that stopped being true because the corpus **grew**, and a corpus does
  not shrink to a third of its size, so a smaller number is a sentence about something else.
- **The patterns win.** A number a specific pattern already matched is skipped, because that
  pattern may be checking it against a different quantity: the line on the home page counting the
  chapters the advert never mentions is a claim about the advert gap, not about the size of the
  site.
- **Named exclusions.** Everything left is either correct or listed in `ExcuseList`, by file, by
  phrase, with a reason: a hypothetical dropdown in `notes.html`, the dated status line in
  `CONTENT-BACKLOG.md`, the sentences in this file describing the drift itself. There are seven,
  and **an exclusion that matches nothing is an error**, so the list cannot quietly grow stale
  either.

Turning it on found five more wrong counts the patterns had never looked at, across six files:
a list in the first chapter that was eight entries out of date, two chapters counting the ones
around them, a module card describing a course a third of its present size, and the last
paragraph of the last chapter congratulating you on having read every chapter that answers the
advert — while naming a total that had been one short since the day a chapter was added.

**What it still does not do.** The scan knows seven nouns. A count of something else — badges,
levels, XP thresholds — is covered only if somebody registers a pattern for it, and starts out
unguarded in exactly the way those six sentences did. This file cannot be scanned at all, for
that matter: its subject *is* these numbers and it quotes wrong ones on purpose, so the scan skips
it and its own two count claims are held by patterns instead.

### site/chapter-count

Course mastery is one formula implemented twice — `mastery()` in `site/assets/store.js` and
`ProfileSummary.From` in the Academy API — because the leaderboard has to compute it server-side
rather than trust a figure the browser asserted. That duplication is deliberate and correct. What
it costs is that both copies divide by a chapter count, and they read it from different places:
the browser counts the manifest, the API reads `Academy:ChapterCount` with a compiled-in fallback.

Three numbers, no reason they agree. They did not: the site reached 47 chapters while the API
stayed on 39, so the same profile scored one percentage on the learner's own dashboard and a
higher one on the leaderboard next to other people's names. Nothing failed and nobody was told,
because it is the one number on the page with nothing to compare it against.

This check compares all three and fails on any disagreement — including the case where somebody
renames `DefaultChapterCount` and the check would otherwise stop looking at anything.

Adding a sentence that states a count? Add a pattern for it. Nothing forces you to, which is
the check's one real limit: it guards the sentences it knows about, not every sentence that
could exist.

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
