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
        => ToggleInline(text, start, end, "**");

    /// <summary>Toggle <c>*italic*</c> around the selection.</summary>
    public static TextEditResult ToggleItalic(string text, int start, int end)
        => ToggleInline(text, start, end, "*");

    /// <summary>Toggle <c>~~strikethrough~~</c> around the selection.</summary>
    public static TextEditResult ToggleStrikethrough(string text, int start, int end)
        => ToggleInline(text, start, end, "~~");

    /// <summary>Toggle <c>`inline code`</c> around the selection.</summary>
    public static TextEditResult ToggleInlineCode(string text, int start, int end)
        => ToggleInline(text, start, end, "`");

    /// <summary>
    /// Generic inline toggler.  Looks for <paramref name="marker"/>
    /// immediately before <c>start</c> and after <c>end</c>.
    /// If found → unwrap; otherwise → wrap.
    /// </summary>
    private static TextEditResult ToggleInline(
        string text, int start, int end, string marker)
    {
        int mLen = marker.Length;

        // ── Combined bold+italic (***text***) handling ────────────
        // When both bold (**) and italic (*) are applied as ***text***,
        // toggling either one should remove just its own markers rather
        // than wrapping an additional layer.

        if (marker == "*" && start >= 3 && end + 3 <= text.Length)
        {
            if (text.Substring(start - 3, 3) == "***" &&
                text.Substring(end, 3) == "***")
            {
                // Remove italic from ***text*** → **text**
                string inner = text.Substring(start, end - start);
                string newText = text.Substring(0, start - 1) + inner + text.Substring(end + 1);
                return new TextEditResult
                {
                    Text = newText,
                    SelectionStart = start - 1,
                    SelectionEnd = end - 1,
                };
            }
        }

        if (marker == "**" && start >= 3 && end + 3 <= text.Length)
        {
            if (text.Substring(start - 3, 3) == "***" &&
                text.Substring(end, 3) == "***")
            {
                // Remove bold from ***text*** → *text*
                string inner = text.Substring(start, end - start);
                string newText = text.Substring(0, start - 2) + inner + text.Substring(end + 2);
                return new TextEditResult
                {
                    Text = newText,
                    SelectionStart = start - 2,
                    SelectionEnd = end - 2,
                };
            }
        }

        // Check if already wrapped
        bool alreadyWrapped = start >= mLen && end + mLen <= text.Length &&
            text.Substring(start - mLen, mLen) == marker &&
            text.Substring(end, mLen) == marker;

        // For italic (*), ensure the * is not part of a ** bold marker.
        // e.g. text = "**text**", start=2, end=6 → text[1]='*' and text[6]='*'
        // look like italic markers, but they're actually the inner * of **.
        if (alreadyWrapped && marker == "*")
        {
            if (start - mLen - 1 >= 0 && text[start - mLen - 1] == '*')
                alreadyWrapped = false; // opening * is the 2nd * of **
            if (end + mLen < text.Length && text[end + mLen] == '*')
                alreadyWrapped = false; // closing * is the 1st * of **
        }

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
        // so the renderer can match opening/closing pairs per-line.
        if (selected.Contains('\n'))
        {
            var lines = selected.Split('\n');

            // ── Multi-line ***text*** handling ────────────────────
            if (marker == "*" || marker == "**")
            {
                bool allBoldItalic = lines.Any(l => !string.IsNullOrWhiteSpace(l));
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Length < 6 ||
                        !line.StartsWith("***") ||
                        !line.EndsWith("***"))
                    {
                        allBoldItalic = false;
                        break;
                    }
                }

                if (allBoldItalic)
                {
                    int totalRemoved = 0;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[i]))
                        {
                            string inner = lines[i].Substring(3, lines[i].Length - 6);
                            if (marker == "*")
                            {
                                // Remove italic: ***text*** → **text**
                                lines[i] = "**" + inner + "**";
                                totalRemoved += 2; // 1 asterisk from each side
                            }
                            else
                            {
                                // Remove bold: ***text*** → *text*
                                lines[i] = "*" + inner + "*";
                                totalRemoved += 4; // 2 asterisks from each side
                            }
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
            }

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
                // For italic (*), ensure the * is not part of a ** bold marker.
                if (marker == "*" && line.Length >= mLen + 1)
                {
                    if (line.StartsWith("**")) { allWrapped = false; break; }
                    if (line.EndsWith("**")) { allWrapped = false; break; }
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
