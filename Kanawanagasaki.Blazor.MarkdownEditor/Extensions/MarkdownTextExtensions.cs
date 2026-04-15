using Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;
using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Markdig;
using Markdig.Syntax;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Extensions;

/// <summary>
/// Provides methods for inserting / toggling Markdown syntax around a
/// text selection. Every method is pure: it receives the full text and
/// selection coordinates and returns the new text plus updated cursor
/// positions.
///
/// <b>Architecture:</b> A custom <see cref="EditorDocument"/> AST is the
/// SINGLE SOURCE OF TRUTH. All inline style changes (bold, italic,
/// strikethrough, code) are applied by MUTATING the AST directly
/// (toggling style flags on <see cref="EditorInlineSegment"/> nodes),
/// and the markdown text is then EXTRACTED from the mutated AST via
/// <see cref="EditorDocumentRenderer"/>.
/// </summary>
public static class MarkdownTextExtensions
{
    private static readonly MarkdownPipeline _detectionPipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build();

    // ── Inline style detection (AST-based) ────────────────────────────

    /// <summary>
    /// Detect which inline styles are active at the given source offset
    /// by parsing with Markdig and walking the AST.
    /// </summary>
    public static InlineStyle DetectInlineStyles(string text, int sourceOffset)
    {
        if (string.IsNullOrEmpty(text)) return InlineStyle.None;

        var doc = Markdig.Markdown.Parse(text, _detectionPipeline);
        var styles = InlineStyle.None;

        foreach (var inline in doc.Descendants<Markdig.Syntax.Inlines.Inline>())
        {
            if (inline.Span.IsEmpty) continue;
            if (sourceOffset >= inline.Span.Start && sourceOffset <= inline.Span.End)
            {
                switch (inline)
                {
                    case Markdig.Syntax.Inlines.EmphasisInline emphasis:
                        if (emphasis.DelimiterChar == '~')
                            styles |= InlineStyle.Strikethrough;
                        else if (emphasis.DelimiterCount >= 2)
                            styles |= InlineStyle.Bold;
                        else
                            styles |= InlineStyle.Italic;
                        break;
                    case Markdig.Syntax.Inlines.CodeInline:
                        styles |= InlineStyle.InlineCode;
                        break;
                }
            }
        }

        return styles;
    }

    // ── Inline toggle (bold, italic, strikethrough, code) ─────────────

    public static TextEditResult ToggleBold(string text, int start, int end)
        => ToggleInline(text, start, end, InlineStyle.Bold);

    public static TextEditResult ToggleItalic(string text, int start, int end)
        => ToggleInline(text, start, end, InlineStyle.Italic);

    public static TextEditResult ToggleStrikethrough(string text, int start, int end)
        => ToggleInline(text, start, end, InlineStyle.Strikethrough);

    public static TextEditResult ToggleInlineCode(string text, int start, int end)
        => ToggleInline(text, start, end, InlineStyle.InlineCode);

    // ── Core inline toggle (AST-driven) ───────────────────────────────

    private static TextEditResult ToggleInline(string text, int selStart, int selEnd, InlineStyle styleToToggle)
    {
        // Empty selection → insert empty markers via string manipulation
        if (selStart == selEnd)
        {
            string marker = EditorInlineSegment.StyleToMarker(styleToToggle);
            string result = text.Substring(0, selStart) + marker + marker + text.Substring(selEnd);
            return new TextEditResult
            {
                Text = result,
                SelectionStart = selStart + marker.Length,
                SelectionEnd = selStart + marker.Length,
            };
        }

        // Parse text → EditorDocument
        var doc = EditorDocumentParser.Parse(text);

        // Normalize whitespace at style boundaries in all blocks.
        // When Markdig re-parses markdown like "*** *four*", the space between
        // markers may be included inside the styled segment. Splitting it ensures
        // correct delta rendering and position mapping.
        foreach (var block in doc.Blocks)
        {
            if (block.HasInlineContent && block.Segments.Count > 0)
            {
                EditorDocumentEditor.SplitWhitespaceAtStyleBoundaries(block.Segments);
                EditorDocument.NormalizeSegments(block.Segments);
            }
        }

        // Map selection → AST positions (using delta renderer for correct positions)
        var (startBlock, startOffset) = EditorDocumentEditor.MapPositionToContent(doc, selStart);
        var (endBlock, endOffset) = EditorDocumentEditor.MapPositionToContent(doc, selEnd);

        if (startBlock != endBlock)
        {
            return ToggleInlineMultiBlock(doc, styleToToggle, startBlock, startOffset, endBlock, endOffset);
        }

        return ToggleInlineSingleBlock(doc, styleToToggle, startBlock, startOffset, endOffset);
    }

