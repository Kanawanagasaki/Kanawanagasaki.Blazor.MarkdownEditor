using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

/// <summary>
/// Describes the result of a toggle operation.
/// </summary>
public struct TextEditResult
{
    /// <summary>New full text value.</summary>
    public string Text { get; init; }

    /// <summary>New caret (selection start).</summary>
    public int SelectionStart { get; init; }

    /// <summary>New selection end (collapse = same as start).</summary>
    public int SelectionEnd { get; init; }
}

/// <summary>
/// Describes the inline styles currently applied around a selection.
/// </summary>
[Flags]
public enum InlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strikethrough = 4,
    InlineCode = 8,
}

/// <summary>
/// Provides methods for inserting / toggling Markdown syntax around a
/// text selection. Every method is pure: it receives the full text and
/// selection coordinates and returns the new text plus updated cursor
/// positions.
///
/// <b>Architecture:</b> Markdig's MarkdownDocument is the source of truth
/// for <em>detecting</em> which styles are active at a given position.
/// The AST-based detection replaces the old unreliable string-scanning
/// approach. However, the actual text mutations still operate on raw
/// source text because Markdig's AST is not designed for fine-grained
/// in-place editing. After each edit, the new text is re-parsed through
/// Markdig to produce a fresh MarkdownDocument.
/// </summary>
public static class MarkdownTextExtensions
{
    // ── Markdig pipeline for style detection ────────────────────

    private static readonly MarkdownPipeline _detectionPipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build();

    // ── AST-aware inline style detection ────────────────────────

    /// <summary>
    /// Detect which inline styles (bold, italic, strikethrough, code)
    /// are currently active at the given source offset by parsing the
    /// text with Markdig and walking the AST.
    /// </summary>
    public static InlineStyle DetectInlineStyles(string text, int sourceOffset)
    {
        if (string.IsNullOrEmpty(text)) return InlineStyle.None;

        var doc = Markdown.Parse(text, _detectionPipeline);
        var styles = InlineStyle.None;

        foreach (var inline in doc.Descendants<Inline>())
        {
            if (inline.Span.IsEmpty) continue;
            if (sourceOffset >= inline.Span.Start && sourceOffset <= inline.Span.End)
            {
                switch (inline)
                {
                    case EmphasisInline emphasis:
                        if (emphasis.DelimiterChar == '~')
                            styles |= InlineStyle.Strikethrough;
                        else if (emphasis.DelimiterCount >= 2)
                            styles |= InlineStyle.Bold;
                        else
                            styles |= InlineStyle.Italic;
                        break;
                    case CodeInline:
                        styles |= InlineStyle.InlineCode;
                        break;
                }
            }
        }

        return styles;
    }

    // ── inline toggle (bold, italic, strikethrough, code) ──────

    /// <summary>Toggle <c>**bold**</c> around the selection.</summary>
    public static TextEditResult ToggleBold(string text, int start, int end)
        => ToggleInlineSmart(text, start, end, InlineStyle.Bold);

    /// <summary>Toggle <c>*italic*</c> around the selection.</summary>
    public static TextEditResult ToggleItalic(string text, int start, int end)
        => ToggleInlineSmart(text, start, end, InlineStyle.Italic);

    /// <summary>Toggle <c>~~strikethrough~~</c> around the selection.</summary>
    public static TextEditResult ToggleStrikethrough(string text, int start, int end)
        => ToggleInlineSmart(text, start, end, InlineStyle.Strikethrough);

    /// <summary>Toggle <c>`inline code`</c> around the selection.</summary>
    public static TextEditResult ToggleInlineCode(string text, int start, int end)
        => ToggleInlineSmart(text, start, end, InlineStyle.InlineCode);

    // ── smart inline toggle (AST-enhanced) ──────────────────────

