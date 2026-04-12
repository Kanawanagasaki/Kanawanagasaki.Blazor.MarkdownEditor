using System.Text;

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
/// text selection.  Every method is pure: it receives the full text and
/// selection coordinates and returns the new text plus updated cursor
/// positions.
/// </summary>
public static class MarkdownTextExtensions
{

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

    // ── smart inline toggle (collects all styles, toggles one) ──

    /// <summary>
    /// Smart inline toggler that collects ALL markdown styles currently
    /// applied around the selection, toggles just the requested style, and
    /// rebuilds the markers in canonical order.
    ///
    /// Canonical marker order (outermost → innermost):
    ///   <c>~~***`text`***~~</c>  (strikethrough → bold+italic → code)
    ///
    /// When bold and italic are both active, they are merged into the
    /// canonical <c>***</c> prefix/suffix rather than written separately.
    /// Code is always the innermost marker because backtick-delimited code
    /// takes rendering precedence over all other inline styles.
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
        // The markers are OUTSIDE the selection: prefix is before `start`,
        // suffix is after `end`.
        var (detectedStyles, prefixLen, suffixLen) = CollectInlineStyles(text, start, end);

        // ── Toggle the requested style ───────────────────────────
        InlineStyle newStyles = detectedStyles ^ styleToToggle;

        // ── Strip existing markers and rebuild ───────────────────
        // The inner content is the selected text itself.
        string inner = text.Substring(start, end - start);

        string newPrefix = BuildMarkerPrefix(newStyles);
        string newSuffix = BuildMarkerSuffix(newStyles);
        int newPrefixLen = newPrefix.Length;

