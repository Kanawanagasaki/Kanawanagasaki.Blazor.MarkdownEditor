namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// Flags describing which inline styles are active on a text segment.
/// Canonical marker order: ~~ (strikethrough) → *** or **/* (bold/italic) → ` (code).
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
