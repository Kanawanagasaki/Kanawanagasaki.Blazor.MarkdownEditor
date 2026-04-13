using System.Text;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Services;

/// <summary>
/// Models a markdown document as a list of lines, each containing plain text
/// and a set of non-overlapping style spans.  This provides a robust
/// foundation for toggle operations because it tracks formatting state
/// explicitly rather than inferring it from raw markdown syntax.
///
/// <para>Architecture:</para>
/// <list type="bullet">
///   <item>Each line stores its <b>plain text content</b> (no markdown markers)</item>
///   <item>Each line stores a list of <see cref="StyleSpan"/> objects describing
///         which inline styles are applied to which character ranges</item>
///   <item>Spans are non-overlapping, sorted by start position, and cover
///         disjoint regions of the line</item>
///   <item>Serializing the model produces canonical markdown with markers in
///         the standard order: ~~***`content`***~~ (strikethrough → bold+italic → code)</item>
/// </list>
///
/// <para>Toggle operations work by:</para>
/// <list type="number">
///   <item>Mapping the selection to line-relative coordinates</item>
///   <item>For each affected line, finding the intersection of the selection
///         with existing style spans</item>
///   <item>Adding or removing the requested style flag for that intersection</item>
///   <item>Merging adjacent spans with identical styles</item>
///   <item>Re-serializing the model to markdown</item>
/// </list>
/// </summary>
public class MarkdownDocument
{
    // ── Public data ────────────────────────────────────────────────

    /// <summary>The lines of the document (with style information).</summary>
    public List<LineModel> Lines { get; } = new();

    // ── Construction ──────────────────────────────────────────────

    public MarkdownDocument() { }

    /// <summary>
    /// Create a document from raw markdown text by stripping existing
    /// markers and extracting style information.
    /// </summary>
    public static MarkdownDocument Parse(string markdown)
    {
        var doc = new MarkdownDocument();
        if (string.IsNullOrEmpty(markdown))
            return doc;

        var textLines = markdown.Split('\n');
        foreach (var line in textLines)
        {
            doc.Lines.Add(ParseLine(line));
        }
        return doc;
    }

    /// <summary>
    /// Serialize the document back to markdown text.
    /// </summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(SerializeLine(Lines[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Diagnostic method to expose internal state after a toggle.
    /// </summary>
    public static (string Serialized, string Line0PlainText, string SpansInfo) DiagToggle(
        string markdown, int start, int end, InlineStyle style)
    {
        var doc = Parse(markdown);
        var startCoord = AbsoluteToLineCol(doc, start);
        var endCoord = AbsoluteToLineCol(doc, end);

        var preInfo = $"Before: PlainText=[{doc.Lines[0].PlainText}] Spans=[{string.Join(", ", doc.Lines[0].Spans.Select(s => $"[{s.Start}..{s.End}) {s.Styles}"))}]";
        preInfo += $" ToggleRange=plain[{startCoord.Col}..{endCoord.Col}]";

        for (int lineIdx = startCoord.Line; lineIdx <= endCoord.Line; lineIdx++)
        {
            var line = doc.Lines[lineIdx];
            int colStart = (lineIdx == startCoord.Line) ? startCoord.Col : 0;
            int colEnd = (lineIdx == endCoord.Line) ? endCoord.Col : line.PlainText.Length;
            ToggleStyleOnLine(line, colStart, colEnd, style);
        }

        var postInfo = $"After: PlainText=[{doc.Lines[0].PlainText}] Spans=[{string.Join(", ", doc.Lines[0].Spans.Select(s => $"[{s.Start}..{s.End}) {s.Styles}"))}]";
        
        // Trace serialization
        Console.WriteLine("  === Serialization trace ===");
        string serialized = SerializeLine(doc.Lines[0], trace: true);
        Console.WriteLine("  === End trace ===\n");
        postInfo += $" Serialized=[{serialized}]";

        return (serialized, doc.Lines[0].PlainText, preInfo + "\n" + postInfo);
    }

