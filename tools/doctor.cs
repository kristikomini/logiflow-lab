#!/usr/bin/env dotnet
// =============================================================================
// doctor.cs — the repository's integrity check, in one command.
//
//   dotnet run tools/doctor.cs            run every check, report, exit 1 on error
//   dotnet run tools/doctor.cs --update   rewrite tools/quiz-ids.lock, then run
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
// Six such couplings hold this place together, and every one of them was
// previously maintained by remembering:
//
//   1. site/assets/chapters.js  <->  site/chapters/*.html     the manifest
//   2. site/assets/quizzes-*.js <->  spaced-repetition keys    ids never move
//   3. course/GOLDEN-RULES.md   <->  each module's own card    hand-synced
//   4. course/GOLDEN-RULES.md   <->  site/assets/rules.js      generated
//   5. course/**.md + src/**.cs <->  course headings and files cross-refs
//   6. course/**.md             <->  Labs.Playground's demos   cited by name
//
// Three of those have already broken at least once. This file turns all six
// into a build failure, which is the only form of documentation that maintains
// itself.
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
];

if (update)
{
    WriteQuizLock();
    Console.WriteLine($"wrote {Rel(quizLock)}");
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
//  9. `Covered in: course/module-.../file.md` — 82 of them at the last count.
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

    // Words that follow `dotnet run` without naming a demo.
    HashSet<string> notDemos = new(StringComparer.Ordinal)
    {
        "list", "all", "tools", "help",
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

internal enum Severity
{
    Warning,
    Error,
}

internal sealed record Issue(Severity Severity, string Message);

internal sealed record Chapter(string N, string Id, bool HasReq, bool HasExtra);

internal sealed record Question(string Chapter, int Index, string Stem, int OptionCount, List<int> Correct, bool HasWhy);
