using System.Text;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

/// <summary>
/// Provides methods for inserting / toggling Markdown syntax around a
/// text selection.  Every method is pure: it receives the full text and
/// selection coordinates and returns the new text plus updated cursor
/// positions.
///
/// <para>
/// This implementation uses an in-memory document model
/// (<see cref="Services.MarkdownDocument"/>) that tracks formatting state
/// explicitly, rather than inferring it from raw markdown syntax.  This
/// ensures correct behavior when selections cross marker boundaries,
/// span multiple lines, or overlap with existing formatting.
/// </para>
/// </summary>
public static class MarkdownTextExtensions
{
    // ── inline toggle (bold, italic, strikethrough, code) ──────

    /// <summary>Toggle <c>**bold**</c> around the selection.</summary>
    public static TextEditResult ToggleBold(string text, int start, int end)
        => ToggleViaDocumentModel(text, start, end, InlineStyle.Bold);

    /// <summary>Toggle <c>*italic*</c> around the selection.</summary>
    public static TextEditResult ToggleItalic(string text, int start, int end)
        => ToggleViaDocumentModel(text, start, end, InlineStyle.Italic);

    /// <summary>Toggle <c>~~strikethrough~~</c> around the selection.</summary>
    public static TextEditResult ToggleStrikethrough(string text, int start, int end)
        => ToggleViaDocumentModel(text, start, end, InlineStyle.Strikethrough);

    /// <summary>Toggle <c>`inline code`</c> around the selection.</summary>
    public static TextEditResult ToggleInlineCode(string text, int start, int end)
        => ToggleViaDocumentModel(text, start, end, InlineStyle.InlineCode);

    // ── document-model-based toggle ─────────────────────────────

