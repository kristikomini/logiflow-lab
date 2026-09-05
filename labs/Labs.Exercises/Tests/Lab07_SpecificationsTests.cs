using System.Linq.Expressions;
using Labs.Exercises.Exercises;
using static Labs.Exercises.Exercises.Lab07Specifications;

namespace Labs.Exercises.Tests;

/// <summary>
/// The spec for Lab 07. Do not edit — edit
/// <c>Exercises/Lab07_Specifications.cs</c> until these pass.
/// </summary>
public sealed class Lab07SpecificationsTests
{
    private static readonly Product[] Catalogue =
    [
        new("ELE-1", "Electronics", 120m, Discontinued: false),
        new("ELE-2", "Electronics",  40m, Discontinued: true),
        new("HOM-1", "Home",        200m, Discontinued: false),
        new("HOM-2", "Home",         15m, Discontinued: false),
    ];

    private static readonly Expression<Func<Product, bool>> Expensive = p => p.Price >= 100m;
    private static readonly Expression<Func<Product, bool>> Electronics = p => p.Category == "Electronics";
    private static readonly Expression<Func<Product, bool>> Available = p => !p.Discontinued;

    // ── Exercise 1 — composition ──────────────────────────────────────────────────────

    [Fact]
    public void And_matches_both()
    {
        Expression<Func<Product, bool>> spec = Lab07Specifications.And(Expensive, Available);

        Catalogue.AsQueryable().Where(spec).Select(p => p.Sku)
            .ShouldBe(["ELE-1", "HOM-1"]);
    }

    [Fact]
    public void And_produces_a_single_expression_tree_not_a_compiled_delegate()
    {
        Expression<Func<Product, bool>> spec = Lab07Specifications.And(Expensive, Available);

        spec.Body.NodeType.ShouldBe(
            ExpressionType.AndAlso,
            "compiling the operands would work here and be untranslatable in EF Core");
    }

    [Fact]
    public void And_rewrites_both_sides_onto_one_parameter()
    {
        Expression<Func<Product, bool>> spec = Lab07Specifications.And(Expensive, Electronics);

        spec.Parameters.Count.ShouldBe(1);

        var parameters = new ParameterCollector();
        parameters.Visit(spec.Body);
        parameters.Found.Distinct().Count()
            .ShouldBe(1, "both operands must be rewritten onto the lambda's own parameter");
    }

    [Fact]
    public void Or_matches_either()
    {
        Expression<Func<Product, bool>> spec = Lab07Specifications.Or(Electronics, Expensive);

        Catalogue.AsQueryable().Where(spec).Select(p => p.Sku)
            .ShouldBe(["ELE-1", "ELE-2", "HOM-1"]);
    }

    [Fact]
    public void Or_produces_an_OrElse_node()
        => Lab07Specifications.Or(Electronics, Expensive).Body.NodeType.ShouldBe(ExpressionType.OrElse);

    [Fact]
    public void Not_inverts()
    {
        Expression<Func<Product, bool>> spec = Lab07Specifications.Not(Electronics);

        Catalogue.AsQueryable().Where(spec).Select(p => p.Sku).ShouldBe(["HOM-1", "HOM-2"]);
        spec.Body.NodeType.ShouldBe(ExpressionType.Not);
    }

    [Fact]
    public void Compositions_nest()
    {
        Expression<Func<Product, bool>> spec =
            Lab07Specifications.And(
                Lab07Specifications.Or(Electronics, Expensive),
                Available);

        Catalogue.AsQueryable().Where(spec).Select(p => p.Sku).ShouldBe(["ELE-1", "HOM-1"]);
    }