    /// <summary>
    /// AST-enhanced smart inline toggler.
    ///
    /// Uses Markdig's parsed AST to detect existing formatting when
    /// the string-based heuristic fails, then applies the toggle
    /// using reliable string manipulation.
    ///
    /// Canonical marker order (outermost → innermost):
    ///   ~~***`text`***~~  (strikethrough → bold+italic → code)
    ///
    /// When bold and italic are both active, they are merged into the
    /// canonical *** prefix/suffix rather than written separately.
    /// </summary>
    private static TextEditResult ToggleInlineSmart(
        string text, int start, int end, InlineStyle styleToToggle)
    {
        // ── Handle empty selection (no text selected) ────────────
        if (start == end)
        {
            string marker = StyleToMarker(styleToToggle);
            string emptyResult = text.Substring(0, start) + marker + marker + text.Substring(end);
            return new TextEditResult
            {
                Text = emptyResult,
                SelectionStart = start + marker.Length,
                SelectionEnd = start + marker.Length,
            };
        }

        // ── Multi-line: delegate to per-line handler ────────────
        string selected = text.Substring(start, end - start);
        if (selected.Contains('\n'))
        {
            return ToggleInlineSmartMultiLine(text, start, end, styleToToggle);
        }

        // ── Collect styles applied around the selection ──────────
        // First try the fast string-based detection
        var (detectedStyles, prefixLen, suffixLen) = CollectInlineStyles(text, start, end);

        // ── If string detection found nothing, try AST-based detection ──
        // This handles cases where the user selects text inside an already-marked
        // region (e.g. selecting "five" inside ~~***`four five six`***~~).
        if (detectedStyles == InlineStyle.None)
        {
            var expanded = TryExpandToMarkerRegion(text, start, end);
            if (expanded != null)
            {
                start = expanded.Value.effectiveStart;
                end = expanded.Value.effectiveEnd;
                detectedStyles = expanded.Value.styles;
                prefixLen = expanded.Value.prefixLen;
                suffixLen = expanded.Value.suffixLen;
            }
        }

        // ── If still nothing, try AST-based expansion ────────────
        if (detectedStyles == InlineStyle.None)
        {
            var astExpansion = TryExpandUsingAst(text, start, end);
            if (astExpansion != null)
            {
                start = astExpansion.Value.effectiveStart;
                end = astExpansion.Value.effectiveEnd;
                detectedStyles = astExpansion.Value.styles;
                prefixLen = astExpansion.Value.prefixLen;
                suffixLen = astExpansion.Value.suffixLen;
            }
        }

        // ── If still no markers, check for overlapping markers ───
        if (detectedStyles == InlineStyle.None)
        {
            var overlapResult = TryResolveOverlappingMarkers(text, start, end, styleToToggle);
            if (overlapResult != null)
                return overlapResult.Value;
        }

        // ── Toggle the requested style ───────────────────────────
        InlineStyle newStyles = detectedStyles ^ styleToToggle;

        // ── Strip existing markers and rebuild ───────────────────
        string inner = text.Substring(start, end - start);

        string newPrefix = BuildMarkerPrefix(newStyles);
        string newSuffix = BuildMarkerSuffix(newStyles);
        int newPrefixLen = newPrefix.Length;

        string newText = text.Substring(0, start - prefixLen)
                       + newPrefix + inner + newSuffix
                       + text.Substring(end + suffixLen);

        int newSelStart = (start - prefixLen) + newPrefixLen;
        int newSelEnd = newSelStart + inner.Length;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

    /// <summary>
    /// Try to expand the selection to cover a marker region using
    /// the Markdig AST. This finds the innermost EmphasisInline or
    /// CodeInline that contains the selection, determines its content
    /// boundaries from the SourceSpan, and returns those boundaries.
    /// </summary>
    private static (int effectiveStart, int effectiveEnd, InlineStyle styles, int prefixLen, int suffixLen)?
        TryExpandUsingAst(string text, int start, int end)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var doc = Markdown.Parse(text, _detectionPipeline);

        // Find the innermost inline that contains the selection
        Inline? best = null;
        int bestLength = int.MaxValue;

        foreach (var inline in doc.Descendants<Inline>())
        {
            if (inline.Span.IsEmpty) continue;

            int contentStart, contentEnd;
            InlineStyle style;

            switch (inline)
            {
                case EmphasisInline emphasis:
                    contentStart = emphasis.Span.Start + emphasis.DelimiterCount;
                    contentEnd = emphasis.Span.End - emphasis.DelimiterCount + 1;
                    if (emphasis.DelimiterChar == '~')
                        style = InlineStyle.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2)
                        style = InlineStyle.Bold;
                    else
                        style = InlineStyle.Italic;
                    break;
                case CodeInline code:
                    contentStart = code.Span.Start + code.DelimiterCount;
                    contentEnd = code.Span.End - code.DelimiterCount + 1;
                    style = InlineStyle.InlineCode;
                    break;
                default:
                    continue;
            }

            // Check if the selection falls within the content area
            if (start >= contentStart && end <= contentEnd)
            {
                int spanLen = inline.Span.Length;
                if (spanLen < bestLength)
                {
                    best = inline;
                    bestLength = spanLen;
                }
            }
        }

