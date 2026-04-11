/**
 * Kanawanagasaki.Blazor.MarkdownEditor – JavaScript interop layer
 *
 * Architecture:
 *  - Textarea: transparent, pointer-events:none, overflow:auto (hidden scrollbar).
 *    Acts as the scroll model. Browser auto-scrolls it to keep cursor visible.
 *  - Overlay: pointer-events:auto, user-select enabled, overflow-y:auto. Captures
 *    clicks and wheel. Users can natively select text on the rendered overlay.
 *    On mouseup, native overlay selection is converted to source offsets via
 *    visibleToSource mappings and synced to the textarea selection.
 *  - Selection layer: pointer-events:none. JS renders .md-sel-seg divs that
 *    visually highlight the textarea's selection range on the rendered overlay.
 *  - Cursor: pointer-events:none, absolutely positioned. Placed at the overlay
 *    line's viewport position. X calculated via Range.getBoundingClientRect().
 */

const _instances = new Map();

class EditorInstance {
    constructor(id, editorBody, textarea, overlay, cursorEl, selectionLayer) {
        this.id = id;
        this.editorBody = editorBody;
        this.textarea = textarea;
        this.overlay = overlay;
        this.cursorEl = cursorEl;
        this.selectionLayer = selectionLayer;
        this.blinkTimer = null;
        this.visible = true;
        this.lineHeight = 0;
        this.lineMappings = [];  // [{ sourceStart, visibleToSource }]
        this._updatingFromTextarea = false;
        this._updatingFromOverlay = false;
        this._boundHandlers = [];
        this._syncingSelection = false; // guard to prevent loops
    }

