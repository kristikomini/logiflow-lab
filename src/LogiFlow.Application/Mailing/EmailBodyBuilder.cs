using System.Globalization;
using System.Net;
using System.Text;

namespace LogiFlow.Application.Mailing;

/// <summary>
/// Builds the plain-text and HTML halves of a message from one description of its content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not Razor, Scriban, or Handlebars?</b> Because a template engine solves the problem of
/// letting <i>non-developers</i> edit content, and nobody here is doing that. What it costs is a
/// dependency, a compilation step, a caching story, and a second place where a missing property
/// becomes a runtime error. When the marketing team wants to edit copy, a real engine earns its
/// place; until then, this is thirty lines and every message is checked by the compiler.
/// </para>
/// <para>
/// <b>One description, two bodies.</b> The usual failure is writing the HTML first and treating
/// the text part as an afterthought, at which point they drift and the text version is the one
/// nobody notices is broken — because the developer's own mail client renders HTML. Describing
/// the content once and rendering it twice makes that impossible by construction.
/// </para>
/// <para>
/// <b>Everything is inline-styled and table-free-ish on purpose.</b> Email is not the web: Gmail
/// strips <c>&lt;style&gt;</c> blocks in some contexts, Outlook renders through Word's HTML
/// engine (no flexbox, no grid, unreliable <c>padding</c> on block elements), and roughly
/// nothing supports external stylesheets. Inline styles on simple block elements are the subset
/// that survives everywhere. If a design ever needs more than this, that is the moment to buy an
/// MJML-style toolchain rather than to hand-write nested tables.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public sealed class EmailBodyBuilder
{
    private readonly List<Block> _blocks = [];

    private enum BlockKind
    {
        Paragraph,
        Fact,
    }

    /// <summary>Adds a paragraph of prose.</summary>
    /// <param name="text">The text. Escaped on the HTML side; used verbatim on the text side.</param>
    /// <returns>The same builder, for chaining.</returns>
    public EmailBodyBuilder Paragraph(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        _blocks.Add(new Block(BlockKind.Paragraph, null, text.Trim()));

        return this;
    }

    /// <summary>Adds a labelled value — an order number, a total, a tracking reference.</summary>
    /// <param name="label">What it is.</param>
    /// <param name="value">The value. Rendered as-is; format it before passing it in.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Consecutive facts render as one aligned block in text and one two-column table in HTML,
    /// which is the one place a table is still the right answer in email markup.
    /// </remarks>
    public EmailBodyBuilder Fact(string label, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(value);

        _blocks.Add(new Block(BlockKind.Fact, label.Trim(), value.Trim()));

        return this;
    }

    /// <summary>Adds a labelled value only when there is one.</summary>
    /// <param name="label">What it is.</param>
    /// <param name="value">The value, or <c>null</c>/blank to skip the fact entirely.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Saves every template writing the same <c>if</c>. An "Estimated delivery: " line with
    /// nothing after it looks like a bug to the person reading it, because it is one.
    /// </remarks>
    public EmailBodyBuilder FactIfPresent(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? this : Fact(label, value);

    /// <summary>Renders both bodies.</summary>
    /// <param name="heading">The message's headline, repeated as the HTML document title.</param>
    /// <param name="signOff">The closing line, typically the sender's name.</param>
    /// <returns>The plain-text body and the HTML body.</returns>
    public (string Text, string Html) Build(string heading, string signOff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);
        ArgumentException.ThrowIfNullOrWhiteSpace(signOff);

        return (BuildText(heading, signOff), BuildHtml(heading, signOff));
    }

    private string BuildText(string heading, string signOff)
    {
        var text = new StringBuilder();

        text.AppendLine(heading);

        // An underline of matching length. Plain text has no headings, and this is how every
        // mail client's users have understood one since 1985.
        text.AppendLine(new string('=', heading.Length));
        text.AppendLine();

        // Facts line up under the widest label in their own run, so a group reads as a block
        // rather than as ragged colons.
        int labelWidth = _blocks
            .Where(b => b.Kind == BlockKind.Fact)
            .Select(b => b.Label!.Length)
            .DefaultIfEmpty(0)
            .Max();

        BlockKind? previous = null;

        foreach (Block block in _blocks)
        {
            if (previous == BlockKind.Fact && block.Kind != BlockKind.Fact)
            {
                text.AppendLine();
            }

            if (block.Kind == BlockKind.Paragraph)
            {
                text.AppendLine(block.Content);
                text.AppendLine();
            }
            else
            {
                string label = block.Label + ":";

                text.Append(label);
                text.Append(new string(' ', Math.Max(1, labelWidth + 2 - label.Length)));
                text.AppendLine(block.Content);
            }

            previous = block.Kind;
        }

        if (previous == BlockKind.Fact)
        {
            text.AppendLine();
        }

        text.AppendLine(signOff);

        return text.ToString();
    }

    private string BuildHtml(string heading, string signOff)
    {
        var html = new StringBuilder();

        html.Append(
            CultureInfo.InvariantCulture,
            $"""
             <!DOCTYPE html>
             <html lang="en">
             <head>
             <meta charset="utf-8">
             <meta name="viewport" content="width=device-width, initial-scale=1">
             <title>{Escape(heading)}</title>
             </head>
             <body style="margin:0;padding:24px;background:#f4f5f7;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2933;">
             <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e4e7eb;border-radius:8px;padding:32px;">
             <h1 style="margin:0 0 20px;font-size:20px;line-height:1.3;color:#102a43;">{Escape(heading)}</h1>

             """);

        var openTable = false;

        foreach (Block block in _blocks)
        {
            if (block.Kind == BlockKind.Fact)
            {
                if (!openTable)
                {
                    html.AppendLine(
                        """<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="width:100%;margin:0 0 20px;font-size:15px;">""");

                    openTable = true;
                }

                html.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"""
                     <tr><td style="padding:4px 16px 4px 0;color:#627d98;white-space:nowrap;">{Escape(block.Label!)}</td>
                     <td style="padding:4px 0;font-weight:600;">{Escape(block.Content)}</td></tr>
                     """);

                continue;
            }

            if (openTable)
            {
                html.AppendLine("</table>");
                openTable = false;
            }

            html.AppendLine(
                CultureInfo.InvariantCulture,
                $"""<p style="margin:0 0 16px;font-size:15px;line-height:1.55;">{Escape(block.Content)}</p>""");
        }

        if (openTable)
        {
            html.AppendLine("</table>");
        }

        html.Append(
            CultureInfo.InvariantCulture,
            $"""
             <p style="margin:24px 0 0;font-size:15px;color:#627d98;">{Escape(signOff)}</p>
             </div>
             </body>
             </html>
             """);

        return html.ToString();
    }

    /// <summary>
    /// HTML-escapes a value on its way into the markup.
    /// </summary>
    /// <remarks>
    /// <b>Not optional, and not only about attackers.</b> Every value here originates in the
    /// database — a customer's name, an address line, a cancellation reason typed by a support
    /// agent. One <c>&amp;</c> in "Rossi &amp; Figli" produces invalid markup that some clients
    /// render as raw text; one <c>&lt;</c> silently eats the rest of the paragraph. The security
    /// case (a stored script tag arriving in a webmail client that executes it) is the same fix,
    /// which is why it is applied to every interpolation rather than to the ones that look
    /// dangerous.
    /// </remarks>
    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    private sealed record Block(BlockKind Kind, string? Label, string Content);
}