        if (best == null) return null;

        // Compute the expansion from the best inline's span
        switch (best)
        {
            case EmphasisInline emphasis:
            {
                int pLen = emphasis.DelimiterCount;
                int sLen = emphasis.DelimiterCount;
                int contentStart = emphasis.Span.Start + pLen;
                int contentEnd = emphasis.Span.End - sLen + 1;

                InlineStyle style = emphasis.DelimiterChar == '~'
                    ? InlineStyle.Strikethrough
                    : emphasis.DelimiterCount >= 2 ? InlineStyle.Bold : InlineStyle.Italic;

                // Also check for parent styles (e.g. *** might be a parent of bold+italic)
                var parentStyles = InlineStyle.None;
                var parent = emphasis.Parent;
                while (parent != null)
                {
                    if (parent is EmphasisInline parentEmph)
                    {
                        if (parentEmph.DelimiterChar == '~')
                            parentStyles |= InlineStyle.Strikethrough;
                        else if (parentEmph.DelimiterCount >= 2)
                            parentStyles |= InlineStyle.Bold;
                        else
                            parentStyles |= InlineStyle.Italic;
                    }
                    parent = parent.Parent;
                }

                var combinedStyles = style | parentStyles;

                // For combined styles, we need to find the outermost span
                if (parentStyles != InlineStyle.None)
                {
                    // Walk up to find the outermost EmphasisInline
                    var outermost = emphasis;
                    var walkParent = emphasis.Parent;
                    while (walkParent != null)
                    {
                        if (walkParent is EmphasisInline outerEmph)
                            outermost = outerEmph;
                        walkParent = walkParent.Parent;
                    }

                    if (outermost != emphasis)
                    {
                        pLen = outermost.Span.Start + outermost.DelimiterCount - outermost.Span.Start;
                        // Actually compute prefix length from outermost span start to content start
                        int outerContentStart = outermost.Span.Start + outermost.DelimiterCount;
                        int outerContentEnd = outermost.Span.End - outermost.DelimiterCount + 1;

                        pLen = outermost.DelimiterCount;
                        sLen = outermost.DelimiterCount;

                        // But we also need the inner delimiters
                        // For ***text*** parsed as a single EmphasisInline with count 3,
                        // we need prefixLen = 3, suffixLen = 3
                        return (outerContentStart, outerContentEnd, combinedStyles, pLen, sLen);
                    }
                }

                return (contentStart, contentEnd, combinedStyles != InlineStyle.None ? combinedStyles : style, pLen, sLen);
            }

            case CodeInline code:
            {
                int pLen = code.DelimiterCount;
                int sLen = code.DelimiterCount;
                int contentStart = code.Span.Start + pLen;
                int contentEnd = code.Span.End - sLen + 1;
                return (contentStart, contentEnd, InlineStyle.InlineCode, pLen, sLen);
            }

            default:
                return null;
        }
    }

    // ── string-based style collection ────────────────────────────

    /// <summary>
    /// Collect all inline styles currently wrapping the selection.
    /// Iteratively scans left from <c>start</c> to find opening markers,
    /// then verifies the expected closing markers exist after <c>end</c>.
    /// </summary>
    private static (InlineStyle styles, int prefixLen, int suffixLen) CollectInlineStyles(
        string text, int start, int end)
    {
        InlineStyle styles = InlineStyle.None;
        int prefixLen = 0;

        int pos = start;

        while (pos > 0)
        {
            bool found = false;

            // Check *** (bold+italic combined) — must check before ** or *
            if ((styles & (InlineStyle.Bold | InlineStyle.Italic)) == 0
                && pos >= 3 && text.Substring(pos - 3, 3) == "***")
            {
                styles |= InlineStyle.Bold | InlineStyle.Italic;
                prefixLen += 3;
                pos -= 3;
                found = true;
            }

            // Check ** (bold)
            if (!found && (styles & InlineStyle.Bold) == 0
                && pos >= 2 && text.Substring(pos - 2, 2) == "**")
            {
                styles |= InlineStyle.Bold;
                prefixLen += 2;
                pos -= 2;
                found = true;
            }

            // Check * (italic, standalone)
            if (!found && (styles & InlineStyle.Italic) == 0
                && pos >= 1 && text[pos - 1] == '*')
            {
                bool isPartOfDoubleStar = (pos >= 2 && text[pos - 2] == '*');
                if (!isPartOfDoubleStar)
                {
                    styles |= InlineStyle.Italic;
                    prefixLen += 1;
                    pos -= 1;
                    found = true;
                }
            }

            // Check ~~ (strikethrough)
            if (!found && (styles & InlineStyle.Strikethrough) == 0
                && pos >= 2 && text.Substring(pos - 2, 2) == "~~")
            {
                styles |= InlineStyle.Strikethrough;
                prefixLen += 2;
                pos -= 2;
                found = true;
            }

            // Check ` (inline code)
            if (!found && (styles & InlineStyle.InlineCode) == 0
                && pos >= 1 && text[pos - 1] == '`')
            {
                styles |= InlineStyle.InlineCode;
                prefixLen += 1;
                pos -= 1;
                found = true;
            }

            if (!found) break;
        }

        // Verify closing markers after end
        int suffixLen = 0;
        if (styles != InlineStyle.None)
        {
            string expectedSuffix = BuildMarkerSuffix(styles);
            if (end + expectedSuffix.Length <= text.Length
                && text.Substring(end, expectedSuffix.Length) == expectedSuffix)
            {
                suffixLen = expectedSuffix.Length;
            }
        }

        return (styles, prefixLen, suffixLen);
    }

    /// <summary>
    /// When a single-line selection falls within a marker region on its line
    /// (but does not start immediately after the opening markers), detect
    /// the surrounding markers and expand the effective selection.
    /// </summary>
    private static (int effectiveStart, int effectiveEnd, InlineStyle styles, int prefixLen, int suffixLen)?
        TryExpandToMarkerRegion(string text, int start, int end)
    {
        int lineStart = start > 0
            ? text.LastIndexOf('\n', start - 1) + 1
            : 0;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        var (styles, prefixLen, suffixLen) = CollectInlineStylesOnLine(line);

        if (styles == InlineStyle.None)
            return null;

        int contentStart = lineStart + prefixLen;
        int contentEnd = lineEnd - suffixLen;

        if (start >= contentStart && end <= contentEnd)
        {
            return (contentStart, contentEnd, styles, prefixLen, suffixLen);
        }

        if (start <= lineStart + prefixLen && end >= lineEnd - suffixLen
            && start < end)
        {
            return (contentStart, contentEnd, styles, prefixLen, suffixLen);
        }

        return null;
    }

    /// <summary>
    /// Collect inline styles on a single line string (used for multi-line
    /// selections where each line is processed independently).
    /// </summary>
    private static (InlineStyle styles, int prefixLen, int suffixLen)
        CollectInlineStylesOnLine(string line)
    {
        InlineStyle styles = InlineStyle.None;
        int prefixLen = 0;

        int pos = 0;

        while (pos < line.Length)
        {
            bool found = false;

            if ((styles & (InlineStyle.Bold | InlineStyle.Italic)) == 0
                && pos + 3 <= line.Length && line.Substring(pos, 3) == "***")
            {
                styles |= InlineStyle.Bold | InlineStyle.Italic;
                prefixLen += 3;
                pos += 3;
                found = true;
            }

            if (!found && (styles & InlineStyle.Bold) == 0
                && pos + 2 <= line.Length && line.Substring(pos, 2) == "**")
            {
                styles |= InlineStyle.Bold;
                prefixLen += 2;
                pos += 2;
                found = true;
            }

            if (!found && (styles & InlineStyle.Italic) == 0
                && pos + 1 <= line.Length && line[pos] == '*')
            {
                bool isPartOfDoubleStar = (pos + 2 <= line.Length && line[pos + 1] == '*');
                if (!isPartOfDoubleStar)
                {
                    styles |= InlineStyle.Italic;
                    prefixLen += 1;
                    pos += 1;
                    found = true;
                }
            }

            if (!found && (styles & InlineStyle.Strikethrough) == 0
                && pos + 2 <= line.Length && line.Substring(pos, 2) == "~~")
            {
                styles |= InlineStyle.Strikethrough;
                prefixLen += 2;
                pos += 2;
                found = true;
            }

            if (!found && (styles & InlineStyle.InlineCode) == 0
                && pos + 1 <= line.Length && line[pos] == '`')
            {
                styles |= InlineStyle.InlineCode;
                prefixLen += 1;
                pos += 1;
                found = true;
            }

            if (!found) break;
        }

        int suffixLen = 0;
        if (styles != InlineStyle.None)
        {
            string expectedSuffix = BuildMarkerSuffix(styles);
            int suffixStart = line.Length - expectedSuffix.Length;
            if (suffixStart >= prefixLen
                && line.Substring(suffixStart, expectedSuffix.Length) == expectedSuffix)
            {
                suffixLen = expectedSuffix.Length;
            }
        }

        return (styles, prefixLen, suffixLen);
    }

    // ── marker building helpers ─────────────────────────────────

    private static string BuildMarkerPrefix(InlineStyle styles)
    {
        var sb = new StringBuilder();

        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;

        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append("`");

        return sb.ToString();
    }

    private static string BuildMarkerSuffix(InlineStyle styles)
    {
        var sb = new StringBuilder();

        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append("`");

        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;

        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        return sb.ToString();
    }

    private static string StyleToMarker(InlineStyle style) => style switch
    {
        InlineStyle.Bold => "**",
        InlineStyle.Italic => "*",
        InlineStyle.Strikethrough => "~~",
        InlineStyle.InlineCode => "`",
        _ => "",
    };

    // ── multi-line inline toggle ────────────────────────────────

    private static TextEditResult ToggleInlineSmartMultiLine(
        string text, int start, int end, InlineStyle styleToToggle)
    {
        string selected = text.Substring(start, end - start);
        var lines = selected.Split('\n');

        int totalOldPrefix = 0;
        int totalOldSuffix = 0;
        int totalNewPrefix = 0;
        int totalNewSuffix = 0;
        var newLines = new string[lines.Length];

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                newLines[i] = lines[i];
                continue;
            }

            var (detectedStyles, prefixLen, suffixLen) =
                CollectInlineStylesOnLine(lines[i]);

            totalOldPrefix += prefixLen;
            totalOldSuffix += suffixLen;

            InlineStyle newStyles = detectedStyles ^ styleToToggle;

            int contentStart = prefixLen;
            int contentEnd = lines[i].Length - suffixLen;
            string inner = contentEnd > contentStart
                ? lines[i].Substring(contentStart, contentEnd - contentStart)
                : lines[i];

            string newLinePrefix = BuildMarkerPrefix(newStyles);
            string newLineSuffix = BuildMarkerSuffix(newStyles);

            totalNewPrefix += newLinePrefix.Length;
            totalNewSuffix += newLineSuffix.Length;

            newLines[i] = newLinePrefix + inner + newLineSuffix;
        }

        string rebuilt = string.Join("\n", newLines);
        string newText = text.Substring(0, start) + rebuilt + text.Substring(end);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = start,
            SelectionEnd = start + rebuilt.Length,
        };
    }

    // ── overlapping marker resolution ────────────────────────────

    private static TextEditResult? TryResolveOverlappingMarkers(
        string text, int start, int end, InlineStyle styleToToggle)
    {
        int lineStart = start > 0
            ? text.LastIndexOf('\n', start - 1) + 1
            : 0;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        int selStart = start - lineStart;
        int selEnd = end - lineStart;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c != '~' && c != '*' && c != '`')
                continue;

            var region = TryParseMarkerRegion(line, i);
            if (region == null) continue;

            var (styles, contentStart, contentEnd, regionStart, regionEnd) = region.Value;

            // Case 1: Selection fully CONTAINS the marker region
            if (selStart <= regionStart && selEnd >= regionEnd)
            {
                string beforeRegion = line.Substring(0, regionStart);
                string regionContent = line.Substring(contentStart, contentEnd - contentStart);
                string afterRegion = line.Substring(regionEnd);
                string cleanContent = beforeRegion + regionContent + afterRegion;

                InlineStyle newStyles = styleToToggle;
                string newPrefix = BuildMarkerPrefix(newStyles);
                string newSuffix = BuildMarkerSuffix(newStyles);

                string newText = text.Substring(0, lineStart)
                               + newPrefix + cleanContent + newSuffix
                               + text.Substring(lineEnd);

                int newSelStart = lineStart + newPrefix.Length;
                int newSelEnd = newSelStart + cleanContent.Length;

                return new TextEditResult
                {
                    Text = newText,
                    SelectionStart = newSelStart,
                    SelectionEnd = newSelEnd,
                };
            }

            i = regionEnd - 1;
        }

        return null;
    }

    private static (InlineStyle styles, int contentStart, int contentEnd, int regionStart, int regionEnd)?
        TryParseMarkerRegion(string line, int openPos)
    {
        InlineStyle styles = InlineStyle.None;
        int pos = openPos;

        while (pos < line.Length)
        {
            bool found = false;

            if ((styles & (InlineStyle.Bold | InlineStyle.Italic)) == 0
                && pos + 3 <= line.Length && line.Substring(pos, 3) == "***")
            {
                styles |= InlineStyle.Bold | InlineStyle.Italic;
                pos += 3;
                found = true;
            }

            if (!found && (styles & InlineStyle.Bold) == 0
                && pos + 2 <= line.Length && line.Substring(pos, 2) == "**")
            {
                styles |= InlineStyle.Bold;
                pos += 2;
                found = true;
            }

            if (!found && (styles & InlineStyle.Italic) == 0
                && pos + 1 <= line.Length && line[pos] == '*')
            {
                bool isPartOfDoubleStar = (pos + 2 <= line.Length && line[pos + 1] == '*');
                if (!isPartOfDoubleStar)
                {
                    styles |= InlineStyle.Italic;
                    pos += 1;
                    found = true;
                }
            }

            if (!found && (styles & InlineStyle.Strikethrough) == 0
                && pos + 2 <= line.Length && line.Substring(pos, 2) == "~~")
            {
                styles |= InlineStyle.Strikethrough;
                pos += 2;
                found = true;
            }

            if (!found && (styles & InlineStyle.InlineCode) == 0
                && pos + 1 <= line.Length && line[pos] == '`')
            {
                styles |= InlineStyle.InlineCode;
                pos += 1;
                found = true;
            }

            if (!found) break;
        }

        if (styles == InlineStyle.None)
            return null;

        int contentStart = pos;

        string expectedSuffix = BuildMarkerSuffix(styles);
        int suffixPos = line.IndexOf(expectedSuffix, contentStart);
        if (suffixPos < 0)
            return null;

        int contentEnd = suffixPos;
        int regionEnd = suffixPos + expectedSuffix.Length;

        return (styles, contentStart, contentEnd, openPos, regionEnd);
    }

    // ── block-level toggles ─────────────────────────────────────

    /// <summary>Toggle an ATX heading prefix on the current line(s).</summary>
    public static TextEditResult ToggleHeading(string text, int start, int end, int level)
    {
        int lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        // Use AST to detect existing heading
        bool isAlreadyHeading = false;
        int existingLevel = 0;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var doc = Markdown.Parse(text, _detectionPipeline);
            foreach (var block in doc)
            {
                if (block is HeadingBlock heading && !heading.Span.IsEmpty
                    && start >= heading.Span.Start && start <= heading.Span.End)
                {
                    isAlreadyHeading = true;
                    existingLevel = heading.Level;
                    break;
                }
            }
        }

        // Fallback: string-based detection
        if (!isAlreadyHeading)
        {
            int hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
                hashes++;
            if (hashes >= 1 && hashes <= 6 && hashes < line.Length && line[hashes] == ' ')
            {
                isAlreadyHeading = true;
                existingLevel = hashes;
            }
        }

        string newLine;
        if (isAlreadyHeading && existingLevel == level)
        {
            int prefixLen = existingLevel + 1;
            newLine = prefixLen < line.Length ? line.Substring(prefixLen) : line.TrimStart('#').TrimStart();
        }
        else
        {
            string cleanLine = line;
            if (isAlreadyHeading)
            {
                int prefixLen = existingLevel + 1;
                cleanLine = prefixLen < line.Length ? line.Substring(prefixLen) : "";
            }
            newLine = new string('#', level) + " " + cleanLine;
        }

        string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);

        // Selection should cover the heading content (after the # markers + space)
        int prefixLength = isAlreadyHeading && existingLevel == level ? 0 : level + 1;
        int newSelStart = lineStart + prefixLength;
        int newSelEnd = lineStart + newLine.Length;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

    /// <summary>Toggle unordered list prefix on the current line(s).</summary>
    public static TextEditResult ToggleUnorderedList(string text, int start, int end)
        => ToggleLinePrefix(text, start, end, "- ", isUnorderedList: true);

    /// <summary>Toggle ordered list prefix on the current line(s).</summary>
    public static TextEditResult ToggleOrderedList(string text, int start, int end)
        => ToggleOrderedListPrefix(text, start, end);

    /// <summary>Toggle blockquote prefix on the current line(s).</summary>
    public static TextEditResult ToggleBlockquote(string text, int start, int end)
        => ToggleLinePrefix(text, start, end, "> ", isBlockquote: true);

    // ── insertions ──────────────────────────────────────────────

    /// <summary>Insert a Markdown link at the current selection.</summary>
    public static TextEditResult InsertLink(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "link text";
        string insertion = $"[{selected}](url)";

        string newText = text.Substring(0, start) + insertion + text.Substring(end);

        int urlStart = start + selected.Length + 3;
        int urlEnd = urlStart + 3;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = urlStart,
            SelectionEnd = urlEnd,
        };
    }

    /// <summary>Insert a Markdown image at the current selection.</summary>
    public static TextEditResult InsertImage(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "alt text";
        string insertion = $"![{selected}](url)";

        string newText = text.Substring(0, start) + insertion + text.Substring(end);

        int urlStart = start + selected.Length + 4;
        int urlEnd = urlStart + 3;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = urlStart,
            SelectionEnd = urlEnd,
        };
    }

    /// <summary>Insert a fenced code block around the current selection.</summary>
    public static TextEditResult InsertCodeBlock(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "";

        string before = text.Substring(0, start);
        string after = text.Substring(end);

        string prefix = before.Length > 0 && !before.EndsWith("\n") ? "\n" : "";
        string suffix = after.Length > 0 && !after.StartsWith("\n") ? "\n" : "";

        string insertion = $"{prefix}```\n{selected}\n```{suffix}";

        string newText = before + insertion + after;

        int cursorPos = start + prefix.Length + 4;
        if (selected.Length > 0)
            cursorPos = start + prefix.Length + 4 + selected.Length;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = cursorPos,
            SelectionEnd = cursorPos,
        };
    }

    /// <summary>Insert a horizontal rule at the current position.</summary>
    public static TextEditResult InsertHorizontalRule(string text, int start, int end)
    {
        // Find the end of the current line to insert after it
        int lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0) lineEnd = text.Length;

        string before = text.Substring(0, lineEnd);
        string after = text.Substring(lineEnd);

        // Insert after the current line
        string insertion = after.Length > 0 && after.StartsWith("\n")
            ? "\n---" : "\n---\n";

        string newText = before + insertion + after;

        int cursorPos = before.Length + insertion.Length;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = cursorPos,
            SelectionEnd = cursorPos,
        };
    }

    // ── line prefix toggles ─────────────────────────────────────

    private static TextEditResult ToggleLinePrefix(string text, int start, int end,
        string prefix, bool isUnorderedList = false, bool isBlockquote = false)
    {
        int lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        int lineEnd = text.IndexOf('\n', Math.Max(start, end));
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        bool hasPrefix = line.StartsWith(prefix) ||
            (isUnorderedList && (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))) ||
            (isBlockquote && (line.StartsWith("> ") || line.StartsWith(">")));

        string newLine;
        if (hasPrefix)
        {
            if (isUnorderedList)
            {
                if (line.StartsWith("- ")) newLine = line.Substring(2);
                else if (line.StartsWith("* ")) newLine = line.Substring(2);
                else if (line.StartsWith("+ ")) newLine = line.Substring(2);
                else newLine = line;
            }
            else if (isBlockquote)
            {
                if (line.StartsWith("> ")) newLine = line.Substring(2);
                else if (line.StartsWith(">")) newLine = line.Substring(1);
                else newLine = line;
            }
            else
            {
                newLine = line.StartsWith(prefix) ? line.Substring(prefix.Length) : line;
            }
        }
        else
        {
            newLine = prefix + line;
        }

        string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);

        int delta = newLine.Length - line.Length;
        int newSelStart = Math.Clamp(start + (hasPrefix ? -prefix.Length : prefix.Length), 0, newText.Length);
        int newSelEnd = Math.Clamp(end + delta, 0, newText.Length);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = Math.Max(0, newSelStart),
            SelectionEnd = Math.Max(0, newSelEnd),
        };
    }

    private static TextEditResult ToggleOrderedListPrefix(string text, int start, int end)
    {
        int lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        int lineEnd = text.IndexOf('\n', Math.Max(start, end));
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        // Check for existing ordered list prefix
        bool hasOlPrefix = false;
        int digitEnd = 0;
        while (digitEnd < line.Length && char.IsDigit(line[digitEnd]))
            digitEnd++;
        if (digitEnd > 0 && digitEnd < line.Length && line[digitEnd] == '.' &&
            digitEnd + 1 < line.Length && line[digitEnd + 1] == ' ')
        {
            hasOlPrefix = true;
        }

        // Check for existing unordered list prefix (to swap)
        bool hasUlPrefix = line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ");

        string newLine;
        if (hasOlPrefix)
        {
            // Remove ordered list prefix
            newLine = line.Substring(digitEnd + 2);
        }
        else if (hasUlPrefix)
        {
            // Swap from unordered to ordered
            string content = line.Substring(2);
            newLine = "1. " + content;
        }
        else
        {
            // Add ordered list prefix
            newLine = "1. " + line;
        }

        string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);

        int delta = newLine.Length - line.Length;
        int newSelStart = hasOlPrefix ? lineStart : lineStart + 3; // after "1. "
        int newSelEnd = Math.Clamp(lineStart + newLine.Length, 0, newText.Length);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }
}