    /// <summary>
    /// Delegate to the in-memory document model for style toggling.
    /// This provides correct behavior for all selection scenarios:
    /// single-line, multi-line, overlapping markers, nested styles, etc.
    /// </summary>
    private static TextEditResult ToggleViaDocumentModel(
        string text, int start, int end, InlineStyle style)
    {
        var (newText, newStart, newEnd) =
            Services.MarkdownDocument.ToggleStyle(text, start, end, style);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = newStart,
            SelectionEnd = newEnd,
        };
    }

    // ── block-level operations (heading, list, blockquote, etc.) ─

    /// <summary>Toggle a heading marker (<c>## </c>) on the current line.</summary>
    public static TextEditResult ToggleHeading(string text, int start, int end, int level)
    {
        // Find the line boundaries
        int lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        string line = text.Substring(lineStart, lineEnd - lineStart);

        // Check if this line already has a heading
        int hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;

        bool hasHeading = hashes >= 1 && hashes <= 6
            && (hashes >= line.Length || line[hashes] == ' ');

        string newLine;
        if (hasHeading && hashes == level)
        {
            // Remove heading
            newLine = line.Substring(hashes + 1); // skip "# "
        }
        else if (hasHeading)
        {
            // Replace heading level
            newLine = new string('#', level) + " " + line.Substring(hashes + 1);
        }
        else
        {
            // Add heading
            newLine = new string('#', level) + " " + line;
        }

        string newText = text.Substring(0, lineStart) + newLine + text.Substring(lineEnd);
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = lineStart + level + 1,
            SelectionEnd = lineStart + newLine.Length,
        };
    }

    /// <summary>Toggle unordered list marker (<c>- </c>).</summary>
    public static TextEditResult ToggleUnorderedList(string text, int start, int end)
    {
        var lines = GetSelectedLines(text, start, end, out int prefix);
        var newLines = new string[lines.Length];
        int addedPrefix = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("- ") || lines[i].StartsWith("* "))
                newLines[i] = lines[i].Substring(2);
            else if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                newLines[i] = "- " + lines[i];
                if (addedPrefix == 0) addedPrefix = 2;
            }
            else
                newLines[i] = lines[i];
        }

        string rebuilt = string.Join("\n", newLines);
        string newText = text.Substring(0, prefix) + rebuilt + text.Substring(start + (end - start > 0 ? end - start : 0));

        int newEnd = prefix + rebuilt.Length;
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = prefix + addedPrefix,
            SelectionEnd = newEnd,
        };
    }

    /// <summary>Toggle ordered list marker (<c>1. </c>).</summary>
    public static TextEditResult ToggleOrderedList(string text, int start, int end)
    {
        var lines = GetSelectedLines(text, start, end, out int prefix);
        var newLines = new string[lines.Length];
        int addedPrefix = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string cleaned = lines[i];
            // Remove existing unordered list markers first
            if (cleaned.StartsWith("- ") || cleaned.StartsWith("* "))
                cleaned = cleaned.Substring(2);

            if (System.Text.RegularExpressions.Regex.IsMatch(cleaned, @"^\d+\. "))
            {
                int dotIdx = cleaned.IndexOf(". ");
                newLines[i] = cleaned.Substring(dotIdx + 2);
            }
            else if (!string.IsNullOrWhiteSpace(cleaned))
            {
                string marker = $"{i + 1}. ";
                newLines[i] = marker + cleaned;
                if (addedPrefix == 0) addedPrefix = marker.Length;
            }
            else
                newLines[i] = cleaned;
        }

        string rebuilt = string.Join("\n", newLines);
        string newText = text.Substring(0, prefix) + rebuilt + text.Substring(prefix + string.Join("\n", lines).Length);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = prefix + addedPrefix,
            SelectionEnd = prefix + rebuilt.Length,
        };
    }

    /// <summary>Toggle blockquote marker (<c>&gt; </c>).</summary>
    public static TextEditResult ToggleBlockquote(string text, int start, int end)
    {
        var lines = GetSelectedLines(text, start, end, out int prefix);
        var newLines = new string[lines.Length];
        int addedPrefix = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("> "))
                newLines[i] = lines[i].Substring(2);
            else if (lines[i] == ">")
                newLines[i] = "";
            else if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                newLines[i] = "> " + lines[i];
                if (addedPrefix == 0) addedPrefix = 2;
            }
            else
                newLines[i] = lines[i];
        }

        string rebuilt = string.Join("\n", newLines);
        string newText = text.Substring(0, prefix) + rebuilt + text.Substring(prefix + string.Join("\n", lines).Length);

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = prefix + addedPrefix,
            SelectionEnd = prefix + rebuilt.Length,
        };
    }

    // ── insertions ───────────────────────────────────────────────

    /// <summary>Insert a link <c>[text](url)</c>.</summary>
    public static TextEditResult InsertLink(string text, int start, int end)
    {
        string selected = text.Substring(start, end - start);
        string linkText = string.IsNullOrEmpty(selected) ? "link text" : selected;
        string insertion = $"[{linkText}](url)";

        string newText = text.Substring(0, start) + insertion + text.Substring(end);
        int urlStart = start + linkText.Length + 3; // after "[text]("
        int urlEnd = urlStart + 3; // "url".Length

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = urlStart,
            SelectionEnd = urlEnd,
        };
    }

    /// <summary>Insert an image <c>![alt](url)</c>.</summary>
    public static TextEditResult InsertImage(string text, int start, int end)
    {
        string selected = text.Substring(start, end - start);
        string altText = string.IsNullOrEmpty(selected) ? "alt text" : selected;
        string insertion = $"![{altText}](url)";

        string newText = text.Substring(0, start) + insertion + text.Substring(end);
        int urlStart = start + altText.Length + 4; // after "![alt]("
        int urlEnd = urlStart + 3; // "url".Length

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = urlStart,
            SelectionEnd = urlEnd,
        };
    }

    /// <summary>Insert a fenced code block <c>```\ncode\n```</c>.</summary>
    public static TextEditResult InsertCodeBlock(string text, int start, int end)
    {
        string selected = text.Substring(start, end - start);
        string codeContent = string.IsNullOrEmpty(selected) ? "code" : selected;

        string insertion = $"```\n{codeContent}\n```";
        string newText = text.Substring(0, start) + insertion + text.Substring(end);

        int contentStart = start + 4; // after "```\n"
        return new TextEditResult
        {
            Text = newText,
            SelectionStart = contentStart,
            SelectionEnd = contentStart + codeContent.Length,
        };
    }

    /// <summary>Insert a horizontal rule <c>---</c>.</summary>
    public static TextEditResult InsertHorizontalRule(string text, int start, int end)
    {
        // Find the end of the current line and insert after it
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;
        string insertion = "\n---";
        string newText = text.Substring(0, lineEnd) + insertion + text.Substring(lineEnd);
        int cursorPos = lineEnd + insertion.Length;

        return new TextEditResult
        {
            Text = newText,
            SelectionStart = cursorPos,
            SelectionEnd = cursorPos,
        };
    }

    // ── helpers ──────────────────────────────────────────────────

    private static string[] GetSelectedLines(string text, int start, int end, out int prefix)
    {
        int lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        int lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;

        prefix = lineStart;
        string selectedText = text.Substring(lineStart, lineEnd - lineStart);
        return selectedText.Split('\n');
    }
}
