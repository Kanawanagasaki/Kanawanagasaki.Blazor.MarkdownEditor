namespace Kanawanagasaki.Blazor.MarkdownEditor.Services;

/// <summary>
/// Result of rendering a Markdown document.  Contains both the clean
/// HTML (no hidden syntax spans) and a per-line character-position
/// mapping so that JavaScript can translate between source positions
/// (textarea) and visible positions (overlay).
/// </summary>
public class RenderResult
{
    /// <summary>HTML string for the overlay.  Syntax characters are stripped.</summary>
    public string Html { get; set; } = "";

    /// <summary>Per-line mapping data.  Index in the array is the 0-based line number.</summary>
    public LineMapping[] Lines { get; set; } = Array.Empty<LineMapping>();
}

/// <summary>
/// Mapping for a single source line.
/// </summary>
public class LineMapping
{
    /// <summary>Character position in the full Markdown where this line starts.</summary>
    public int SourceStart { get; set; }

    /// <summary>
    /// Maps visible-character indices to source-character indices.
    /// <c>visibleToSource[v]</c> gives the Markdown source position for
    /// the character that appears at visible index <c>v</c> in the overlay.
    /// </summary>
    public int[] VisibleToSource { get; set; } = Array.Empty<int>();
}
