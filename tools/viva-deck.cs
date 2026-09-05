#!/usr/bin/env dotnet
// =============================================================================
// viva-deck.cs — turns course/GOLDEN-RULES.md into the viva deck the site drills.
//
//   dotnet run tools/viva-deck.cs            regenerate site/assets/rules.js
//   dotnet run tools/viva-deck.cs --check    parse and report, write nothing
//
// This is a .NET 10 *file-based app*: one .cs file, no .csproj, not in the
// solution. `dotnet run` on a source file compiles and runs it, which is the
// right shape for a repository tool that must never end up in the product's
// build graph. It still picks up the root Directory.Build.props, so it is
// net10.0 with nullable enabled like everything else here.
//
// WHY A GENERATOR AND NOT A fetch()
// The site is deliberately buildless — every page opens from disk over
// file://, where XHR is blocked, and the Academy host serves site/ only, so
// course/ is not reachable over HTTP either. The deck therefore has to be
// baked into a .js file that a <script> tag can load. Committing generated
// output is the price of that, so: rules.js is generated, never hand-edited,
// and this file is the only thing that writes it.
//
// RERUN IT whenever a Golden rules card changes. GOLDEN-RULES.md is itself
// hand-synced from the per-module cards, so the order is: edit the module
// card, update GOLDEN-RULES.md, run this.
// =============================================================================

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// ── Where things live ────────────────────────────────────────────────────────
// The repository root is found by walking up for the solution file, so the tool
// behaves identically from the repo root, from tools/, or from an editor's
// "run this file" button.
string here = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]) ?? ".";
string repo = FindRepoRoot(AppContext.BaseDirectory)
              ?? FindRepoRoot(here)
              ?? FindRepoRoot(Directory.GetCurrentDirectory())
              ?? Directory.GetCurrentDirectory();
string source = Path.Combine(repo, "course", "GOLDEN-RULES.md");
string target = Path.Combine(repo, "site", "assets", "rules.js");

bool checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);

if (!File.Exists(source))
{
    Console.Error.WriteLine($"Cannot find {source}. Run this from inside the repository.");
    return 2;
}

// ── Parse ────────────────────────────────────────────────────────────────────
(List<Rule> rules, List<string> warnings) = Parse(File.ReadAllLines(source));

foreach (string w in warnings)
{
    Console.Error.WriteLine("warning: " + w);
}

if (rules.Count == 0)
{
    Console.Error.WriteLine("No rules parsed. The format of GOLDEN-RULES.md has changed; fix the parser.");
    return 1;
}

// Ids are spaced-repetition keys. A collision would silently merge two cards'
// review history, so it fails the run rather than shipping.
List<string> collisions = rules.GroupBy(r => r.Id, StringComparer.Ordinal)
                               .Where(g => g.Count() > 1)
                               .Select(g => g.Key)
                               .ToList();
if (collisions.Count > 0)
{
    Console.Error.WriteLine("Duplicate card ids: " + string.Join(", ", collisions));
    return 1;
}

int twelve = rules.Count(r => r.Tier == "twelve");
int senior = rules.Count(r => r.Tier == "senior");
int complete = rules.Count(r => r.Kind == "complete");
int modules = rules.Where(r => r.Tier == "module").Select(r => r.Module).Distinct().Count();

Console.WriteLine($"{rules.Count} cards — {twelve} in the twelve, {senior} in the six, " +
                  $"{rules.Count - twelve - senior} across {modules} modules " +
                  $"({complete} are completion cards, the rest ask for the justification).");

// The two curated sections are fixed-size by name. If the file grows a
// thirteenth rule the heading is wrong, and if the parser drops one the deck is
// quietly incomplete — both are worth saying out loud.
if (twelve != 12)
{
    Console.Error.WriteLine($"warning: \"The twelve that decide interviews\" parsed as {twelve} cards.");
}
if (senior != 6)
{
    Console.Error.WriteLine($"warning: \"The six that separate a senior candidate\" parsed as {senior} cards.");
}

if (checkOnly)
{
    Console.WriteLine("--check: nothing written.");
    return 0;
}

