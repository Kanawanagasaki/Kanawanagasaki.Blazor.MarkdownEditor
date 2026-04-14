namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

// ── Block type enum ──────────────────────────────────────────────────

/// <summary>Types of block-level elements in the editor AST.</summary>
public enum EditorBlockType
{
    Paragraph,
    Heading1, Heading2, Heading3, Heading4, Heading5, Heading6,
    Blockquote,
    UnorderedListItem,
    OrderedListItem,
    FencedCodeBlock,
    ThematicBreak,
    HtmlBlock,
}

// ── Abstract base ─────────────────────────────────────────────────────

/// <summary>
/// Base class for all block-level elements in the editor AST.
/// Each block contains inline content as a list of <see cref="EditorInlineSegment"/>.
/// This flat representation makes style toggling trivial.
/// </summary>
public abstract class EditorBlock
{
    /// <summary>The inline content segments of this block.</summary>
    public List<EditorInlineSegment> Segments { get; } = new();

    /// <summary>Whether this block type supports inline content.</summary>
    public virtual bool HasInlineContent => true;

    /// <summary>The block type discriminator.</summary>
    public abstract EditorBlockType BlockType { get; }
}

// ── Concrete block types ──────────────────────────────────────────────

public class EditorParagraphBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.Paragraph;
}

public class EditorHeadingBlock : EditorBlock
{
    public int Level { get; set; } = 1;
    public override EditorBlockType BlockType => (EditorBlockType)((int)EditorBlockType.Heading1 + Level - 1);
}

public class EditorBlockquoteBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.Blockquote;
    public override bool HasInlineContent => true;
}

public class EditorUnorderedListItemBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.UnorderedListItem;
}

public class EditorOrderedListItemBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.OrderedListItem;
    /// <summary>The item number (1-based).</summary>
    public int Number { get; set; } = 1;
}

public class EditorFencedCodeBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.FencedCodeBlock;
    public override bool HasInlineContent => false;
    /// <summary>The language identifier after the opening fence.</summary>
    public string Language { get; set; } = "";
    /// <summary>The code content (may contain newlines).</summary>
    public string Content { get; set; } = "";
}

public class EditorThematicBreakBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.ThematicBreak;
    public override bool HasInlineContent => false;
}

public class EditorHtmlBlock : EditorBlock
{
    public override EditorBlockType BlockType => EditorBlockType.HtmlBlock;
    public override bool HasInlineContent => false;
    public string Content { get; set; } = "";
}
