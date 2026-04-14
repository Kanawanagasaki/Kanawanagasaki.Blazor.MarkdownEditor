namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// A flat text segment with a set of active inline styles.
/// This is the fundamental unit of the custom Editor AST.
/// Instead of nested EmphasisInline/CodeInline trees (like Markdig),
/// we use flat segments where each carries its own style flags.
/// This makes style toggling trivial: flip the flag, merge adjacent same-style segments.
/// </summary>
public class EditorInlineSegment
{
    /// <summary>The visible text content of this segment.</summary>
    public string Text { get; set; } = "";

    /// <summary>The active inline styles on this segment.</summary>
    public InlineStyle Styles { get; set; } = InlineStyle.None;

    public EditorInlineSegment() { }

    public EditorInlineSegment(string text, InlineStyle styles = InlineStyle.None)
    {
        Text = text;
        Styles = styles;
    }

    /// <summary>Get the markdown marker prefix for this segment's styles.</summary>
    public string GetMarkerPrefix()
    {
        return BuildMarkerPrefix(Styles);
    }

    /// <summary>Get the markdown marker suffix for this segment's styles.</summary>
    public string GetMarkerSuffix()
    {
        return BuildMarkerSuffix(Styles);
    }

    /// <summary>Render this segment to markdown (markers + text + markers).</summary>
    public string ToMarkdown()
    {
        return BuildMarkerPrefix(Styles) + Text + BuildMarkerSuffix(Styles);
    }

    /// <summary>Total rendered length in markdown (including markers).</summary>
    public int MarkdownLength => GetMarkerPrefix().Length + Text.Length + GetMarkerSuffix().Length;

    // ── Static marker helpers ────────────────────────────────────────

    public static string BuildMarkerPrefix(InlineStyle styles)
    {
        var sb = new System.Text.StringBuilder();

        // Canonical order: strikethrough → bold+italic → bold → italic → code
        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;

        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append("`");

        return sb.ToString();
    }

    public static string BuildMarkerSuffix(InlineStyle styles)
    {
        var sb = new System.Text.StringBuilder();

        // Reverse order: code → bold+italic → bold → italic → strikethrough
        if ((styles & InlineStyle.InlineCode) != 0)
            sb.Append("`");

        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;

        if (hasBold && hasItalic)
            sb.Append("***");
        else if (hasBold)
            sb.Append("**");
        else if (hasItalic)
            sb.Append("*");

        if ((styles & InlineStyle.Strikethrough) != 0)
            sb.Append("~~");

        return sb.ToString();
    }

    public static string StyleToMarker(InlineStyle style) => style switch
    {
        InlineStyle.Bold => "**",
        InlineStyle.Italic => "*",
        InlineStyle.Strikethrough => "~~",
        InlineStyle.InlineCode => "`",
        _ => "",
    };
}