    // ── Core toggle operation ─────────────────────────────────────

    /// <summary>
    /// Toggle an inline style on a text range.  Returns the new markdown
    /// text and the updated selection coordinates.
    /// </summary>
    public static (string Text, int SelectionStart, int SelectionEnd) ToggleStyle(
        string markdown, int start, int end, InlineStyle style)
    {
        if (start == end)
        {
            // Empty selection: insert empty markers and place cursor between them
            string marker = StyleToMarker(style);
            string result = markdown.Substring(0, start) + marker + marker + markdown.Substring(end);
            return (result, start + marker.Length, start + marker.Length);
        }

        var doc = Parse(markdown);

        // Map absolute positions to line/col coordinates
        var startCoord = AbsoluteToLineCol(doc, start);
        var endCoord = AbsoluteToLineCol(doc, end);

        // Apply toggle to each affected line
        for (int lineIdx = startCoord.Line; lineIdx <= endCoord.Line; lineIdx++)
        {
            var line = doc.Lines[lineIdx];
            int colStart = (lineIdx == startCoord.Line) ? startCoord.Col : 0;
            int colEnd = (lineIdx == endCoord.Line) ? endCoord.Col : line.PlainText.Length;

            if (colStart >= colEnd && (lineIdx != startCoord.Line || lineIdx != endCoord.Line))
                continue;

            // Expand to containing span boundaries to prevent fragmenting.
            // If the range is fully within a single span, expand to cover
            // the entire span so toggling affects the whole formatted region.
            foreach (var span in line.Spans)
            {
                if (span.Start <= colStart && span.End >= colEnd)
                {
                    colStart = span.Start;
                    colEnd = span.End;
                    break;
                }
            }

            // Update coordinates so returned positions cover the expanded area
            if (lineIdx == startCoord.Line)
                startCoord = new LineCol(startCoord.Line, colStart);
            if (lineIdx == endCoord.Line)
                endCoord = new LineCol(endCoord.Line, colEnd);

            ToggleStyleOnLine(line, colStart, colEnd, style);
        }

        string newMarkdown = doc.Serialize();

        // Map the model coordinates back to serialized text positions.
        // These positions point to the content, not the markers.
        int contentStart = ComputeNewPosition(doc, startCoord);
        int contentEnd = ComputeNewPosition(doc, endCoord);

        // Return content positions directly — the next toggle will correctly
        // detect existing styles via AbsoluteToLineCol → InvertMapping.
        return (newMarkdown, contentStart, contentEnd);
    }

    // ── Line parsing ──────────────────────────────────────────────

    /// <summary>
    /// Parse a single line of markdown into a LineModel with plain text
    /// and style spans.  Handles canonical marker order:
    /// ~~***`content`***~~
    /// </summary>
    private static LineModel ParseLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return new LineModel { PlainText = line ?? "" };

        var spans = new List<StyleSpan>();
        var text = new StringBuilder();
        int pos = 0;

