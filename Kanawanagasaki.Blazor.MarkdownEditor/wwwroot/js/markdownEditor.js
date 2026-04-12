/**
 * Kanawanagasaki.Blazor.MarkdownEditor – JavaScript interop layer
 *
 * Architecture:
 *  - Textarea: transparent, pointer-events:none, overflow:auto (hidden scrollbar).
 *    Acts as the scroll model. Browser auto-scrolls it to keep cursor visible.
 *  - Overlay: pointer-events:auto, overflow-y:auto. Captures clicks and wheel.
 *    Users can natively select text on the rendered overlay via programmatic
 *    Selection ranges created during mousedown/mousemove/mouseup. On mouseup,
 *    the native overlay selection is kept and synced to textarea source offsets
 *    via visibleToSource mappings.
 *  - Cursor: pointer-events:none, absolutely positioned. Placed at the overlay
 *    line's viewport position. X calculated via Range.getBoundingClientRect().
 */

const _instances = new Map();

class EditorInstance {
    constructor(id, editorBody, textarea, overlay, cursorEl) {
        this.id = id;
        this.editorBody = editorBody;
        this.textarea = textarea;
        this.overlay = overlay;
        this.cursorEl = cursorEl;
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
//  Selection: native browser Selection API on overlay
// ═══════════════════════════════════════════════════════════════════

/**
 * Map a source offset to a visible character offset using the line's
 * visibleToSource mapping.  Invisible source characters (syntax markers
 * like **, ##, `) are skipped so the highlight covers only visible text.
 */
function sourceToVisibleOffset(visibleToSource, sourceOffset) {
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
 * Given a visible character offset and text nodes of a line,
 * return { node, offset } for creating a Range endpoint.
 */
function findNodeAtVisibleOffset(textNodes, visibleOffset) {
    let remaining = visibleOffset;
    for (const node of textNodes) {
        const len = node.textContent.length;
        if (remaining <= len) {
            return { node, offset: remaining };
        }
        remaining -= len;
    }
    // Past the end — clamp to last node
    if (textNodes.length > 0) {
        const last = textNodes[textNodes.length - 1];
        return { node: last, offset: last.textContent.length };
    }
    return null;
}

/**
 * Sync the textarea's selection range to a native browser Selection
 * on the overlay.  Creates a single Range spanning from the visible
 * character at selStart to the visible character at selEnd.
 */
function syncTextareaSelectionToOverlay(inst) {
    const { textarea, overlay, lineMappings } = inst;
    const selStart = textarea.selectionStart;
    const selEnd = textarea.selectionEnd;

    if (selStart === selEnd) {
        // No range selection — clear any native selection
        const sel = window.getSelection();
        if (sel) sel.removeAllRanges();
        return;
    }

    const text = textarea.value;

    // Determine line range
    const startLineIdx = text.substring(0, selStart).split('\n').length - 1;
    const endLineIdx = text.substring(0, selEnd).split('\n').length - 1;

    const startMapping = lineMappings[startLineIdx];
    const endMapping = lineMappings[endLineIdx];
    if (!startMapping || !endMapping) return;

    // Convert source offsets to visible offsets per line
    const visStart = sourceToVisibleOffset(startMapping.visibleToSource, selStart);
    const visEnd = sourceToVisibleOffset(endMapping.visibleToSource, selEnd);

    // Find DOM elements for start and end lines
    const startLineEl = overlay.querySelector('[data-line-index="' + startLineIdx + '"]');
    const endLineEl = overlay.querySelector('[data-line-index="' + endLineIdx + '"]');
    if (!startLineEl || !endLineEl) return;

    const startNodes = collectTextNodes(startLineEl);
    const endNodes = collectTextNodes(endLineEl);
    if (startNodes.length === 0 || endNodes.length === 0) return;

    const startPos = findNodeAtVisibleOffset(startNodes, visStart);
    const endPos = findNodeAtVisibleOffset(endNodes, visEnd);
    if (!startPos || !endPos) return;

    try {
        const range = document.createRange();
        range.setStart(startPos.node, startPos.offset);
        range.setEnd(endPos.node, endPos.offset);

        const sel = window.getSelection();
        if (sel) {
            sel.removeAllRanges();
            sel.addRange(range);
        }
    } catch {
        // ignore Range errors (e.g. detached nodes during re-render)
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
    const paddingTop = parseFloat(getComputedStyle(overlay).paddingTop);
    const paddingLeft = parseFloat(getComputedStyle(overlay).paddingLeft);

    if (!overlayLine) {
        // Empty editor or line not found — place cursor at top with fallback height
        cursorEl.style.top = paddingTop + 'px';
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

    // Sync native overlay selection from textarea selection
    syncTextareaSelectionToOverlay(inst);
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

export function initEditor(id, editorBody, textarea, overlay, cursorEl) {
    const inst = new EditorInstance(id, editorBody, textarea, overlay, cursorEl);
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
        // Clear native overlay selection on blur
        const sel = window.getSelection();
        if (sel) sel.removeAllRanges();
    };

    const onTextareaScroll = () => {
        syncOverlayFromTextarea(inst);
    };

    // ── overlay event handlers ──────────────────────────────

    const onOverlayWheel = (e) => {
        handleOverlayWheel(inst, e);
    };

    // ── Selection via mousedown / mousemove / mouseup ───────
    // We preventDefault on mousedown so the browser doesn't steal focus
    // from the textarea. During drag (mousemove), we update the textarea
    // selection and let updateCursor → syncTextareaSelectionToOverlay
    // create the native browser Selection on the overlay for visual
    // feedback. On mouseup we keep the native selection (no clearing).

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
    };

    const onOverlayMousemove = (e) => {
        // Only react when left button is actually held down.
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
        // updateCursor will call syncTextareaSelectionToOverlay
        // which creates the native browser Selection on the overlay
        updateCursor(inst);
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
        // updateCursor will sync native overlay selection via
        // syncTextareaSelectionToOverlay — keep it, don't clear
        updateCursor(inst);

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
