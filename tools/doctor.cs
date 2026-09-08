#!/usr/bin/env dotnet
// =============================================================================
// doctor.cs — the repository's integrity check, in one command.
//
//   dotnet run tools/doctor.cs            run every check, report, exit 1 on error
//   dotnet run tools/doctor.cs --update   rewrite the two lock files, then run
//   dotnet run tools/doctor.cs --quiet    print only failures
//
// WHY THIS FILE EXISTS
// Almost everything valuable in this repository is a *cross-reference*, and a
// cross-reference is the one kind of content that rots without a symptom. A
// broken build shouts. A `Covered in:` comment pointing at a chapter that was
// renamed six weeks ago says nothing at all, to anybody, ever — it is simply
// wrong from then on, and the reader who follows it concludes the repository is
// sloppy rather than that one line is.
//
// Eight such couplings hold this place together, and every one of them was
// previously maintained by remembering:
//
//   1. site/assets/chapters.js  <->  site/chapters/*.html     the manifest
//   2. site/assets/quizzes-*.js <->  spaced-repetition keys    ids never move
//   3. course/GOLDEN-RULES.md   <->  each module's own card    hand-synced
//   4. course/GOLDEN-RULES.md   <->  site/assets/rules.js      generated
//   5. course/**.md + src/**.cs <->  course headings and files cross-refs
//   6. course/**.md             <->  Labs.Playground's demos   cited by name
//   7. every README and page    <->  the counts they state     "53 chapters"
//      (patterns for the sentences that state one, then a scan of the rest)
//   8. site/assets/store.js     <->  the Academy API's copy    one formula, twice
//
// Five of those have already broken at least once — the seventh was broken in
// twenty-three places when it was first checked, and the eighth was wrong for
// as long as it existed. This file turns all eight into a build failure, which
// is the only form of documentation that maintains itself.
//
// WHY IT IS NOT A TEST PROJECT
// It checks markdown, HTML and JavaScript — none of which the solution compiles
// — and it must run before and independently of `dotnet build`. Making it a
// file-based app keeps it out of the product's build graph entirely, the same
// argument tools/README.md makes for viva-deck.cs. It also means CI can run it
// on a machine with no SQL Server.
//
// ADDING A CHECK
// Write a method returning IEnumerable<Issue>, and add it to the Checks array.
// Report an Error for something that is definitely wrong and a Warning for
// something that is probably wrong; only Errors set the exit code, because a
// gate people learn to ignore is worse than no gate.
// =============================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

// ── Where things live ────────────────────────────────────────────────────────
string repo = FindRepoRoot(AppContext.BaseDirectory)
              ?? FindRepoRoot(Directory.GetCurrentDirectory())
              ?? Directory.GetCurrentDirectory();

bool update = args.Contains("--update", StringComparer.OrdinalIgnoreCase);
bool quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);

string Site(params string[] p) => Path.Combine([repo, "site", .. p]);
string Course(params string[] p) => Path.Combine([repo, "course", .. p]);
string quizLock = Path.Combine(repo, "tools", "quiz-ids.lock");
string swLock = Path.Combine(repo, "tools", "sw-cache.lock");

if (!Directory.Exists(Course()) || !Directory.Exists(Site()))
{
    Console.Error.WriteLine($"Cannot find course/ and site/ under {repo}. Run this from inside the repository.");
    return 2;
}

// ── The checks ───────────────────────────────────────────────────────────────
(string Name, Func<IEnumerable<Issue>> Run)[] checks =
[
    ("site/manifest", CheckSiteManifest),
    ("site/links", CheckSiteLinks),
    ("site/quiz-bank", CheckQuizBank),
    ("site/quiz-ids", CheckQuizIds),
    ("viva/deck", CheckVivaDeck),
    ("course/golden-rules", CheckGoldenRules),
    ("docs/links", CheckCourseLinks),
    ("course/sections", CheckSectionReferences),
    ("code/covered-in", CheckCoveredIn),
    ("labs/demos", CheckDemoCitations),
    ("docs/counts", CheckProseCounts),
    ("site/chapter-count", CheckChapterCount),
    ("site/code-dissection", CheckCodeDissection),
    ("site/sw-cache", CheckServiceWorkerCache),
];

if (update)
{
    WriteQuizLock();
    Console.WriteLine($"wrote {Rel(quizLock)}");

    WriteSwLock();
    Console.WriteLine($"wrote {Rel(swLock)}");
}

Console.WriteLine();
Console.WriteLine("LogiFlow doctor");
Console.WriteLine(new string('─', 60));

int errors = 0, warnings = 0;

foreach ((string name, Func<IEnumerable<Issue>> run) in checks)
{
    List<Issue> issues;
    try
    {
        issues = run().ToList();
    }
    catch (Exception ex)
    {
        // A check that throws is itself a failure — usually the format it parses
        // has changed, which is exactly the drift this tool exists to catch.
        issues = [new Issue(Severity.Error, $"the check itself threw: {ex.Message}")];
    }

    int e = issues.Count(i => i.Severity == Severity.Error);
    int w = issues.Count - e;
    errors += e;
    warnings += w;

    if (issues.Count == 0)
    {
        if (!quiet)
        {
            Console.WriteLine($"  ok    {name}");
        }

        continue;
    }

    Console.WriteLine($"  {(e > 0 ? "FAIL" : "warn")}  {name}");
    foreach (Issue issue in issues.Take(40))
    {
        Console.WriteLine($"          {(issue.Severity == Severity.Error ? "error" : "warn ")}: {issue.Message}");
    }

    if (issues.Count > 40)
    {
        Console.WriteLine($"          … and {issues.Count - 40} more");
    }
}

Console.WriteLine(new string('─', 60));
Console.WriteLine(errors == 0 && warnings == 0
    ? "  everything lines up."
    : $"  {errors} error(s), {warnings} warning(s).");
Console.WriteLine();

return errors == 0 ? 0 : 1;