        // Rebuild: text before old prefix + new prefix + inner + new suffix + text after old suffix
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
    /// Collect all inline styles currently wrapping the selection.
    /// Iteratively scans left from <c>start</c> to find opening markers
    /// (from innermost to outermost), then verifies the expected closing
    /// markers exist after <c>end</c> based on the detected styles.
    /// </summary>
    private static (InlineStyle styles, int prefixLen, int suffixLen) CollectInlineStyles(
        string text, int start, int end)
    {
        InlineStyle styles = InlineStyle.None;
        int prefixLen = 0;

        // ── Scan left from start to find opening markers ─────────
        // Iterate outward: each pass detects one marker layer, then
        // continues scanning further left for outer layers.
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

            // Check ** (bold) — must check before *
            if (!found && (styles & InlineStyle.Bold) == 0
                && pos >= 2 && text.Substring(pos - 2, 2) == "**")
            {
                styles |= InlineStyle.Bold;
                prefixLen += 2;
                pos -= 2;
                found = true;
            }

            // Check * (italic, standalone, not part of **)
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

            // Check ` (inline code — innermost marker)
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

        // ── Verify closing markers after end ──────────────────────
        // Build the expected closing suffix from the detected styles
        // and check if it matches the text after `end`.
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
    /// Build the opening marker string for the given set of inline styles.
    /// Canonical order: ~~***` (strikethrough outermost, then bold+italic
    /// merged as ***, or individual bold/italic markers, then code innermost).
    /// </summary>
    private static string BuildMarkerPrefix(InlineStyle styles)
    {
        var sb = new StringBuilder();

        // Strikethrough is outermost
        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        // Bold + Italic → merged as ***
        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;

        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        // Code is innermost
        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append("`");

        return sb.ToString();
    }

    /// <summary>
    /// Build the closing marker string for the given set of inline styles.
    /// Closing markers are in reverse order of the opening markers:
    /// `***~~ (code closes first, then bold+italic, then strikethrough outermost).
    /// </summary>
    private static string BuildMarkerSuffix(InlineStyle styles)
    {
        var sb = new StringBuilder();

        // Code closes first (innermost)
        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append("`");

        // Bold + Italic → merged as ***
        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;

        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        // Strikethrough is outermost (closes last)
        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        return sb.ToString();
    }

    /// <summary>Convert an InlineStyle to its markdown marker string.</summary>
    private static string StyleToMarker(InlineStyle style) => style switch
    {
        InlineStyle.Bold => "**",
        InlineStyle.Italic => "*",
        InlineStyle.Strikethrough => "~~",
        InlineStyle.InlineCode => "`",
        _ => "",
    };

    /// <summary>
    /// Multi-line handler for smart inline toggle.  Processes each
    /// non-empty line independently, collecting and toggling styles
    /// per-line.
    /// </summary>
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

            // Collect styles on this line
            var (detectedStyles, prefixLen, suffixLen) =
                CollectInlineStylesOnLine(lines[i]);

            totalOldPrefix += prefixLen;
            totalOldSuffix += suffixLen;

            // Toggle the requested style
            InlineStyle newStyles = detectedStyles ^ styleToToggle;

            // Strip existing markers and rebuild
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

        // The selection must include ALL markers on every line so that
        // subsequent toggles can detect them via CollectInlineStylesOnLine.
        // Each line in the selected text is a self-contained unit with its
        // own prefix/suffix markers — the selection covers the entire
        // rebuilt region from the first line's opening marker to the last
        // line's closing marker.
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = start,
            SelectionEnd = start + rebuilt.Length,
        };
    }

    /// <summary>
    /// Collect inline styles on a single line string (used for multi-line
    /// selections where each line is processed independently).
    /// Scans from the start of the line outward (left→right for opening,
    /// right→left for closing) using the same iterative approach as
    /// <see cref="CollectInlineStyles"/>.
    /// </summary>
    private static (InlineStyle styles, int prefixLen, int suffixLen)
        CollectInlineStylesOnLine(string line)
    {
        InlineStyle styles = InlineStyle.None;
        int prefixLen = 0;

        // ── Opening markers (scan from start of line outward) ─────
        int pos = 0;

        while (pos < line.Length)
        {
            bool found = false;

            // Check *** (bold+italic combined)
            if ((styles & (InlineStyle.Bold | InlineStyle.Italic)) == 0
                && pos + 3 <= line.Length && line.Substring(pos, 3) == "***")
            {
                styles |= InlineStyle.Bold | InlineStyle.Italic;
                prefixLen += 3;
                pos += 3;
                found = true;
            }

            // Check ** (bold)
            if (!found && (styles & InlineStyle.Bold) == 0
                && pos + 2 <= line.Length && line.Substring(pos, 2) == "**")
            {
                styles |= InlineStyle.Bold;
                prefixLen += 2;
                pos += 2;
                found = true;
            }

            // Check * (italic, standalone)
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

            // Check ~~ (strikethrough)
            if (!found && (styles & InlineStyle.Strikethrough) == 0
                && pos + 2 <= line.Length && line.Substring(pos, 2) == "~~")
            {
                styles |= InlineStyle.Strikethrough;
                prefixLen += 2;
                pos += 2;
                found = true;
            }

            // Check ` (inline code — innermost marker)
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

        // ── Closing markers (verify expected suffix at end of line) ──
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

    /// <summary>
    /// Simple inline toggler used for inline code (backtick) which does
    /// not combine with other markers.  Looks for <paramref name="marker"/>
    /// immediately before <c>start</c> and after <c>end</c>.
    /// If found → unwrap; otherwise → wrap.
    /// </summary>
    private static TextEditResult ToggleInline(
        string text, int start, int end, string marker)
    {
        int mLen = marker.Length;

        // Check if already wrapped
        bool alreadyWrapped = start >= mLen && end + mLen <= text.Length &&
            text.Substring(start - mLen, mLen) == marker &&
            text.Substring(end, mLen) == marker;

        if (alreadyWrapped)
        {
            // Unwrap
            string inner = text.Substring(start, end - start);
            string newText = text.Substring(0, start - mLen) + inner + text.Substring(end + mLen);
            return new TextEditResult
            {
                Text = newText,
                SelectionStart = start - mLen,
                SelectionEnd = end - mLen,
            };
        }

        // Wrap
        if (start == end)
        {
            // No selection – insert marker pair and place cursor between them
            string newText = text.Substring(0, start) + marker + marker + text.Substring(end);
            return new TextEditResult
            {
                Text = newText,
                SelectionStart = start + mLen,
                SelectionEnd = start + mLen,
            };
        }

        string selected = text.Substring(start, end - start);

        // For multi-line selections, wrap/unwrap markers on EACH content line
        if (selected.Contains('\n'))
        {
            var lines = selected.Split('\n');

            // Check if ALL non-empty lines are already wrapped with this marker
            bool allWrapped = lines.Any(l => !string.IsNullOrWhiteSpace(l));
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length < 2 * mLen ||
                    line.Substring(0, mLen) != marker ||
                    line.Substring(line.Length - mLen) != marker)
                {
                    allWrapped = false;
                    break;
                }
            }

            if (allWrapped)
            {
                // Unwrap: remove markers from each non-empty line
                int totalRemoved = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        lines[i] = lines[i].Substring(mLen, lines[i].Length - 2 * mLen);
                        totalRemoved += 2 * mLen;
                    }
                }
                string unwrapped = string.Join("\n", lines);
                string newT = text.Substring(0, start) + unwrapped + text.Substring(end);
                return new TextEditResult
                {
                    Text = newT,
                    SelectionStart = start,
                    SelectionEnd = end - totalRemoved,
                };
            }
            else
            {
                // Wrap: add markers to each non-empty line
                int totalAdded = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        lines[i] = marker + lines[i] + marker;
                        totalAdded += 2 * mLen;
                    }
                }
                string wrapped = string.Join("\n", lines);
                string newT = text.Substring(0, start) + wrapped + text.Substring(end);
                return new TextEditResult
                {
                    Text = newT,
                    SelectionStart = start,
                    SelectionEnd = end + totalAdded,
                };
            }
        }

        // Single-line: simple wrap
        string wrappedSingle = marker + selected + marker;
        string newTSingle = text.Substring(0, start) + wrappedSingle + text.Substring(end);
        return new TextEditResult
        {
            Text = newTSingle,
            SelectionStart = start + mLen,
            SelectionEnd = end + mLen,
        };
    }

    // ── block-level prefix toggles ─────────────────────────────

    /// <summary>Toggle heading prefix on the current line(s).</summary>
    public static TextEditResult ToggleHeading(
        string text, int start, int end, int level)
    {
        string prefix = new string('#', level) + " ";

        // Find the start of the current line
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        // Check if the line already has a heading prefix
        int hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
            hashes++;

        bool hasSpace = hashes < line.Length && line[hashes] == ' ';

        string newLine;

        if (hasSpace && hashes > 0)
        {
            // Already a heading – replace prefix
            string content = line.Substring(hashes + 1);
            newLine = prefix + content;
        }
        else
        {
            // Not a heading – prepend prefix
            newLine = prefix + line;
        }

        string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = lineStart + prefix.Length,
            SelectionEnd = lineStart + newLine.Length,
        };
    }

    /// <summary>Toggle unordered-list prefix on the current line.</summary>
    public static TextEditResult ToggleUnorderedList(string text, int start, int end)
        => ToggleBlockPrefix(text, start, end, "- ");

    /// <summary>Toggle ordered-list prefix on the current line.</summary>
    public static TextEditResult ToggleOrderedList(string text, int start, int end)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        // Check for existing ordered list marker  (e.g. "1. ")
        int numEnd = 0;
        while (numEnd < line.Length && char.IsDigit(line[numEnd]))
            numEnd++;
        bool hasOlMarker = numEnd > 0 &&
                           numEnd < line.Length && line[numEnd] == '.' &&
                           numEnd + 1 < line.Length && line[numEnd + 1] == ' ';

        if (hasOlMarker)
        {
            // Remove marker
            int markerLen = numEnd + 2; // "1. "
            string content = line.Substring(markerLen);
            string newText = text.Substring(0, lineStart) + content + text.Substring(lineEnd);
            return new TextEditResult
            {
                Text = newText,
                SelectionStart = lineStart,
                SelectionEnd = lineStart + content.Length,
            };
        }

        // Check if it has UL marker instead – remove it first
        if (line.StartsWith("- ") || line.StartsWith("* "))
        {
            string content = line.Substring(2);
            // Then add OL marker
            string newLine = "1. " + content;
            string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);
            return new TextEditResult
            {
                Text = newText,
                SelectionStart = lineStart + 3,
                SelectionEnd = lineStart + newLine.Length,
            };
        }

        // Add marker
        string newL = "1. " + line;
        string newT = text.Substring(0, lineStart) + newL + text.Substring(lineEnd);
        return new TextEditResult
        {
            Text = newT,
            SelectionStart = lineStart + 3,
            SelectionEnd = lineStart + newL.Length,
        };
    }

    /// <summary>Toggle blockquote prefix on the current line.</summary>
    public static TextEditResult ToggleBlockquote(string text, int start, int end)
        => ToggleBlockPrefix(text, start, end, "> ");

    /// <summary>
    /// Generic block-prefix toggler.  If the line already starts with
    /// <paramref name="prefix"/> → remove it; otherwise → prepend it.
    /// </summary>
    private static TextEditResult ToggleBlockPrefix(
        string text, int start, int end, string prefix)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        string newLine;
        int selStart;
        if (line.StartsWith(prefix))
        {
            newLine = line.Substring(prefix.Length);
            selStart = lineStart;
        }
        else
        {
            newLine = prefix + line;
            selStart = lineStart + prefix.Length;
        }

        string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = selStart,
            SelectionEnd = lineStart + newLine.Length,
        };
    }

    // ── insertions (no toggle) ─────────────────────────────────

    /// <summary>Insert a link template <c>[text](url)</c>.</summary>
    public static TextEditResult InsertLink(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "link text";
        string insert = $"[{selected}](url)";
        string newText = text.Substring(0, start) + insert + text.Substring(end);

        // Place cursor on "url" so user can type the URL
        int urlStart = start + selected.Length + 3; // after "](  "
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = urlStart,
            SelectionEnd = urlStart + 3, // select "url"
        };
    }

    /// <summary>Insert an image template <c>![alt](url)</c>.</summary>
    public static TextEditResult InsertImage(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "alt text";
        string insert = $"![{selected}](url)";
        string newText = text.Substring(0, start) + insert + text.Substring(end);

        int urlStart = start + selected.Length + 4;
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = urlStart,
            SelectionEnd = urlStart + 3,
        };
    }

    /// <summary>Insert a fenced code block template at cursor.</summary>
    public static TextEditResult InsertCodeBlock(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "";
        string insert = $"```\n{selected}\n```";
        string newText = text.Substring(0, start) + insert + text.Substring(end);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = start + 4, // after opening ```
            SelectionEnd = start + 4 + selected.Length,
        };
    }

    /// <summary>Insert a horizontal rule on its own line.</summary>
    public static TextEditResult InsertHorizontalRule(string text, int start, int end)
    {
        // Ensure we're on a blank line
        int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string existingLine = text.Substring(lineStart, lineEnd - lineStart);
        string insert;

        if (string.IsNullOrWhiteSpace(existingLine))
        {
            // Line is empty – just insert HR
            insert = "---\n";
            string newText = text.Substring(0, lineStart) + insert + text.Substring(lineEnd);
            return new TextEditResult
            {
                Text = newText,
                SelectionStart = lineStart + 4,
                SelectionEnd = lineStart + 4,
            };
        }
        else
        {
            // Line has content – insert HR after it
            insert = "\n---\n";
            string newText = text.Substring(0, lineEnd) + insert + text.Substring(lineEnd);
            return new TextEditResult
            {
                Text = newText,
                SelectionStart = lineEnd + 5,
                SelectionEnd = lineEnd + 5,
            };
        }
    }
}