    private static TextEditResult ToggleInlineSingleBlock(EditorDocument doc,
        InlineStyle styleToToggle, int blockIndex, int contentStart, int contentEnd)
    {
        var block = doc.Blocks[blockIndex];
        if (!block.HasInlineContent || block.Segments.Count == 0)
        {
            // No inline content — fallback to wrapping with markers on the rendered markdown
            string oldMd = EditorDocumentRenderer.Render(doc);
            string marker = EditorInlineSegment.StyleToMarker(styleToToggle);

            // Find the block's position in the rendered markdown
            int blockMdStart = GetBlockMdStart(doc, blockIndex);
            int prefixLen = EditorDocumentRenderer.GetBlockPrefixLength(block);
            int contentMdStart = blockMdStart + prefixLen;
            int contentMdLen = GetContentTextLength(block);
            string inner = oldMd.Substring(contentMdStart, contentMdLen);
            string newMd = oldMd.Substring(0, contentMdStart) + marker + inner + marker + oldMd.Substring(contentMdStart + contentMdLen);
            return new TextEditResult
            {
                Text = newMd,
                SelectionStart = contentMdStart + marker.Length,
                SelectionEnd = contentMdStart + marker.Length + inner.Length,
            };
        }

        bool styleActive = IsStyleActiveOnRange(block.Segments, contentStart, contentEnd, styleToToggle);

        if (styleActive)
        {
            // Expand to full marker region: when toggling OFF, remove the style
            // from ALL segments in the contiguous styled region, not just the selection.
            // This matches user expectation: select part of a bold word → entire word loses bold.
            var expandedRange = ExpandToMarkerRegion(block.Segments, contentStart, contentEnd, styleToToggle);
            RemoveStyleFromRange(block.Segments, expandedRange.Start, expandedRange.End, styleToToggle);
            // Update content range to expanded range for selection computation
            contentStart = expandedRange.Start;
            contentEnd = expandedRange.End;
        }
        else
        {
            AddStyleToRange(block.Segments, contentStart, contentEnd, styleToToggle);
        }

        // Split whitespace at style boundaries for correct rendering of overlapping styles
        EditorDocumentEditor.SplitWhitespaceAtStyleBoundaries(block.Segments);
        EditorDocument.NormalizeSegments(block.Segments);
        
        string renderedMd = EditorDocumentRenderer.Render(doc);

        // Compute content-based selection positions for single-block (using delta renderer)
        int newSelStart = EditorDocumentEditor.MapContentToPosition(doc, blockIndex, contentStart);
        int newSelEnd = EditorDocumentEditor.MapContentToPosition(doc, blockIndex, contentEnd);

        return new TextEditResult
        {
            Text = renderedMd,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

    private static TextEditResult ToggleInlineMultiBlock(EditorDocument doc,
        InlineStyle styleToToggle, int startBlock, int startOffset, int endBlock, int endOffset)
    {
        // Determine if the style is currently active (check first block)
        bool styleActive = false;
        if (startBlock < doc.Blocks.Count && doc.Blocks[startBlock].HasInlineContent && doc.Blocks[startBlock].Segments.Count > 0)
        {
            styleActive = IsStyleActiveOnRange(doc.Blocks[startBlock].Segments, startOffset,
                GetContentLength(doc.Blocks[startBlock].Segments), styleToToggle);
        }

        // Toggle on each block
        for (int i = startBlock; i <= endBlock && i < doc.Blocks.Count; i++)
        {
            var block = doc.Blocks[i];
            if (!block.HasInlineContent || block.Segments.Count == 0) continue;

            int segStart = (i == startBlock) ? startOffset : 0;
            int segEnd = (i == endBlock) ? endOffset : GetContentLength(block.Segments);

            if (styleActive)
            {
                // Expand to marker region for toggle OFF
                var expanded = ExpandToMarkerRegion(block.Segments, segStart, segEnd, styleToToggle);
                RemoveStyleFromRange(block.Segments, expanded.Start, expanded.End, styleToToggle);
            }
            else
                AddStyleToRange(block.Segments, segStart, segEnd, styleToToggle);

            EditorDocumentEditor.SplitWhitespaceAtStyleBoundaries(block.Segments);
            EditorDocument.NormalizeSegments(block.Segments);
        }

        string renderedMd = EditorDocumentRenderer.Render(doc);

        // Compute selection positions that include markers for multi-block
        var (newSelStart, newSelEnd) = ComputeSelectionWithMarkers(doc, startBlock, startOffset, endBlock, endOffset);

        return new TextEditResult
        {
            Text = renderedMd,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

    // ── Position mapping ────────────────────────────────────────────

    /// <summary>
    /// Map a (blockIndex, contentCharOffset) to an absolute position in the
    /// rendered markdown. The returned position is at the CONTENT character
    /// (after the opening markers of the containing segment).
    /// </summary>
    private static int MapContentToMdPosition(EditorDocument doc, int blockIndex, int contentOffset)
    {
        int pos = 0;

        for (int i = 0; i < blockIndex && i < doc.Blocks.Count; i++)
        {
            if (i > 0) pos++;
            pos += EditorDocumentRenderer.GetBlockMarkdownLength(doc.Blocks[i]);
        }
        if (blockIndex > 0) pos++;

        if (blockIndex >= doc.Blocks.Count) return pos;

        int prefixLen = EditorDocumentRenderer.GetBlockPrefixLength(doc.Blocks[blockIndex]);
        pos += prefixLen;

        int remaining = contentOffset;
        foreach (var seg in doc.Blocks[blockIndex].Segments)
        {
            if (remaining <= seg.Text.Length)
            {
                pos += seg.GetMarkerPrefix().Length + remaining;
                return pos;
            }
            remaining -= seg.Text.Length;
            pos += seg.MarkdownLength;
        }

        return pos;
    }

    /// <summary>
    /// Map an absolute position in the rendered markdown to a (blockIndex, contentCharOffset).
    /// Content offset is the character position within the block's content text,
    /// not counting block prefix or inline markers.
    /// </summary>
    private static (int BlockIndex, int ContentOffset) MapPositionToContent(
        EditorDocument doc, int mdPosition)
    {
        string md = EditorDocumentRenderer.Render(doc);
        if (string.IsNullOrEmpty(md)) return (0, 0);

        mdPosition = Math.Clamp(mdPosition, 0, md.Length);

        int pos = 0;
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            if (i > 0) pos++; // newline between blocks

            int prefixLen = EditorDocumentRenderer.GetBlockPrefixLength(doc.Blocks[i]);
            int blockContentStart = pos + prefixLen;
            int blockLen = EditorDocumentRenderer.GetBlockMarkdownLength(doc.Blocks[i]);
            int blockEnd = pos + blockLen;

            if (mdPosition <= blockEnd || i == doc.Blocks.Count - 1)
            {
                int curPos = blockContentStart;
                var segments = doc.Blocks[i].Segments;
                int contentOffset = 0;

                foreach (var seg in segments)
                {
                    int segPrefixLen = seg.GetMarkerPrefix().Length;
                    int segContentStart = curPos + segPrefixLen;
                    int segContentEnd = segContentStart + seg.Text.Length;

                    if (mdPosition <= segContentEnd)
                    {
                        contentOffset += Math.Max(0, Math.Min(mdPosition - segContentStart, seg.Text.Length));
                        return (i, contentOffset);
                    }

                    contentOffset += seg.Text.Length;
                    curPos += seg.MarkdownLength;
                }

                return (i, contentOffset);
            }

            pos = blockEnd;
        }

        return (0, 0);
    }

    /// <summary>
    /// Get the start position of a block in the rendered markdown.
    /// </summary>
    private static int GetBlockMdStart(EditorDocument doc, int blockIndex)
    {
        int pos = 0;
        for (int i = 0; i < blockIndex && i < doc.Blocks.Count; i++)
        {
            if (i > 0) pos++;
            pos += EditorDocumentRenderer.GetBlockMarkdownLength(doc.Blocks[i]);
        }
        if (blockIndex > 0) pos++;
        return pos;
    }

    /// <summary>
    /// Compute selection positions in the rendered markdown that include
    /// the markers around the specified content range.
    /// For the start: position at the marker prefix start of the first overlapping segment.
    /// For the end: position just after the marker suffix end of the last overlapping segment.
    /// This ensures the selection covers the entire styled region so subsequent
    /// toggles can detect the existing style.
    /// </summary>
    private static (int SelectionStart, int SelectionEnd) ComputeSelectionWithMarkers(
        EditorDocument doc, int startBlock, int contentStart, int endBlock, int contentEnd)
    {
        int selStart = -1;
        int selEnd = -1;
        int blockPos = 0;

        for (int bi = 0; bi < doc.Blocks.Count; bi++)
        {
            if (bi > 0) blockPos++; // newline

            var block = doc.Blocks[bi];
            int prefixLen = EditorDocumentRenderer.GetBlockPrefixLength(block);
            int segMdPos = blockPos + prefixLen; // markdown position of current segment start

            int contentPos = 0; // content offset within this block

            // The content range for this block
            int rangeStart = (bi == startBlock) ? contentStart : 0;
            int rangeEnd = (bi == endBlock) ? contentEnd : GetContentLength(block.Segments);

            foreach (var seg in block.Segments)
            {
                int segContentStart = contentPos;
                int segContentEnd = contentPos + seg.Text.Length;

                // Check if this segment overlaps with the selection range for this block
                bool overlaps = segContentEnd > rangeStart && segContentStart < rangeEnd;

                if (overlaps)
                {
                    if (selStart == -1)
                    {
                        // First overlapping segment — selection starts at its marker prefix
                        selStart = segMdPos;
                    }
                    // Selection end always extends to after this segment's full markdown rendering
                    selEnd = segMdPos + seg.MarkdownLength;
                }

                contentPos += seg.Text.Length;
                segMdPos += seg.MarkdownLength;
            }

            blockPos += EditorDocumentRenderer.GetBlockMarkdownLength(block);
        }

        if (selStart == -1) selStart = 0;
        if (selEnd == -1) selEnd = 0;

        return (selStart, selEnd);
    }

    /// <summary>
    /// Overload for single-block selection.
    /// </summary>
    private static (int SelectionStart, int SelectionEnd) ComputeSelectionWithMarkers(
        EditorDocument doc, int blockIndex, int contentStart, int contentEnd)
    {
        return ComputeSelectionWithMarkers(doc, blockIndex, contentStart, blockIndex, contentEnd);
    }

    /// <summary>Get total content text length (no markers) of a segment list.</summary>
    private static int GetContentLength(List<EditorInlineSegment> segments)
    {
        int len = 0;
        foreach (var seg in segments) len += seg.Text.Length;
        return len;
    }

    /// <summary>Get total content text length (no markers, no prefix) of a block.</summary>
    private static int GetContentTextLength(EditorBlock block)
    {
        int len = 0;
        foreach (var seg in block.Segments) len += seg.Text.Length;
        return len;
    }

    // ── Segment manipulation ───────────────────────────────────────────

    /// <summary>
    /// Expand the content range to cover the full marker region for a style.
    /// When toggling OFF, if any part of the selection is within a styled segment,
    /// expand to include ALL contiguous segments with that style.
    /// This ensures: selecting "wor" in ~~***`hello world`***~~ and toggling bold
    /// removes bold from the entire "hello world" region.
    /// </summary>
    private static (int Start, int End) ExpandToMarkerRegion(
        List<EditorInlineSegment> segments, int contentStart, int contentEnd, InlineStyle style)
    {
        // Find the first segment that overlaps with the range and has the style
        int expandedStart = contentStart;
        int expandedEnd = contentEnd;

        int pos = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            int segStart = pos;
            int segEnd = pos + seg.Text.Length;

            if (segEnd > contentStart && segStart < contentEnd && (seg.Styles & style) != 0)
            {
                // This segment overlaps with the range and has the style
                // Expand to include this segment's full extent
                expandedStart = Math.Min(expandedStart, segStart);
                expandedEnd = Math.Max(expandedEnd, segEnd);
            }

            pos += seg.Text.Length;
        }

        // Now expand further to include any adjacent segments with the same style
        // that form a contiguous styled region
        bool changed = true;
        while (changed)
        {
            changed = false;
            pos = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                int segStart = pos;
                int segEnd = pos + seg.Text.Length;

                if ((seg.Styles & style) != 0)
                {
                    // This segment has the style. If it's adjacent to our expanded range, include it.
                    if (segStart <= expandedEnd && segEnd >= expandedStart)
                    {
                        if (segStart < expandedStart) { expandedStart = segStart; changed = true; }
                        if (segEnd > expandedEnd) { expandedEnd = segEnd; changed = true; }
                    }
                }

                pos += seg.Text.Length;
            }
        }

        return (expandedStart, expandedEnd);
    }

    /// <summary>
    /// Check if a style is "active" on the given content range.
    /// A style is considered active (should be toggled OFF) only if ALL
    /// non-whitespace segments in the range have it. If only SOME non-whitespace
    /// segments have the style, it's considered inactive (should be toggled ON
    /// to expand coverage to the full selection).
    /// Whitespace-only segments are ignored.
    /// </summary>
    private static bool IsStyleActiveOnRange(List<EditorInlineSegment> segments,
        int contentStart, int contentEnd, InlineStyle style)
    {
        int pos = 0;
        bool anyNonWsInRange = false;
        bool allNonWsHaveStyle = true;

        foreach (var seg in segments)
        {
            int segStart = pos;
            int segEnd = pos + seg.Text.Length;
            if (segEnd > contentStart && segStart < contentEnd)
            {
                bool isWsOnly = seg.Text.Trim().Length == 0;
                if (!isWsOnly)
                {
                    anyNonWsInRange = true;
                    if ((seg.Styles & style) == 0)
                        allNonWsHaveStyle = false;
                }
            }
            pos += seg.Text.Length;
        }

        // Only consider the style "active" (for toggle-off) if ALL non-WS content has it
        return anyNonWsInRange && allNonWsHaveStyle;
    }

    private static void AddStyleToRange(List<EditorInlineSegment> segments,
        int contentStart, int contentEnd, InlineStyle style)
    {
        SplitSegmentsAtBoundaries(segments, contentStart, contentEnd);
        int pos = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            int segStart = pos;
            int segEnd = pos + seg.Text.Length;
            if (segEnd > contentStart && segStart < contentEnd)
                seg.Styles |= style;
            pos += seg.Text.Length;
        }
    }

