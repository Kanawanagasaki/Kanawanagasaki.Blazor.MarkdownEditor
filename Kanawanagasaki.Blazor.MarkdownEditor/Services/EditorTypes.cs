namespace Kanawanagasaki.Blazor.MarkdownEditor.Services;

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