    dispose() {
        if (this.blinkTimer) clearInterval(this.blinkTimer);
        if (this._boundHandlers) {
            for (const fn of this._boundHandlers) {
                if (fn.target) fn.target.removeEventListener(fn.event, fn.handler, fn.options);
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Selection highlight rendering
// ═══════════════════════════════════════════════════════════════════

/**
 * Map a source offset to a visible character offset using the line's
 * visibleToSource mapping.  Invisible source characters (syntax markers
 * like **, ##, `) are skipped so the highlight covers only visible text.
 */
function sourceToVisibleOffset(visibleToSource, sourceOffset, isEnd) {
    if (!visibleToSource || visibleToSource.length === 0) return 0;

    // Before first visible char
    if (sourceOffset <= visibleToSource[0]) return 0;

    // Past last visible char
    if (sourceOffset > visibleToSource[visibleToSource.length - 1])
        return visibleToSource.length;

    // Find the closest visible offset
    for (let v = 0; v < visibleToSource.length; v++) {
        if (visibleToSource[v] >= sourceOffset) return v;
    }
    return visibleToSource.length;
}

/**
 * Collect all text nodes inside an overlay line element.
 */
function collectTextNodes(lineEl) {
    const nodes = [];
    const walker = document.createTreeWalker(lineEl, NodeFilter.SHOW_TEXT, null, false);
    let node;
    while ((node = walker.nextNode())) nodes.push(node);
    return nodes;
}

/**
 * Given visible start/end offsets and the text nodes of an overlay line,
 * return { startNode, startOffset, endNode, endOffset } suitable for
 * creating a Range.
 */
function findVisibleRangeInNodes(textNodes, visStart, visEnd) {
    let startNode = null, startOffset = 0;
    let endNode = null, endOffset = 0;
    let remaining = visStart;

    for (let i = 0; i < textNodes.length; i++) {
        const len = textNodes[i].textContent.length;
        if (remaining <= len && startNode === null) {
            startNode = textNodes[i];
            startOffset = remaining;
            remaining = visEnd - visStart; // reset for end search
        } else if (startNode !== null) {
            // we already found start, keep counting for end
            // (remaining was adjusted above)
        }
        if (startNode !== null) {
            if (remaining <= len) {
                endNode = textNodes[i];
                endOffset = remaining;
                break;
            }
            remaining -= len;
        } else {
            remaining -= len;
        }
    }

    // Fallback: past the end
    if (!startNode && textNodes.length > 0) {
        startNode = textNodes[0];
        startOffset = 0;
    }
    if (!endNode && startNode) {
        const last = textNodes[textNodes.length - 1];
        endNode = last;
        endOffset = last.textContent.length;
    }
    if (!endNode) endNode = startNode;
    if (!startNode) return null;

    return { startNode, startOffset, endNode, endOffset };
}

/**
 * Update the visible selection highlight on the overlay.
 * Creates/removes .md-sel-seg divs inside the selection layer.
 */
function updateSelectionHighlight(inst) {
    const { textarea, overlay, selectionLayer, lineMappings } = inst;
    const selStart = textarea.selectionStart;
    const selEnd = textarea.selectionEnd;

    if (selStart === selEnd || !selectionLayer) {
        if (selectionLayer) selectionLayer.innerHTML = '';
        return;
    }

    const text = textarea.value;

    // Determine line range
    const startLineIdx = text.substring(0, selStart).split('\n').length - 1;
    const endLineIdx = text.substring(0, selEnd).split('\n').length - 1;

    const overlayRect = overlay.getBoundingClientRect();
    const segments = [];

    for (let lineIdx = startLineIdx; lineIdx <= endLineIdx; lineIdx++) {
        const mapping = lineMappings[lineIdx];
        if (!mapping) continue;

        const lineEl = overlay.querySelector('[data-line-index="' + lineIdx + '"]');
        if (!lineEl) continue;

        // Source bounds of this line
        const lineSourceStart = mapping.sourceStart;
        const nextMapping = lineMappings[lineIdx + 1];
        const lineSourceEnd = nextMapping
            ? nextMapping.sourceStart - 1  // char before next line start
            : text.length - 1;

        // Intersection of selection with this line
        const lineSelStart = Math.max(selStart, lineSourceStart);
        const lineSelEnd = Math.min(selEnd, lineSourceEnd + 1);
        if (lineSelStart >= lineSelEnd) continue;

        // Convert to visible offsets
        const visStart = sourceToVisibleOffset(mapping.visibleToSource, lineSelStart, false);
        const visEnd = sourceToVisibleOffset(mapping.visibleToSource, lineSelEnd, true);

        if (visStart >= visEnd) continue;

        // Get bounding rect using Range API on the overlay text nodes
        const textNodes = collectTextNodes(lineEl);
        if (textNodes.length === 0) continue;

        const vr = findVisibleRangeInNodes(textNodes, visStart, visEnd);
        if (!vr) continue;

        try {
            const range = document.createRange();
            range.setStart(vr.startNode, vr.startOffset);
            range.setEnd(vr.endNode, vr.endOffset);

            const rects = range.getClientRects();
            // Merge adjacent rects that belong to the same visual line
            for (const rect of rects) {
                if (rect.width < 1) continue;
                segments.push({
                    top: rect.top - overlayRect.top,
                    left: rect.left - overlayRect.left,
                    width: rect.width,
                    height: rect.height,
                });
            }
        } catch {
            // ignore Range errors
        }
    }

    // Reuse or create segment elements
    let children = selectionLayer.children;
    while (children.length > segments.length) {
        selectionLayer.removeChild(children[children.length - 1]);
        children = selectionLayer.children;
    }

    for (let i = 0; i < segments.length; i++) {
        let seg = children[i];
        if (!seg) {
            seg = document.createElement('div');
            seg.className = 'md-sel-seg';
            selectionLayer.appendChild(seg);
        }
        seg.style.top = segments[i].top + 'px';
        seg.style.left = segments[i].left + 'px';
        seg.style.width = segments[i].width + 'px';
        seg.style.height = segments[i].height + 'px';
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Scroll mapping: textarea scrollTop ↔ overlay scrollTop
// ═══════════════════════════════════════════════════════════════════

function getLineContentOffsetTop(lineEl, overlayEl) {
    let top = 0;
    let el = lineEl;
    while (el && el !== overlayEl) {
        top += el.offsetTop;
        el = el.offsetParent;
    }
    return top;
}

function getLineHeightPx(lineEl) {
    return lineEl.offsetHeight;
}

function mapTextareaScrollToOverlay(textareaScrollTop, inst) {
    const lh = inst.lineHeight;
    if (lh <= 0) return 0;

    const allLines = inst.overlay.querySelectorAll('[data-line-index]');
    if (allLines.length === 0) return 0;

    const topLineFloat = textareaScrollTop / lh;
    const topLineIndex = Math.min(Math.floor(topLineFloat), allLines.length - 1);
    const fraction = topLineFloat - topLineIndex;
    const line = allLines[topLineIndex];

    if (topLineIndex === 0) {
        const lineTop = getLineContentOffsetTop(line, inst.overlay);
        const lineH = getLineHeightPx(line);
        return lineTop + fraction * lineH;
    }

    const prevLine = allLines[topLineIndex - 1];
    const prevBottom = getLineContentOffsetTop(prevLine, inst.overlay)
                     + getLineHeightPx(prevLine);
    const lineTop = getLineContentOffsetTop(line, inst.overlay);

    return prevBottom + fraction * (lineTop - prevBottom);
}

// ═══════════════════════════════════════════════════════════════════
//  Cursor simulation
// ═══════════════════════════════════════════════════════════════════

function updateCursor(inst) {
    const { textarea, overlay, cursorEl } = inst;

    const pos = textarea.selectionStart;
    const text = textarea.value;

    const textBefore = text.substring(0, pos);
    const lineIdx = textBefore.split('\n').length - 1;

    const allLines = overlay.querySelectorAll('[data-line-index]');
    let overlayLine = null;
    for (const el of allLines) {
        if (parseInt(el.dataset.lineIndex) === lineIdx) {
            overlayLine = el;
            break;
        }
    }

    // Y position and height
    const overlayRect = overlay.getBoundingClientRect();
    const paddingLeft = parseFloat(getComputedStyle(overlay).paddingLeft);

    if (!overlayLine) {
        // Empty editor or line not found — place cursor at top with fallback height
        cursorEl.style.top = '0px';
        cursorEl.style.height = inst.lineHeight + 'px';
        cursorEl.style.left = paddingLeft + 'px';
    } else {
        const heading = overlayLine.querySelector('.md-h');
        const lineRect = heading
            ? heading.getBoundingClientRect()
            : overlayLine.getBoundingClientRect();

        cursorEl.style.top = (lineRect.top - overlayRect.top) + 'px';
        cursorEl.style.height = (lineRect.height > 0 ? lineRect.height : inst.lineHeight) + 'px';

        // X position via Range API
        const mapping = inst.lineMappings[lineIdx];
        if (mapping && mapping.visibleToSource && mapping.visibleToSource.length > 0) {
            let visibleOffset = 0;
            const v2s = mapping.visibleToSource;
            for (let v = 0; v < v2s.length; v++) {
                if (v2s[v] >= pos) break;
                visibleOffset = v + 1;
            }
            if (v2s[visibleOffset - 1] === pos && visibleOffset > 0) {
                // cursor right after this visible char
            } else if (v2s.length > 0 && v2s[0] > pos) {
                visibleOffset = 0;
            }

            const x = getCaretXInOverlayLine(overlayLine, visibleOffset);
            cursorEl.style.left = (x + paddingLeft) + 'px';
        } else {
            cursorEl.style.left = paddingLeft + 'px';
        }
    }

    // Show/hide: hide during range selection, show for caret
    if (textarea.selectionStart !== textarea.selectionEnd) {
        cursorEl.style.display = 'none';
    } else {
        cursorEl.style.display = 'block';
    }

    updateSelectionHighlight(inst);
}

function getCaretXInOverlayLine(overlayLine, visibleOffset) {
    const walker = document.createTreeWalker(overlayLine, NodeFilter.SHOW_TEXT, null, false);
    let remaining = visibleOffset;
    let targetNode = null;
    let targetOffset = 0;
    let node;

    while ((node = walker.nextNode())) {
        const len = node.textContent.length;
        if (remaining <= len) {
            targetNode = node;
            targetOffset = remaining;
            break;
        }
        remaining -= len;
    }

    if (!targetNode) {
        walker.currentNode = overlayLine;
        let lastNode = null;
        while ((node = walker.nextNode())) lastNode = node;
        if (lastNode) {
            targetNode = lastNode;
            targetOffset = lastNode.textContent.length;
        }
    }

    if (!targetNode) return 0;

    try {
        const range = document.createRange();
        range.setStart(targetNode, targetOffset);
        range.collapse(true);
        const rect = range.getBoundingClientRect();
        const lineRect = overlayLine.getBoundingClientRect();
        return rect.left - lineRect.left;
    } catch {
        return 0;
    }
}

function startBlinking(inst) {
    stopBlinking(inst);
    inst.visible = true;
    inst.cursorEl.style.opacity = '1';
    inst.blinkTimer = setInterval(() => {
        inst.visible = !inst.visible;
        inst.cursorEl.style.opacity = inst.visible ? '1' : '0';
    }, 530);
}

function stopBlinking(inst) {
    if (inst.blinkTimer) {
        clearInterval(inst.blinkTimer);
        inst.blinkTimer = null;
    }
    inst.visible = true;
    inst.cursorEl.style.opacity = '1';
}

// ═══════════════════════════════════════════════════════════════════
//  Position mapping: overlay point → source offset
// ═══════════════════════════════════════════════════════════════════

function getCaretRangeFromPoint(x, y) {
    if (document.caretRangeFromPoint) {
        return document.caretRangeFromPoint(x, y);
    }
    if (document.caretPositionFromPoint) {
        const pos = document.caretPositionFromPoint(x, y);
        if (pos && pos.offsetNode) {
            const range = document.createRange();
            range.setStart(pos.offsetNode, pos.offset);
            range.collapse(true);
            return range;
        }
    }
    return null;
}

function getSourceOffsetFromPoint(inst, clientX, clientY) {
    const range = getCaretRangeFromPoint(clientX, clientY);
    const startNode = range?.startContainer;
    let lineEl = null;
    let visibleOffset = -1;

    if (startNode && startNode.nodeType === Node.TEXT_NODE) {
        lineEl = startNode.parentElement;
        while (lineEl && !lineEl.hasAttribute('data-line-index')) {
            lineEl = lineEl.parentElement;
        }
        if (lineEl) {
            const walker = document.createTreeWalker(lineEl, NodeFilter.SHOW_TEXT, null, false);
            let node;
            visibleOffset = 0;
            while ((node = walker.nextNode())) {
                if (node === startNode) {
                    visibleOffset += range.startOffset;
                    break;
                }
                visibleOffset += node.textContent.length;
            }
        }
    }

    if (!lineEl) {
        const allLines = inst.overlay.querySelectorAll('[data-line-index]');
        if (allLines.length === 0) return 0;
        const overlayRect = inst.overlay.getBoundingClientRect();
        const contentY = clientY - overlayRect.top + inst.overlay.scrollTop;
        let bestLine = allLines[allLines.length - 1];
        for (const line of allLines) {
            const lineTop = getLineContentOffsetTop(line, inst.overlay);
            if (lineTop > contentY + 2) break;
            bestLine = line;
        }
        lineEl = bestLine;
    }

    const lineIndex = parseInt(lineEl.dataset.lineIndex);
    const mapping = inst.lineMappings[lineIndex];
    if (!mapping) return -1;

    let sourceOffset;
    if (visibleOffset >= 0 && mapping.visibleToSource && mapping.visibleToSource.length > 0) {
        const v2s = mapping.visibleToSource;
        if (visibleOffset >= v2s.length) {
            sourceOffset = v2s[v2s.length - 1] + 1;
        } else if (visibleOffset <= 0) {
            sourceOffset = v2s[0];
        } else {
            sourceOffset = v2s[visibleOffset];
        }
    } else {
        sourceOffset = mapping.sourceStart;
        if (mapping.visibleToSource && mapping.visibleToSource.length > 0) {
            sourceOffset = mapping.visibleToSource[mapping.visibleToSource.length - 1] + 1;
        }
    }

    return sourceOffset;
}

// ═══════════════════════════════════════════════════════════════════
//  Native overlay selection → textarea selection sync
// ═══════════════════════════════════════════════════════════════════

/**
 * Given a text node and an offset within it, find the containing
 * overlay line element ([data-line-index]) and the total visible
 * character offset from the start of that line.
 */
function getVisibleOffsetFromTextNode(textNode, offset, inst) {
    let lineEl = textNode.parentElement;
    while (lineEl && !lineEl.hasAttribute('data-line-index')) {
        lineEl = lineEl.parentElement;
    }
    if (!lineEl) return { lineIndex: -1, visibleOffset: -1 };

    const lineIndex = parseInt(lineEl.dataset.lineIndex);
    const walker = document.createTreeWalker(lineEl, NodeFilter.SHOW_TEXT, null, false);
    let visibleOffset = 0;
    let node;
    while ((node = walker.nextNode())) {
        if (node === textNode) {
            visibleOffset += offset;
            return { lineIndex, visibleOffset };
        }
        visibleOffset += node.textContent.length;
    }
    return { lineIndex, visibleOffset };
}

/**
 * Convert a visible character offset on a line to a source offset
 * using the line's visibleToSource mapping.
 */
function visibleToSourceOffset(visibleToSource, visibleOffset) {
    if (!visibleToSource || visibleToSource.length === 0) return -1;
    if (visibleOffset <= 0) return visibleToSource[0];
    if (visibleOffset >= visibleToSource.length) {
        return visibleToSource[visibleToSource.length - 1] + 1;
    }
    return visibleToSource[visibleOffset];
}

/**
 * Sync native browser selection on the overlay back to the textarea.
 * Called on mouseup and selectionchange.
 */
function syncOverlaySelectionToTextarea(inst) {
    if (inst._syncingSelection) return;

    const sel = window.getSelection();
    if (!sel || sel.isCollapsed || sel.rangeCount === 0) return;

    const range = sel.getRangeAt(0);

    // Make sure selection is within the overlay
    if (!inst.overlay.contains(range.startContainer) || !inst.overlay.contains(range.endContainer)) return;

    // Get line index + visible offset for start
    const startInfo = range.startContainer.nodeType === Node.TEXT_NODE
        ? getVisibleOffsetFromTextNode(range.startContainer, range.startOffset, inst)
        : { lineIndex: -1, visibleOffset: -1 };

    // Get line index + visible offset for end
    const endInfo = range.endContainer.nodeType === Node.TEXT_NODE
        ? getVisibleOffsetFromTextNode(range.endContainer, range.endOffset, inst)
        : { lineIndex: -1, visibleOffset: -1 };

    if (startInfo.lineIndex < 0 || endInfo.lineIndex < 0) return;

    // Convert visible offsets to source offsets
    const startMapping = inst.lineMappings[startInfo.lineIndex];
    const endMapping = inst.lineMappings[endInfo.lineIndex];
    if (!startMapping || !endMapping) return;

    const sourceStart = visibleToSourceOffset(startMapping.visibleToSource, startInfo.visibleOffset);
    const sourceEnd = visibleToSourceOffset(endMapping.visibleToSource, endInfo.visibleOffset);

    if (sourceStart < 0 || sourceEnd < 0) return;

    inst._syncingSelection = true;
    inst.textarea.setSelectionRange(sourceStart, sourceEnd);
    updateCursor(inst);
    inst._syncingSelection = false;
}

// ═══════════════════════════════════════════════════════════════════
//  Scroll handling
// ═══════════════════════════════════════════════════════════════════

function syncOverlayFromTextarea(inst) {
    if (inst._updatingFromOverlay) return;
    inst._updatingFromTextarea = true;

    const overlayScroll = mapTextareaScrollToOverlay(inst.textarea.scrollTop, inst);
    inst.overlay.scrollTop = overlayScroll;

    requestAnimationFrame(() => {
        inst._updatingFromTextarea = false;
        updateCursor(inst);
    });
}

function handleOverlayWheel(inst, e) {
    e.preventDefault();
    let delta = e.deltaY;
    if (e.deltaMode === 1) delta *= inst.lineHeight;
    else if (e.deltaMode === 2) delta *= inst.overlay.clientHeight;

    inst._updatingFromOverlay = true;
    inst.textarea.scrollTop = Math.max(0, inst.textarea.scrollTop + delta);
    requestAnimationFrame(() => {
        inst._updatingFromOverlay = false;
    });
}

// ═══════════════════════════════════════════════════════════════════
//  Public API (called from Blazor)
// ═══════════════════════════════════════════════════════════════════

export function initEditor(id, editorBody, textarea, overlay, cursorEl, selectionLayer) {
    const inst = new EditorInstance(id, editorBody, textarea, overlay, cursorEl, selectionLayer);
    _instances.set(id, inst);
    window.__mdEditorInstances = _instances;

    const cs = getComputedStyle(textarea);
    inst.lineHeight = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize) * 1.5;

    // ── textarea event handlers ──────────────────────────────

    const onTextareaInput = () => {
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnInputFromJs', inst.textarea.value);
        }
    };

    const onTextareaClick = () => {
        updateCursor(inst);
        startBlinking(inst);
    };

    const onTextareaKeydown = (e) => {
        if (e.key === 'Tab') e.preventDefault();
        updateCursor(inst);
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnKeyDownFromJs', e.key, e.ctrlKey, e.metaKey);
        }
    };

    const onTextareaKeyup = () => {
        updateCursor(inst);
    };

    const onTextareaFocus = () => {
        updateCursor(inst);
        startBlinking(inst);
        cursorEl.style.display = 'block';
    };

    const onTextareaBlur = () => {
        stopBlinking(inst);
        cursorEl.style.display = 'none';
        if (inst.selectionLayer) inst.selectionLayer.innerHTML = '';
    };

    const onTextareaScroll = () => {
        syncOverlayFromTextarea(inst);
    };

    // ── overlay event handlers ──────────────────────────────

    const onOverlayWheel = (e) => {
        handleOverlayWheel(inst, e);
    };

    // ── Hybrid selection approach ──────────────────────────
    // We preventDefault on mousedown so the browser doesn't steal focus
    // from the textarea (which needs focus for keyboard input). Then we
    // programmatically create native Selection ranges on the overlay using
    // caretRangeFromPoint, which gives precise positioning even for
    // heading-sized text. On mouseup, we sync the programmatic selection
    // back to textarea source positions via visibleToSource mappings.

    const DRAG_THRESHOLD = 3;

    const onOverlayMousedown = (e) => {
        if (e.button !== 0) return;
        e.preventDefault(); // prevent browser from stealing focus

        // Focus the textarea so it receives keyboard input
        inst.textarea.focus();

        // Record start for drag detection
        inst._dragStartX = e.clientX;
        inst._dragStartY = e.clientY;
        inst._isDragging = false;

        // Position caret immediately on click
        const sourceOffset = getSourceOffsetFromPoint(inst, e.clientX, e.clientY);
        if (sourceOffset >= 0) {
            inst._dragAnchorOffset = sourceOffset;
            inst.textarea.setSelectionRange(sourceOffset, sourceOffset);
        } else {
            inst._dragAnchorOffset = inst.textarea.selectionStart;
        }

        // Clear any previous native overlay selection
        const sel = window.getSelection();
        if (sel) sel.removeAllRanges();

        updateCursor(inst);
        stopBlinking(inst);
        cursorEl.style.display = 'block';

        // Debug: log sourceOffset for diagnosis
        console.log('[mousedown] clientXY=' + e.clientX + ',' + e.clientY +
            ' sourceOffset=' + sourceOffset +
            ' sel=' + inst.textarea.selectionStart + ',' + inst.textarea.selectionEnd);
    };

    const onOverlayMousemove = (e) => {
        // Only react when left button is actually held down.
        // Use e.buttons (bitmask) because e.button is unreliable during
        // mousemove — it returns 0 even when no button is pressed.
        if (!(e.buttons & 1)) return;
        // Check if we've moved enough to start a drag
        if (!inst._isDragging) {
            if (inst._dragStartX == null || inst._dragStartY == null) return;
            const dx = Math.abs(e.clientX - inst._dragStartX);
            const dy = Math.abs(e.clientY - inst._dragStartY);
            if (dx < DRAG_THRESHOLD && dy < DRAG_THRESHOLD) return;
            inst._isDragging = true;
        }

        e.preventDefault();

        // Get the source offset at the current mouse position
        const currentOffset = getSourceOffsetFromPoint(inst, e.clientX, e.clientY);
        if (currentOffset < 0) return;

        // Update textarea selection
        const anchor = inst._dragAnchorOffset || 0;
        const selStart = Math.min(anchor, currentOffset);
        const selEnd = Math.max(anchor, currentOffset);
        inst.textarea.setSelectionRange(selStart, selEnd);
        updateCursor(inst);

        // Also create a programmatic native selection on the overlay
        // so the user sees the selection highlight on the rendered text.
        // Use caretRangeFromPoint for precise positioning (handles headings).
        const startRange = getCaretRangeFromPoint(
            inst._dragStartX || e.clientX, inst._dragStartY || e.clientY);
        const endRange = getCaretRangeFromPoint(e.clientX, e.clientY);

        if (startRange && endRange) {
            const sel = window.getSelection();
            if (sel) {
                sel.removeAllRanges();
                try {
                    // Determine direction
                    let range;
                    if (startRange.startContainer === endRange.startContainer &&
                        startRange.startOffset <= endRange.startOffset) {
                        range = document.createRange();
                        range.setStart(startRange.startContainer, startRange.startOffset);
                        range.setEnd(endRange.startContainer, endRange.startOffset);
                    } else {
                        range = document.createRange();
                        range.setStart(endRange.startContainer, endRange.startOffset);
                        range.setEnd(startRange.startContainer, startRange.startOffset);
                    }
                    sel.addRange(range);
                } catch {
                    // ignore Range errors
                }
            }
        }
    };

    const onOverlayMouseup = (e) => {
        if (e.button !== 0) return;

        if (!inst._isDragging) {
            // Single click — caret already positioned in mousedown
            startBlinking(inst);
            if (inst.dotNetRef) {
                inst.dotNetRef.invokeMethodAsync('OnCursorChangedFromJs');
            }
            return;
        }

        // End of drag — final sync
        inst._isDragging = false;

        // Final textarea selection sync
        const currentOffset = getSourceOffsetFromPoint(inst, e.clientX, e.clientY);
        if (currentOffset >= 0) {
            const anchor = inst._dragAnchorOffset || 0;
            const selStart = Math.min(anchor, currentOffset);
            const selEnd = Math.max(anchor, currentOffset);
            inst.textarea.setSelectionRange(selStart, selEnd);
        }
        updateCursor(inst);

        // Clear the native overlay selection (we use .md-sel-seg highlights instead)
        const sel = window.getSelection();
        if (sel) sel.removeAllRanges();

        // Re-focus textarea (ensure it wasn't lost)
        inst.textarea.focus();

        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnCursorChangedFromJs');
        }
    };

    // Handle mouseup outside overlay (drag released outside)
    const onDocumentMouseup = (e) => {
        if (!inst._isDragging) return;
        inst._isDragging = false;

        // Clear native selection
        const sel = window.getSelection();
        if (sel) sel.removeAllRanges();

        inst.textarea.focus();
        updateCursor(inst);
        if (inst.dotNetRef) {
            inst.dotNetRef.invokeMethodAsync('OnCursorChangedFromJs');
        }
    };

    // ── register handlers ────────────────────────────────────

    textarea.addEventListener('input', onTextareaInput);
    textarea.addEventListener('click', onTextareaClick);
    textarea.addEventListener('keydown', onTextareaKeydown);
    textarea.addEventListener('keyup', onTextareaKeyup);
    textarea.addEventListener('focus', onTextareaFocus);
    textarea.addEventListener('blur', onTextareaBlur);
    textarea.addEventListener('scroll', onTextareaScroll);

    overlay.addEventListener('mousedown', onOverlayMousedown);
    overlay.addEventListener('mousemove', onOverlayMousemove);
    overlay.addEventListener('mouseup', onOverlayMouseup);
    overlay.addEventListener('wheel', onOverlayWheel, { passive: false });
    document.addEventListener('mouseup', onDocumentMouseup);

    inst._boundHandlers = [
        { target: textarea, event: 'input',     handler: onTextareaInput },
        { target: textarea, event: 'click',     handler: onTextareaClick },
        { target: textarea, event: 'keydown',   handler: onTextareaKeydown },
        { target: textarea, event: 'keyup',     handler: onTextareaKeyup },
        { target: textarea, event: 'focus',     handler: onTextareaFocus },
        { target: textarea, event: 'blur',      handler: onTextareaBlur },
        { target: textarea, event: 'scroll',    handler: onTextareaScroll },
        { target: overlay,  event: 'mousedown', handler: onOverlayMousedown },
        { target: overlay,  event: 'mousemove', handler: onOverlayMousemove },
        { target: overlay,  event: 'mouseup',   handler: onOverlayMouseup },
        { target: overlay,  event: 'wheel',     handler: onOverlayWheel, options: { passive: false } },
        { target: document, event: 'mouseup',   handler: onDocumentMouseup },
    ];

    return true;
}

// ── debug helper (called from tests) ────────────────────
export function debugGetSourceOffset(id, clientX, clientY) {
    const inst = _instances.get(id);
    if (!inst) return JSON.stringify({ error: 'instance not found' });
    const offset = getSourceOffsetFromPoint(inst, clientX, clientY);
    const mappingCount = inst.lineMappings ? inst.lineMappings.length : 0;
    return JSON.stringify({
        offset: offset,
        lineMappingsCount: mappingCount,
        lineMappings: inst.lineMappings ? inst.lineMappings.map(m => ({
            sourceStart: m.sourceStart,
            v2sLen: m.visibleToSource ? m.visibleToSource.length : 0
        })) : []
    });
}

export function debugGetInstance(id) {
    const inst = _instances.get(id);
    if (!inst) return JSON.stringify({ error: 'instance not found' });
    return JSON.stringify({
        id: inst.id,
        lineMappingsCount: inst.lineMappings ? inst.lineMappings.length : 0,
        textareaValue: inst.textarea ? inst.textarea.value.substring(0, 50) : '',
        textareaFocused: inst.textarea ? document.activeElement === inst.textarea : false
    });
}

export function updateMappings(id, mappings) {
    const inst = _instances.get(id);
    if (!inst) return;
    inst.lineMappings = mappings;
}

export function setDotNetRef(id, dotNetRef) {
    const inst = _instances.get(id);
    if (!inst) return;
    inst.dotNetRef = dotNetRef;
}

export function getSelection(id) {
    const inst = _instances.get(id);
    if (!inst) return { start: 0, end: 0 };
    return {
        start: inst.textarea.selectionStart,
        end: inst.textarea.selectionEnd,
    };
}

export function setSelection(id, start, end) {
    const inst = _instances.get(id);
    if (!inst) return;
    inst.textarea.focus();
    inst.textarea.setSelectionRange(start, end);
}

export function setTextValue(id, value) {
    const inst = _instances.get(id);
    if (!inst) return;
    inst.textarea.value = value;
}

export function updateCursorPosition(id) {
    const inst = _instances.get(id);
    if (!inst) return;
    requestAnimationFrame(() => {
        updateCursor(inst);
        syncOverlayFromTextarea(inst);
        startBlinking(inst);
    });
}

export function nativeUndo(id) {
    const inst = _instances.get(id);
    if (!inst) return;
    inst.textarea.focus();
    document.execCommand('undo');
    updateCursor(inst);
}

export function nativeRedo(id) {
    const inst = _instances.get(id);
    if (!inst) return;
    inst.textarea.focus();
    document.execCommand('redo');
    updateCursor(inst);
}

export function dispose(id) {
    const inst = _instances.get(id);
    if (inst) {
        inst.dispose();
        _instances.delete(id);
    }
}