        while (pos < line.Length)
        {
            // ── Try to match opening markers (in canonical order) ────
            InlineStyle openingStyles = InlineStyle.None;
            int markerLen = 0;
            int savedPos = pos;

            // Check for strikethrough ~~ (outermost)
            if (pos + 2 <= line.Length && line.Substring(pos, 2) == "~~")
            {
                openingStyles |= InlineStyle.Strikethrough;
                markerLen += 2;
                pos += 2;
            }

            // Check for bold+italic *** (merged form)
            if (pos + 3 <= line.Length && line.Substring(pos, 3) == "***")
            {
                openingStyles |= InlineStyle.Bold | InlineStyle.Italic;
                markerLen += 3;
                pos += 3;
            }
            else if (pos + 2 <= line.Length && line.Substring(pos, 2) == "**")
            {
                // Bold only
                openingStyles |= InlineStyle.Bold;
                markerLen += 2;
                pos += 2;
            }
            else if (pos + 1 <= line.Length && line[pos] == '*')
            {
                // Italic only (not part of ** or ***)
                openingStyles |= InlineStyle.Italic;
                markerLen += 1;
                pos += 1;
            }

            // Check for inline code ` (innermost)
            if (pos + 1 <= line.Length && line[pos] == '`')
            {
                openingStyles |= InlineStyle.InlineCode;
                markerLen += 1;
                pos += 1;
            }

            if (openingStyles == InlineStyle.None)
            {
                // No markers found, this is a plain character
                text.Append(line[pos]);
                pos++;
                continue;
            }

            // ── Find the matching closing markers ──────────────────
            // Build expected closing suffix in reverse order
            string expectedClose = BuildMarkerSuffix(openingStyles);
            int closeIdx = FindClosingMarker(line, pos, expectedClose);
            if (closeIdx < 0)
            {
                // No matching close — treat the first character as plain text
                // and retry from the next position.  We must NOT append the
                // entire remaining string (Substring(savedPos)) because that
                // would include characters that get re-processed in subsequent
                // iterations, causing exponential content duplication.
                text.Append(line[savedPos]);
                pos = savedPos + 1;
                continue;
            }

            // Extract the content between markers
            string content = line.Substring(pos, closeIdx - pos);
            int contentStart = text.Length;

            // Recursively parse the content (it might contain nested markers)
            var innerLine = ParseLine(content);
            text.Append(innerLine.PlainText);

            // Merge the inner spans with the outer styles
            foreach (var innerSpan in innerLine.Spans)
            {
                spans.Add(new StyleSpan
                {
                    Start = contentStart + innerSpan.Start,
                    End = contentStart + innerSpan.End,
                    Styles = innerSpan.Styles | openingStyles,
                });
            }

            if (content.Length > 0)
            {
                if (innerLine.Spans.Count == 0)
                {
                    // No inner spans — outer style covers the entire content
                    spans.Add(new StyleSpan
                    {
                        Start = contentStart,
                        End = contentStart + innerLine.PlainText.Length,
                        Styles = openingStyles,
                    });
                }
                else
                {
                    // Inner spans exist — outer style also applies to gaps
                    // (regions of content not covered by any inner span).
                    int gapStart = contentStart;
                    foreach (var innerSpan in innerLine.Spans)
                    {
                        int absInnerStart = contentStart + innerSpan.Start;
                        int absInnerEnd = contentStart + innerSpan.End;
                        if (absInnerStart > gapStart)
                        {
                            spans.Add(new StyleSpan
                            {
                                Start = gapStart,
                                End = absInnerStart,
                                Styles = openingStyles,
                            });
                        }
                        gapStart = absInnerEnd;
                    }
                    int contentPlainTextEnd = contentStart + innerLine.PlainText.Length;
                    if (gapStart < contentPlainTextEnd)
                    {
                        spans.Add(new StyleSpan
                        {
                            Start = gapStart,
                            End = contentPlainTextEnd,
                            Styles = openingStyles,
                        });
                    }
                }
            }

            // Skip past the closing markers
            pos = closeIdx + expectedClose.Length;
        }

        // Merge overlapping/adjacent spans with the same styles
        spans = MergeSpans(spans);

