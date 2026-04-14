using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services;

/// <summary>
/// Converts raw Markdown into clean HTML (no hidden syntax spans) plus
/// per-line character-position mappings for textarea ↔ overlay translation.
/// Uses Markdig's MarkdownDocument as the source of truth for all parsing,
/// position tracking, and AST walking. Every source position is derived from
/// Markdig's SourceSpan on AST nodes.
/// </summary>
public static class MarkdownRenderer
{
    // ── pipeline (immutable, shared) ───────────────────────────

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .EnableTrackTrivia()
        .Build();

    /// <summary>Expose the pipeline so other components can use the same parse settings.</summary>
    public static MarkdownPipeline Pipeline => _pipeline;

    // ── public API ──────────────────────────────────────────────

    /// <summary>
    /// Parse <paramref name="markdown"/> into a Markdig <see cref="MarkdownDocument"/>.
    /// The returned document is the single source of truth for all position mapping,
    /// formatting detection, and AST-based editing.
    /// </summary>
    public static MarkdownDocument ParseDocument(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new MarkdownDocument();

        return Markdown.Parse(markdown, _pipeline);
    }

    /// <summary>
    /// Render <paramref name="markdown"/> to overlay-ready HTML and
    /// build the position mapping for every source line.
    /// Returns both the rendered HTML and the MarkdownDocument used
    /// to produce it, so callers can inspect the AST.
    /// </summary>
    public static RenderResult Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new RenderResult
            {
                Html = "",
                Lines = Array.Empty<LineMapping>(),
                Document = new MarkdownDocument()
            };