    private sealed class ParameterCollector : ExpressionVisitor
    {
        public List<ParameterExpression> Found { get; } = [];

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found.Add(node);
            return base.VisitParameter(node);
        }
    }

    // ── Exercise 2 — the cursor ───────────────────────────────────────────────────────

    [Fact]
    public void Cursor_round_trips()
    {
        var original = new Cursor(
            new DateTimeOffset(2026, 3, 14, 15, 9, 26, 535, TimeSpan.FromHours(1)),
            Guid.Parse("6f0f4b3e-6a0f-4f9d-9c1e-2b7a5d8e1c40"));

        Cursor.TryDecode(original.Encode(), out Cursor decoded).ShouldBeTrue();

        decoded.ShouldBe(original);
        decoded.CreatedAt.Offset.ShouldBe(TimeSpan.FromHours(1), "the offset must survive");
    }

    [Fact]
    public void Cursor_is_url_safe()
    {
        string token = new Cursor(DateTimeOffset.UtcNow, Guid.NewGuid()).Encode();

        token.ShouldNotContain("+");
        token.ShouldNotContain("/");
        token.ShouldNotContain("=");
    }

    [Fact]
    public void Cursor_is_opaque()
    {
        var moment = new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero);

        new Cursor(moment, Guid.Empty).Encode().ShouldNotContain("2026");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-cursor")]
    [InlineData("!!!!")]
    [InlineData("aGVsbG8")]                       // valid base64url, wrong content
    public void Cursor_rejects_rubbish_without_throwing(string? token)
    {
        Should.NotThrow(() => Cursor.TryDecode(token, out _));
        Cursor.TryDecode(token, out _).ShouldBeFalse();
    }

    // ── Exercise 3 — keyset pagination ────────────────────────────────────────────────

    private static Row[] Rows(int count)
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return Enumerable.Range(0, count)
            .Select(i => new Row(
                Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                start.AddMinutes(i),
                $"row-{i}"))
            .ToArray();
    }

    [Fact]
    public void First_page_is_the_newest_rows()
    {
        Page<Row> page = KeysetPage(Rows(10), pageSize: 3);

        page.Items.Select(r => r.Name).ShouldBe(["row-9", "row-8", "row-7"]);
        page.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public void Paging_covers_every_row_exactly_once()
    {
        Row[] all = Rows(10);
        var seen = new List<string>();
        string? cursor = null;

        do
        {
            Page<Row> page = KeysetPage(all, pageSize: 3, cursor);
            seen.AddRange(page.Items.Select(r => r.Name));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Count.ShouldBe(10);
        seen.Distinct().Count().ShouldBe(10);
        seen.ShouldBe(all.OrderByDescending(r => r.CreatedAt).Select(r => r.Name).ToList());
    }

    [Fact]
    public void The_last_page_reports_no_next_cursor()
    {
        Page<Row> page = KeysetPage(Rows(3), pageSize: 5);

        page.Items.Count.ShouldBe(3);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public void An_exactly_full_last_page_still_terminates()
    {
        Page<Row> first = KeysetPage(Rows(6), pageSize: 3);
        Page<Row> second = KeysetPage(Rows(6), pageSize: 3, first.NextCursor);

        second.Items.Count.ShouldBe(3);
        second.NextCursor.ShouldBeNull("there is nothing after this, even though the page was full");
    }

    [Fact]
    public void Rows_sharing_a_timestamp_are_not_skipped()
    {
        var moment = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        Row[] all =
        [
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), moment, "a"),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"), moment, "b"),
            new(Guid.Parse("00000000-0000-0000-0000-000000000003"), moment, "c"),
        ];

        Page<Row> first = KeysetPage(all, pageSize: 2);
        Page<Row> second = KeysetPage(all, pageSize: 2, first.NextCursor);

        first.Items.Select(r => r.Name).Concat(second.Items.Select(r => r.Name))
            .ShouldBe(["c", "b", "a"], "the id tie-breaker is what makes this deterministic");
    }

    [Fact]
    public void Inserting_a_newer_row_mid_paging_does_not_duplicate_or_skip()
    {
        List<Row> all = [.. Rows(6)];

        Page<Row> first = KeysetPage(all, pageSize: 3);

        // Somebody creates a new row while the caller is between pages.
        all.Add(new Row(
            Guid.Parse("00000000-0000-0000-0000-000000000099"),
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            "brand-new"));

        Page<Row> second = KeysetPage(all, pageSize: 3, first.NextCursor);

        string[] seen = [.. first.Items.Select(r => r.Name), .. second.Items.Select(r => r.Name)];

        seen.Distinct().Count().ShouldBe(seen.Length, "Skip/Take would repeat a row here");
        seen.ShouldNotContain("brand-new", "and it would also push one off the end unseen");
        seen.ShouldBe(["row-5", "row-4", "row-3", "row-2", "row-1", "row-0"]);
    }

    [Fact]
    public void An_unreadable_cursor_starts_from_the_beginning()
    {
        Page<Row> page = KeysetPage(Rows(4), pageSize: 2, "garbage");

        page.Items.Select(r => r.Name).ShouldBe(["row-3", "row-2"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_page_size_is_rejected(int pageSize)
        => Should.Throw<ArgumentOutOfRangeException>(() => KeysetPage(Rows(3), pageSize));
}