// ── Emit ─────────────────────────────────────────────────────────────────────
// Serialized through a source-generated context, not by reflection. That is not
// premature polish: a file-based app compiles with
// JsonSerializerIsReflectionEnabled=false, so JsonSerializer.Serialize(rules,
// options) builds cleanly and then throws at runtime. The generator is the
// supported route, and it is the same one you want in any trimmed or AOT
// application — see module 14.
//
// The encoder is the one setting the context cannot carry, so the options are
// copied from it and adjusted: the deck is full of — and « » and é, and
// escaping those to \uXXXX would make the generated file unreadable for no
// benefit. The page that loads it declares UTF-8, and so does the write below.
JsonSerializerOptions jsonOptions = new(DeckJson.Default.Options)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

string json = JsonSerializer.Serialize(rules, jsonOptions.GetTypeInfo(typeof(List<Rule>)));

StringBuilder output = new();
output.AppendLine("/* ==========================================================================");
output.AppendLine("   rules.js — the viva deck. GENERATED FILE, DO NOT EDIT.");
output.AppendLine();
output.AppendLine("   Source:      course/GOLDEN-RULES.md");
output.AppendLine("   Regenerate:  dotnet run tools/viva-deck.cs");
output.AppendLine();
output.AppendLine("   One card per Golden rule. `kind` is \"explain\" (say why the claim is true)");
output.AppendLine("   or \"complete\" (finish the sentence), the second being for the rules the");
output.AppendLine("   course states without a written justification — there has to be something");
output.AppendLine("   on paper to mark yourself against, or self-marking drifts generous.");
output.AppendLine();
output.AppendLine("   `id` is the spaced-repetition key. It is derived from the claim text, so");
output.AppendLine("   reordering the rules costs nothing and rewording one resets that card only.");
output.AppendLine("   ========================================================================== */");
output.AppendLine("window.RULES = " + json + ";");

Directory.CreateDirectory(Path.GetDirectoryName(target)!);
File.WriteAllText(target, output.ToString(), new UTF8Encoding(false));
Console.WriteLine("Wrote " + Path.GetRelativePath(repo, target));
return 0;

// =============================================================================
// The parser
// =============================================================================

static (List<Rule> Rules, List<string> Warnings) Parse(string[] lines)
{
    List<string> warnings = [];
    List<Rule> rules = [];

    // Section state.
    string tier = "";          // "twelve" | "senior" | "module" | "" (not collecting)
    string module = "";        // "07"
    string part = "";          // "Module 07 — SQL, transactions and concurrency"
    string href = "";          // "module-07-sql-and-transactions/"
    Dictionary<string, int> used = new(StringComparer.Ordinal);

    Regex moduleHeading = new(@"^##\s+\[Module\s+(?<n>\d+)\s+—\s+(?<title>.+?)\]\((?<href>[^)]+)\)\s*$");
    Regex anyHeading = new(@"^#{1,6}\s+(?<text>.+?)\s*$");
    Regex itemStart = new(@"^(?<n>\d+)\.\s+(?<text>.*)$");

    bool fenced = false;
    StringBuilder? current = null;

    void Flush()
    {
        if (current is null)
        {
            return;
        }

        string text = Collapse(current.ToString());
        current = null;
        if (text.Length == 0)
        {
            return;
        }

        Rule? rule = ToRule(text, tier, module, part, href, used, out string? problem);
        if (rule is null)
        {
            warnings.Add(problem + " — " + Excerpt(text));
            return;
        }

        rules.Add(rule);
    }

    foreach (string raw in lines)
    {
        string line = raw.TrimEnd();

        // The layer-map diagram at the top of the file is a fenced block whose
        // lines would otherwise look like list items.
        if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            fenced = !fenced;
            continue;
        }

        if (fenced)
        {
            continue;
        }

        Match m = moduleHeading.Match(line);
        if (m.Success)
        {
            Flush();
            tier = "module";
            module = m.Groups["n"].Value;
            part = "Module " + module + " — " + m.Groups["title"].Value;
            href = m.Groups["href"].Value;
            continue;
        }

        Match h = anyHeading.Match(line);
        if (h.Success)
        {
            Flush();
            string text = h.Groups["text"].Value;

            // The two curated sections at the top of the file. They restate module
            // rules in the wording you would actually say out loud under pressure,
            // which is a different card even when the underlying fact is the same.
            if (text.Contains("twelve that decide", StringComparison.OrdinalIgnoreCase))
            {
                tier = "twelve";
                module = "";
                part = "The twelve that decide interviews";
                href = "";
            }
            else if (text.Contains("six that separate", StringComparison.OrdinalIgnoreCase))
            {
                tier = "senior";
                module = "";
                part = "The six that separate a senior candidate";
                href = "";
            }
            else
            {
                tier = "";
                module = "";
                part = "";
                href = "";
            }

            continue;
        }

        if (tier.Length == 0)
        {
            continue;
        }

        // A blank line or the "> The card." blockquote ends the current item.
        if (line.Length == 0 || line.TrimStart().StartsWith('>'))
        {
            Flush();
            continue;
        }

        Match item = itemStart.Match(line);
        if (item.Success)
        {
            Flush();
            current = new StringBuilder(item.Groups["text"].Value);
            continue;
        }

        // A wrapped continuation line: indented, and an item is open.
        if (current is not null && (raw.StartsWith("   ", StringComparison.Ordinal) || raw.StartsWith('\t')))
        {
            current.Append(' ').Append(line.Trim());
            continue;
        }

        Flush();
    }

    Flush();
    return (rules, warnings);
}

