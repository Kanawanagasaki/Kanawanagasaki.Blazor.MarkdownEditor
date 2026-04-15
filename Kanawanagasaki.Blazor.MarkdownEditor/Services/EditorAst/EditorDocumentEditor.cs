using System.Text;
using Markdig;
using Markdig.Syntax;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services.EditorAst;

/// <summary>
/// Provides all editing operations on an <see cref="EditorDocument"/>.
/// Every edit mutates the AST directly — NO string manipulation.
/// </summary>
public static class EditorDocumentEditor
{
    // ── Position mapping ───────────────────────────────────────────────

    /// <summary>
    /// Map an absolute position in the rendered markdown to a (blockIndex, contentCharOffset).
    /// </summary>
    public static (int BlockIndex, int ContentOffset) MapPositionToContent(
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
            int blockStart = pos;
            int blockLen = EditorDocumentRenderer.GetBlockMarkdownLength(doc.Blocks[i]);
            int blockEnd = blockStart + blockLen;

            if (mdPosition <= blockEnd || i == doc.Blocks.Count - 1)
            {
                // Position is within this block - render the block's content
                // and find where the position maps to in the content
                int contentOffset = FindContentOffsetInBlock(doc.Blocks[i], mdPosition - blockStart, prefixLen);
                return (i, contentOffset);
            }

            pos = blockEnd;
        }