// ═════════════════════════════════════════════════════════════════════════════
//  1. The site manifest drives the sidebar, the cards and the pager. A chapter
//     file with no manifest entry is unreachable; a manifest entry with no file
//     is a 404 in the navigation of every other page.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckSiteManifest()
{
    List<Chapter> manifest = ReadManifest();
    HashSet<string> files = Directory.EnumerateFiles(Site("chapters"), "*.html")
                                     .Select(f => Path.GetFileNameWithoutExtension(f))
                                     .ToHashSet(StringComparer.Ordinal);

    foreach (Chapter c in manifest.Where(c => !files.Contains(c.Id)))
    {
        yield return Error($"manifest lists '{c.Id}' but site/chapters/{c.Id}.html does not exist");
    }

    foreach (string f in files.Where(f => manifest.All(c => c.Id != f)).Order(StringComparer.Ordinal))
    {
        yield return Error($"site/chapters/{f}.html exists but nothing in chapters.js points at it");
    }

    foreach (Chapter c in manifest)
    {
        string path = Site("chapters", c.Id + ".html");
        if (!File.Exists(path))
        {
            continue;
        }

        string html = File.ReadAllText(path);

        // Three numbers say which chapter a page is, and the learning layer keys
        // progress off the first of them. They are written by hand in three
        // places, which is two more than anyone reliably updates.
        if (!html.Contains($"data-chapter=\"{c.Id}\"", StringComparison.Ordinal))
        {
            yield return Error($"{c.Id}.html is missing data-chapter=\"{c.Id}\" — its progress will be recorded against nothing");
        }

        if (!html.Contains($"<title>{c.N} ", StringComparison.Ordinal))
        {
            yield return Error($"{c.Id}.html: <title> does not start with the chapter number {c.N}");
        }

        if (!html.Contains($"Chapter {c.N}</p>", StringComparison.Ordinal))
        {
            yield return Warn($"{c.Id}.html: the breadcrumb does not say 'Chapter {c.N}'");
        }

        // The home page renders two tables — advert coverage and everything the
        // advert never mentions — from exactly these two fields. A chapter with
        // neither silently drops out of both, and the "nothing in the advert is
        // uncovered" claim quietly stops being checkable.
        if (!c.HasReq && !c.HasExtra)
        {
            yield return Error($"manifest entry '{c.Id}' has neither req: nor extra:, so it appears in neither coverage table");
        }

        if (c.HasReq && c.HasExtra)
        {
            yield return Warn($"manifest entry '{c.Id}' has both req: and extra: — the home page will list it twice");
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  2. Every link on a chapter page, including the ../../ ones into the source.
//     Those are the whole point of the site sitting inside the repository, and
//     they are also the first thing a rename breaks.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckSiteLinks()
{
    foreach (string file in Directory.EnumerateFiles(Site("chapters"), "*.html").Order(StringComparer.Ordinal))
    {
        string html = File.ReadAllText(file);
        string name = Path.GetFileName(file);

        foreach (Match m in Regex.Matches(html, "(?:href|src)=\"([^\"]+)\""))
        {
            string href = m.Groups[1].Value;

            if (RepoLink(href) is string repoPath)
            {
                string full = Path.Combine(repo, repoPath.Replace('/', Path.DirectorySeparatorChar));
                bool wantsDirectory = href.Contains("/tree/main/", StringComparison.Ordinal);

                if (wantsDirectory ? !Directory.Exists(full) : !File.Exists(full))
                {
                    yield return Error($"{name} -> {href} points at nothing in this repository");
                }

                continue;
            }

            if (Skip(href))
            {
                continue;
            }

            string target = href.Split('#')[0].Split('?')[0];
            if (target.Length == 0)
            {
                continue;
            }

            string resolved = Path.GetFullPath(Path.Combine(Site("chapters"), target));
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
            {
                yield return Error($"{name} -> {href} does not exist");
            }
        }
    }

    // The four standalone pages link to each other and to the assets too.
    foreach (string file in Directory.EnumerateFiles(Site(), "*.html").Order(StringComparer.Ordinal))
    {
        string html = File.ReadAllText(file);
        string name = Path.GetFileName(file);

        foreach (Match m in Regex.Matches(html, "(?:href|src)=\"([^\"]+)\""))
        {
            string href = m.Groups[1].Value;
            if (RepoLink(href) is string repoPath)
            {
                string full = Path.Combine(repo, repoPath.Replace('/', Path.DirectorySeparatorChar));
                bool wantsDirectory = href.Contains("/tree/main/", StringComparison.Ordinal);

                if (wantsDirectory ? !Directory.Exists(full) : !File.Exists(full))
                {
                    yield return Error($"{name} -> {href} points at nothing in this repository");
                }

                continue;
            }

            if (Skip(href))
            {
                continue;
            }

            string target = href.Split('#')[0].Split('?')[0];
            if (target.Length == 0)
            {
                continue;
            }

            string resolved = Path.GetFullPath(Path.Combine(Site(), target));
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
            {
                yield return Error($"{name} -> {href} does not exist");
            }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  3. The bank itself: one array per chapter, questions well-formed, correct
//     answer in range. A `c:` pointing past the end of `a:` renders a question
//     nobody can get right, and the only symptom is a learner's mastery quietly
//     capping below 100%.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckQuizBank()
{
    Dictionary<string, List<Question>> bank = ReadQuizBank();
    List<Chapter> manifest = ReadManifest();

    foreach (string id in bank.Keys.Where(k => manifest.All(c => c.Id != k)).Order(StringComparer.Ordinal))
    {
        yield return Error($"the quiz bank has questions for '{id}', which is not a chapter");
    }

    foreach (Chapter c in manifest.Where(c => !bank.ContainsKey(c.Id)))
    {
        yield return Warn($"chapter '{c.Id}' has no questions, so three quarters of its mastery is unreachable");
    }

    foreach ((string id, List<Question> qs) in bank.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        foreach (Question q in qs)
        {
            if (q.OptionCount < 2)
            {
                yield return Error($"{id}#{q.Index}: fewer than two options");
            }

            foreach (int correct in q.Correct.Where(correct => correct < 0 || correct >= q.OptionCount))
            {
                yield return Error($"{id}#{q.Index}: c: {correct} is outside the {q.OptionCount} options");
            }

            if (q.Correct.Count == 0)
            {
                yield return Error($"{id}#{q.Index}: no correct answer");
            }

            if (!q.HasWhy)
            {
                yield return Warn($"{id}#{q.Index}: no why: — the explanation is what makes a wrong answer worth something");
            }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  4. Question ids are spaced-repetition keys, and they are *positional*:
//     `<chapter>#<index>`. Appending is free. Reordering or deleting silently
//     re-points somebody's review history at a different question — their
//     schedule survives, attached to the wrong thing, and nothing anywhere
//     says so. The lock file is what makes that a build failure.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckQuizIds()
{
    if (!File.Exists(quizLock))
    {
        yield return Warn($"no {Rel(quizLock)} yet — run `dotnet run tools/doctor.cs --update` to record today's ids");
        yield break;
    }

    Dictionary<string, string> locked = File.ReadAllLines(quizLock)
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.Split('\t'))
        .Where(p => p.Length == 2)
        .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);

    Dictionary<string, string> current = CurrentQuizIds();

    foreach ((string id, string hash) in locked.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        if (!current.TryGetValue(id, out string? now))
        {
            yield return Error($"{id} has disappeared — every learner's review schedule for it now points at nothing");
        }
        else if (now != hash)
        {
            yield return Error($"{id} is now a different question — reordering re-points review history. Append instead, or run --update if the rewording was deliberate");
        }
    }

    int added = current.Count - locked.Count(kv => current.ContainsKey(kv.Key));
    if (added > 0)
    {
        yield return Warn($"{added} new question(s) since the lock file — run `dotnet run tools/doctor.cs --update`");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  5. rules.js is generated from GOLDEN-RULES.md. Editing a card and forgetting
//     the generator leaves the viva drilling last month's wording — the failure
//     tools/README.md warns about in prose, checked here instead.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckVivaDeck()
{
    string deckPath = Site("assets", "rules.js");
    if (!File.Exists(deckPath))
    {
        yield return Error("site/assets/rules.js is missing — run `dotnet run tools/viva-deck.cs`");
        yield break;
    }

    List<string> claims = GoldenRuleClaims(File.ReadAllText(Course("GOLDEN-RULES.md")));

    // The deck stores claims as JSON strings. Only two escapes can appear in
    // one — a quote and a backslash — so unescaping is exact, not a guess.
    HashSet<string> inDeck = Regex.Matches(File.ReadAllText(deckPath), """"claim":\s*"((?:[^"\\]|\\.)*)"""")
        .Select(m => m.Groups[1].Value.Replace("\\\"", "\"", StringComparison.Ordinal)
                                      .Replace("\\\\", "\\", StringComparison.Ordinal))
        .ToHashSet(StringComparer.Ordinal);

    foreach (string claim in claims.Where(c => !inDeck.Contains(c)))
    {
        yield return Error($"the deck does not have this rule — run `dotnet run tools/viva-deck.cs`: {Trim(claim)}");
    }

    foreach (string stale in inDeck.Where(d => !claims.Contains(d)).Order(StringComparer.Ordinal))
    {
        yield return Error($"the deck still drills a rule GOLDEN-RULES.md no longer has: {Trim(stale)}");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  6. GOLDEN-RULES.md is hand-assembled from the cards at the end of each
//     module. Edit a card, forget the page, and the two teach different things
//     — and because the viva deck is generated from the *page*, the drill then
//     disagrees with the module a learner just read.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckGoldenRules()
{
    string page = File.ReadAllText(Course("GOLDEN-RULES.md"));

    Dictionary<string, string> sections = new(StringComparer.Ordinal);
    MatchCollection headings = Regex.Matches(page, @"^## \[Module (\d+)[^\]]*\]\([^)]*\)\s*$", RegexOptions.Multiline);
    for (int i = 0; i < headings.Count; i++)
    {
        int start = headings[i].Index + headings[i].Length;
        int end = i + 1 < headings.Count ? headings[i + 1].Index : page.Length;
        sections[headings[i].Groups[1].Value] = page[start..end];
    }

    foreach (string dir in Directory.EnumerateDirectories(Course()).Where(d => Path.GetFileName(d).StartsWith("module-", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
    {
        string folder = Path.GetFileName(dir);
        string number = Regex.Match(folder, @"module-(\d+)").Groups[1].Value;
        string readme = Path.Combine(dir, "README.md");

        if (!File.Exists(readme))
        {
            yield return Error($"{folder} has no README.md");
            continue;
        }

        // The heading is "## 6. Golden rules" in most modules and a bare
        // "## Golden rules" in three of them. Both are fine; the card is what
        // matters.
        Match card = Regex.Match(File.ReadAllText(readme),
            @"^## (?:\d+\.\s*)?Golden rules\s*$(.*?)(?=^## |\Z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        if (!card.Success)
        {
            yield return Error($"{folder}/README.md has no Golden rules card");
            continue;
        }

        if (!sections.TryGetValue(number, out string? section))
        {
            yield return Error($"GOLDEN-RULES.md has no section for module {number}");
            continue;
        }

        List<string> inCard = GoldenRuleClaims(card.Groups[1].Value);
        List<string> onPage = GoldenRuleClaims(section);

        foreach (string rule in inCard.Where(r => !onPage.Contains(r)))
        {
            yield return Error($"module {number}: card has a rule GOLDEN-RULES.md is missing: {Trim(rule)}");
        }

        foreach (string rule in onPage.Where(r => !inCard.Contains(r)))
        {
            yield return Error($"module {number}: GOLDEN-RULES.md has a rule the module's own card does not: {Trim(rule)}");
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  7. Relative links between markdown files. The course is a hypertext; a dead
//     link in it is indistinguishable from a chapter that was never written.
//
//     docs/ and deploy/ are included because they link INTO course/ and src/,
//     which is exactly the direction a rename breaks — and an ADR index whose
//     entries 404 is worse than no index.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckCourseLinks()
{
    IEnumerable<string> markdown = new[] { "course", "docs", "deploy", "tools" }
        .Select(d => Path.Combine(repo, d))
        .Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*.md", SearchOption.AllDirectories));

    foreach (string file in markdown.Order(StringComparer.Ordinal))
    {
        string dir = Path.GetDirectoryName(file)!;
        string text = File.ReadAllText(file);

        foreach (Match m in Regex.Matches(text, @"\]\(([^)\s]+)(?:\s+""[^""]*"")?\)"))
        {
            string href = m.Groups[1].Value;
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith('#')
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string target = href.Split('#')[0];
            if (target.Length == 0)
            {
                continue;
            }

            string resolved = Path.GetFullPath(Path.Combine(dir, target));
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
            {
                yield return Error($"{Rel(file)} -> {href}");
            }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  8. "see module 13 section 2" — a citation by number, which survives a file
//     rename and dies on a heading renumber. Renumbering headings is exactly
//     the kind of tidy-up that feels free.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckSectionReferences()
{
    Dictionary<string, HashSet<int>> sections = new(StringComparer.Ordinal);

    foreach (string dir in Directory.EnumerateDirectories(Course()).Where(d => Path.GetFileName(d).StartsWith("module-", StringComparison.Ordinal)))
    {
        string number = Regex.Match(Path.GetFileName(dir), @"module-(\d+)").Groups[1].Value;
        string readme = Path.Combine(dir, "README.md");
        if (!File.Exists(readme))
        {
            continue;
        }

        sections[number.TrimStart('0').PadLeft(1, '0')] = Regex
            .Matches(File.ReadAllText(readme), @"^## (\d+)\.", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();
    }

    IEnumerable<string> sources =
    [
        .. Directory.EnumerateFiles(Course(), "*.md", SearchOption.AllDirectories),
        .. SourceFiles(),
    ];

    foreach (string file in sources.Order(StringComparer.Ordinal))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"module (\d+),? section (\d+)", RegexOptions.IgnoreCase))
        {
            string module = m.Groups[1].Value.TrimStart('0').PadLeft(1, '0');
            int section = int.Parse(m.Groups[2].Value);

            if (!sections.TryGetValue(module, out HashSet<int>? have))
            {
                yield return Error($"{Rel(file)} cites module {m.Groups[1].Value}, which does not exist");
            }
            else if (!have.Contains(section))
            {
                yield return Error($"{Rel(file)} cites module {m.Groups[1].Value} section {section}; that module has sections 1–{(have.Count == 0 ? 0 : have.Max())}");
            }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  9. `Covered in: course/module-.../file.md` — 95 of them at the last count.
//     These are the reason reading a class and reading its chapter is one
//     gesture, and a dead one costs exactly that.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckCoveredIn()
{
    foreach (string file in SourceFiles().Order(StringComparer.Ordinal))
    {
        string text = File.ReadAllText(file);

        foreach (Match m in Regex.Matches(text, @"course/(module-[a-z0-9-]+)(?:/([A-Za-z0-9._-]+\.md))?"))
        {
            string module = Path.Combine(Course(), m.Groups[1].Value);
            if (!Directory.Exists(module))
            {
                yield return Error($"{Rel(file)} cites course/{m.Groups[1].Value}/, which does not exist");
                continue;
            }

            if (m.Groups[2].Success && !File.Exists(Path.Combine(module, m.Groups[2].Value)))
            {
                yield return Error($"{Rel(file)} cites course/{m.Groups[1].Value}/{m.Groups[2].Value}, which does not exist");
            }
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// 10. The modules tell you to run demos by name. A renamed demo turns an
//     instruction into a wrong one, and the reader finds out by typing it.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckDemoCitations()
{
    string program = Path.Combine(repo, "labs", "Labs.Playground", "Program.cs");
    if (!File.Exists(program))
    {
        yield return Error("labs/Labs.Playground/Program.cs is missing");
        yield break;
    }

    HashSet<string> demos = Regex.Matches(File.ReadAllText(program), @"new\(""([a-z0-9-]+)"",")
        .Select(m => m.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);

    if (demos.Count == 0)
    {
        yield return Error("could not parse the demo registry — its shape has changed, so this check is now blind");
        yield break;
    }

    // Words that follow `dotnet run` without naming a demo. "tools" and "labs"
    // are there because a .NET 10 file-based app is run by PATH rather than by
    // name — `dotnet run tools/doctor.cs`, `dotnet run labs/opc-ua.cs` — and the
    // capture below stops at the slash, leaving the directory looking like a
    // demo that does not exist.
    HashSet<string> notDemos = new(StringComparer.Ordinal)
    {
        "list", "all", "tools", "labs", "help",
    };

    HashSet<string> cited = new(StringComparer.Ordinal);

    foreach (string file in Directory.EnumerateFiles(Course(), "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"dotnet run ([a-z][a-z0-9-]*)\b"))
        {
            string name = m.Groups[1].Value;
            if (notDemos.Contains(name))
            {
                continue;
            }

            cited.Add(name);
            if (!demos.Contains(name))
            {
                yield return Error($"{Rel(file)} says `dotnet run {name}`, but there is no such demo");
            }
        }
    }

    foreach (string orphan in demos.Where(d => !cited.Contains(d)).Order(StringComparer.Ordinal))
    {
        yield return Warn($"demo '{orphan}' is never cited by a module, so nobody will be told to run it");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// 11. Counts written into prose: "53 chapters", "464 questions", "362 Golden
//     rules". Every other check in this file compares two things that both
//     move, so drift shows up as a mismatch. A number in a sentence has no
//     other end to compare against — it is simply true on the day it is typed
//     and quietly false afterwards, and no reader can tell the difference.
//
//     This was the worst-maintained thing in the repository. At the audit that
//     produced this check, TWENTY-THREE stated counts were wrong across eleven
//     files — the home page said thirty-nine chapters over a sidebar listing
//     forty-seven, and the table in tools/README.md describing these very
//     checks was out by eleven on its own row. One was in this file's header,
//     and the last two were only found by this check, once it existed.
//
//     WHY THE PATTERNS ARE SO SPECIFIC
//     Matching on the noun alone does not work: "30 questions" in exam.html is
//     the length of a paper, "400 cards" in store.js is an argument about
//     pacing, and "100 cards" is a daily cap. None of those are counts of
//     anything, and a check that shouts about them is a check somebody turns
//     off. So each pattern is anchored on the words around the number.
//
//     The cost of that is a pattern can stop matching — someone rewords the
//     sentence and the guard silently lapses, which is precisely the failure
//     this check exists to prevent, reintroduced one level up. So a pattern
//     that matches nothing is itself an error. Rewording a sentence therefore
//     means updating its pattern here, deliberately, which is the whole bargain.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckProseCounts()
{
    string n = Numbers.Pattern;
    List<Chapter> manifest = ReadManifest();

    string[] moduleDirs = Directory.EnumerateDirectories(Course(), "module-*").ToArray();

    // Hoisted, because the scan at the bottom of this method needs the same
    // figures. Computing them twice is how the two halves would come to disagree
    // about how many chapters there are, in a check about exactly that.
    int chapters = manifest.Count;
    int questions = ReadQuizBank().Values.Sum(q => q.Count);
    int rules = GoldenRuleClaims(File.ReadAllText(Course("GOLDEN-RULES.md"))).Count;
    int modules = moduleDirs.Length;
    int terms = CountOf(Site("assets", "glossary.js"), @"^\s*\{ en:");
    int demos = CountOf(Path.Combine(repo, "labs", "Labs.Playground", "Program.cs"), @"new\(""[a-z0-9-]+"",");
    int checkCount = CountOf(Path.Combine(repo, "tools", "doctor.cs"), @"^\s*\(""[a-z/-]+"", Check[A-Za-z]+\),");

    (string What, int Actual, string[] Sentences)[] quantities =
    [
        ("chapters", chapters,
        [
            "tutorial: " + n + " ordered chapters",
            // Anchored on their own sentences: docs/CONTENT-BACKLOG.md says
            // "All eight chapters have…" about a batch of work, and a bare
            // "all N chapters" would read that as a claim about the site.
            "as an app — all " + n + " chapters",
            "does not cover all " + n + " chapters",
            n + " chapters, in dependency order",
            n + " chapters, in order, covering",
            n + " chapters, one file each",
            "of the " + n + " chapters",
            "\"" + n + " chapters covering every line",
            @"from the manifest\. " + n + " files nobody",

            // Added after all three of these had drifted to 39 and stayed there:
            // the site grew from 39 chapters to 47 and nothing here was looking
            // at the sentences that said so. See the note at the bottom of this
            // method about what that costs.
            @"worth `1/" + n + "` of the course",

            // The two places doctor.cs and the docs about it quote a count as
            // an EXAMPLE of a count. They are still claims, and still wrong the
            // day the site grows — and they are the only claims in doctor.cs
            // itself that the scan is not allowed to read. See the skip in it.
            @"they state\s+""" + n + @" chapters",
            @"""" + n + @" chapters"", ""\d",
            "precaches all " + n + @"\s+chapters",
            "keeps " + n + " hand-written chapter files",
        ]),

        ("chapters answering the advert", manifest.Count(c => c.HasReq),
        [
            "dependency order: " + n + " cover",

            // The last paragraph of the last chapter. Also found by the scan.
            "read " + n + " chapters covering every line",
        ]),

        ("chapters the advert never mentions", manifest.Count(c => c.HasExtra),
        [
            "— " + n + " of them the advert",
            "and " + n + " are not in it",
            "Three of those " + n,
            "These " + n + " chapters are not in it",

            // Chapter 00 enumerates them by number, so the count and the list
            // have to move together. Found by the scan below: the list was
            // seventeen long and eight chapters out of date.
            n + " chapters &mdash; <a href",
        ]),

        ("deeper chapters under course/", moduleDirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*.md"))
            .Count(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase)),
        [
            "modules, and " + n + " deeper chapters",
        ]),

        ("course modules", modules,
        [
            n + @" modules, and \d+ deeper",
            n + " modules that take you",
            "All " + n + " cards are collected",
            "the " + n + "-module course",
            "hand-synced from " + n + " module cards",
        ]),

        ("questions in the bank", questions,
        [
            "grades you: " + n + " questions",
            "THE QUESTION BANK — " + n + " questions",
            n + " questions in `assets/quizzes",
            "<strong>" + n + " bank questions</strong>",
            "of " + n + " questions are in your schedule",

            // Same story: these three said 312 long after the bank reached 432.
            "turns " + n + " recognition items",
            "makes " + n + " multiple-choice questions",
            n + " scheduled cards",
        ]),

        ("Golden rules", rules,
        [
            "those same " + n + " rules",
            "THE VIVA DECK — " + n + " Golden rules",
            "the " + n + " Golden rules of the course",
            "<strong>" + n + " Golden rules</strong>",
            "Mixing " + n + " rules into",
            n + " cards, one per Golden rule",

            // And this one said 352 after the deck reached 362.
            n + " unanswered rules",
        ]),

        ("glossary terms", terms,
        [
            n + " Italian/English terms",
            n + @" terms\. Between them",
        ]),

        ("chapters with an Italian panel", CountOf(Site("assets", "italiano.js"), "^\\s{2}\"[0-9][0-9a-z-]*\":"),
        [
            "panel on " + n + " of the",
        ]),

        ("Playground demos", demos,
        [
            "list the " + n + " demos",
            n + " demos, grouped by topic",
            n + " runnable demos",
        ]),

        // The number of checks in this file, counted off the registration lines
        // at the top of it. Not `checks.Length`: this method is *in* that array,
        // so reading it here is a definite-assignment cycle the compiler
        // rightly refuses. Adding a check and forgetting to say so is not a
        // hypothetical — adding *this* check made both sentences below wrong.
        ("checks in doctor.cs", checkCount,
        [
            n + " checks; the CI gate",
            "doctor.cs            # " + n + " checks, exit 1",
        ]),

        // Only comments that actually point somewhere, and not the ones in
        // tools/ — this file describes the convention in several places, and a
        // tool that counts its own documentation as an instance of the thing it
        // documents will always be wrong by however much it says about it.
        // "none of the other N chapters answers them directly" — chapter 08,
        // about itself. Everything except the page you are reading.
        ("chapters other than the one you are reading", chapters - 1,
        [
            "none of the other " + n + " chapters",
        ]),

        // "N chapters of technical preparation are worth nothing until…" —
        // the opening line of the CV chapter, counting everything before it
        // that is about the work rather than about getting hired: the whole
        // site except chapter 00 and the three that follow this one.
        ("technical chapters before the CV chapter", chapters - 4,
        [
            n + " chapters of technical preparation",
        ]),

        // "Technique is the other N modules" — module 17's card, about itself.
        ("modules other than the one you are reading", modules - 1,
        [
            "the other " + n + " modules",
        ]),

        ("`Covered in:` comments", SourceFiles()
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(f => CountOf(f, "Covered in: (?:<c>)?course/")),
        [
            @"\| " + n + " `Covered in:`",
            n + " of them at the last count",
        ]),
    ];

    // Read once. Thirty-odd patterns over a few hundred files is otherwise a
    // few thousand pointless reads of the same text.
    (string Path, string Body)[] files = ProseFiles()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal)
        .Select(f => (f, File.ReadAllText(f)))
        .ToArray();

    // Filled in by the anchored pass, consumed by the scan after it.
    List<(string Path, int Start, int End)> owned = [];

    // Built fresh each run: `Used` is mutated as the scan goes, and a static
    // instance would carry one run's marks into the next.
    Excuse[] Excuses = ExcuseList.Build();

    foreach ((string what, int actual, string[] sentences) in quantities)
    {
        foreach (string pattern in sentences)
        {
            Regex rx = new(pattern, RegexOptions.IgnoreCase);
            int found = 0;

            foreach ((string path, string body) in files)
            {
                foreach (Match m in rx.Matches(body))
                {
                    // A token the number-reader cannot read is not a count
                    // claim at all — the pattern caught an ordinary sentence.
                    // Do not score it either way.
                    if (Numbers.Parse(m.Groups["n"].Value) is not int claimed)
                    {
                        continue;
                    }

                    found++;

                    // Remember where this pattern matched. The scan below skips
                    // any number inside one of these spans: a specific pattern
                    // has already checked it, quite possibly against a different
                    // quantity — "these thirty chapters are not in it" is a
                    // claim about the advert-gap count, not about the site's 47.
                    owned.Add((path, m.Index, m.Index + m.Length));

                    if (claimed != actual)
                    {
                        yield return Error(
                            $"{Rel(path)}:{LineAt(body, m.Index)} says {m.Groups["n"].Value} {what}; there are {actual}");
                    }
                }
            }

            if (found == 0)
            {
                // Print the pattern with the number group folded back to "N".
                // Expanded, it is nine lines of alternation and the sentence it
                // is looking for cannot be seen at all — which is the only part
                // of the message anybody needs in order to act on it.
                yield return Error(
                    $"nothing matches /{pattern.Replace(n, "N", StringComparison.Ordinal)}/ any more, so the " +
                    $"{what} count it guarded is no longer checked — fix the pattern in CheckProseCounts, " +
                    "or drop it if the sentence is gone for good");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  THE SCAN
    //
    //  Everything above is an allowlist of sentences: prose is unchecked until
    //  somebody registers it, which is how six counts sat wrong for a month with
    //  this check green. Below is the other way round — every number next to one
    //  of these nouns is a claim, and anything that is not gets excused
    //  explicitly, by name, with a reason.
    // ─────────────────────────────────────────────────────────────────────────

    (string What, int Actual, string Noun)[] scanned =
    [
        ("chapters", chapters, "chapters"),
        ("questions in the bank", questions, "questions"),
        ("Golden rules", rules, "(?:Golden )?rules"),
        ("course modules", modules, "modules"),
        ("glossary terms", terms, "terms"),
        ("Playground demos", demos, "demos"),
        ("checks in doctor.cs", checkCount, "checks"),
    ];

    // WHY THERE IS A FLOOR
    // English uses small cardinals as ordinary quantifiers, constantly: "two
    // questions decide where a thing lives", "three rules, and everyone knows
    // the first", "eleven other modules". None of those are counts of
    // anything, there are dozens of them, and putting every one in the
    // exclusions below would bury the handful of entries worth reading.
    //
    // A third of the true figure separates them cleanly, and not by luck: this
    // check exists to catch a count that stopped being true because the corpus
    // GREW. A corpus does not shrink to a third of its size, so a number below
    // that is not a stale version of anything — it is a sentence about
    // something else that happens to end in the same noun.
    //
    // The cost is real and worth stating: a genuine claim below the floor is
    // invisible here. If the question bank ever loses two thirds of itself, the
    // sentences that still say 432 will have to be found by the patterns above.
    foreach ((string what, int actual, string noun) in scanned)
    {
        if (actual <= 0)
        {
            yield return Error($"cannot scan for '{what}': the count came out as {actual}");
            continue;
        }

        int floor = actual / 3;
        Regex rx = new(@"\b" + n + @"\s+(?:" + noun + @")\b", RegexOptions.IgnoreCase);

        foreach ((string path, string body) in files)
        {
            // THE ONE FILE THE SCAN CANNOT READ IS THIS ONE.
            // Its subject *is* these numbers. It quotes wrong ones deliberately
            // — as examples of what the patterns look for, as the history of
            // what drifted, and in the exclusion list below, which is a list of
            // sentences containing numbers that are not counts. Scanning it
            // reports every one of those as a mistake, which would leave a
            // check whose steady state is nineteen false alarms, and a check
            // like that gets ignored within a week.
            //
            // What this costs: a count claim in this file's own prose is
            // guarded only if it has a pattern above. Two of them do.
            if (string.Equals(Rel(path), "tools/doctor.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match m in rx.Matches(body))
            {
                if (Numbers.Parse(m.Groups["n"].Value) is not int claimed ||
                    claimed == actual ||
                    claimed < floor)
                {
                    continue;
                }

                if (owned.Any(o => string.Equals(o.Path, path, StringComparison.Ordinal) &&
                                   m.Index < o.End && o.Start < m.Index + m.Length))
                {
                    continue;
                }

                string context = Context(body, m);
                string relative = Rel(path);

                // Every excuse that applies is marked, not just the first.
                // Two of them describe the same sentence from opposite ends
                // ("grew from 39 chapters to 47 and from 312 questions to 432"),
                // so first-match-wins would leave the second one looking unused
                // and report it as stale on every run.
                bool excused = false;
                foreach (Excuse e in Excuses)
                {
                    if (relative.EndsWith(e.File, StringComparison.OrdinalIgnoreCase) &&
                        context.Contains(e.Phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        e.Used = true;
                        excused = true;
                    }
                }

                if (excused)
                {
                    continue;
                }

                yield return Error(
                    $"{Rel(path)}:{LineAt(body, m.Index)} says \"{Collapse(m.Value)}\" but there are " +
                    $"{actual} {what} — correct it, or add an Excuse for it in CheckProseCounts");
            }
        }
    }

    // An excuse that stops matching is the same rot one level up as a pattern
    // that stops matching, and gets the same treatment: the sentence it was
    // written for has been reworded or deleted, and nobody has looked at whether
    // the exemption is still deserved.
    foreach (Excuse e in Excuses.Where(e => !e.Used))
    {
        yield return Error(
            $"nothing in {e.File} matches the excused phrase \"{e.Phrase}\" any more — " +
            "delete the Excuse, or point it at the sentence that replaced it");
    }

    // WHAT IS STILL NOT CHECKED
    //
    // The scan only knows the seven nouns listed in `scanned`. A count of
    // something else — badges, levels, XP thresholds, the number of tables in
    // the Academy schema — is covered only if somebody registers a pattern for
    // it above, and starts out silently unguarded exactly the way the six
    // chapter and question counts did.
    //
    // That is a narrower hole than the one this replaced, and it is the reason
    // the noun list is short and boring rather than clever: every noun added to
    // it turns a whole class of sentence from unchecked into checked, and the
    // cost of adding one is whatever exclusions fall out of the first run.
}


// ═════════════════════════════════════════════════════════════════════════════
//  12. The mastery percentage is one formula written twice — once in the browser
//      and once in the API, because the leaderboard needs it server-side. Both
//      copies divide by the number of chapters, and they get that number from
//      different places: the browser counts the manifest, the API reads a config
//      key with a compiled-in fallback.
//
//      So there are three numbers that must agree and no reason they will. They
//      did not: the site grew to 47 chapters while the API stayed on 39, and for
//      that whole time the same profile scored one percentage on the learner's
//      dashboard and a higher one on the leaderboard next to other people's
//      names. Nothing failed, nobody was told, and the only symptom was a number
//      being wrong in a place where nobody had a second number to compare it to.
//
//      That is the exact failure mode this file exists for, so it is now a check.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckChapterCount()
{
    int manifest = ReadManifest().Count;

    // The API's compiled-in fallback, used when the key is absent entirely.
    string endpoints = Path.Combine(
        repo, "src", "LogiFlow.Academy.Api", "Endpoints", "ProfileEndpoints.cs");

    if (!File.Exists(endpoints))
    {
        // The Academy service is optional: the site runs from disk without it.
        // Its absence is not a failure, but silently checking nothing would be.
        yield return Warn($"{Rel(endpoints)} is missing, so the API's mastery denominator is unchecked");
        yield break;
    }

    Match fallback = Regex.Match(
        File.ReadAllText(endpoints), @"DefaultChapterCount\s*=\s*(?<n>\d+)");

    if (!fallback.Success)
    {
        yield return Error(
            "ProfileEndpoints.cs no longer declares DefaultChapterCount, so the API's mastery " +
            "denominator is no longer checked — point this regex at whatever replaced it");
    }
    else if (int.Parse(fallback.Groups["n"].Value, CultureInfo.InvariantCulture) is int n && n != manifest)
    {
        yield return Error(
            $"ProfileEndpoints.DefaultChapterCount is {n} but the site ships {manifest} chapters; " +
            "the leaderboard's mastery percentage will not match the dashboard's");
    }

    // And the configured value, which overrides the fallback wherever it is set.
    string settings = Path.Combine(repo, "src", "LogiFlow.Academy.Api", "appsettings.json");

    if (!File.Exists(settings))
    {
        yield break;
    }

    Match configured = Regex.Match(
        File.ReadAllText(settings), @"""ChapterCount""\s*:\s*(?<n>\d+)");

    if (!configured.Success)
    {
        // Absent is legitimate — the fallback above then applies, and it is checked.
        yield break;
    }

    if (int.Parse(configured.Groups["n"].Value, CultureInfo.InvariantCulture) is int c && c != manifest)
    {
        yield return Error(
            $"appsettings.json sets Academy:ChapterCount to {c} but the site ships {manifest} chapters; " +
            "the leaderboard's mastery percentage will not match the dashboard's");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  Readers
// ═════════════════════════════════════════════════════════════════════════════

List<Chapter> ReadManifest()
{
    string js = File.ReadAllText(Site("assets", "chapters.js"));
    List<Chapter> chapters = [];

    foreach (Match m in Regex.Matches(js,
        @"\{\s*n:\s*""([^""]*)"",\s*id:\s*""([^""]+)"",(.*?)\n\s*\},",
        RegexOptions.Singleline))
    {
        string body = m.Groups[3].Value;
        chapters.Add(new Chapter(
            m.Groups[1].Value,
            m.Groups[2].Value,
            Regex.IsMatch(body, @"\breq:\s*"""),
            Regex.IsMatch(body, @"\bextra:\s*""")));
    }

    return chapters;
}

Dictionary<string, List<Question>> ReadQuizBank()
{
    Dictionary<string, List<Question>> bank = new(StringComparer.Ordinal);

    foreach (string file in Directory.EnumerateFiles(Site("assets"), "quizzes-*.js").Order(StringComparer.Ordinal))
    {
        string? chapter = null;
        int index = 0;
        List<string> pending = [];

        foreach (string line in File.ReadAllLines(file))
        {
            Match head = Regex.Match(line, @"^\s{0,4}""([a-z0-9-]+)"":\s*\[");
            if (head.Success)
            {
                chapter = head.Groups[1].Value;
                index = 0;
                bank[chapter] = [];
                continue;
            }

            if (chapter is null)
            {
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*\{\s*q:"))
            {
                pending = [line];
                continue;
            }

            if (pending.Count > 0)
            {
                pending.Add(line);

                // A question ends at its `why:` line — every one has one, and it
                // is the last field in the object.
                if (Regex.IsMatch(line, @"^\s*why:") || Regex.IsMatch(line, @"why:\s*"".*""\s*\},?\s*$"))
                {
                    bank[chapter].Add(ParseQuestion(chapter, index++, string.Join('\n', pending)));
                    pending = [];
                }
            }
        }
    }

    return bank;
}

static Question ParseQuestion(string chapter, int index, string text)
{
    Match stem = Regex.Match(text, @"q:\s*""((?:[^""\\]|\\.)*)""");

    // Options run from `a: [` to the matching `]`. Counting the quoted strings
    // inside is enough; their content is not this tool's business.
    Match options = Regex.Match(text, @"a:\s*\[(.*?)\]", RegexOptions.Singleline);
    int count = options.Success
        ? Regex.Matches(options.Groups[1].Value, @"""(?:[^""\\]|\\.)*""").Count
        : 0;

    List<int> correct = [];
    Match single = Regex.Match(text, @"\bc:\s*(\d+)");
    Match multi = Regex.Match(text, @"\bc:\s*\[([\d,\s]+)\]");
    if (multi.Success)
    {
        correct.AddRange(multi.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse));
    }
    else if (single.Success)
    {
        correct.Add(int.Parse(single.Groups[1].Value));
    }

    return new Question(chapter, index, stem.Groups[1].Value, count, correct, text.Contains("why:", StringComparison.Ordinal));
}

Dictionary<string, string> CurrentQuizIds() =>
    ReadQuizBank()
        .SelectMany(kv => kv.Value)
        .ToDictionary(q => $"{q.Chapter}#{q.Index}", q => Sha12(q.Stem), StringComparer.Ordinal);

void WriteQuizLock()
{
    StringBuilder sb = new();
    sb.AppendLine("# quiz-ids.lock — the spaced-repetition keys, and what they point at.");
    sb.AppendLine("#");
    sb.AppendLine("# `<chapter>#<index>` is positional, and it is the key a learner's review");
    sb.AppendLine("# schedule is stored under. Appending a question is free. Reordering or");
    sb.AppendLine("# deleting one re-points existing history at a different question, with no");
    sb.AppendLine("# symptom, so the hash of each stem is recorded here and doctor.cs compares.");
    sb.AppendLine("#");
    sb.AppendLine("# Regenerate deliberately, never to make a failure go away:");
    sb.AppendLine("#   dotnet run tools/doctor.cs --update");
    sb.AppendLine();

    foreach ((string id, string hash) in CurrentQuizIds().OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        sb.Append(id).Append('\t').AppendLine(hash);
    }

    File.WriteAllText(quizLock, sb.ToString());
}

// Everything a count could be written into: the READMEs, the course, the site's
// own pages and scripts, and the C# — which includes this file, because the
// header of check 9 carried a stale number for months.
IEnumerable<string> ProseFiles() =>
    Directory.EnumerateFiles(repo, "*.md", SearchOption.AllDirectories)
        .Concat(Directory.Exists(Site())
            ? Directory.EnumerateFiles(Site(), "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase))
            : [])
        .Concat(SourceFiles())
        .Where(f => !Generated(f));

// Build output and mutation-testing reports are full of copies of the real
// files. Counting a claim twice is harmless; reporting it against a path
// nobody edits is not.
static bool Generated(string path)
{
    char s = Path.DirectorySeparatorChar;
    return path.Contains($"{s}obj{s}", StringComparison.Ordinal)
        || path.Contains($"{s}bin{s}", StringComparison.Ordinal)
        || path.Contains($"{s}node_modules{s}", StringComparison.Ordinal)
        || path.Contains($"{s}StrykerOutput{s}", StringComparison.Ordinal)
        || path.Contains($"{s}.git{s}", StringComparison.Ordinal);
}

static int CountOf(string path, string pattern) =>
    File.Exists(path)
        ? Regex.Matches(File.ReadAllText(path), pattern, RegexOptions.Multiline).Count
        : 0;

// The text around a scan hit, whitespace collapsed. Collapsing matters: prose
// wraps, so "from 39\nchapters" and "from 39 chapters" are the same sentence and
// an excuse written for one must match the other.
static string Context(string body, Match match)
{
    int start = Math.Max(0, match.Index - 75);
    int end = Math.Min(body.Length, match.Index + match.Length + 50);
    return Collapse(body[start..end]);
}

static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

static int LineAt(string text, int index)
{
    int line = 1;
    for (int i = 0; i < index && i < text.Length; i++)
    {
        if (text[i] == '\n')
        {
            line++;
        }
    }

    return line;
}

IEnumerable<string> SourceFiles() =>
    new[] { "src", "tests", "labs", "tools" }
        .Select(d => Path.Combine(repo, d))
        .Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

// Which hrefs this tool has no opinion about. The last case is the interesting
// one: several pages build a link in inline JavaScript — href="chapters/' + id +
// '.html" — and the attribute value is then a fragment of source, not a path.
// Checking those would mean evaluating the page, so they are skipped by the one
// signal that is unambiguous in an attribute: a quote or a concatenation.
// ═════════════════════════════════════════════════════════════════════════════
// 13. Every code block should be dissected line by line: what each line DOES in
//     a comment inside it, and what each line IS in a .dissect box under it —
//     which token is a keyword, which is a type, which is a method call, where
//     a conversion is happening that nobody wrote.
//
//     IT WAS A RATCHET, AND IS NOW A REQUIREMENT
//     While the work was in progress this failed only when coverage went DOWN,
//     against a floor raised by hand as each batch landed. Demanding all 282
//     blocks on day one would have meant a check that was red for weeks, and a
//     check that is red for weeks is one people learn to scroll past — the same
//     argument the header makes about warnings.
//
//     Coverage reached 100% on 2026-09-08, so the floor was removed and the
//     rule is now the plain one: every code block carries a dissection. Note
//     the ratchet would NOT have caught the case this replaces — adding an
//     undissected block leaves `dissected` untouched, so it cleared the floor
//     and only warned. Checked by adding one and watching this fail.
//
//     It also enforces the shape, unconditionally: a .dissect must sit directly
//     under the <pre> it explains. The CSS pulls it up by a negative margin to
//     join the two into one object, so a stray one does not merely read oddly,
//     it renders as a box glued to whatever paragraph happened to precede it.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckCodeDissection()
{
    // The open tag is matched loosely because a handful of blocks carry an
    // inline style. Counting only the bare `<pre><code>` undercounted the
    // denominator while still crediting those blocks' dissections, so the
    // ratio flattered itself — the one direction a progress metric must not
    // be wrong in.
    int blocks = 0, dissected = 0;
    List<(string Chapter, int Done, int Total)> perChapter = [];

    foreach (string file in Directory.EnumerateFiles(Site("chapters"), "*.html").Order(StringComparer.Ordinal))
    {
        string html = File.ReadAllText(file);
        string name = Path.GetFileName(file);

        int here = Regex.Matches(html, "<pre[^>]*><code[^>]*>").Count;
        int covered = Regex.Matches(html, @"</code></pre>\s*<div class=""dissect"">").Count;

        blocks += here;
        dissected += covered;

        if (here > 0)
        {
            perChapter.Add((Path.GetFileNameWithoutExtension(file), covered, here));
        }

        // A dissection that is not attached to a code block.
        int boxes = Regex.Matches(html, @"<div class=""dissect"">").Count;

        if (boxes > covered)
        {
            yield return Error(
                $"{name}: {boxes - covered} .dissect box(es) do not directly follow a </code></pre> — " +
                "the CSS joins them to the block above, so a detached one renders glued to a paragraph");
        }
    }

    if (dissected < blocks)
    {
        string missing = string.Join(", ", perChapter
            .Where(c => c.Done < c.Total)
            .OrderByDescending(c => c.Total - c.Done)
            .Select(c => $"{c.Chapter} ({c.Done}/{c.Total})"));

        yield return Error(
            $"{blocks - dissected} code block(s) have no dissection: {missing}. " +
            "Every code example carries one — inline comments for what a line does, and a " +
            ".dissect box under it for what each line IS");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
//  14. The service worker's cache version, against what it actually precaches.
//
//      sw.js says, in capitals, BUMP THIS ON EVERY CONTENT CHANGE — and it is
//      the one instruction in this repository that nothing enforced. It was
//      forgotten the day chapters 32b and 32c were added, which is what this
//      check exists because of.
//
//      WHAT ACTUALLY BREAKS, precisely, because it is narrower than the comment
//      in sw.js suggests. The fetch handler is cache-first with a background
//      refresh, so an EDIT to a file that is already cached self-heals: the
//      visitor reads one stale copy and the next visit is current. That is
//      annoying, not wrong, and it is not what this guards.
//
//      What never self-heals is the precached SET. CHAPTER_FILES is built at
//      install time, and install only runs when the browser sees a byte-changed
//      sw.js. Add a chapter without touching sw.js and the worker is never
//      reinstalled, so the new page is never precached — the site goes on
//      claiming every chapter works offline while two of them do not, with no
//      error anywhere and nothing in CI to notice. The cache name is the
//      intended signal that the set changed, so that is what is locked here.
//
//      A SHELL entry that does not exist is checked at the same time, because
//      precache() logs a warning and carries on by design: a typo there is a
//      file that is silently never cached, which is the same broken promise
//      arriving by a different route.
// ═════════════════════════════════════════════════════════════════════════════
IEnumerable<Issue> CheckServiceWorkerCache()
{
    string swPath = Site("sw.js");
    if (!File.Exists(swPath))
    {
        yield return Error("site/sw.js is missing");
        yield break;
    }

    string sw = File.ReadAllText(swPath);

    Match version = Regex.Match(sw, @"const CACHE = ""([^""]+)"";");
    if (!version.Success)
    {
        yield return Error("could not find `const CACHE = \"...\"` in site/sw.js — this check is now blind");
        yield break;
    }

    Match shell = Regex.Match(sw, @"const SHELL = \[(.*?)\];", RegexOptions.Singleline);
    if (!shell.Success)
    {
        yield return Error("could not find the SHELL array in site/sw.js — this check is now blind");
        yield break;
    }

    List<string> shellFiles = [.. Regex.Matches(shell.Groups[1].Value, @"""([^""]+)""").Select(m => m.Groups[1].Value)];

    // "./" is the bare origin, which the host serves as index.html. Every other
    // entry is a real file that must exist, or it is precached into a warning
    // nobody reads.
    foreach (string entry in shellFiles.Where(e => e != "./").Order(StringComparer.Ordinal))
    {
        if (!File.Exists(Site(entry.Split('/'))))
        {
            yield return Error($"site/sw.js precaches '{entry}', which does not exist — it will never be cached, and the offline claim is wrong by that one file");
        }
    }

    string fingerprint = SwPrecacheFingerprint(shellFiles);

    if (!File.Exists(swLock))
    {
        yield return Warn($"no {Rel(swLock)} yet — run `dotnet run tools/doctor.cs --update` to record today's precache set");
        yield break;
    }

    string[] locked = [.. File.ReadAllLines(swLock).Where(l => l.Length > 0 && !l.StartsWith('#'))];
    if (locked.Length == 0 || locked[0].Split('\t') is not [string lockedVersion, string lockedFingerprint])
    {
        yield return Error($"{Rel(swLock)} is not `<cache name>\\t<fingerprint>` — run --update");
        yield break;
    }

    if (fingerprint == lockedFingerprint)
    {
        yield break;
    }

    if (version.Groups[1].Value == lockedVersion)
    {
        yield return Error(
            $"the service worker precaches a different set of files than when '{lockedVersion}' was recorded, " +
            "and CACHE was not bumped. Returning visitors keep the old set: any new page is missing from " +
            "their offline copy, silently. Bump CACHE in site/sw.js, then run `dotnet run tools/doctor.cs --update`");
        yield break;
    }

    yield return Warn(
        $"the precache set changed and CACHE is now '{version.Groups[1].Value}' — run `dotnet run tools/doctor.cs --update` to record it");
}

// What the worker will precache: the shell it lists by hand, plus one page per
// chapter from the manifest. Order-insensitive on purpose — moving a line in
// SHELL changes nothing a visitor can observe, and a check that fired on it
// would train people to bump the version for no reason, which is how a real
// signal becomes noise.
string SwPrecacheFingerprint(IEnumerable<string> shellFiles) =>
    Sha12(string.Join(
        '\n',
        shellFiles
            .Concat(ReadManifest().Select(c => $"chapters/{c.Id}.html"))
            .Order(StringComparer.Ordinal)));

void WriteSwLock()
{
    string sw = File.ReadAllText(Site("sw.js"));
    string version = Regex.Match(sw, @"const CACHE = ""([^""]+)"";").Groups[1].Value;
    List<string> shellFiles =
    [
        .. Regex.Matches(
            Regex.Match(sw, @"const SHELL = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value,
            @"""([^""]+)""").Select(m => m.Groups[1].Value),
    ];

    StringBuilder sb = new();
    sb.AppendLine("# sw-cache.lock — what the service worker precached, and under which cache name.");
    sb.AppendLine("#");
    sb.AppendLine("# sw.js builds its precache list at INSTALL time, and install only runs when the");
    sb.AppendLine("# browser sees a byte-changed sw.js. Add a chapter without bumping CACHE and the");
    sb.AppendLine("# worker is never reinstalled, so returning visitors keep the old set and the");
    sb.AppendLine("# site's offline claim quietly stops being true. The fingerprint below is the");
    sb.AppendLine("# precached set; doctor.cs fails if it moves while CACHE stands still.");
    sb.AppendLine("#");
    sb.AppendLine("# Regenerate deliberately, after bumping CACHE — never to make a failure go away:");
    sb.AppendLine("#   dotnet run tools/doctor.cs --update");
    sb.AppendLine();
    sb.Append(version).Append('\t').AppendLine(SwPrecacheFingerprint(shellFiles));

    File.WriteAllText(swLock, sb.ToString());
}

// A link into the repository's own source, as an absolute GitHub URL.
//
// These used to be relative — ../../src/LogiFlow.Domain/Catalog/Product.cs from
// a chapter page. That resolves on disk, which is why this check was green for
// as long as it existed, and 404s on every deployment: Cloudflare, Vercel,
// Netlify and Pages all publish site/ alone, so nothing above it is served.
// Eighty-three links were correct locally and broken in production, which is
// the worst combination there is.
//
// Absolute URLs fix the deployment and would ordinarily end the checking, since
// Skip() ignores anything beginning with http. So they are matched here first,
// the repo-relative path is recovered, and it is verified against the working
// tree exactly as before. Renaming a source file still fails the build; the
// only thing that changed is who can follow the link.
static string? RepoLink(string href)
{
    const string repoBase = "https://github.com/kristi2002/LogiFlow/";

    foreach (string kind in (string[])["blob/main/", "tree/main/"])
    {
        string prefix = repoBase + kind;

        if (href.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return href[prefix.Length..].Split('#')[0].Split('?')[0].TrimEnd('/');
        }
    }

    return null;
}

static bool Skip(string href) =>
    href.Length == 0
    || href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
    || href.StartsWith('#')
    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
    || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
    || href.Contains('\'', StringComparison.Ordinal)
    || href.Contains('+', StringComparison.Ordinal)
    || href.Contains("${", StringComparison.Ordinal);

// A Golden rule is a numbered list item whose first bold run is the claim. The
// prose after it is the justification, which is the viva's answer, not its key.
static List<string> GoldenRuleClaims(string markdown) =>
    Regex.Matches(markdown, @"^\s{0,3}\d+\.\s+\*\*(.+?)\*\*", RegexOptions.Multiline | RegexOptions.Singleline)
        .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
        .ToList();

// ═════════════════════════════════════════════════════════════════════════════
//  Plumbing
// ═════════════════════════════════════════════════════════════════════════════

static string? FindRepoRoot(string start)
{
    DirectoryInfo? dir = new(start);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "LogiFlow.slnx")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}

string Rel(string path) => Path.GetRelativePath(repo, path).Replace('\\', '/');

static string Sha12(string value) =>
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];

static string Trim(string s) => s.Length <= 72 ? s : s[..69] + "…";

static Issue Error(string message) => new(Severity.Error, message);

static Issue Warn(string message) => new(Severity.Warning, message);

// ═════════════════════════════════════════════════════════════════════════════
//  Numbers the scan in CheckProseCounts would otherwise report, and why each
//  one is not a claim about how much of this repository there is.
//
//  Every entry has to earn its place. "It is annoying" is not a reason; the
//  reason has to be that the sentence means something other than a count, and
//  an entry that matches nothing is an error, so this list cannot quietly
//  accumulate exemptions for sentences that no longer exist.
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class Excuse(string file, string phrase, string why)
{
    /// <summary>Path suffix the sentence lives in.</summary>
    public string File { get; } = file;

    /// <summary>Text from around the number, matched against the collapsed context.</summary>
    public string Phrase { get; } = phrase;

    /// <summary>Why this is not a count. Read by people, not by code.</summary>
    public string Why { get; } = why;

    /// <summary>Set when it matches, so a stale entry can be reported.</summary>
    public bool Used { get; set; }
}

internal static class ExcuseList
{
    internal static Excuse[] Build() =>
    [
        // ── Illustrations. A number invented to make a point about a design. ──
        new("site/notes.html", "a dropdown of forty chapters",
            "a hypothetical badly-designed filter, not this site's chapter count"),

        // ── History. Sentences ABOUT the drift this check exists to prevent. ──
        // These have to be allowed to state the old figures; that is the point
        // of them. They are also the reason the phrases are this specific — a
        // blanket exemption for these files would excuse a real future drift in
        // the same paragraph.
        new("tools/README.md", "advertised thirty-nine chapters above a sidebar",
            "the audit that produced this check, describing what it found"),
        new("tools/README.md", "the site grew from 39 chapters to 47",
            "the same, in the section about what the scan added"),
        new("tools/README.md", "from 312 questions to 432",
            "the same sentence, counting the other way"),
        new("tools/README.md", "the site reached 47 chapters while the API",
            "the mastery-denominator drift, in the section explaining why that check exists"),
        new("docs/BLUEPRINT.md", "The site grew to 47 chapters and the API stayed on 39",
            "§11's post-mortem of a bug that was live until 2026-09-06 and is described in the past tense"),
        new("docs/CONTENT-BACKLOG.md", "is green on all ten checks",
            "a dated status entry from 2026-09-05, when there were ten"),

        // ── A figure for a project that does not exist yet. ──
        new("docs/BLUEPRINT.md", "47 chapters and ~430 questions",
            "the size a PHP rebuild should aim for, deliberately approximate"),
    ];
}

internal enum Severity
{
    Warning,
    Error,
}

internal sealed record Issue(Severity Severity, string Message);

internal sealed record Chapter(string N, string Id, bool HasReq, bool HasExtra);

internal sealed record Question(string Chapter, int Index, string Stem, int OptionCount, List<int> Correct, bool HasWhy);

// Counts are written both ways here — "47 files" in a code comment, "Forty-seven
// chapters" in a sentence — and both have to be readable. The pattern and the
// parser are built from one pair of lists precisely so they cannot drift apart,
// which would be a slightly embarrassing bug in a check about drift.
internal static class Numbers
{
    private static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] Tens =
    [
        "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    ];

    /// <summary>A group named "n" matching a count written as digits or as words.</summary>
    public static string Pattern { get; } = Build();

    public static int? Parse(string token)
    {
        if (int.TryParse(token, out int digits))
        {
            return digits;
        }

        string[] parts = token.ToLowerInvariant().Split('-');
        int ten = Array.IndexOf(Tens, parts[0]);

        if (ten >= 0)
        {
            int value = (ten + 2) * 10;
            if (parts.Length == 1)
            {
                return value;
            }

            int unit = Array.IndexOf(Ones, parts[1]);
            return unit is > 0 and < 10 ? value + unit : null;
        }

        int one = Array.IndexOf(Ones, parts[0]);
        return parts.Length == 1 && one >= 0 ? one : null;
    }

    private static string Build()
    {
        // Longest first, both times: "twenty-four" has to win over "twenty", and
        // "nineteen" over "nine", or the alternation stops at the short one and
        // reads the wrong number.
        string units = string.Join('|', Ones[1..10].Reverse());
        IEnumerable<string> tens = Tens.Select(t => t + "(?:-(?:" + units + "))?");

        return "(?<n>[0-9]{1,4}|" + string.Join('|', tens.Concat(Ones.Reverse())) + ")";
    }
}