// Turns one joined list item into a card, or explains why it cannot become one.
static Rule? ToRule(
    string text,
    string tier,
    string module,
    string part,
    string href,
    Dictionary<string, int> used,
    out string? problem)
{
    problem = null;

    // A trailing "— [19](module-19-memory-and-gc/) · [20](...)" is a pointer,
    // not prose. Only strip it when the tail is nothing but links, because an
    // em dash in the middle of a sentence is ordinary punctuation here.
    List<string> refs = [];
    Match tail = Regex.Match(text, @"\s*—\s*(?:\[(?<n>\d+)\]\((?<href>[^)]*)\)\s*(?:·\s*)?)+$");
    if (tail.Success)
    {
        foreach (Capture c in tail.Groups["n"].Captures)
        {
            refs.Add(c.Value);
        }

        text = text[..tail.Index].TrimEnd();
    }

    // The claim is the bold run the rule opens with — the sentence worth having ready.
    Match bold = Regex.Match(text, @"^\*\*(?<claim>.+?)\*\*");
    if (!bold.Success)
    {
        problem = "no bold claim, skipped";
        return null;
    }

    string claim = Collapse(bold.Groups["claim"].Value);

    // Trimmed at both ends: the space after the closing ** is separator, not
    // content, but a leading comma is — "**...costs more than an allocation**,
    // because it is copied" only reads correctly with the comma kept.
    string why = text[bold.Length..].Trim();

    // Nine of the rules are stated without a justification: the claim is the
    // whole card. Asking "why is this true?" would leave nothing written to mark
    // the answer against, and a card you cannot mark is a card that teaches you
    // to mark yourself generously. So those become completion cards instead —
    // the stem is shown, the rest of the sentence is the answer.
    bool terse = Strip(why).Length < 20;

    return new Rule
    {
        Id = MakeId(tier, module, claim, used),
        Kind = terse ? "complete" : "explain",
        Tier = tier,
        Module = module,
        Part = part,
        Href = href.Length > 0 ? "course/" + href : null,
        Claim = claim,
        Stem = terse ? Stem(claim) : null,
        Why = terse ? "" : why,

        // True when the claim does not end as its own sentence — "**...costs more
        // than an allocation**, because it is copied on every assignment". The
        // prompt then shows an ellipsis rather than pretending it is finished.
        Continues = why.StartsWith(',') || why.StartsWith(';') || why.StartsWith(':'),
        Checkpoints = Checkpoints(claim + " " + why),
        Refs = refs.Count > 0 ? refs : null,
    };
}

// The visible half of a completion card. Cut at the first clause boundary so
// what remains is a real prompt rather than half a phrase; failing that, cut at
// roughly half the words.
static string Stem(string claim)
{
    int at = claim.IndexOfAny([';', ',']);
    if (at > 12 && at < claim.Length - 12)
    {
        return claim[..(at + 1)];
    }

    string[] words = claim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    int take = Math.Max(3, words.Length / 2);
    return string.Join(' ', words.Take(take));
}

