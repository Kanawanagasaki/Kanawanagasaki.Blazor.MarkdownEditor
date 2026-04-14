using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// Renders an <see cref="EditorDocument"/> to HTML for the overlay,
/// with per-line character-position mappings for textarea ↔ overlay translation.
/// Uses Markdig's MarkdownDocument as the source for AST-based HTML rendering
/// and position tracking.
/// </summary>
public static class EditorDocumentHtmlRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .EnableTrackTrivia()
        .Build();

    /// <summary>
    /// Render the document's current markdown text to HTML + position mappings.
    /// This re-parses the markdown via Markdig for accurate HTML and source spans.
    /// </summary>
    public static RenderResult Render(EditorDocument doc)
    {
        string markdown = EditorDocumentRenderer.Render(doc);
        return MarkdownRenderer.Render(markdown);
    }
}