        return new LineModel { PlainText = text.ToString(), Spans = spans };
    }

    // ── Line serialization ────────────────────────────────────────

    /// <summary>
    /// Serialize a LineModel back to markdown using a stack-based approach
    /// that properly handles transitions between adjacent spans sharing styles.
    /// When moving from one span to the next, shared markers are kept open,
    /// avoiding ambiguous sequences like "*****" or "****".
    /// </summary>
    private static string SerializeLine(LineModel line, bool trace = false)
    {
        if (string.IsNullOrEmpty(line.PlainText) || line.Spans.Count == 0)
            return line.PlainText;

        var sb = new StringBuilder();
        int pos = 0;
        InlineStyle currentOpen = InlineStyle.None;

        for (int i = 0; i <= line.Spans.Count; i++)
        {
            InlineStyle nextStyles = (i < line.Spans.Count) ? line.Spans[i].Styles : InlineStyle.None;
            int nextStart = (i < line.Spans.Count) ? line.Spans[i].Start : line.PlainText.Length;

            // Emit plain text gap between spans
            if (nextStart > pos)
            {
                if (currentOpen != InlineStyle.None)
                {
                    sb.Append(BuildMarkerSuffix(currentOpen));
                    currentOpen = InlineStyle.None;
                }
                var gapText = line.PlainText.Substring(pos, nextStart - pos);
                if (trace) Console.WriteLine($"      gap: [{gapText}]");
                sb.Append(gapText);
                pos = nextStart;
            }

            if (i >= line.Spans.Count) break;

            var span = line.Spans[i];

            // Calculate style transition
            InlineStyle toClose = currentOpen & ~nextStyles;
            InlineStyle toOpen = nextStyles & ~currentOpen;

            if (toClose != InlineStyle.None)
            {
                // Need to close some styles. Close all currently open, then
                // reopen only the styles that should remain. This ensures
                // proper nesting since markdown can't partially close nested
                // markers (e.g., can't close Bold while keeping Italic open
                // inside it without closing both and reopening).
                string closing = BuildMarkerSuffix(currentOpen);
                string reopening = BuildMarkerPrefix(nextStyles);
                if (trace) Console.WriteLine($"      transition: close [{closing}] reopen [{reopening}]");
                sb.Append(closing);
                sb.Append(reopening);
            }
            else if (toOpen != InlineStyle.None)
            {
                // Adding new styles — emit their opening markers only.
                // We keep the existing markers open (no close/reopen), which
                // produces patterns like **text*text***text* that the renderer
                // handles correctly via HasUnmatchedItalicOpener.
                // This avoids ambiguous sequences like ***** that arise from
                // the close-all/reopen-all approach.
                string opening = BuildMarkerPrefix(toOpen);
                if (trace) Console.WriteLine($"      open additional: [{opening}]");
                sb.Append(opening);
            }

            // Emit span content
            var content = line.PlainText.Substring(span.Start, span.End - span.Start);
            if (trace) Console.WriteLine($"      content: [{content}]");
            sb.Append(content);
            pos = span.End;
            currentOpen = nextStyles;
        }

        // Close any remaining open styles
        if (currentOpen != InlineStyle.None)
        {
            var closing = BuildMarkerSuffix(currentOpen);
            if (trace) Console.WriteLine($"      final close: [{closing}]");
            sb.Append(closing);
        }

        var result = sb.ToString();
        if (trace) Console.WriteLine($"      result: [{result}]");
        return result;
    }

    /// <summary>
    /// Serialize a LineModel back to markdown with canonical markers,
    /// also building a mapping from plain-text column indices to source
    /// (serialized) positions. Uses stack-based serialization for proper
    /// handling of adjacent spans with shared styles.
    /// </summary>
    private static (string Serialized, int[] PlainToSource) SerializeLineWithMapping(LineModel line)
    {
        string plainText = line.PlainText ?? "";
        int plainLen = plainText.Length;

        if (plainLen == 0 || line.Spans == null || line.Spans.Count == 0)
        {
            var mapping = new int[plainLen + 1];
            for (int i = 0; i <= plainLen; i++)
                mapping[i] = i;
            return (plainText, mapping);
        }

        var sb = new StringBuilder();
        var plainToSource = new int[plainLen + 1];
        int pos = 0; // current position in plain text
        InlineStyle currentOpen = InlineStyle.None;

        for (int i = 0; i <= line.Spans.Count; i++)
        {
            InlineStyle nextStyles = (i < line.Spans.Count) ? line.Spans[i].Styles : InlineStyle.None;
            int nextStart = (i < line.Spans.Count) ? line.Spans[i].Start : plainLen;

            // Emit plain text gap
            if (nextStart > pos)
            {
                if (currentOpen != InlineStyle.None)
                {
                    sb.Append(BuildMarkerSuffix(currentOpen));
                    currentOpen = InlineStyle.None;
                }

                int gapLen = nextStart - pos;
                for (int j = 0; j < gapLen; j++)
                    plainToSource[pos + j] = sb.Length + j;
                sb.Append(plainText, pos, gapLen);
            }

            if (i >= line.Spans.Count) break;

            var span = line.Spans[i];

            // Calculate style transition
            InlineStyle toClose = currentOpen & ~nextStyles;
            InlineStyle toOpen = nextStyles & ~currentOpen;

            if (toClose != InlineStyle.None)
            {
                sb.Append(BuildMarkerSuffix(currentOpen));
                sb.Append(BuildMarkerPrefix(nextStyles));
            }
            else if (toOpen != InlineStyle.None)
            {
                // Adding new styles — just emit their opening markers.
                // Keep existing markers open to avoid ambiguous sequences.
                sb.Append(BuildMarkerPrefix(toOpen));
            }

            // Emit span content with position mapping
            int cl = span.End - span.Start;
            for (int j = 0; j < cl; j++)
                plainToSource[span.Start + j] = sb.Length + j;
            sb.Append(plainText, span.Start, cl);

            pos = span.End;
            currentOpen = nextStyles;
        }

        // Close remaining
        if (currentOpen != InlineStyle.None)
        {
            sb.Append(BuildMarkerSuffix(currentOpen));
        }

        // "Past end" mapping
        plainToSource[plainLen] = sb.Length;

        return (sb.ToString(), plainToSource);
    }

    // ── Style toggling on a single line ───────────────────────────

    /// <summary>
    /// Toggle a style on a range within a line.  The range [colStart, colEnd)
    /// is in plain-text coordinates (not markdown source coordinates).
    /// </summary>
    private static void ToggleStyleOnLine(LineModel line, int colStart, int colEnd, InlineStyle style)
    {
        // Clamp to line bounds
        colStart = Math.Max(0, Math.Min(colStart, line.PlainText.Length));
        colEnd = Math.Max(0, Math.Min(colEnd, line.PlainText.Length));

        if (colStart >= colEnd)
            return;

        // Expand to cover any span that the toggle range partially overlaps,
        // but ONLY for spans that CONTAIN the style being toggled.
        // Expanding for spans without the style would incorrectly enlarge
        // the operation range when adding a new style to a sub-region of
        // a differently-styled span (e.g., adding Italic inside a Bold region).
        foreach (var span in line.Spans.ToList())
        {
            if ((span.Styles & style) == 0) continue;

            // If toggle start falls within this span, expand to span start
            if (span.Start < colStart && span.End > colStart)
            {
                colStart = span.Start;
            }
            // If toggle end falls within this span, expand to span end
            if (span.Start < colEnd && span.End > colEnd)
            {
                colEnd = span.End;
            }
        }

        // Also expand the range to cover any span that fully contains it,
        // but ONLY for spans that contain the style being toggled.
        foreach (var span in line.Spans)
        {
            if ((span.Styles & style) == 0) continue;
            if (span.Start <= colStart && span.End >= colEnd)
            {
                colStart = span.Start;
                colEnd = span.End;
                break;
            }
        }

        // Collect span boundaries within the toggle range to split it
        // into sub-ranges.  Each sub-range may overlap with different
        // existing spans, so we handle each independently.
        // Include boundaries of ALL spans (not just those with the matching
        // style) because the sub-ranges need to account for all existing
        // formatting when computing the XOR result.
        var boundaries = new List<int> { colStart };
        foreach (var span in line.Spans)
        {
            if (span.Start > colStart && span.Start < colEnd)
                boundaries.Add(span.Start);
            if (span.End > colStart && span.End < colEnd)
                boundaries.Add(span.End);
        }
        boundaries.Add(colEnd);
        boundaries.Sort();
        boundaries = boundaries.Distinct().ToList();

        // Clamp range to plain text bounds after expansion
        colStart = Math.Max(0, Math.Min(colStart, line.PlainText.Length));
        colEnd = Math.Max(0, Math.Min(colEnd, line.PlainText.Length));

        // Build new span list: keep non-overlapping parts of partially
        // overlapping spans, then handle the toggle range via sub-ranges
        var newSpans = new List<StyleSpan>();
        foreach (var span in line.Spans)
        {
            if (span.End <= colStart || span.Start >= colEnd)
            {
                // Completely outside — keep as-is
                newSpans.Add(span);
                continue;
            }

            // Partially overlapping — keep the non-overlapping parts.
            // But if this span does NOT have the style being toggled, keep the
            // entire span (it should not be split by the toggle operation).
            if ((span.Styles & style) == 0)
            {
                // This span has a different style — keep it whole.
                // It will be merged with the new toggle spans by MergeSpans.
                newSpans.Add(span);
                continue;
            }

            // This span HAS the style being toggled — split it.
            if (span.Start < colStart)
            {
                newSpans.Add(new StyleSpan
                {
                    Start = span.Start,
                    End = colStart,
                    Styles = span.Styles,
                });
            }

            if (span.End > colEnd)
            {
                newSpans.Add(new StyleSpan
                {
                    Start = colEnd,
                    End = span.End,
                    Styles = span.Styles,
                });
            }

            // The overlapping part is handled by the sub-range processing below
        }

        // Process each sub-range independently
        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            int subStart = boundaries[i];
            int subEnd = boundaries[i + 1];

            // Find all styles active in this sub-range by checking
            // which original spans overlap with it
            InlineStyle existingStyles = InlineStyle.None;
            foreach (var span in line.Spans)
            {
                if (span.Start < subEnd && span.End > subStart)
                    existingStyles |= span.Styles;
            }

            InlineStyle newStyles = existingStyles ^ style;
            if (newStyles != InlineStyle.None)
            {
                newSpans.Add(new StyleSpan
                {
                    Start = subStart,
                    End = subEnd,
                    Styles = newStyles,
                });
            }
        }

        line.Spans = MergeSpans(newSpans);
    }

    /// <summary>
    /// Get the combined styles that are active at every position within
    /// the range [start, end).  Returns the intersection of all styles
    /// in that range.
    /// </summary>
    private static InlineStyle GetStylesInRange(LineModel line, int start, int end)
    {
        // Collect styles at the midpoint of the range
        // (if styles are consistent across the range, the midpoint represents all)
        int mid = (start + end) / 2;
        InlineStyle styles = InlineStyle.None;

        foreach (var span in line.Spans)
        {
            if (span.Start <= mid && span.End > mid)
            {
                styles |= span.Styles;
            }
        }

        return styles;
    }

    // ── Span merging ──────────────────────────────────────────────

    /// <summary>
    /// Merge overlapping and adjacent spans that have the same styles.
    /// Returns a sorted, non-overlapping list of spans.
    /// </summary>
    private static List<StyleSpan> MergeSpans(List<StyleSpan> spans)
    {
        if (spans.Count == 0) return spans;

        // Sort by start position
        var sorted = spans.OrderBy(s => s.Start).ToList();

        var merged = new List<StyleSpan>();
        var current = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];

            if (next.Start <= current.End && current.Styles == next.Styles)
            {
                // Overlapping or adjacent with same styles — merge
                current = new StyleSpan
                {
                    Start = current.Start,
                    End = Math.Max(current.End, next.End),
                    Styles = current.Styles,
                };
            }
            else if (next.Start <= current.End && next.Start < current.End)
            {
                // Truly overlapping (not just adjacent) with DIFFERENT styles — need to split
                // The overlapping region should have the union of styles
                int overlapStart = next.Start;
                int overlapEnd = Math.Min(current.End, next.End);
                InlineStyle mergedStyles = current.Styles | next.Styles;

                // Keep the non-overlapping part of current
                if (current.Start < overlapStart)
                {
                    merged.Add(new StyleSpan
                    {
                        Start = current.Start,
                        End = overlapStart,
                        Styles = current.Styles,
                    });
                }

                // Add the merged overlap region
                current = new StyleSpan
                {
                    Start = overlapStart,
                    End = overlapEnd,
                    Styles = mergedStyles,
                };

                // Push current and handle the rest of next in the next iteration
                if (next.End > overlapEnd)
                {
                    // Need to handle the remaining part of next
                    // We'll do this by treating current as the overlap
                    // and adding the rest of next separately
                    merged.Add(current);
                    current = new StyleSpan
                    {
                        Start = overlapEnd,
                        End = next.End,
                        Styles = next.Styles,
                    };
                }
            }
            else
            {
                // Non-overlapping or merely adjacent with different styles —
                // emit current and start new.  We keep adjacent spans separate
                // so the serializer emits proper markers for each.
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    // ── Coordinate mapping ────────────────────────────────────────

    /// <summary>
    /// Represents a position within the document as line and column indices.
    /// Column is in plain-text coordinates (no markers).
    /// </summary>
    private struct LineCol(int line, int col)
    {
        public int Line = line;
        public int Col = col;
    }

    /// <summary>
    /// Convert an absolute position in the original markdown text to
    /// a (line, plainTextColumn) coordinate.
    /// Uses <see cref="SerializeLineWithMapping"/> for accurate bidirectional
    /// position mapping that is consistent with the actual serialized output.
    /// </summary>
    private static LineCol AbsoluteToLineCol(MarkdownDocument doc, int absPos)
    {
        int runningSourcePos = 0;
        int lineIdx = 0;

        foreach (var line in doc.Lines)
        {
            var (serialized, plainToSource) = SerializeLineWithMapping(line);
            int lineLen = serialized.Length;

            if (absPos <= runningSourcePos + lineLen)
            {
                // The position is within this line
                int relPos = absPos - runningSourcePos;
                // Invert the plainToSource mapping to find plain-text column
                int plainCol = InvertMapping(plainToSource, relPos);
                return new LineCol(lineIdx, plainCol);
            }

            runningSourcePos += lineLen + 1; // +1 for newline
            lineIdx++;
        }

        // Past the end — return end of last line
        int lastLine = doc.Lines.Count - 1;
        return new LineCol(lastLine, doc.Lines[lastLine].PlainText.Length);
    }

    /// <summary>
    /// Invert a plainToSource mapping array to find which plain-text
    /// column corresponds to a given source position.  The mapping array
    /// is monotonic (each entry >= previous), so we use binary search.
    /// For positions in marker gaps (opening/closing markers), returns
    /// the nearest content boundary.
    /// </summary>
    private static int InvertMapping(int[] plainToSource, int sourcePos)
    {
        // Binary search: find the largest index i such that plainToSource[i] <= sourcePos
        int lo = 0;
        int hi = plainToSource.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (plainToSource[mid] <= sourcePos)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// Compute the new absolute position in the serialized text for
    /// a given line/col coordinate in the model.
    /// Uses <see cref="SerializeLineWithMapping"/> for accurate forward
    /// mapping consistent with the actual serialized output.
    /// </summary>
    private static int ComputeNewPosition(MarkdownDocument doc, LineCol coord)
    {
        int absPos = 0;
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            if (i > 0) absPos++; // newline
            if (i == coord.Line)
            {
                var (_, plainToSource) = SerializeLineWithMapping(doc.Lines[i]);
                int clamped = Math.Min(coord.Col, plainToSource.Length - 1);
                absPos += plainToSource[Math.Max(0, clamped)];
                return absPos;
            }
            var (serialized, _) = SerializeLineWithMapping(doc.Lines[i]);
            absPos += serialized.Length;
        }
        return absPos;
    }

    // ── Closing marker search ────────────────────────────────────

    /// <summary>
    /// Find the position of a closing marker in <paramref name="line"/>
    /// starting from <paramref name="start"/>, skipping positions where
    /// the marker is part of a longer marker sequence.
    ///
    /// <para>Rules:</para>
    /// <list type="bullet">
    ///   <item>For <c>*</c> (italic close): skip if followed by <c>*</c>
    ///         (i.e. part of <c>**</c> or <c>***</c>).</item>
    ///   <item>For <c>**</c> (bold close): skip if followed by <c>*</c>
    ///         (i.e. part of <c>***</c>).</item>
    ///   <item>For <c>***</c>, <c>~~</c>, <c>`</c>: no skipping needed.</item>
    /// </list>
    ///
    /// <para>This prevents the parser from matching the first <c>*</c> of a
    /// <c>**</c> bold opener as an italic close, which would leave the rest
    /// of the bold marker orphaned.</para>
    /// </summary>
    private static int FindClosingMarker(string line, int start, string marker)
    {
        if (marker == "*")
        {
            // For italic close: find the LAST * (after start) that is
            // not immediately followed by another *. This handles the key
            // case where *** at the end of a line is `**` (bold close) + `*`
            // (italic close).  The last * of the run is a standalone italic
            // close, while the first and second * are part of the bold close.
            for (int i = line.Length - 1; i >= start; i--)
            {
                if (line[i] != '*') continue;
                if (i + 1 < line.Length && line[i + 1] == '*') continue;
                return i;
            }
            return -1;
        }

        if (marker == "**")
        {
            // For bold close: skip any ** that is followed by * (part of ***).
            int idx = line.IndexOf(marker, start);
            while (idx >= 0)
            {
                // ** followed by * means this is the start of ***, not a
                // standalone bold close.
                if (idx + 2 < line.Length && line[idx + 2] == '*')
                {
                    start = idx + 3;
                    idx = line.IndexOf(marker, start);
                }
                else
                {
                    return idx;
                }
            }
            return -1;
        }

        // For *** and other markers: simple IndexOf
        return line.IndexOf(marker, start);
    }

    // ── Marker builders ───────────────────────────────────────────

    private static string BuildMarkerPrefix(InlineStyle styles)
    {
        var sb = new StringBuilder();
        if ((styles & InlineStyle.Strikethrough) != 0) sb.Append("~~");
        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;
        if (hasBold && hasItalic) sb.Append("***");
        else if (hasBold) sb.Append("**");
        else if (hasItalic) sb.Append("*");
        if ((styles & InlineStyle.InlineCode) != 0) sb.Append("`");
        return sb.ToString();
    }

    private static string BuildMarkerSuffix(InlineStyle styles)
    {
        var sb = new StringBuilder();
        if ((styles & InlineStyle.InlineCode) != 0) sb.Append("`");
        bool hasBold = (styles & InlineStyle.Bold) != 0;
        bool hasItalic = (styles & InlineStyle.Italic) != 0;
        if (hasBold && hasItalic) sb.Append("***");
        else if (hasBold) sb.Append("**");
        else if (hasItalic) sb.Append("*");
        if ((styles & InlineStyle.Strikethrough) != 0) sb.Append("~~");
        return sb.ToString();
    }

    private static string StyleToMarker(InlineStyle style) => style switch
    {
        InlineStyle.Bold => "**",
        InlineStyle.Italic => "*",
        InlineStyle.Strikethrough => "~~",
        InlineStyle.InlineCode => "`",
        _ => "",
    };
}

/// <summary>
/// Models a single line of the document with its plain text content
/// and associated style spans.
/// </summary>
public class LineModel
{
    /// <summary>The plain text content of this line (no markdown markers).</summary>
    public string PlainText { get; set; } = "";

    /// <summary>Non-overlapping style spans for this line.</summary>
    public List<StyleSpan> Spans { get; set; } = new();


}

/// <summary>
/// Describes a range of characters within a line that have specific
/// inline styles applied.
/// </summary>
public class StyleSpan
{
    /// <summary>Start position within the line's plain text (inclusive).</summary>
    public int Start { get; set; }

    /// <summary>End position within the line's plain text (exclusive).</summary>
    public int End { get; set; }

    /// <summary>The set of inline styles active for this range.</summary>
    public InlineStyle Styles { get; set; }
}