// The terms a correct answer almost certainly contains: everything the rule
// itself puts in code spans, plus HTTP status codes. The page shows which of
// them your own answer actually mentioned — a check on generous self-marking,
// and the reason those code spans are worth keeping accurate in the course.
static List<string>? Checkpoints(string text)
{
    List<string> seen = [];

    foreach (Match m in Regex.Matches(text, @"`(?<term>[^`]{2,40})`"))
    {
        string term = m.Groups["term"].Value.Trim();
        if (term.Length is < 2 or > 40)
        {
            continue;
        }

        if (!seen.Contains(term, StringComparer.OrdinalIgnoreCase))
        {
            seen.Add(term);
        }
    }

    foreach (Match m in Regex.Matches(text, @"(?<![\w.])(?<code>[1-5]\d{2})(?![\w.])"))
    {
        string code = m.Groups["code"].Value;
        if (!seen.Contains(code, StringComparer.Ordinal))
        {
            seen.Add(code);
        }
    }

    return seen.Count > 0 ? seen.Take(6).ToList() : null;
}

// A stable, human-readable card id. Derived from the claim rather than from the
// rule's position, because positions move: inserting a rule at the top of module
// 07's card must not reassign the review history of every rule below it.
static string MakeId(string tier, string module, string claim, Dictionary<string, int> used)
{
    string prefix = tier switch
    {
        "twelve" => "t12",
        "senior" => "s6",
        _ => "m" + module,
    };

    string slug = Strip(claim).ToLowerInvariant();
    slug = Regex.Replace(slug, @"[^a-z0-9]+", "-").Trim('-');

    // Cut on a word boundary so the id stays readable in an exported profile.
    if (slug.Length > 44)
    {
        int cut = slug.LastIndexOf('-', 44);
        slug = slug[..(cut > 20 ? cut : 44)];
    }

    if (slug.Length == 0)
    {
        slug = "rule";
    }

    string id = prefix + "-" + slug;
    if (used.TryGetValue(id, out int n))
    {
        used[id] = n + 1;
        return id + "-" + (n + 1).ToString(CultureInfo.InvariantCulture);
    }

    used[id] = 1;
    return id;
}

// Markdown emphasis and links removed. For slugs and length checks only — the
// page renders the original text, formatting and all.
static string Strip(string s) =>
    Collapse(Regex.Replace(s, @"[*`_]|\[|\]\([^)]*\)", " "));

static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();

static string Excerpt(string s) => s.Length <= 70 ? s : s[..68] + "…";

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

// =============================================================================
// The shape the site consumes. Serialized camelCase.
// =============================================================================

/// <summary>
/// The serializer, generated at compile time. Everything the deck needs in
/// order to be written out is reachable from this one declaration.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<Rule>))]
internal sealed partial class DeckJson : JsonSerializerContext;

/// <summary>One drillable rule.</summary>
internal sealed class Rule
{
    /// <summary>Spaced-repetition key. Stable across reordering, not across rewording.</summary>
    public required string Id { get; init; }

    /// <summary>"explain" — say why the claim is true. "complete" — finish the sentence.</summary>
    public required string Kind { get; init; }

    /// <summary>"twelve" | "senior" | "module".</summary>
    public required string Tier { get; init; }

    /// <summary>Two-digit module number; empty for the two curated sections.</summary>
    public required string Module { get; init; }

    /// <summary>Human label for where the rule comes from.</summary>
    public required string Part { get; init; }

    /// <summary>Repository path of the module, for readers who have the repo open.</summary>
    public string? Href { get; init; }

    /// <summary>The sentence worth having ready.</summary>
    public required string Claim { get; init; }

    /// <summary>The visible half of a completion card; null on an explain card.</summary>
    public string? Stem { get; init; }

    /// <summary>The justification — the model answer. Empty on a completion card.</summary>
    public required string Why { get; init; }

    /// <summary>The claim does not end as its own sentence; render it with an ellipsis.</summary>
    public bool Continues { get; init; }

    /// <summary>Terms a good answer probably names.</summary>
    public List<string>? Checkpoints { get; init; }

    /// <summary>Module numbers this rule points at, for the curated sections.</summary>
    public List<string>? Refs { get; init; }
}
