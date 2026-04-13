using System.Text;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services;

/// <summary>
/// Converts raw Markdown into clean HTML (no hidden syntax spans) plus
/// per-line character-position mappings for textarea ↔ overlay translation.
/// </summary>
public static class MarkdownRenderer
{
    // ── public API ──────────────────────────────────────────────

    /// <summary>
    /// Render <paramref name="markdown"/> to overlay-ready HTML and
    /// build the position mapping for every source line.
    /// </summary>
    public static RenderResult Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new RenderResult { Html = "", Lines = Array.Empty<LineMapping>() };

        var lines = markdown.Split('\n');
        var htmlSb = new StringBuilder();
        var mappings = new List<LineMapping>();
        int pos = 0; // running source character position

        bool inCodeBlock = false;
        var codeBlockLines = new List<(int Index, int SourceStart, string Text)>();
        var codeBlockMappings = new List<LineMapping>();
        string? codeBlockFence = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int lineSourceStart = pos;

            // ── fenced code blocks ───────────────────────────
            if (!inCodeBlock && IsFencedCodeBlockStart(line, out string fence, out string? lang))
            {
                inCodeBlock = true;
                codeBlockFence = fence;
                codeBlockLines.Clear();
                codeBlockMappings.Clear();

                // Opening fence line – zero-height placeholder for cursor mapping
                htmlSb.Append($"<div class=\"md-line md-fence\" data-line-index=\"{i}\" data-source-start=\"{pos}\"></div>");

                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = Array.Empty<int>() });
                pos += line.Length + 1;
                continue;
            }

            if (inCodeBlock)
            {
                string trimmedLine = line.TrimStart();
                if (trimmedLine.StartsWith(codeBlockFence!) &&
                    trimmedLine.Substring(codeBlockFence.Length).Trim() == "")
                {
                    // ── closing fence: emit the code block with per-line divs ─
                    htmlSb.Append("<pre class=\"md-codeblock\"><code>");
                    for (int ci = 0; ci < codeBlockLines.Count; ci++)
                    {
                        var cl = codeBlockLines[ci];
                        htmlSb.Append($"<div class=\"md-line md-code-line\" data-line-index=\"{cl.Index}\" data-source-start=\"{cl.SourceStart}\">");
                        htmlSb.Append(EscapeHtml(cl.Text));
                        htmlSb.Append("</div>");
                    }
                    htmlSb.Append("</code></pre>");

                    // Merge code block mappings into the main list
                    mappings.AddRange(codeBlockMappings);

                    // Closing fence line – zero-height placeholder
                    htmlSb.Append($"<div class=\"md-line md-fence\" data-line-index=\"{i}\" data-source-start=\"{pos}\"></div>");
                    mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = Array.Empty<int>() });

                    inCodeBlock = false;
                }
                else
                {
                    // Code content line – build mapping (identity) and store for later emission
                    var codeMapping = new LineMapping { SourceStart = pos };
                    var v2s = new List<int>();
                    for (int c = 0; c < line.Length; c++)
                        v2s.Add(pos + c);
                    codeMapping.VisibleToSource = v2s.ToArray();
                    codeBlockMappings.Add(codeMapping);
                    codeBlockLines.Add((i, pos, line));
                }
                pos += line.Length + 1;
                continue;
            }

            // ── horizontal rule ───────────────────────────────
            if (IsHorizontalRule(line))
            {
                htmlSb.Append($"<div class=\"md-line md-hr-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\">");
                htmlSb.Append("<hr class=\"md-hr\" />");
                htmlSb.Append("</div>");

                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = Array.Empty<int>() });
                pos += line.Length + 1;
                continue;
            }

            // ── headings ──────────────────────────────────────
            if (TryMatchHeading(line, out int headingLevel, out string headingContent, out string headingMarkers))
            {
                var inline = RenderInlineWithMapping(headingContent, pos + headingMarkers.Length);
                string tag = $"h{headingLevel}";

                htmlSb.Append($"<div class=\"md-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\">");
                htmlSb.Append($"<{tag} class=\"md-h md-h{headingLevel}\">");
                htmlSb.Append(inline.Html);
                htmlSb.Append($"</{tag}></div>");

                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = inline.VisibleToSource.ToArray() });
                pos += line.Length + 1;
                continue;
            }

            // ── blockquote ────────────────────────────────────
            if (line.StartsWith("> "))
            {
                string bqContent = line.Substring(2);
                var inline = RenderInlineWithMapping(bqContent, pos + 2);

                htmlSb.Append($"<div class=\"md-line md-bq-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\">");
                htmlSb.Append("<blockquote class=\"md-bq\">");
                htmlSb.Append(inline.Html);
                htmlSb.Append("</blockquote></div>");

                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = inline.VisibleToSource.ToArray() });
                pos += line.Length + 1;
                continue;
            }
            if (line == ">")
            {
                htmlSb.Append($"<div class=\"md-line md-bq-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\"></div>");
                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = Array.Empty<int>() });
                pos += line.Length + 1;
                continue;
            }

            // ── unordered list ────────────────────────────────
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                string itemContent = line.Substring(2);
                var inline = RenderInlineWithMapping(itemContent, pos + 2);

                htmlSb.Append($"<div class=\"md-line md-li-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\">");
                htmlSb.Append("<span class=\"md-li-marker\" aria-hidden=\"true\"></span>");
                htmlSb.Append(inline.Html);
                htmlSb.Append("</div>");

                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = inline.VisibleToSource.ToArray() });
                pos += line.Length + 1;
                continue;
            }

            // ── ordered list ──────────────────────────────────
            if (TryMatchOrderedList(line, out string olMarker, out string olContent))
            {
                var inline = RenderInlineWithMapping(olContent, pos + olMarker.Length);

                htmlSb.Append($"<div class=\"md-line md-li-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\">");
                htmlSb.Append($"<span class=\"md-oli-marker\" aria-hidden=\"true\" data-marker=\"{olMarker.TrimEnd('.', ' ')}.\"></span>");
                htmlSb.Append(inline.Html);
                htmlSb.Append("</div>");

                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = inline.VisibleToSource.ToArray() });
                pos += line.Length + 1;
                continue;
            }

            // ── empty line → paragraph break ──────────────────
            if (string.IsNullOrWhiteSpace(line))
            {
                htmlSb.Append($"<div class=\"md-line md-empty\" data-line-index=\"{i}\" data-source-start=\"{pos}\"></div>");
                mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = Array.Empty<int>() });
                pos += line.Length + 1;
                continue;
            }

            // ── paragraph ─────────────────────────────────────
            var paraInline = RenderInlineWithMapping(line, pos);

            htmlSb.Append($"<div class=\"md-line\" data-line-index=\"{i}\" data-source-start=\"{pos}\">");
            htmlSb.Append(paraInline.Html);
            htmlSb.Append("</div>");

            mappings.Add(new LineMapping { SourceStart = pos, VisibleToSource = paraInline.VisibleToSource.ToArray() });
            pos += line.Length + 1;
        }

        return new RenderResult
        {
            Html = htmlSb.ToString(),
            Lines = mappings.ToArray(),
        };
    }

    /// <summary>
    /// Public test method for diagnosing rendering issues.
    /// </summary>
    public static string TestRender(string markdown)
    {
        var result = Render(markdown);
        return result.Html;
    }

    // ── inline rendering WITH position mapping ──────────────────

    /// <summary>
    /// Renders inline markdown to HTML while building a
    /// <see cref="VisibleToSource"/> mapping.  Syntax characters are
    /// emitted as HTML tags (strong, em, del, code) but NOT included
    /// in the mapping — only visible content characters are mapped.
    /// </summary>
    private static (string Html, List<int> VisibleToSource) RenderInlineWithMapping(
        string text, int basePos)
    {
        if (string.IsNullOrEmpty(text))
            return ("", new List<int>());

        var html = new StringBuilder();
        var v2s = new List<int>(); // visible char index → source char index
        int i = 0;

        while (i < text.Length)
        {
            // ── images ![alt](url) ─────────────────────────
            if (i + 1 < text.Length && text[i] == '!' && text[i + 1] == '[')
            {
                int closeBracket = text.IndexOf(']', i + 2);
                if (closeBracket > i + 2)
                {
                    int openParen = closeBracket + 1;
                    if (openParen < text.Length && text[openParen] == '(')
                    {
                        int closeParen = text.IndexOf(')', openParen + 1);
                        if (closeParen > openParen)
                        {
                            string alt = text.Substring(i + 2, closeBracket - i - 2);
                            string url = text.Substring(openParen + 1, closeParen - openParen - 1);

                            // "!" and "[" are syntax — skip them
                            // alt text is visible — emit as image
                            html.Append($"<img src=\"{EscapeHtml(url)}\" alt=\"{EscapeHtml(alt)}\" class=\"md-img\" />");
                            // Map alt text characters
                            for (int a = 0; a < alt.Length; a++)
                                v2s.Add(basePos + i + 2 + a);
                            // "](url)" is syntax — skip

                            i = closeParen + 1;
                            continue;
                        }
                    }
                }
            }

            // ── links [text](url) ───────────────────────────
            if (text[i] == '[')
            {
                int closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i)
                {
                    int openParen = closeBracket + 1;
                    if (openParen < text.Length && text[openParen] == '(')
                    {
                        int closeParen = text.IndexOf(')', openParen + 1);
                        if (closeParen > openParen)
                        {
                            string linkText = text.Substring(i + 1, closeBracket - i - 1);
                            string url = text.Substring(openParen + 1, closeParen - openParen - 1);

                            // "[" and "](url)" are syntax — skip
                            // linkText is visible
                            html.Append($"<a href=\"{EscapeHtml(url)}\" class=\"md-link\" target=\"_blank\" rel=\"noopener\">");
                            for (int c = 0; c < linkText.Length; c++)
                            {
                                html.Append(EscapeHtmlChar(linkText[c]));
                                v2s.Add(basePos + i + 1 + c);
                            }
                            html.Append("</a>");

                            i = closeParen + 1;
                            continue;
                        }
                    }
                }
            }

            // ── bold+italic ***text*** ─────────────────────
            // Handle *** before ** so that ***text*** renders as
            // <strong><em>text</em></strong> instead of <strong>*text</strong>*.
            if (i + 2 < text.Length && text[i] == '*' && text[i + 1] == '*' && text[i + 2] == '*')
            {
                int closePos = text.LastIndexOf("***");
                if (closePos > i + 2)
                {
                    string inner = text.Substring(i + 3, closePos - i - 3);
                    html.Append("<strong><em>");
                    var innerResult = RenderInlineWithMapping(inner, basePos + i + 3);
                    html.Append(innerResult.Html);
                    v2s.AddRange(innerResult.VisibleToSource);
                    html.Append("</em></strong>");
                    i = closePos + 3;
                    continue;
                }
            }

            // ── bold **text** ───────────────────────────────
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                int closePos = text.IndexOf("**", i + 2);
                if (closePos > i + 1)
                {
                    // Check if the closing ** is followed by * (making ***).
                    // If so, and the inner content has an unmatched italic opener,
                    // treat the *** as closing both italic and bold.
                    // Example: **two *three*** → bold contains italic "three"
                    if (closePos + 2 < text.Length && text[closePos + 2] == '*'
                        && HasUnmatchedItalicOpener(text, i + 2, closePos))
                    {
                        // The *** closes both italic inside and bold outside
                        string inner = text.Substring(i + 2, closePos - i - 2);
                        // Append the italic closing * to the inner content so
                        // recursive rendering can match *three* properly
                        string innerWithItalicClose = inner + "*";
                        html.Append("<strong>");
                        var innerResult = RenderInlineWithMapping(innerWithItalicClose, basePos + i + 2);
                        html.Append(innerResult.Html);
                        v2s.AddRange(innerResult.VisibleToSource);
                        html.Append("</strong>");
                        i = closePos + 3; // skip past ***
                        continue;
                    }

                    string innerText = text.Substring(i + 2, closePos - i - 2);
                    html.Append("<strong>");
                    var innerResult2 = RenderInlineWithMapping(innerText, basePos + i + 2);
                    html.Append(innerResult2.Html);
                    v2s.AddRange(innerResult2.VisibleToSource);
                    html.Append("</strong>");
                    i = closePos + 2;
                    continue;
                }
            }

            // ── italic *text* ────────────────────────────────
            if (text[i] == '*' &&
                (i + 1 < text.Length && text[i + 1] != '*') &&
                (i == 0 || text[i - 1] != '*'))
            {
                int closePos = text.IndexOf('*', i + 1);
                if (closePos > i && (closePos + 1 >= text.Length || text[closePos + 1] != '*'))
                {
                    string inner = text.Substring(i + 1, closePos - i - 1);
                    html.Append("<em>");
                    var innerResult = RenderInlineWithMapping(inner, basePos + i + 1);
                    html.Append(innerResult.Html);
                    v2s.AddRange(innerResult.VisibleToSource);
                    html.Append("</em>");
                    i = closePos + 1;
                    continue;
                }
            }

            // ── strikethrough ~~text~~ ──────────────────────
            if (i + 1 < text.Length && text[i] == '~' && text[i + 1] == '~')
            {
                int closePos = text.IndexOf("~~", i + 2);
                if (closePos > i + 1)
                {
                    string inner = text.Substring(i + 2, closePos - i - 2);
                    html.Append("<del>");
                    var innerResult = RenderInlineWithMapping(inner, basePos + i + 2);
                    html.Append(innerResult.Html);
                    v2s.AddRange(innerResult.VisibleToSource);
                    html.Append("</del>");
                    i = closePos + 2;
                    continue;
                }
            }

            // ── inline code `text` ──────────────────────────
            if (text[i] == '`')
            {
                int closePos = text.IndexOf('`', i + 1);
                if (closePos > i)
                {
                    string inner = text.Substring(i + 1, closePos - i - 1);
                    html.Append("<code class=\"md-inline-code\">");
                    html.Append(EscapeHtml(inner));
                    html.Append("</code>");
                    for (int c = 0; c < inner.Length; c++)
                        v2s.Add(basePos + i + 1 + c);
                    i = closePos + 1;
                    continue;
                }
            }

            // ── regular character (visible) ─────────────────
            html.Append(EscapeHtmlChar(text[i]));
            v2s.Add(basePos + i);
            i++;
        }

        return (html.ToString(), v2s);
    }

    // ── block detection helpers (unchanged) ─────────────────────

    private static bool IsFencedCodeBlockStart(string line, out string fence, out string? lang)
    {
        fence = "";
        lang = null;

        if (line.StartsWith("```"))
        {
            fence = "```";
            lang = line.Length > 3 ? line.Substring(3).Trim() : null;
            return true;
        }
        if (line.StartsWith("~~~"))
        {
            fence = "~~~";
            lang = line.Length > 3 ? line.Substring(3).Trim() : null;
            return true;
        }
        return false;
    }

    private static bool IsHorizontalRule(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length < 3) return false;

        char c = trimmed[0];
        if (c != '-' && c != '*' && c != '_') return false;

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] != c && !char.IsWhiteSpace(trimmed[i]))
                return false;
        }
        int nonSpace = trimmed.Count(ch => !char.IsWhiteSpace(ch));
        return nonSpace >= 3;
    }

    private static bool TryMatchHeading(
        string line, out int level, out string content, out string markers)
    {
        level = 0;
        content = "";
        markers = "";

        int hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
            hashes++;

        if (hashes < 1 || hashes > 6) return false;
        if (hashes < line.Length && line[hashes] != ' ') return false;

        level = hashes;
        markers = line.Substring(0, hashes + 1);
        content = line.Substring(hashes + 1);
        return true;
    }

    private static bool TryMatchOrderedList(
        string line, out string marker, out string content)
    {
        marker = "";
        content = "";

        int i = 0;
        while (i < line.Length && char.IsDigit(line[i]))
            i++;

        if (i == 0) return false;
        if (i >= line.Length || line[i] != '.') return false;
        if (i + 1 >= line.Length || line[i + 1] != ' ') return false;

        marker = line.Substring(0, i + 2);
        content = line.Substring(i + 2);
        return true;
    }

    // ── HTML helpers ───────────────────────────────────────────

    /// <summary>
    /// Checks whether the substring text[start..end] contains an unmatched
    /// italic opener (<c>*</c> not part of <c>**</c> or <c>***</c>) that
    /// does not have a corresponding closing <c>*</c> within the same range.
    /// Used by the bold handler to detect cases like
    /// <c>**two *three***</c> where the trailing <c>***</c> closes both
    /// the inner italic and the outer bold.
    /// </summary>
    private static bool HasUnmatchedItalicOpener(string text, int start, int end)
    {
        int italicDepth = 0;
        int j = start;
        while (j < end)
        {
            if (text[j] == '*')
            {
                // Count consecutive asterisks
                int runStart = j;
                while (j < end && text[j] == '*')
                    j++;

                int runLen = j - runStart;

                if (runLen >= 3)
                {
                    // *** or more: treat as bold+italic open/close pair(s)
                    // Each *** is a complete bold+italic open→close pair,
                    // so it doesn't contribute unmatched italic openers.
                    continue;
                }

                if (runLen == 2)
                {
                    // ** is a bold open/close — no italic contribution
                    // Skip past its potential closing
                    int closePos = text.IndexOf("**", j);
                    if (closePos > j && closePos < end)
                        j = closePos + 2;
                    continue;
                }

                // runLen == 1: single * — toggle italic depth
                italicDepth++;
            }
            else
            {
                j++;
            }
        }

        // If italic depth is odd, there's an unmatched italic opener
        return italicDepth % 2 == 1;
    }

    private static string EscapeHtml(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(EscapeHtmlChar(c));
        return sb.ToString();
    }

    private static string EscapeHtmlChar(char c) => c switch
    {
        '&'  => "&amp;",
        '<'  => "&lt;",
        '>'  => "&gt;",
        '"'  => "&quot;",
        '\'' => "&#39;",
        _    => c.ToString(),
    };
}
