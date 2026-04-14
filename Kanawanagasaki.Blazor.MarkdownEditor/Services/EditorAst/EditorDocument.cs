namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// The root of the custom Editor AST. This is the SINGLE SOURCE OF TRUTH
/// for the editor. Markdown text is DERIVED from this document via
/// <see cref="EditorDocumentRenderer"/>.
///
/// All style edits (bold, italic, strikethrough, code) go through
/// AST mutation on this document. Block-level edits (heading, list,
/// blockquote) also mutate this document. The markdown string is only
/// produced as the final step to populate the textarea and trigger
/// the ValueChanged callback.
/// </summary>
public class EditorDocument
{
    /// <summary>The blocks (paragraphs, headings, etc.) in this document.</summary>
    public List<EditorBlock> Blocks { get; } = new();

    /// <summary>Create an empty document.</summary>
    public EditorDocument() { }

    /// <summary>Get the heading level of a block, or 0 if not a heading.</summary>
    public static int GetHeadingLevel(EditorBlock block) => block switch
    {
        EditorHeadingBlock h => h.Level,
        _ => 0
    };

    /// <summary>Check if a block is any kind of heading.</summary>
    public static bool IsHeading(EditorBlock block) => block is EditorHeadingBlock;

    /// <summary>Check if a block is any kind of list item.</summary>
    public static bool IsListItem(EditorBlock block) =>
        block is EditorUnorderedListItemBlock or EditorOrderedListItemBlock;

    /// <summary>Check if a block is an unordered list item.</summary>
    public static bool IsUnorderedListItem(EditorBlock block) =>
        block is EditorUnorderedListItemBlock;

    /// <summary>Check if a block is an ordered list item.</summary>
    public static bool IsOrderedListItem(EditorBlock block) =>
        block is EditorOrderedListItemBlock;

    /// <summary>Check if a block is a blockquote.</summary>
    public static bool IsBlockquote(EditorBlock block) =>
        block is EditorBlockquoteBlock;

    /// <summary>Normalize the document by merging adjacent segments with the same styles.</summary>
    public void Normalize()
    {
        foreach (var block in Blocks)
        {
            if (!block.HasInlineContent) continue;
            NormalizeSegments(block.Segments);
        }
    }

    /// <summary>Merge adjacent segments with identical styles and remove empty segments.</summary>
    public static void NormalizeSegments(List<EditorInlineSegment> segments)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            // Remove empty segments
            if (string.IsNullOrEmpty(segments[i].Text))
            {
                segments.RemoveAt(i);
                continue;
            }
            // Merge with previous if same styles
            if (i > 0 && segments[i].Styles == segments[i - 1].Styles)
            {
                segments[i - 1].Text += segments[i].Text;
                segments.RemoveAt(i);
            }
        }
    }
}
