using System.Text;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// Renders an <see cref="EditorDocument"/> to a markdown source string.
/// Uses a delta-based renderer that handles transitions between segments
/// with different styles, producing correctly nested markdown.
/// </summary>
public static class EditorDocumentRenderer
{
    /// <summary>Render the document to markdown text.</summary>
    public static string Render(EditorDocument doc)
    {
        if (doc.Blocks.Count == 0)
            return "";

        var sb = new StringBuilder();
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            if (i > 0)
                sb.Append('\n');
            RenderBlock(doc.Blocks[i], sb);
        }
        return sb.ToString();
    }

    private static void RenderBlock(EditorBlock block, StringBuilder sb)
    {
        switch (block)
        {
            case EditorParagraphBlock:
                RenderSegments(block.Segments, sb);
                break;

            case EditorHeadingBlock heading:
                sb.Append(new string('#', heading.Level));
                sb.Append(' ');
                RenderSegments(block.Segments, sb);
                break;

            case EditorBlockquoteBlock:
                sb.Append("> ");
                RenderSegments(block.Segments, sb);
                break;

            case EditorUnorderedListItemBlock:
                sb.Append("- ");
                RenderSegments(block.Segments, sb);
                break;

            case EditorOrderedListItemBlock oli:
                sb.Append(oli.Number);
                sb.Append(". ");
                RenderSegments(block.Segments, sb);
                break;

            case EditorFencedCodeBlock fenced:
                sb.Append("```");
                if (!string.IsNullOrEmpty(fenced.Language))
                    sb.Append(fenced.Language);
                if (!string.IsNullOrEmpty(fenced.Content))
                {
                    sb.Append('\n');
                    sb.Append(fenced.Content);
                }
                sb.Append("\n```");
                break;

            case EditorThematicBreakBlock:
                sb.Append("---");
                break;

            case EditorHtmlBlock html:
                sb.Append(html.Content);
                break;

            default:
                RenderSegments(block.Segments, sb);
                break;
        }
    }

    /// <summary>
    /// Render inline segments using a delta-based approach.
    /// Tracks current open styles and only emits changes between segments,
    /// producing correctly nested markdown.
    /// </summary>
    public static void RenderSegments(List<EditorInlineSegment> segments, StringBuilder sb)
    {
        if (segments.Count == 0) return;

        InlineStyle currentStyles = InlineStyle.None;

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var targetStyles = seg.Styles;

            if (targetStyles != currentStyles)
            {
                sb.Append(ComputeTransitionMarkers(currentStyles, targetStyles));
                currentStyles = targetStyles;
            }

            sb.Append(seg.Text);
        }

        // Close any remaining open styles
        if (currentStyles != InlineStyle.None)
        {
            sb.Append(ComputeCloseMarkers(currentStyles));
        }
    }

    /// <summary>
    /// Compute the markers needed to transition from currentStyles to targetStyles.
    /// When closing an outer style while inner styles remain open, we must
    /// close the inner styles first, then close outer, then reopen inner.
    /// </summary>
    public static string ComputeTransitionMarkers(InlineStyle currentStyles, InlineStyle targetStyles)
    {
        if (currentStyles == targetStyles) return "";

        var toClose = currentStyles & ~targetStyles;
        var toOpen = targetStyles & ~currentStyles;

        if (toClose == InlineStyle.None && toOpen == InlineStyle.None) return "";

        // When closing styles that have inner styles still open (that should remain),
        // we must temporarily close those inner styles and reopen them.
        var needTempClose = ComputeTemporaryCloses(currentStyles, targetStyles, toClose);

        var close = toClose | needTempClose;
        var open = toOpen | needTempClose;

        return ComputeCloseMarkers(close) + ComputeOpenMarkers(open);
    }

    /// <summary>
    /// Determine which currently-open styles need to be temporarily closed
    /// because an outer style is being closed.
    /// Nesting order (outer to inner): Strikethrough → Bold|Italic → Code
    /// </summary>
    private static InlineStyle ComputeTemporaryCloses(InlineStyle currentStyles, InlineStyle targetStyles, InlineStyle toClose)
    {
        var temp = InlineStyle.None;

        // If closing Strikethrough (outermost), must close everything inside
        if ((toClose & InlineStyle.Strikethrough) != 0)
        {
            temp |= currentStyles & ~toClose & ~InlineStyle.Strikethrough;
        }

        // If closing Bold or Italic, must close Code if it should stay open
        if ((toClose & (InlineStyle.Bold | InlineStyle.Italic)) != 0)
        {
            if ((currentStyles & InlineStyle.InlineCode) != 0 && (targetStyles & InlineStyle.InlineCode) != 0)
            {
                temp |= InlineStyle.InlineCode;
            }
        }

        return temp;
    }

    /// <summary>Compute closing markers for the given styles (innermost first).</summary>
    public static string ComputeCloseMarkers(InlineStyle styles)
    {
        if (styles == InlineStyle.None) return "";

        var sb = new StringBuilder();

        // Close Code (innermost)
        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append('`');

        // Close Bold/Italic
        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;
        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        // Close Strikethrough (outermost)
        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        return sb.ToString();
    }

    /// <summary>Compute opening markers for the given styles (outermost first).</summary>
    public static string ComputeOpenMarkers(InlineStyle styles)
    {
        if (styles == InlineStyle.None) return "";

        var sb = new StringBuilder();

        // Open Strikethrough (outermost)
        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        // Open Bold/Italic
        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;
        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        // Open Code (innermost)
        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append('`');

        return sb.ToString();
    }

    // ── Position tracking ─────────────────────────────────────────────

    /// <summary>Get the length of a block's prefix in rendered markdown.</summary>
    public static int GetBlockPrefixLength(EditorBlock block) => block switch
    {
        EditorHeadingBlock h => h.Level + 1,
        EditorBlockquoteBlock => 2,
        EditorUnorderedListItemBlock => 2,
        EditorOrderedListItemBlock oli => oli.Number.ToString().Length + 2,
        _ => 0
    };

    /// <summary>Get the total rendered markdown length of a block.</summary>
    public static int GetBlockMarkdownLength(EditorBlock block)
    {
        var sb = new StringBuilder();
        RenderBlock(block, sb);
        return sb.Length;
    }
}
