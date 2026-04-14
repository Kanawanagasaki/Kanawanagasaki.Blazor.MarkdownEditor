using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// Parses a markdown string into an <see cref="EditorDocument"/>
/// using Markdig for the initial parse, then walking Markdig's AST
/// to build our custom flat-segment representation.
///
/// Markdig is ONLY used here for parsing. All editing operations
/// work on the custom <see cref="EditorDocument"/>.
///
/// KEY: After collecting inlines from a Markdig block, if the content
/// contains \n characters, we split into separate EditorParagraphBlocks
/// per line. This ensures multi-line selections work correctly.
/// </summary>
public static class EditorDocumentParser
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .EnableTrackTrivia()
        .Build();

    /// <summary>Parse a markdown string into an EditorDocument.</summary>
    public static EditorDocument Parse(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new EditorDocument();

        var doc = Markdown.Parse(markdown, _pipeline);
        var result = new EditorDocument();

        foreach (var block in doc)
        {
            ProcessBlock(block, result.Blocks, markdown);
        }

        return result;
    }

    private static void ProcessBlock(Block block, List<EditorBlock> output, string sourceText)
    {
        switch (block)
        {
            case HeadingBlock heading:
                ProcessHeading(heading, output, sourceText);
                break;

            case ParagraphBlock para:
                ProcessParagraph(para, output, sourceText);
                break;

            case QuoteBlock quote:
                ProcessBlockquote(quote, output, sourceText);
                break;

            case ListBlock list:
                ProcessList(list, output, sourceText);
                break;

            case FencedCodeBlock fenced:
                output.Add(ProcessFencedCode(fenced, sourceText));
                break;

            case CodeBlock code:
                output.Add(ProcessCodeBlock(code, sourceText));
                break;

            case ThematicBreakBlock:
                output.Add(new EditorThematicBreakBlock());
                break;

            case HtmlBlock html:
                output.Add(ProcessHtmlBlock(html, sourceText));
                break;

            default:
                if (!block.Span.IsEmpty)
                {
                    var rawText = sourceText.Substring(block.Span.Start, block.Span.Length);
                    var lines = rawText.Split('\n');
                    foreach (var line in lines)
                    {
                        output.Add(new EditorParagraphBlock { Segments = { new EditorInlineSegment(line) } });
                    }
                }
                break;
        }
    }

    private static void ProcessHeading(HeadingBlock heading, List<EditorBlock> output, string sourceText)
    {
        var segments = new List<EditorInlineSegment>();
        if (heading.Inline != null)
        {
            CollectInlines(heading.Inline, segments, sourceText);
        }
        EditorDocument.NormalizeSegments(segments);

        var splitBlocks = SplitSegmentsAtNewlines(segments);
        if (splitBlocks.Count == 1)
        {
            var result = new EditorHeadingBlock { Level = heading.Level };
            result.Segments.AddRange(splitBlocks[0]);
            output.Add(result);
        }
        else
        {
            var headingResult = new EditorHeadingBlock { Level = heading.Level };
            headingResult.Segments.AddRange(splitBlocks[0]);
            output.Add(headingResult);
            for (int i = 1; i < splitBlocks.Count; i++)
            {
                var paraResult = new EditorParagraphBlock();
                paraResult.Segments.AddRange(splitBlocks[i]);
                output.Add(paraResult);
            }
        }
    }

    private static void ProcessParagraph(ParagraphBlock para, List<EditorBlock> output, string sourceText)
    {
        var segments = new List<EditorInlineSegment>();
        if (para.Inline != null)
        {
            CollectInlines(para.Inline, segments, sourceText);
        }
        EditorDocument.NormalizeSegments(segments);

        var splitBlocks = SplitSegmentsAtNewlines(segments);
        foreach (var blockSegments in splitBlocks)
        {
            var result = new EditorParagraphBlock();
            result.Segments.AddRange(blockSegments);
            output.Add(result);
        }
    }

    private static void ProcessBlockquote(QuoteBlock quote, List<EditorBlock> output, string sourceText)
    {
        var segments = new List<EditorInlineSegment>();
        foreach (var subBlock in quote)
        {
            if (subBlock is ParagraphBlock para && para.Inline != null)
            {
                CollectInlines(para.Inline, segments, sourceText);
            }
        }
        EditorDocument.NormalizeSegments(segments);

        var splitBlocks = SplitSegmentsAtNewlines(segments);
        foreach (var blockSegments in splitBlocks)
        {
            var result = new EditorBlockquoteBlock();
            result.Segments.AddRange(blockSegments);
            output.Add(result);
        }
    }

    private static void ProcessList(ListBlock list, List<EditorBlock> output, string sourceText)
    {
        bool isOrdered = list.BulletType == '1' || list.BulletType == '0';
        int itemNumber = 1;

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var segments = new List<EditorInlineSegment>();
                foreach (var subBlock in listItem)
                {
                    if (subBlock is ParagraphBlock para && para.Inline != null)
                    {
                        CollectInlines(para.Inline, segments, sourceText);
                    }
                }
                EditorDocument.NormalizeSegments(segments);

                var splitBlocks = SplitSegmentsAtNewlines(segments);
                for (int i = 0; i < splitBlocks.Count; i++)
                {
                    var resultBlock = isOrdered
                        ? (EditorBlock)new EditorOrderedListItemBlock { Number = itemNumber++ }
                        : new EditorUnorderedListItemBlock();
                    resultBlock.Segments.AddRange(splitBlocks[i]);
                    output.Add(resultBlock);
                }
            }
        }
    }

    /// <summary>
    /// Split segments at \n characters. Each group of segments between newlines
    /// becomes a separate list of segments. The newline character itself is NOT included.
    /// </summary>
    private static List<List<EditorInlineSegment>> SplitSegmentsAtNewlines(List<EditorInlineSegment> segments)
    {
        var result = new List<List<EditorInlineSegment>>();
        var current = new List<EditorInlineSegment>();

        foreach (var seg in segments)
        {
            if (!seg.Text.Contains('\n'))
            {
                current.Add(new EditorInlineSegment(seg.Text, seg.Styles));
                continue;
            }

            var parts = seg.Text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    result.Add(current);
                    current = new List<EditorInlineSegment>();
                }

                if (!string.IsNullOrEmpty(parts[i]))
                {
                    current.Add(new EditorInlineSegment(parts[i], seg.Styles));
                }
            }
        }

        if (current.Count > 0)
        {
            result.Add(current);
        }

        if (result.Count == 0)
        {
            result.Add(new List<EditorInlineSegment>());
        }

        return result;
    }

    private static EditorBlock ProcessFencedCode(FencedCodeBlock fenced, string sourceText)
    {
        var lines = sourceText.Split('\n');
        var lineStarts = ComputeLineStarts(lines);

        var result = new EditorFencedCodeBlock
        {
            Language = fenced.Info ?? ""
        };

        int startLine = fenced.Line + 1;
        int closingLine = FindClosingFenceLine(fenced, sourceText, lines, lineStarts);
        int endLine = closingLine > 0 ? closingLine - 1 : lines.Length - 1;

        var contentLines = new List<string>();
        for (int li = startLine; li <= endLine; li++)
        {
            if (li >= 0 && li < lines.Length)
            {
                string line = lines[li];
                int indent = fenced.IndentCount;
                if (indent > 0 && line.Length >= indent)
                    line = line.Substring(indent);
                contentLines.Add(line);
            }
        }
        result.Content = string.Join("\n", contentLines);
        return result;
    }

    private static int FindClosingFenceLine(FencedCodeBlock fenced, string sourceText,
        string[] lines, int[] lineStarts)
    {
        for (int i = fenced.Line + 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            int fenceCount = 0;
            char fenceChar = fenced.FencedChar;
            foreach (char c in trimmed)
            {
                if (c == fenceChar) fenceCount++;
                else break;
            }
            if (fenceCount >= 3 && fenceCount >= fenced.OpeningFencedCharCount)
            {
                string rest = trimmed.Substring(fenceCount).TrimEnd();
                if (string.IsNullOrEmpty(rest))
                    return i;
            }
        }
        return -1;
    }

    private static EditorBlock ProcessCodeBlock(CodeBlock code, string sourceText)
    {
        var rawText = code.Span.IsEmpty ? "" : sourceText.Substring(code.Span.Start, code.Span.Length);
        return new EditorFencedCodeBlock { Content = rawText, Language = "" };
    }

    private static EditorBlock ProcessHtmlBlock(HtmlBlock html, string sourceText)
    {
        var rawText = html.Span.IsEmpty ? "" : sourceText.Substring(html.Span.Start, html.Span.Length);
        return new EditorHtmlBlock { Content = rawText };
    }

    // ── Inline collection: Markdig AST → flat EditorInlineSegments ──

    private static void CollectInlines(ContainerInline container, List<EditorInlineSegment> segments,
        string sourceText, InlineStyle parentStyles = InlineStyle.None)
    {
        var child = container.FirstChild;
        while (child != null)
        {
            switch (child)
            {
                case LiteralInline literal:
                    string text = literal.Content.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        if (segments.Count > 0 && segments[segments.Count - 1].Styles == parentStyles)
                        {
                            segments[segments.Count - 1].Text += text;
                        }
                        else
                        {
                            segments.Add(new EditorInlineSegment(text, parentStyles));
                        }
                    }
                    break;

                case EmphasisInline emphasis:
                    InlineStyle emphasisStyle = ClassifyEmphasis(emphasis);
                    CollectInlines(emphasis, segments, sourceText, parentStyles | emphasisStyle);
                    break;

                case CodeInline code:
                    string codeText = code.Content;
                    if (!string.IsNullOrEmpty(codeText))
                    {
                        segments.Add(new EditorInlineSegment(codeText, parentStyles | InlineStyle.InlineCode));
                    }
                    break;

                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        segments.Add(new EditorInlineSegment("  ", parentStyles));
                    }
                    segments.Add(new EditorInlineSegment("\n", parentStyles));
                    break;

                case LinkInline link:
                    CollectLinkInlines(link, segments, sourceText, parentStyles);
                    break;

                case AutolinkInline autolink:
                    segments.Add(new EditorInlineSegment(autolink.Url, parentStyles));
                    break;

                case HtmlInline htmlInline:
                    segments.Add(new EditorInlineSegment(htmlInline.Tag, parentStyles));
                    break;

                case HtmlEntityInline entity:
                    segments.Add(new EditorInlineSegment(entity.Transcoded.ToString(), parentStyles));
                    break;

                case DelimiterInline delimiter:
                    if (delimiter is ContainerInline ci)
                        CollectInlines(ci, segments, sourceText, parentStyles);
                    break;

                default:
                    if (child is ContainerInline containerChild)
                        CollectInlines(containerChild, segments, sourceText, parentStyles);
                    break;
            }

            child = child.NextSibling;
        }
    }

    private static void CollectLinkInlines(LinkInline link, List<EditorInlineSegment> segments,
        string sourceText, InlineStyle parentStyles)
    {
        if (link.FirstChild != null)
        {
            CollectInlines(link, segments, sourceText, parentStyles);
        }
    }

    private static InlineStyle ClassifyEmphasis(EmphasisInline emphasis)
    {
        if (emphasis.DelimiterChar == '~')
            return InlineStyle.Strikethrough;

        if (emphasis.DelimiterCount >= 3)
            return InlineStyle.Bold | InlineStyle.Italic;

        if (emphasis.DelimiterCount == 2)
            return InlineStyle.Bold;

        return InlineStyle.Italic;
    }

    private static int[] ComputeLineStarts(string[] lines)
    {
        var starts = new int[lines.Length];
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            starts[i] = pos;
            pos += lines[i].Length + 1;
        }
        return starts;
    }
}