    private static void RemoveStyleFromRange(List<EditorInlineSegment> segments,
        int contentStart, int contentEnd, InlineStyle style)
    {
        SplitSegmentsAtBoundaries(segments, contentStart, contentEnd);
        int pos = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            int segStart = pos;
            int segEnd = pos + seg.Text.Length;
            if (segEnd > contentStart && segStart < contentEnd)
                seg.Styles &= ~style;
            pos += seg.Text.Length;
        }
    }

    private static void SplitSegmentsAtBoundaries(List<EditorInlineSegment> segments,
        int contentStart, int contentEnd)
    {
        // Split at contentStart
        int pos = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            int segStart = pos;
            int segEnd = pos + seg.Text.Length;
            if (contentStart > segStart && contentStart < segEnd)
            {
                int splitOffset = contentStart - segStart;
                var before = new EditorInlineSegment(seg.Text.Substring(0, splitOffset), seg.Styles);
                var after = new EditorInlineSegment(seg.Text.Substring(splitOffset), seg.Styles);
                segments.RemoveAt(i);
                segments.Insert(i, before);
                segments.Insert(i + 1, after);
                i++;
            }
            pos += segments[i].Text.Length;
        }

        // Split at contentEnd (recalculate positions)
        pos = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            int segStart = pos;
            int segEnd = pos + seg.Text.Length;
            if (contentEnd > segStart && contentEnd < segEnd)
            {
                int splitOffset = contentEnd - segStart;
                var before = new EditorInlineSegment(seg.Text.Substring(0, splitOffset), seg.Styles);
                var after = new EditorInlineSegment(seg.Text.Substring(splitOffset), seg.Styles);
                segments.RemoveAt(i);
                segments.Insert(i, before);
                segments.Insert(i + 1, after);
                i++;
            }
            pos += seg.Text.Length;
        }
    }

    // ── Multi-line inline toggle helper ────────────────────────────

    internal static TextEditResult ToggleInlineSmartMultiLine(string text, int selStart, int selEnd, InlineStyle styleToToggle)
    {
        return ToggleInline(text, selStart, selEnd, styleToToggle);
    }

    // ── Block-level toggles ────────────────────────────────────────────

    public static TextEditResult ToggleHeading(string text, int start, int end, int level)
    {
        var doc = EditorDocumentParser.Parse(text);

        var (blockIdx, _) = MapPositionToContent(doc, start);
        if (blockIdx >= doc.Blocks.Count) return new TextEditResult { Text = text, SelectionStart = start, SelectionEnd = end };

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        if (block is EditorHeadingBlock h && h.Level == level)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }
        else
        {
            var newBlock = new EditorHeadingBlock { Level = level };
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }

        string newMd = EditorDocumentRenderer.Render(doc);

        // Compute new selection: content start to content end in the new markdown
        int newPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(doc.Blocks[blockIdx]);
        int blockStart = GetBlockMdStart(doc, blockIdx);
        int contentMdStart = blockStart + newPrefixLen;
        int contentLen = GetContentTextLength(doc.Blocks[blockIdx]);
        int contentMdEnd = contentMdStart + contentLen;

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = contentMdStart,
            SelectionEnd = contentMdEnd,
        };
    }

    public static TextEditResult ToggleUnorderedList(string text, int start, int end)
    {
        var doc = EditorDocumentParser.Parse(text);

        var (blockIdx, contentOffset) = MapPositionToContent(doc, start);
        if (blockIdx >= doc.Blocks.Count) return new TextEditResult { Text = text, SelectionStart = start, SelectionEnd = end };

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        if (block is EditorUnorderedListItemBlock)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }
        else
        {
            var newBlock = new EditorUnorderedListItemBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int newPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(doc.Blocks[blockIdx]);
        int blockStart = GetBlockMdStart(doc, blockIdx);
        int contentMdStart = blockStart + newPrefixLen;
        int contentLen = GetContentTextLength(doc.Blocks[blockIdx]);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = contentMdStart,
            SelectionEnd = contentMdStart + contentLen,
        };
    }

    public static TextEditResult ToggleOrderedList(string text, int start, int end)
    {
        var doc = EditorDocumentParser.Parse(text);

        var (blockIdx, _) = MapPositionToContent(doc, start);
        if (blockIdx >= doc.Blocks.Count) return new TextEditResult { Text = text, SelectionStart = start, SelectionEnd = end };

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        if (block is EditorOrderedListItemBlock)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }
        else
        {
            var newBlock = new EditorOrderedListItemBlock { Number = 1 };
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int newPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(doc.Blocks[blockIdx]);
        int blockStart = GetBlockMdStart(doc, blockIdx);
        int contentMdStart = blockStart + newPrefixLen;
        int contentLen = GetContentTextLength(doc.Blocks[blockIdx]);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = contentMdStart,
            SelectionEnd = contentMdStart + contentLen,
        };
    }

    public static TextEditResult ToggleBlockquote(string text, int start, int end)
    {
        var doc = EditorDocumentParser.Parse(text);

        var (blockIdx, _) = MapPositionToContent(doc, start);
        if (blockIdx >= doc.Blocks.Count) return new TextEditResult { Text = text, SelectionStart = start, SelectionEnd = end };

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        if (block is EditorBlockquoteBlock)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }
        else
        {
            var newBlock = new EditorBlockquoteBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int newPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(doc.Blocks[blockIdx]);
        int blockStart = GetBlockMdStart(doc, blockIdx);
        int contentMdStart = blockStart + newPrefixLen;
        int contentLen = GetContentTextLength(doc.Blocks[blockIdx]);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = contentMdStart,
            SelectionEnd = contentMdStart + contentLen,
        };
    }

    // ── Insertions ─────────────────────────────────────────────────────

    public static TextEditResult InsertLink(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "link text";
        string insertion = $"[{selected}](url)";
        string newMd = text.Substring(0, start) + insertion + text.Substring(end);
        // Select the "url" part so the user can immediately type to replace it
        int urlStart = start + 1 + selected.Length + 2; // after "[selected]("
        int urlEnd = urlStart + 3; // length of "url"
        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = urlStart,
            SelectionEnd = urlEnd,
        };
    }

    public static TextEditResult InsertImage(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "alt text";
        string insertion = $"![{selected}](url)";
        string newMd = text.Substring(0, start) + insertion + text.Substring(end);
        // Select the "url" part so the user can immediately type to replace it
        int urlStart = start + 2 + selected.Length + 2; // after "![selected]("
        int urlEnd = urlStart + 3; // length of "url"
        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = urlStart,
            SelectionEnd = urlEnd,
        };
    }

    public static TextEditResult InsertCodeBlock(string text, int start, int end)
    {
        string selected = start < end ? text.Substring(start, end - start) : "";
        string insertion = $"```\n{selected}\n```";
        string newMd = text.Substring(0, start) + insertion + text.Substring(end);
        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = start + 4,
            SelectionEnd = start + 4 + selected.Length,
        };
    }

    public static TextEditResult InsertHorizontalRule(string text, int start, int end)
    {
        // Find the boundaries of the current line
        int lineStart = start > 0 ? text.LastIndexOf('\n', start - 1) + 1 : 0;
        int lineEnd = text.IndexOf('\n', start);
        if (lineEnd == -1) lineEnd = text.Length;

        string lineContent = text.Substring(lineStart, lineEnd - lineStart).Trim();

        if (lineContent.Length > 0)
        {
            // Line has content: insert HR on a new line after the current line
            string newMd = text.Substring(0, lineEnd) + "\n---" + text.Substring(lineEnd);
            return new TextEditResult
            {
                Text = newMd,
                SelectionStart = lineEnd + 4,
                SelectionEnd = lineEnd + 4,
            };
        }
        else
        {
            // Empty/whitespace line: replace selection with HR
            string newMd = text.Substring(0, start) + "---" + text.Substring(end);
            return new TextEditResult
            {
                Text = newMd,
                SelectionStart = start + 3,
                SelectionEnd = start + 3,
            };
        }
    }
}