        var doc = ParseDocument(markdown);
        var (html, lines) = RenderFromDocument(doc, markdown);
        return new RenderResult
        {
            Html = html,
            Lines = lines,
            Document = doc
        };
    }

    /// <summary>
    /// Render from an existing <see cref="MarkdownDocument"/> plus the original
    /// source text. This avoids re-parsing when the document is already available.
    /// </summary>
    public static (string Html, LineMapping[] Lines) RenderFromDocument(
        MarkdownDocument doc, string sourceText)
    {
        if (doc == null || string.IsNullOrEmpty(sourceText))
            return ("", Array.Empty<LineMapping>());

        // Build per-line data from the raw source lines
        var sourceLines = sourceText.Split('\n');
        var htmlSb = new StringBuilder();
        var mappings = new List<LineMapping>();

        // Track running source position for each line
        var lineStartPositions = new int[sourceLines.Length];
        int pos = 0;
        for (int i = 0; i < sourceLines.Length; i++)
        {
            lineStartPositions[i] = pos;
            pos += sourceLines[i].Length + 1; // +1 for '\n'
        }

        // ── Walk the AST blocks in document order ───────────────
        // Each top-level block maps to one or more source lines.
        // We iterate over doc's direct children and produce overlay HTML
        // with proper position mapping.

        // First, emit empty lines that precede any blocks
        // and then render each block.

        // We process blocks in order but also need to handle
        // blank lines between blocks. We'll walk line-by-line
        // and check which block owns each line.

        var blockLineMap = BuildBlockLineMap(doc, sourceText);

        for (int lineIdx = 0; lineIdx < sourceLines.Length; lineIdx++)
        {
            int lineSourceStart = lineStartPositions[lineIdx];
            string line = sourceLines[lineIdx];

            var block = blockLineMap.TryGetValue(lineIdx, out var b) ? b : null;

            // ── Blank / empty line ────────────────────────────
            if (block == null)
            {
                htmlSb.Append($"<div class=\"md-line md-empty\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\"></div>");
                mappings.Add(new LineMapping
                {
                    SourceStart = lineSourceStart,
                    VisibleToSource = Array.Empty<int>()
                });
                continue;
            }

            // ── Render the block for this line ────────────────
            // Some blocks span multiple lines; we track which lines
            // belong to which block to avoid re-rendering.

            switch (block)
            {
                case HeadingBlock heading:
                    RenderHeadingLine(heading, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                case FencedCodeBlock fenced:
                    RenderFencedCodeLine(fenced, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                case CodeBlock codeBlock:
                    RenderCodeBlockLine(codeBlock, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                case ThematicBreakBlock:
                    RenderThematicBreakLine(lineIdx, lineSourceStart, htmlSb, mappings);
                    break;

                case QuoteBlock quote:
                    RenderQuoteLine(quote, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                case ListBlock listBlock:
                    RenderListLine(listBlock, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                case ListItemBlock listItem:
                    if (listItem.Parent is ListBlock listParent)
                        RenderListLine(listParent, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    else
                        RenderFallbackLine(lineIdx, lineSourceStart, line, htmlSb, mappings);
                    break;

                case ParagraphBlock para:
                    // ParagraphBlock inside a container — delegate to the
                    // container renderer so blockquote/list formatting is applied.
                    if (para.Parent is QuoteBlock parentQuote)
                        RenderQuoteLine(parentQuote, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    else if (para.Parent is ListItemBlock parentListItem
                             && parentListItem.Parent is ListBlock parentList)
                        RenderListLine(parentList, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    else
                        RenderParagraphLine(para, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                case HtmlBlock htmlBlock:
                    RenderHtmlBlockLine(htmlBlock, lineIdx, lineSourceStart, line, sourceText, htmlSb, mappings);
                    break;

                default:
                    // Fallback: render as plain text line
                    RenderFallbackLine(lineIdx, lineSourceStart, line, htmlSb, mappings);
                    break;
            }
        }

        return (htmlSb.ToString(), mappings.ToArray());
    }

    /// <summary>
    /// Public test method for diagnosing rendering issues.
    /// </summary>
    public static string TestRender(string markdown)
    {
        var result = Render(markdown);
        return result.Html;
    }

    // ── Block-to-line mapping ───────────────────────────────────

    /// <summary>
    /// Build a map from source line index to the Block that covers that line.
    /// Uses each block's SourceSpan to determine which lines it owns.
    /// </summary>
    private static Dictionary<int, Block> BuildBlockLineMap(MarkdownDocument doc, string sourceText)
    {
        var map = new Dictionary<int, Block>();
        var sourceLines = sourceText.Split('\n');
        var lineStarts = new int[sourceLines.Length];
        int pos = 0;
        for (int i = 0; i < sourceLines.Length; i++)
        {
            lineStarts[i] = pos;
            pos += sourceLines[i].Length + 1;
        }

        foreach (var block in doc.Descendants())
        {
            if (block is not Block b) continue;
            if (b.Span.IsEmpty) continue;

            // Find the line range this block covers
            int startLine = FindLineIndex(lineStarts, b.Span.Start);
            int endLine = FindLineIndex(lineStarts, b.Span.End);

            // Clamp to valid range
            startLine = Math.Max(0, Math.Min(startLine, sourceLines.Length - 1));
            endLine = Math.Max(0, Math.Min(endLine, sourceLines.Length - 1));

            for (int li = startLine; li <= endLine; li++)
            {
                // Only assign if not already assigned (inner blocks take priority)
                if (!map.ContainsKey(li))
                {
                    map[li] = b;
                }
            }
        }

        // Second pass: give inner (more specific) blocks priority
        foreach (var block in doc.Descendants())
        {
            if (block is not Block b) continue;
            if (b.Span.IsEmpty) continue;

            int startLine = FindLineIndex(lineStarts, b.Span.Start);
            int endLine = FindLineIndex(lineStarts, b.Span.End);
            startLine = Math.Max(0, Math.Min(startLine, sourceLines.Length - 1));
            endLine = Math.Max(0, Math.Min(endLine, sourceLines.Length - 1));

            for (int li = startLine; li <= endLine; li++)
            {
                map[li] = b;
            }
        }

        // Third pass: remap ParagraphBlocks inside containers to their
        // parent container blocks.  Markdig nests content inside containers
        // (e.g. `> text` → QuoteBlock > ParagraphBlock), and the second
        // pass assigns the inner ParagraphBlock.  For rendering we need the
        // line to map to the container so the correct renderer is invoked.
        foreach (var li in map.Keys.ToList())
        {
            if (map[li] is ParagraphBlock para)
            {
                if (para.Parent is QuoteBlock quote)
                    map[li] = quote;
                else if (para.Parent is ListItemBlock listItem)
                    map[li] = listItem;
            }
        }

        return map;
    }

    /// <summary>
    /// Find the line index that contains the given source offset,
    /// using binary search on line start positions.
    /// </summary>
    private static int FindLineIndex(int[] lineStarts, int sourceOffset)
    {
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (lineStarts[mid] <= sourceOffset)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return Math.Max(0, hi);
    }

    // ── Block-level renderers ────────────────────────────────────

    private static void RenderHeadingLine(HeadingBlock heading, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        // The heading line starts with # markers
        int hashCount = 0;
        while (hashCount < line.Length && line[hashCount] == '#')
            hashCount++;

        int markerLen = hashCount < line.Length && line[hashCount] == ' ' ? hashCount + 1 : hashCount;
        string content = markerLen < line.Length ? line.Substring(markerLen) : "";

        var inline = RenderInlineWithMapping(content, lineSourceStart + markerLen);
        string tag = $"h{heading.Level}";

        htmlSb.Append($"<div class=\"md-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append($"<{tag} class=\"md-h md-h{heading.Level}\">");
        htmlSb.Append(inline.Html);
        htmlSb.Append($"</{tag}></div>");

        mappings.Add(new LineMapping
        {
            SourceStart = lineSourceStart,
            VisibleToSource = inline.VisibleToSource.ToArray()
        });
    }

    private static void RenderFencedCodeLine(FencedCodeBlock fenced, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        var sourceLines = sourceText.Split('\n');
        var lineStarts = new int[sourceLines.Length];
        int p = 0;
        for (int i = 0; i < sourceLines.Length; i++)
        {
            lineStarts[i] = p;
            p += sourceLines[i].Length + 1;
        }

        // Opening fence line
        if (lineIdx == fenced.Line)
        {
            htmlSb.Append($"<div class=\"md-line md-fence\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\"></div>");
            mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = Array.Empty<int>() });

            // Emit the full code block content immediately after the opening fence
            EmitFencedCodeContent(fenced, sourceText, htmlSb, mappings);
            return;
        }

        // Closing fence line — detect by checking if this is past the span end
        // or if the line matches the closing fence pattern
        bool isClosingFence = IsClosingFenceLine(fenced, lineIdx, sourceText, lineStarts);
        if (isClosingFence)
        {
            htmlSb.Append($"<div class=\"md-line md-fence\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\"></div>");
            mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = Array.Empty<int>() });
            return;
        }

        // Code content line — these are already rendered by EmitFencedCodeContent,
        // so we output a zero-height placeholder to keep line indices consistent
        htmlSb.Append($"<div class=\"md-line md-fence\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\"></div>");
        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = Array.Empty<int>() });
    }

    private static bool IsClosingFenceLine(FencedCodeBlock fenced, int lineIdx, string sourceText, int[] lineStarts)
    {
        // Check if this line is the closing fence by matching the line content
        var lines = sourceText.Split('\n');
        if (lineIdx < 0 || lineIdx >= lines.Length) return false;

        // The closing fence must be after the opening fence
        if (lineIdx <= fenced.Line) return false;

        // Must have at least 3 matching fence chars
        string trimmedLine = lines[lineIdx].TrimStart();
        int fenceCount = 0;
        char fenceChar = fenced.FencedChar;

        foreach (char c in trimmedLine)
        {
            if (c == fenceChar) fenceCount++;
            else break;
        }

        if (fenceCount < 3 || fenceCount < fenced.OpeningFencedCharCount) return false;

        // The rest after the fence chars must be whitespace only
        string rest = trimmedLine.Substring(fenceCount).TrimEnd();
        return string.IsNullOrEmpty(rest);
    }

    private static int FindClosingFenceLine(FencedCodeBlock fenced, string sourceText, int[] lineStarts)
    {
        var lines = sourceText.Split('\n');
        for (int i = fenced.Line + 1; i < lines.Length; i++)
        {
            if (IsClosingFenceLine(fenced, i, sourceText, lineStarts))
                return i;
        }
        return -1;
    }

    private static void EmitFencedCodeContent(FencedCodeBlock fenced, string sourceText,
        StringBuilder htmlSb, List<LineMapping> mappings)
    {
        var lines = sourceText.Split('\n');

        // Calculate source positions
        var lineStarts = new int[lines.Length];
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            lineStarts[i] = pos;
            pos += lines[i].Length + 1;
        }

        int startLine = fenced.Line + 1; // skip opening fence
        int closingLine = FindClosingFenceLine(fenced, sourceText, lineStarts);
        int endLine = closingLine > 0 ? closingLine - 1 : lines.Length - 1;

        htmlSb.Append("<pre class=\"md-codeblock\"><code>");
        for (int li = startLine; li <= endLine; li++)
        {
            string codeLine = lines[li];
            int indent = fenced.IndentCount;
            string effectiveLine = indent > 0 && codeLine.Length >= indent
                ? codeLine.Substring(indent)
                : codeLine;

            htmlSb.Append($"<div class=\"md-line md-code-line\" data-line-index=\"{li}\" data-source-start=\"{lineStarts[li]}\">");
            htmlSb.Append(EscapeHtml(effectiveLine));
            htmlSb.Append("</div>");

            // Build position mapping for code line
            var v2s = new List<int>();
            int offset = codeLine.Length - effectiveLine.Length;
            for (int c = 0; c < effectiveLine.Length; c++)
                v2s.Add(lineStarts[li] + offset + c);
            mappings.Add(new LineMapping { SourceStart = lineStarts[li], VisibleToSource = v2s.ToArray() });
        }
        htmlSb.Append("</code></pre>");
    }

    private static void RenderCodeBlockLine(CodeBlock codeBlock, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        // Indented code block
        htmlSb.Append($"<div class=\"md-line md-code-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append(EscapeHtml(line.TrimStart()));
        htmlSb.Append("</div>");

        var trimmed = line.TrimStart();
        var v2s = new List<int>();
        int offset = line.Length - trimmed.Length;
        for (int c = 0; c < trimmed.Length; c++)
            v2s.Add(lineSourceStart + offset + c);
        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = v2s.ToArray() });
    }

    private static void RenderThematicBreakLine(int lineIdx, int lineSourceStart,
        StringBuilder htmlSb, List<LineMapping> mappings)
    {
        htmlSb.Append($"<div class=\"md-line md-hr-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append("<hr class=\"md-hr\" />");
        htmlSb.Append("</div>");

        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = Array.Empty<int>() });
    }

    private static void RenderQuoteLine(QuoteBlock quote, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        // Strip the > prefix
        string stripped = line;
        int prefixLen = 0;
        if (stripped.StartsWith("> "))
        {
            stripped = stripped.Substring(2);
            prefixLen = 2;
        }
        else if (stripped.StartsWith(">"))
        {
            stripped = stripped.Substring(1);
            prefixLen = 1;
        }

        if (string.IsNullOrWhiteSpace(stripped))
        {
            htmlSb.Append($"<div class=\"md-line md-bq-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\"></div>");
            mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = Array.Empty<int>() });
            return;
        }

        var inline = RenderInlineWithMapping(stripped, lineSourceStart + prefixLen);

        htmlSb.Append($"<div class=\"md-line md-bq-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append("<blockquote class=\"md-bq\">");
        htmlSb.Append(inline.Html);
        htmlSb.Append("</blockquote></div>");

        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = inline.VisibleToSource.ToArray() });
    }

    private static void RenderListLine(ListBlock listBlock, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        // Find the list item that contains this line
        ListItemBlock? currentItem = null;
        foreach (var child in listBlock)
        {
            if (child is ListItemBlock item)
            {
                if (item.Line <= lineIdx &&
                    (item.Span.End >= lineSourceStart || item.Span.IsEmpty))
                {
                    currentItem = item;
                }
            }
        }

        // Detect marker type from the line itself
        string strippedLine = line.TrimStart();
        int indent = line.Length - strippedLine.Length;

        if (strippedLine.StartsWith("- ") || strippedLine.StartsWith("* ") || strippedLine.StartsWith("+ "))
        {
            string marker = strippedLine.Substring(0, 2);
            string content = strippedLine.Substring(2);
            int totalPrefix = indent + marker.Length;
            var inline = RenderInlineWithMapping(content, lineSourceStart + totalPrefix);

            htmlSb.Append($"<div class=\"md-line md-li-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
            htmlSb.Append("<span class=\"md-li-marker\" aria-hidden=\"true\"></span>");
            htmlSb.Append(inline.Html);
            htmlSb.Append("</div>");

            mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = inline.VisibleToSource.ToArray() });
            return;
        }

        // Ordered list
        int digitEnd = 0;
        while (digitEnd < strippedLine.Length && char.IsDigit(strippedLine[digitEnd]))
            digitEnd++;

        if (digitEnd > 0 && digitEnd < strippedLine.Length && strippedLine[digitEnd] == '.')
        {
            string olMarker = strippedLine.Substring(0, digitEnd + 1);
            string content = digitEnd + 2 < strippedLine.Length ? strippedLine.Substring(digitEnd + 2) : "";
            int totalPrefix = indent + olMarker.Length + 1; // +1 for space after dot
            var inline = RenderInlineWithMapping(content, lineSourceStart + totalPrefix);

            htmlSb.Append($"<div class=\"md-line md-li-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
            htmlSb.Append($"<span class=\"md-oli-marker\" aria-hidden=\"true\" data-marker=\"{olMarker}\"></span>");
            htmlSb.Append(inline.Html);
            htmlSb.Append("</div>");

            mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = inline.VisibleToSource.ToArray() });
            return;
        }

        // Continuation line in a list item
        var contInline = RenderInlineWithMapping(strippedLine, lineSourceStart + indent);
        htmlSb.Append($"<div class=\"md-line md-li-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append(contInline.Html);
        htmlSb.Append("</div>");
        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = contInline.VisibleToSource.ToArray() });
    }

    private static void RenderParagraphLine(ParagraphBlock para, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        // Normal (top-level) paragraph rendering.
        // ParagraphBlocks inside containers (QuoteBlock / ListItemBlock)
        // are handled directly in the main rendering switch before this
        // method is reached.
        var inline = RenderInlineWithMapping(line, lineSourceStart);

        htmlSb.Append($"<div class=\"md-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append(inline.Html);
        htmlSb.Append("</div>");

        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = inline.VisibleToSource.ToArray() });
    }

    private static void RenderHtmlBlockLine(HtmlBlock htmlBlock, int lineIdx, int lineSourceStart,
        string line, string sourceText, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        htmlSb.Append($"<div class=\"md-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append(EscapeHtml(line));
        htmlSb.Append("</div>");

        var v2s = new List<int>();
        for (int c = 0; c < line.Length; c++)
            v2s.Add(lineSourceStart + c);
        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = v2s.ToArray() });
    }

    private static void RenderFallbackLine(int lineIdx, int lineSourceStart,
        string line, StringBuilder htmlSb, List<LineMapping> mappings)
    {
        var inline = RenderInlineWithMapping(line, lineSourceStart);

        htmlSb.Append($"<div class=\"md-line\" data-line-index=\"{lineIdx}\" data-source-start=\"{lineSourceStart}\">");
        htmlSb.Append(inline.Html);
        htmlSb.Append("</div>");

        mappings.Add(new LineMapping { SourceStart = lineSourceStart, VisibleToSource = inline.VisibleToSource.ToArray() });
    }

    // ── Inline rendering WITH position mapping (Markdig-aware) ──

    /// <summary>
    /// Renders inline markdown to HTML while building a
    /// <see cref="VisibleToSource"/> mapping. Syntax characters are
    /// emitted as HTML tags (strong, em, del, code) but NOT included
    /// in the mapping — only visible content characters are mapped.
    ///
    /// This implementation uses Markdig's AST for inline rendering,
    /// walking ContainerInline trees to produce accurate HTML and
    /// source position mappings.
    /// </summary>
    private static (string Html, List<int> VisibleToSource) RenderInlineWithMapping(
        string text, int basePos)
    {
        if (string.IsNullOrEmpty(text))
            return ("", new List<int>());

        // Parse the inline text with Markdig to get accurate AST
        var inlineDoc = Markdown.Parse(text, _pipeline);

        // Find the first paragraph's inline container
        ContainerInline? inlineRoot = null;
        foreach (var block in inlineDoc)
        {
            if (block is ParagraphBlock para && para.Inline != null)
            {
                inlineRoot = para.Inline;
                break;
            }
        }

        if (inlineRoot == null)
        {
            // No inline parsed — just escape and map as plain text
            var v2s = new List<int>();
            for (int i = 0; i < text.Length; i++)
                v2s.Add(basePos + i);
            return (EscapeHtml(text), v2s);
        }

        var htmlSb = new StringBuilder();
        var visToSrc = new List<int>();

        RenderInlineContainer(inlineRoot, text, basePos, htmlSb, visToSrc);

        return (htmlSb.ToString(), visToSrc);
    }

    /// <summary>
    /// Recursively render a ContainerInline tree, mapping visible
    /// characters to their source positions.
    /// </summary>
    private static void RenderInlineContainer(ContainerInline container, string sourceText,
        int basePos, StringBuilder html, List<int> visToSrc)
    {
        var child = container.FirstChild;
        while (child != null)
        {
            switch (child)
            {
                case LiteralInline literal:
                    RenderLiteral(literal, sourceText, basePos, html, visToSrc);
                    break;

                case EmphasisInline emphasis:
                    RenderEmphasis(emphasis, sourceText, basePos, html, visToSrc);
                    break;

                case CodeInline codeInline:
                    RenderCodeInline(codeInline, sourceText, basePos, html, visToSrc);
                    break;

                case LinkInline linkInline:
                    RenderLinkInline(linkInline, sourceText, basePos, html, visToSrc);
                    break;

                case AutolinkInline autolink:
                    RenderAutolinkInline(autolink, sourceText, basePos, html, visToSrc);
                    break;

                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                        html.Append("<br />");
                    html.Append('\n');
                    break;

                case HtmlInline htmlInline:
                    html.Append(EscapeHtml(htmlInline.Tag));
                    if (!htmlInline.Span.IsEmpty)
                    {
                        for (int i = htmlInline.Span.Start; i <= htmlInline.Span.End && i < sourceText.Length; i++)
                            visToSrc.Add(basePos + i);
                    }
                    break;

                case HtmlEntityInline entity:
                    html.Append(entity.Transcoded);
                    // Map the transcoded characters
                    if (!entity.Span.IsEmpty)
                    {
                        for (int i = 0; i < entity.Transcoded.Length; i++)
                            visToSrc.Add(basePos + entity.Span.Start + i);
                    }
                    break;

                case DelimiterInline delimiter:
                    // Delimiters are structural nodes; their content is
                    // rendered through the emphasis/link handling above.
                    // Skip the delimiter marker itself in the mapping.
                    break;

                default:
                    // For unknown inlines, try to render children if container
                    if (child is ContainerInline ci)
                        RenderInlineContainer(ci, sourceText, basePos, html, visToSrc);
                    break;
            }

            child = child.NextSibling;
        }
    }

    private static void RenderLiteral(LiteralInline literal, string sourceText,
        int basePos, StringBuilder html, List<int> visToSrc)
    {
        var slice = literal.Content;
        if (slice.Length == 0) return;

        string text = slice.ToString();
        html.Append(EscapeHtml(text));

        // Map visible characters to source positions using the literal's SourceSpan
        if (!literal.Span.IsEmpty)
        {
            int spanStart = literal.Span.Start;
            int spanEnd = literal.Span.End;
            for (int i = spanStart; i <= spanEnd && i < sourceText.Length; i++)
            {
                visToSrc.Add(basePos + i);
            }
        }
        else
        {
            // Fallback: use slice offset
            int offset = slice.Start;
            for (int i = 0; i < text.Length; i++)
            {
                visToSrc.Add(basePos + offset + i);
            }
        }
    }

    private static void RenderEmphasis(EmphasisInline emphasis, string sourceText,
        int basePos, StringBuilder html, List<int> visToSrc)
    {
        bool isBold = emphasis.DelimiterCount >= 2;
        bool isItalic = !isBold || emphasis.DelimiterCount == 3 || emphasis.DelimiterCount == 1;
        bool isStrikethrough = emphasis.DelimiterChar == '~';

        // The delimiter characters are syntax — skip them in the mapping
        // but we still need to account for their source position offset

        if (isStrikethrough)
            html.Append("<del>");
        else if (isBold && isItalic)
            html.Append("<strong><em>");
        else if (isBold)
            html.Append("<strong>");
        else
            html.Append("<em>");

        // Render children — the visible content
        RenderInlineContainer(emphasis, sourceText, basePos, html, visToSrc);

        if (isStrikethrough)
            html.Append("</del>");
        else if (isBold && isItalic)
            html.Append("</em></strong>");
        else if (isBold)
            html.Append("</strong>");
        else
            html.Append("</em>");
    }

    private static void RenderCodeInline(CodeInline codeInline, string sourceText,
        int basePos, StringBuilder html, List<int> visToSrc)
    {
        html.Append("<code class=\"md-inline-code\">");
        html.Append(EscapeHtml(codeInline.Content));
        html.Append("</code>");

        // Map code content characters to source positions
        if (!codeInline.Span.IsEmpty)
        {
            // The span includes the backtick delimiters
            int contentStart = codeInline.Span.Start + codeInline.DelimiterCount;
            int contentEnd = codeInline.Span.End - codeInline.DelimiterCount + 1;
            for (int i = contentStart; i < contentEnd; i++)
            {
                visToSrc.Add(basePos + i);
            }
        }
    }

    private static void RenderLinkInline(LinkInline linkInline, string sourceText,
        int basePos, StringBuilder html, List<int> visToSrc)
    {
        if (linkInline.IsImage)
        {
            // Image: ![alt](url)
            string alt = GetLinkText(linkInline);
            string url = linkInline.Url ?? "";
            string title = linkInline.Title ?? "";

            html.Append($"<img src=\"{EscapeHtml(url)}\" alt=\"{EscapeHtml(alt)}\" class=\"md-img\" />");

            // Map alt text characters
            if (!linkInline.LabelSpan.IsEmpty)
            {
                for (int i = linkInline.LabelSpan.Start; i <= linkInline.LabelSpan.End; i++)
                    visToSrc.Add(basePos + i);
            }
        }
        else
        {
            // Link: [text](url)
            string url = linkInline.Url ?? "";

            html.Append($"<a href=\"{EscapeHtml(url)}\" class=\"md-link\" target=\"_blank\" rel=\"noopener\">");

            // Render link text children
            RenderInlineContainer(linkInline, sourceText, basePos, html, visToSrc);

            html.Append("</a>");
        }
    }

    private static void RenderAutolinkInline(AutolinkInline autolink, string sourceText,
        int basePos, StringBuilder html, List<int> visToSrc)
    {
        html.Append($"<a href=\"{EscapeHtml(autolink.Url)}\" class=\"md-link\" target=\"_blank\" rel=\"noopener\">");
        html.Append(EscapeHtml(autolink.Url));
        html.Append("</a>");

        // Map visible URL characters
        if (!autolink.Span.IsEmpty)
        {
            // Skip the < and > delimiters
            for (int i = autolink.Span.Start + 1; i < autolink.Span.End; i++)
                visToSrc.Add(basePos + i);
        }
    }

    private static string GetLinkText(LinkInline linkInline)
    {
        var sb = new StringBuilder();
        var child = linkInline.FirstChild;
        while (child != null)
        {
            if (child is LiteralInline lit)
                sb.Append(lit.Content.ToString());
            child = child.NextSibling;
        }
        return sb.ToString();
    }

    // ── HTML helpers ───────────────────────────────────────────

    private static string EscapeHtml(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(EscapeHtmlChar(c));
        return sb.ToString();
    }

    private static string EscapeHtmlChar(char c) => c switch
    {
        '&'  => "&amp;",
        '<'  => "&lt;",
        '>'  => "&gt;",
        '"'  => "&quot;",
        '\'' => "&#39;",
        _    => c.ToString(),
    };
}