        return (0, 0);
    }

    /// <summary>
    /// Find the content offset within a block for a given position offset from the block start.
    /// Uses the delta renderer to compute positions correctly.
    /// </summary>
    private static int FindContentOffsetInBlock(EditorBlock block, int posInBlock, int prefixLen)
    {
        if (posInBlock < prefixLen)
            return 0;

        int posAfterPrefix = posInBlock - prefixLen;
        var segments = block.Segments;

        // Simulate delta rendering and track content offset
        InlineStyle currentStyles = InlineStyle.None;
        int renderedPos = 0;
        int contentOffset = 0;

        foreach (var seg in segments)
        {
            if (seg.Styles != currentStyles)
            {
                string transition = EditorDocumentRenderer.ComputeTransitionMarkers(currentStyles, seg.Styles);
                
                if (renderedPos + transition.Length > posAfterPrefix)
                {
                    // Position is within the transition markers - return current content offset
                    return contentOffset;
                }
                renderedPos += transition.Length;
                currentStyles = seg.Styles;
            }

            if (renderedPos + seg.Text.Length >= posAfterPrefix)
            {
                // Position is within this segment's text
                contentOffset += posAfterPrefix - renderedPos;
                return contentOffset;
            }

            renderedPos += seg.Text.Length;
            contentOffset += seg.Text.Length;
        }

        // Position is past all content (in closing markers or beyond)
        // Close remaining styles and check
        if (currentStyles != InlineStyle.None)
        {
            string close = EditorDocumentRenderer.ComputeCloseMarkers(currentStyles);
            // Position might be in the closing markers
            // Return end of content
        }

        return contentOffset;
    }

    /// <summary>
    /// Map a (blockIndex, contentCharOffset) to an absolute position in the rendered markdown.
    /// The returned position points to the content character at the given offset,
    /// which is after any opening markers for the segment containing that character.
    /// </summary>
    public static int MapContentToPosition(EditorDocument doc, int blockIndex, int contentOffset)
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

        var segments = doc.Blocks[blockIndex].Segments;
        InlineStyle currentStyles = InlineStyle.None;
        int remaining = contentOffset;

        foreach (var seg in segments)
        {
            // Always compute transition markers BEFORE checking remaining,
            // because the content position must be after the opening markers
            // of the segment that contains the target content character.
            if (seg.Styles != currentStyles)
            {
                string transition = EditorDocumentRenderer.ComputeTransitionMarkers(currentStyles, seg.Styles);
                pos += transition.Length;
                currentStyles = seg.Styles;
            }

            if (remaining <= 0)
                break;

            if (remaining <= seg.Text.Length)
            {
                pos += remaining;
                remaining = 0;
                break;
            }

            pos += seg.Text.Length;
            remaining -= seg.Text.Length;
        }

        return pos;
    }

    // ── Inline style toggles ───────────────────────────────────────────

    public static TextEditResult ToggleBold(EditorDocument doc, int selStart, int selEnd)
        => ToggleInline(doc, selStart, selEnd, InlineStyle.Bold);

    public static TextEditResult ToggleItalic(EditorDocument doc, int selStart, int selEnd)
        => ToggleInline(doc, selStart, selEnd, InlineStyle.Italic);

    public static TextEditResult ToggleStrikethrough(EditorDocument doc, int selStart, int selEnd)
        => ToggleInline(doc, selStart, selEnd, InlineStyle.Strikethrough);

    public static TextEditResult ToggleInlineCode(EditorDocument doc, int selStart, int selEnd)
        => ToggleInline(doc, selStart, selEnd, InlineStyle.InlineCode);

    public static TextEditResult ToggleInline(EditorDocument doc, int selStart, int selEnd, InlineStyle styleToToggle)
    {
        string oldMd = EditorDocumentRenderer.Render(doc);

        if (selStart == selEnd)
        {
            string marker = EditorInlineSegment.StyleToMarker(styleToToggle);
            string newMd = oldMd.Substring(0, selStart) + marker + marker + oldMd.Substring(selEnd);
            return new TextEditResult
            {
                Text = newMd,
                SelectionStart = selStart + marker.Length,
                SelectionEnd = selStart + marker.Length,
            };
        }

        var (startBlock, startOffset) = MapPositionToContent(doc, selStart);
        var (endBlock, endOffset) = MapPositionToContent(doc, selEnd);

        if (startBlock != endBlock)
        {
            return ToggleInlineMultiBlock(doc, selStart, selEnd, styleToToggle, startBlock, startOffset, endBlock, endOffset);
        }

        return ToggleInlineSingleBlock(doc, selStart, selEnd, styleToToggle, startBlock, startOffset, endOffset);
    }

    private static TextEditResult ToggleInlineSingleBlock(EditorDocument doc, int selStart, int selEnd,
        InlineStyle styleToToggle, int blockIndex, int contentStart, int contentEnd)
    {
        var block = doc.Blocks[blockIndex];
        if (!block.HasInlineContent || block.Segments.Count == 0)
        {
            string oldMd = EditorDocumentRenderer.Render(doc);
            string marker = EditorInlineSegment.StyleToMarker(styleToToggle);
            string inner = oldMd.Substring(selStart, selEnd - selStart);
            string newMd = oldMd.Substring(0, selStart) + marker + inner + marker + oldMd.Substring(selEnd);
            return new TextEditResult
            {
                Text = newMd,
                SelectionStart = selStart + marker.Length,
                SelectionEnd = selStart + marker.Length + inner.Length,
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

        // Split whitespace at style boundaries for correct rendering
        SplitWhitespaceAtStyleBoundaries(block.Segments);

        EditorDocument.NormalizeSegments(block.Segments);

        string renderedMd = EditorDocumentRenderer.Render(doc);
        int newSelStart = MapContentToPosition(doc, blockIndex, contentStart);
        int newSelEnd = MapContentToPosition(doc, blockIndex, contentEnd);

        return new TextEditResult
        {
            Text = renderedMd,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

    private static TextEditResult ToggleInlineMultiBlock(EditorDocument doc, int selStart, int selEnd,
        InlineStyle styleToToggle, int startBlock, int startOffset, int endBlock, int endOffset)
    {
        bool styleActive = false;
        if (startBlock < doc.Blocks.Count && doc.Blocks[startBlock].HasInlineContent && doc.Blocks[startBlock].Segments.Count > 0)
        {
            styleActive = IsStyleActiveOnRange(doc.Blocks[startBlock].Segments, startOffset,
                GetContentLength(doc.Blocks[startBlock].Segments), styleToToggle);
        }

        for (int i = startBlock; i <= endBlock && i < doc.Blocks.Count; i++)
        {
            var block = doc.Blocks[i];
            if (!block.HasInlineContent || block.Segments.Count == 0) continue;

            int segStart = (i == startBlock) ? startOffset : 0;
            int segEnd = (i == endBlock) ? endOffset : GetContentLength(block.Segments);

            if (styleActive)
                RemoveStyleFromRange(block.Segments, segStart, segEnd, styleToToggle);
            else
                AddStyleToRange(block.Segments, segStart, segEnd, styleToToggle);

            SplitWhitespaceAtStyleBoundaries(block.Segments);
            EditorDocument.NormalizeSegments(block.Segments);
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int newSelStart = MapContentToPosition(doc, startBlock, startOffset);
        int newSelEnd = MapContentToPosition(doc, endBlock, endOffset);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

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
                expandedStart = Math.Min(expandedStart, segStart);
                expandedEnd = Math.Max(expandedEnd, segEnd);
            }

            pos += seg.Text.Length;
        }

        // Expand further to include any adjacent segments with the same style
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

    // ── Segment manipulation ───────────────────────────────────────────

    private static int GetContentLength(List<EditorInlineSegment> segments)
    {
        int len = 0;
        foreach (var seg in segments) len += seg.Text.Length;
        return len;
    }

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
            pos = 0;
            for (int j = 0; j <= i; j++) pos += segments[j].Text.Length;
        }

        // Split at contentEnd
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

    /// <summary>
    /// After applying styles, split whitespace at style boundaries.
    /// Split when there's a "nesting conflict" - i.e., when transitioning
    /// between segments where styles change in a way that requires markers
    /// to be closed and reopened. This includes:
    /// 1. Bidirectional conflicts: both adding and removing styles (A|B ↔ A|C)
    /// 2. Unidirectional conflicts: removing a style while keeping shared styles
    ///    (e.g., A|B → A, where closing B requires temporarily closing A)
    ///
    /// The whitespace split ensures that spaces between words fall outside
    /// the style markers, allowing the delta renderer to produce correct nesting.
    /// </summary>
    internal static void SplitWhitespaceAtStyleBoundaries(List<EditorInlineSegment> segments)
    {

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];

            if (seg.Styles == InlineStyle.None || string.IsNullOrEmpty(seg.Text))
                continue;

            // Check previous segment - split if there's a nesting conflict
            if (i > 0)
            {
                var prevStyles = segments[i - 1].Styles;
                // A nesting conflict occurs when:
                // 1. Bidirectional: both segments have styles the other doesn't (A|B ↔ A|C)
                // 2. Unidirectional: previous has a style current doesn't, AND they share
                //    styles (e.g., Bold|Italic → Italic: closing Bold requires temporarily
                //    closing Italic, so whitespace must be outside the markers)
                var onlyInPrev = prevStyles & ~seg.Styles;
                var onlyInCur = seg.Styles & ~prevStyles;
                var shared = prevStyles & seg.Styles;
                bool hasConflict = (onlyInPrev != InlineStyle.None && onlyInCur != InlineStyle.None)
                    || (onlyInPrev != InlineStyle.None && shared != InlineStyle.None);

                if (hasConflict)
                {
                    // Split leading whitespace from current segment
                    int wsLen = 0;
                    while (wsLen < seg.Text.Length && char.IsWhiteSpace(seg.Text[wsLen]))
                        wsLen++;

                    if (wsLen > 0 && wsLen < seg.Text.Length)
                    {
                        var wsSeg = new EditorInlineSegment(seg.Text.Substring(0, wsLen), InlineStyle.None);
                        var restSeg = new EditorInlineSegment(seg.Text.Substring(wsLen), seg.Styles);
                        segments.RemoveAt(i);
                        segments.Insert(i, wsSeg);
                        segments.Insert(i + 1, restSeg);
                        i++; // Skip the new segment
                    }
                }
            }

            if (i >= segments.Count) break;
            seg = segments[i];

            // Check next segment - split if there's a nesting conflict
            if (i < segments.Count - 1)
            {
                var nextStyles = segments[i + 1].Styles;
                var onlyInCur = seg.Styles & ~nextStyles;
                var onlyInNext = nextStyles & ~seg.Styles;
                var shared = seg.Styles & nextStyles;
                bool hasConflict = (onlyInCur != InlineStyle.None && onlyInNext != InlineStyle.None)
                    || (onlyInCur != InlineStyle.None && shared != InlineStyle.None);

                if (hasConflict)
                {
                    // Split trailing whitespace from current segment
                    int wsLen = 0;
                    int idx = seg.Text.Length - 1;
                    while (idx >= 0 && char.IsWhiteSpace(seg.Text[idx]))
                    {
                        wsLen++;
                        idx--;
                    }

                    if (wsLen > 0 && wsLen < seg.Text.Length)
                    {
                        var restSeg = new EditorInlineSegment(seg.Text.Substring(0, seg.Text.Length - wsLen), seg.Styles);
                        var wsSeg = new EditorInlineSegment(seg.Text.Substring(seg.Text.Length - wsLen), InlineStyle.None);
                        segments.RemoveAt(i);
                        segments.Insert(i, restSeg);
                        segments.Insert(i + 1, wsSeg);
                    }
                }
            }
        }
    }

    // ── Block-level toggles ────────────────────────────────────────────

    public static TextEditResult ToggleHeading(EditorDocument doc, int selStart, int selEnd, int level)
    {
        var (blockIdx, _) = MapPositionToContent(doc, selStart);
        if (blockIdx >= doc.Blocks.Count) return NoOp(doc);

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        int oldPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(block);
        int newPrefixLen;

        if (block is EditorHeadingBlock h && h.Level == level)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 0;
        }
        else
        {
            var newBlock = new EditorHeadingBlock { Level = level };
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = level + 1;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int prefixDelta = newPrefixLen - oldPrefixLen;
        int newSelStart = Math.Clamp(selStart + prefixDelta, 0, newMd.Length);
        int newSelEnd = Math.Clamp(selEnd + prefixDelta, 0, newMd.Length);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = newSelStart,
            SelectionEnd = newSelEnd,
        };
    }

    public static TextEditResult ToggleUnorderedList(EditorDocument doc, int selStart, int selEnd)
    {
        var (blockIdx, _) = MapPositionToContent(doc, selStart);
        if (blockIdx >= doc.Blocks.Count) return NoOp(doc);

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        int oldPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(block);
        int newPrefixLen;

        if (block is EditorUnorderedListItemBlock)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 0;
        }
        else
        {
            var newBlock = new EditorUnorderedListItemBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 2;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int prefixDelta = newPrefixLen - oldPrefixLen;
        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = Math.Clamp(selStart + prefixDelta, 0, newMd.Length),
            SelectionEnd = Math.Clamp(selEnd + prefixDelta, 0, newMd.Length),
        };
    }

    public static TextEditResult ToggleOrderedList(EditorDocument doc, int selStart, int selEnd)
    {
        var (blockIdx, _) = MapPositionToContent(doc, selStart);
        if (blockIdx >= doc.Blocks.Count) return NoOp(doc);

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        int oldPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(block);
        int newPrefixLen;

        if (block is EditorOrderedListItemBlock)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 0;
        }
        else
        {
            var newBlock = new EditorOrderedListItemBlock { Number = 1 };
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 3;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int prefixDelta = newPrefixLen - oldPrefixLen;
        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = Math.Clamp(selStart + prefixDelta, 0, newMd.Length),
            SelectionEnd = Math.Clamp(selEnd + prefixDelta, 0, newMd.Length),
        };
    }

    public static TextEditResult ToggleBlockquote(EditorDocument doc, int selStart, int selEnd)
    {
        var (blockIdx, _) = MapPositionToContent(doc, selStart);
        if (blockIdx >= doc.Blocks.Count) return NoOp(doc);

        var block = doc.Blocks[blockIdx];
        var contentSegments = new List<EditorInlineSegment>(block.Segments);

        int oldPrefixLen = EditorDocumentRenderer.GetBlockPrefixLength(block);
        int newPrefixLen;

        if (block is EditorBlockquoteBlock)
        {
            var newBlock = new EditorParagraphBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 0;
        }
        else
        {
            var newBlock = new EditorBlockquoteBlock();
            newBlock.Segments.AddRange(contentSegments);
            doc.Blocks[blockIdx] = newBlock;
            newPrefixLen = 2;
        }

        string newMd = EditorDocumentRenderer.Render(doc);
        int prefixDelta = newPrefixLen - oldPrefixLen;
        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = Math.Clamp(selStart + prefixDelta, 0, newMd.Length),
            SelectionEnd = Math.Clamp(selEnd + prefixDelta, 0, newMd.Length),
        };
    }

    // ── Insertions ─────────────────────────────────────────────────────

    public static TextEditResult InsertLink(EditorDocument doc, int selStart, int selEnd)
    {
        string md = EditorDocumentRenderer.Render(doc);
        string selected = selStart < selEnd ? md.Substring(selStart, selEnd - selStart) : "link text";
        string insertion = $"[{selected}](url)";
        string newMd = md.Substring(0, selStart) + insertion + md.Substring(selEnd);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = selStart + 1,
            SelectionEnd = selStart + 1 + selected.Length,
        };
    }

    public static TextEditResult InsertImage(EditorDocument doc, int selStart, int selEnd)
    {
        string md = EditorDocumentRenderer.Render(doc);
        string selected = selStart < selEnd ? md.Substring(selStart, selEnd - selStart) : "alt text";
        string insertion = $"![{selected}](url)";
        string newMd = md.Substring(0, selStart) + insertion + md.Substring(selEnd);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = selStart + 2,
            SelectionEnd = selStart + 2 + selected.Length,
        };
    }

    public static TextEditResult InsertCodeBlock(EditorDocument doc, int selStart, int selEnd)
    {
        string md = EditorDocumentRenderer.Render(doc);
        string selected = selStart < selEnd ? md.Substring(selStart, selEnd - selStart) : "";
        string insertion = $"```\n{selected}\n```";
        string newMd = md.Substring(0, selStart) + insertion + md.Substring(selEnd);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = selStart + 4,
            SelectionEnd = selStart + 4 + selected.Length,
        };
    }

    public static TextEditResult InsertHorizontalRule(EditorDocument doc, int selStart, int selEnd)
    {
        string md = EditorDocumentRenderer.Render(doc);
        string newMd = md.Substring(0, selStart) + "---" + md.Substring(selEnd);

        return new TextEditResult
        {
            Text = newMd,
            SelectionStart = selStart + 3,
            SelectionEnd = selStart + 3,
        };
    }

    // ── Helper ─────────────────────────────────────────────────────────

    private static TextEditResult NoOp(EditorDocument doc)
    {
        return new TextEditResult
        {
            Text = EditorDocumentRenderer.Render(doc),
            SelectionStart = 0,
            SelectionEnd = 0,
        };
    }
}

/// <summary>
/// Describes the result of a toggle/insert operation.
/// </summary>
public struct TextEditResult
{
    public string Text { get; init; }
    public int SelectionStart { get; init; }
    public int SelectionEnd { get; init; }
}
