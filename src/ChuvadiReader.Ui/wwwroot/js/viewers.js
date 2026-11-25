// Helpers for the view-only viewers (Components/Viewers): scroll position → counter, in-document
// anchor links, go-to, and Find highlighting via the CSS Custom Highlight API (no DOM mutation, so
// Blazor's render tree is never disturbed). One module instance serves every viewer on the page.

const state = new WeakMap();      // scroll container → { dotnet, mode, last, raf, onScroll, onClick }
const hits = new Map();           // scroll container → Range[]
const HL = 'chvd-find';
let styled = false;

function ensureStyle() {
    if (styled) return;
    styled = true;
    const s = document.createElement('style');
    s.textContent = `::highlight(${HL}){background-color:rgba(196,116,22,.38);color:inherit}`;
    document.head.appendChild(s);
}

export function attach(el, dotnet, mode) {
    if (!el) return;
    detach(el);
    const st = { dotnet, mode, last: -1, raf: 0 };
    st.onScroll = () => {
        if (st.raf) return;
        st.raf = requestAnimationFrame(() => { st.raf = 0; report(el, st); });
    };
    st.onClick = (e) => {
        const a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (!a || !el.contains(a)) return;
        e.preventDefault(); // keeps Blazor's router from treating "#slug" as a navigation, and stops every link
        const anchor = a.getAttribute('data-anchor-link');
        if (anchor !== null) { scrollToAnchor(el, anchor); return; }
        // BUG-01: a web or mail link asks first (as the HTML viewer does); nothing opens on its own.
        const href = a.getAttribute('href') || '';
        if (/^(https?:|mailto:)/i.test(href)) dotnet.invokeMethodAsync('OnExternalLink', href).catch(() => { });
    };
    el.addEventListener('scroll', st.onScroll, { passive: true });
    el.addEventListener('click', st.onClick);
    state.set(el, st);
    requestAnimationFrame(() => report(el, st));
}

export function detach(el) {
    if (!el) return;
    const st = state.get(el);
    if (st) {
        el.removeEventListener('scroll', st.onScroll);
        el.removeEventListener('click', st.onClick);
        if (st.raf) cancelAnimationFrame(st.raf);
        state.delete(el);
    }
    clearHighlight(el);
}

function report(el, st) {
    if (!el.isConnected) return;
    const top = el.getBoundingClientRect().top;
    let idx = 1;
    if (st.mode === 'lines') {
        for (const it of el.querySelectorAll('[data-ln]')) {
            if (it.getBoundingClientRect().bottom > top + 1) { idx = parseInt(it.dataset.ln, 10) || 1; break; }
        }
    } else {
        const secs = el.querySelectorAll('[data-sec]');
        for (const s of secs) {
            if (s.getBoundingClientRect().top - top <= 48) idx = parseInt(s.dataset.sec, 10) || 1;
            else break;
        }
        if (secs.length && el.scrollTop > 0 && el.scrollTop + el.clientHeight >= el.scrollHeight - 2)
            idx = parseInt(secs[secs.length - 1].dataset.sec, 10) || idx;
    }
    if (idx !== st.last) {
        st.last = idx;
        st.dotnet.invokeMethodAsync('OnViewerScroll', idx).catch(() => { });
    }
}

function cssEscape(v) { return (window.CSS && CSS.escape) ? CSS.escape(v) : String(v).replace(/["\\]/g, '\\$&'); }

export function scrollToAnchor(el, anchor) {
    if (!el || !anchor) return false;
    const t = el.querySelector(`[data-anchor="${cssEscape(anchor)}"]`)
        || el.querySelector(`[data-anchor="${cssEscape(String(anchor).toLowerCase())}"]`);
    if (!t) return false;
    t.scrollIntoView({ block: 'start' });
    return true;
}

export function scrollToSection(el, index) {
    if (!el) return;
    if (index <= 1 && !el.querySelector('[data-sec="1"]')) { el.scrollTop = 0; return; }
    const t = el.querySelector(`[data-sec="${index | 0}"]`);
    if (t) t.scrollIntoView({ block: 'start' });
    else if (index <= 1) el.scrollTop = 0;
}

// Virtualized lines: jump near the line by arithmetic, then settle on the real element once rendered.
export function scrollToLine(el, line, approxPx, center) {
    if (!el) return;
    const page = el.firstElementChild;
    const pad = page ? page.offsetTop + (parseFloat(getComputedStyle(page).paddingTop) || 0) : 0;
    el.scrollTop = Math.max(0, (line - 1) * approxPx + pad - (center ? el.clientHeight / 2 : 0));
    let tries = 0;
    const settle = () => {
        const t = el.querySelector(`[data-ln="${line | 0}"]`);
        if (t) { t.scrollIntoView({ block: center ? 'center' : 'start' }); return; }
        if (++tries < 15) requestAnimationFrame(settle);
    };
    requestAnimationFrame(settle);
}

/** Scrolls a grid's container to a vertical offset (virtualised rows: bring a row on screen first). */
// The Excel grid is drawn as a window onto the sheet (E-05): this tells the view which part is on screen, once
// now and then whenever it is scrolled or resized (at most once a frame). Returns true once it is on.
export function gridWindow(el, dotnetRef) {
    if (!el || !el.isConnected) return false;
    if (el._chvdWindow) { el._chvdWindow(true); return true; }
    let frame = 0, last = '';
    const report = (force) => {
        frame = 0;
        const left = Math.abs(el.scrollLeft), top = el.scrollTop, w = el.clientWidth, h = el.clientHeight;
        const key = `${left | 0},${top | 0},${w},${h}`;
        if (!force && key === last) return;
        last = key;
        dotnetRef.invokeMethodAsync('OnGridWindow', left, top, w, h).catch(() => { });
    };
    const queue = () => { if (!frame) frame = requestAnimationFrame(() => report(false)); };
    el.addEventListener('scroll', queue, { passive: true });
    if (window.ResizeObserver) new ResizeObserver(queue).observe(el);
    el._chvdWindow = report;
    report(true);
    return true;
}

// Another sheet starts at its top-left corner.
export function scrollGridHome(el) {
    if (!el) return;
    el.scrollTop = 0;
    el.scrollLeft = 0;
    if (el._chvdWindow) el._chvdWindow(true);
    // The value bar showed a cell of the sheet that was on screen before.
    const bar = el.parentElement && el.parentElement.querySelector('.xv-valbar');
    if (bar) bar.hidden = true;
}

// Brings a place on the sheet (x, y from the corner of the part that scrolls, in screen pixels; -1 = leave that
// direction alone, the cell is in the frozen part) to the middle of the view. headW, headH: the part that stays put.
export function scrollGridToCell(el, x, y, headW, headH) {
    if (!el) return;
    const rtl = getComputedStyle(el).direction === 'rtl';
    if (y >= 0) el.scrollTop = Math.max(0, y - (el.clientHeight - headH) / 2);
    if (x >= 0) {
        const left = Math.max(0, x - (el.clientWidth - headW) / 2);
        el.scrollLeft = rtl ? -left : left;
    }
}

// ── D-190: column widths and row heights for viewing only ───────────────────────────────────────────────────────
// Drag the far edge of a column letter or the bottom edge of a row number to resize it; press that edge twice to fit
// it. Nothing is written to the file: the new size goes to .NET (OnGridResize / OnGridFit), which redraws the grid.
// One set of listeners on the grid; nothing per header. Mouse, pen and finger (a finger gets a wider edge).

// Some engines (older WebKit) report an element's rectangle under CSS zoom in its own unzoomed pixels; pointer
// events are in screen pixels. The engine is asked once which it does (as reader.js does for marks, M-27).
let chvdUnzoomedRects = null;
function screenRect(el) {
    if (chvdUnzoomedRects === null) {
        const probe = document.createElement('div');
        probe.style.cssText = 'position:fixed;left:0;top:0;width:10px;height:10px;zoom:2;visibility:hidden;pointer-events:none';
        document.body.appendChild(probe);
        chvdUnzoomedRects = Math.abs(probe.getBoundingClientRect().width - 10) < 0.5;
        probe.remove();
    }
    const r = el.getBoundingClientRect();
    if (!chvdUnzoomedRects) return { left: r.left, top: r.top, width: r.width, height: r.height };
    let zoom = 1;
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) {
        const z = parseFloat(getComputedStyle(e).zoom);
        if (isFinite(z) && z > 0) zoom *= z;
    }
    return { left: r.left * zoom, top: r.top * zoom, width: r.width * zoom, height: r.height * zoom };
}

// touchCapable (M-29): false where the host has no touch screen (the Mac). Mac Catalyst sometimes hands WebKit a
// mouse drag as a "touch" pointer (seen on macOS 15 Intel, and once on macOS 26); there every pointer is a mouse.
export function gridResize(el, dotnetRef, touchCapable) {
    if (!el || !el.isConnected) return false;
    if (el._chvdResize) { el._chvdResize.dotnet = dotnetRef; el._chvdResize.touchCapable = touchCapable !== false; return true; }
    const st = el._chvdResize = { dotnet: dotnetRef, drag: null, lastPress: null, touchCapable: touchCapable !== false };
    const isFinger = (e) => e.pointerType === 'touch' && st.touchCapable;
    const SMALLEST = 8;

    // The header edge under a point: { column, index, hdr, size (screen px), scale (screen px per px at 100 %) }.
    const edgeAt = (e) => {
        const hdr = e.target && e.target.closest ? e.target.closest('.xv-colhdr, .xv-rowhdr') : null;
        if (!hdr || !el.contains(hdr)) return null;
        const column = hdr.classList.contains('xv-colhdr');
        const reach = isFinger(e) ? 14 : 5;
        const rtl = getComputedStyle(el).direction === 'rtl';
        const pick = (h) => {
            const r = screenRect(h), own = parseFloat(column ? h.style.width : h.style.height);
            const size = column ? r.width : r.height;
            return { column, hdr: h, index: parseInt(h.getAttribute(column ? 'data-c' : 'data-r'), 10), size, scale: own > 0 ? size / own : 1, rect: r };
        };
        const me = pick(hdr);
        if (!(me.index > 0)) return null;
        const far = column ? (rtl ? me.rect.left : me.rect.left + me.rect.width) : me.rect.top + me.rect.height;
        const near = column ? (rtl ? me.rect.left + me.rect.width : me.rect.left) : me.rect.top;
        const at = column ? e.clientX : e.clientY;
        if (Math.abs(at - far) <= reach) return me;
        // Just past the edge of the one before (still inside this header): that edge belongs to the one before.
        if (Math.abs(at - near) <= reach) {
            const before = hdr.previousElementSibling;
            if (before && before.classList.contains(column ? 'xv-colhdr' : 'xv-rowhdr')) { const b = pick(before); if (b.index > 0) return b; }
        }
        return null;
    };

    const guide = (d, at) => {
        if (!d.line) {
            d.line = document.createElement('div');
            d.line.style.cssText = 'position:fixed;z-index:50;background:var(--chvd-binding,#8a3b12);pointer-events:none';
            document.body.appendChild(d.line);
        }
        const w = el.getBoundingClientRect();
        if (d.column) d.line.style.cssText += `;left:${at}px;top:${w.top}px;width:1px;height:${w.height}px`;
        else d.line.style.cssText += `;top:${at}px;left:${w.left}px;height:1px;width:${w.width}px`;
    };

    const sizeNow = (d, e) => {
        const moved = d.column ? (e.clientX - d.start) * d.dir : e.clientY - d.start;
        return Math.max(SMALLEST, (d.size + moved) / d.scale);
    };

    const finish = (commit, e) => {
        const d = st.drag;
        if (!d) return;
        st.drag = null;
        if (d.line) d.line.remove();
        el.classList.remove('xv-resizing');
        try { el.releasePointerCapture(d.id); } catch { }
        if (commit && d.moved) st.dotnet.invokeMethodAsync('OnGridResize', d.column, d.index, sizeNow(d, e)).catch(() => { });
    };

    // D-193: by finger, an edge is taken by resting on it (HOLD_MS); a finger that moves at once scrolls the sheet.
    // The guide line appears when the hold has taken. Mouse and pen take the edge at once, as before.
    const HOLD_MS = 350, HOLD_SLOP = 8;
    const dropHold = () => { if (st.hold) { clearTimeout(st.hold.timer); st.hold = null; } };
    const take = (edge, id, x, y) => {
        const rtl = getComputedStyle(el).direction === 'rtl';
        st.drag = { column: edge.column, index: edge.index, size: edge.size, scale: edge.scale, id,
            start: edge.column ? x : y, dir: edge.column && rtl ? -1 : 1, moved: false, line: null };
        try { el.setPointerCapture(id); } catch { }
        el.classList.add('xv-resizing');
    };

    el.addEventListener('pointerdown', (e) => {
        // A second finger while an edge is held is a pinch (the sheet zooms): the resize is dropped, nothing changes size.
        if (st.drag && isFinger(e) && e.pointerId !== st.drag.id) { st.lastPress = null; finish(false, e); return; }
        if (st.hold && e.pointerId !== st.hold.id) { st.lastPress = null; dropHold(); return; }
        if (e.button > 0 || st.drag) return;
        const edge = edgeAt(e);
        if (!edge) { st.lastPress = null; return; }
        const finger = isFinger(e);
        if (!finger) e.preventDefault();
        const now = performance.now(), last = st.lastPress;
        if (last && last.column === edge.column && last.index === edge.index && now - last.time < 450) {
            // The second press on the same edge: fit it.
            st.lastPress = null;
            dropHold();
            e.preventDefault();
            st.dotnet.invokeMethodAsync('OnGridFit', edge.column, edge.index).catch(() => { });
            return;
        }
        st.lastPress = { column: edge.column, index: edge.index, time: now };
        if (finger) {
            const id = e.pointerId, x = e.clientX, y = e.clientY;
            dropHold();
            st.hold = { id, x, y, timer: setTimeout(() => {
                if (!st.hold || st.hold.id !== id) return;
                st.hold = null;
                take(edge, id, x, y);
                guide(st.drag, st.drag.start);      // the hold has taken: the line shows it
            }, HOLD_MS) };
            return;
        }
        take(edge, e.pointerId, e.clientX, e.clientY);
    });

    el.addEventListener('pointermove', (e) => {
        // A finger that moves before the hold has taken is scrolling: the edge is let go.
        if (st.hold && e.pointerId === st.hold.id && Math.hypot(e.clientX - st.hold.x, e.clientY - st.hold.y) > HOLD_SLOP) { dropHold(); st.lastPress = null; }
        const d = st.drag;
        if (!d) {
            if (!isFinger(e) && e.pointerType !== 'pen') {
                const edge = edgeAt(e);
                el.style.cursor = edge ? (edge.column ? 'col-resize' : 'row-resize') : '';
            }
            return;
        }
        if (e.pointerId !== d.id) {
            // M-29 (macOS 15 Intel): one mouse press gives a mouse pointerdown and a "touch" pointerdown, and every
            // move comes on the touch pointer. Where the host has no touch screen every pointer is the mouse, so the
            // drag follows whichever pointer moves.
            if (st.touchCapable) return;
            d.id = e.pointerId;
            try { el.setPointerCapture(d.id); } catch { }
        }
        const at = d.column ? e.clientX : e.clientY;
        if (!d.moved && Math.abs(at - d.start) < 3) return;   // a press, not a drag (it may become a double press)
        d.moved = true;
        st.lastPress = null;
        e.preventDefault();
        // The line shows where the edge will be; it stops where the smallest size is.
        const edgeNow = d.start + (sizeNow(d, e) * d.scale - d.size) * (d.column ? d.dir : 1);
        guide(d, edgeNow);
    });

    // M-29: on a host with no touch screen the release may come on the other pointer of the pair, too.
    const ours = (e) => st.drag && (e.pointerId === st.drag.id || !st.touchCapable);
    el.addEventListener('pointerup', (e) => { if (st.hold && e.pointerId === st.hold.id) dropHold(); if (ours(e)) finish(true, e); });
    el.addEventListener('pointercancel', (e) => { if (st.hold && e.pointerId === st.hold.id) dropHold(); if (ours(e) && e.pointerId === st.drag.id) finish(false, e); });
    el.addEventListener('keydown', (e) => { if (e.key === 'Escape' && st.drag) finish(false, e); });
    // Once a finger has taken an edge its moves resize; they must not scroll the sheet as well. Until then they may.
    el.addEventListener('touchmove', (e) => { if (st.drag) e.preventDefault(); }, { passive: false });
    // A long rest on an edge is the resize, not the browser's long-press menu.
    el.addEventListener('contextmenu', (e) => { if (st.hold || st.drag) e.preventDefault(); });
    return true;
}

// The room the cells in the measuring box need, at 100 %: the widest (a column) or the tallest (a row). 0: none.
export function gridMeasure(box, column) {
    if (!box || !box.isConnected) return 0;
    let need = 0;
    for (const item of box.children) need = Math.max(need, column ? item.offsetWidth : item.offsetHeight);
    return need;
}

// D-161: a spreadsheet cell shows its display text; clicking or tapping it shows the value the file stores in the bar
// above the grid (hovering shows it too, as the cell's title). One listener on the grid, nothing per cell.
// Returns true once the listener is on, so the caller can ask again after a render that had no grid yet.
export function gridValueBar(el) {
    if (!el || !el.isConnected) return false;
    if (el._chvdValueBar) return true;
    const bar = el.parentElement && el.parentElement.querySelector('.xv-valbar');
    if (!bar) return false;
    let sel = null;
    el._chvdValueBar = (e) => {
        const td = e.target && e.target.closest ? e.target.closest('.xv-cell') : null;
        if (sel) sel.classList.remove('sel');
        // An empty cell is not an element (only cells that hold something are drawn): a click there clears the bar.
        if (!td || !el.contains(td) || !td.hasAttribute('data-a')) { sel = null; bar.hidden = true; return; }
        sel = td;
        td.classList.add('sel');
        bar.querySelector('.xv-val-addr').textContent = td.getAttribute('data-a');
        bar.querySelector('.xv-val-text').textContent = td.getAttribute('data-v') || '';
        // E-15: the cell's link and its comment, as the file holds them (shown, never followed).
        const more = bar.querySelector('.xv-val-more');
        if (more) {
            const link = td.getAttribute('data-l'), note = td.getAttribute('data-n');
            more.textContent = [link ? 'Link: ' + link : null, note].filter(Boolean).join('   ·   ');
        }
        bar.hidden = false;
    };
    el.addEventListener('click', el._chvdValueBar);
    return true;
}

export function scrollToSelector(el, selector) {
    if (!el) return;
    const t = el.querySelector(selector);
    if (t) t.scrollIntoView({ block: 'center', inline: 'center' });
}

function rebuildHighlight() {
    if (!(window.CSS && CSS.highlights && window.Highlight)) return;
    const all = [];
    for (const [el, rs] of hits) { if (el.isConnected) all.push(...rs); else hits.delete(el); }
    if (all.length) CSS.highlights.set(HL, new Highlight(...all));
    else CSS.highlights.delete(HL);
}

export function clearHighlight(el) {
    if (hits.delete(el)) rebuildHighlight();
}

// Highlights every case-insensitive match of `query` in the visible text under `root` and returns
// the count; scrolls the first match into view of `el` (the scroll container).
export function highlight(el, root, query) {
    if (!el) return 0;
    hits.delete(el);
    const ranges = [];
    const q = (query || '').toLowerCase();
    if (q && root) {
        ensureStyle();
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
            acceptNode: n => (n.parentElement && n.parentElement.closest('style,script')) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
        });
        let n;
        while ((n = walker.nextNode()) && ranges.length < 20000) {
            const raw = n.data;
            const low = raw.toLowerCase();
            const hay = low.length === raw.length ? low : raw;
            const needle = low.length === raw.length ? q : query;
            let i = hay.indexOf(needle);
            while (i >= 0) {
                const r = document.createRange();
                r.setStart(n, i);
                r.setEnd(n, i + needle.length);
                ranges.push(r);
                i = hay.indexOf(needle, i + needle.length);
            }
        }
    }
    if (ranges.length) hits.set(el, ranges);
    rebuildHighlight();
    if (ranges.length) {
        const t = ranges[0].startContainer.parentElement;
        if (t) t.scrollIntoView({ block: 'center' });
    }
    return ranges.length;
}

// ── HTML viewer frame (decision 2026-11-06, option B) ──────────────────────────────────────
// The frame is sandbox="allow-same-origin" — never allow-scripts — so nothing inside it can run,
// but the app can read its layout and scroll it. Every listener below lives in the app's own
// script realm. State hangs off the viewer's root element (always rendered), not the <iframe>:
// Blazor may create the frame again (Source view, reload) and each srcdoc change reloads it, so
// the frame is found afresh and re-hooked on every 'load' (caught in the capture phase).
const frames = new WeakMap();     // viewer root → { dotnet, anchors, pendingHit, pendingAnchor, ... }
const FRAME_SEL = 'iframe.htv-frame';

export function frameAttach(root, dotnet, anchors) {
    if (!root || !root.addEventListener) return;
    frameDetach(root);
    const st = { dotnet, anchors: anchors || [], pendingHit: false, pendingAnchor: null, last: -1, raf: 0, frame: null, win: null, doc: null, hookedSrc: null };
    st.onLoad = (e) => { if (e.target && e.target.matches && e.target.matches(FRAME_SEL)) hookFrame(e.target, st); };
    root.addEventListener('load', st.onLoad, true);
    frames.set(root, st);
    const f = root.querySelector(FRAME_SEL);
    if (f) hookFrame(f, st);
}

export function frameDetach(root) {
    if (!root) return;
    const st = frames.get(root);
    if (!st) return;
    root.removeEventListener('load', st.onLoad, true);
    unhookFrame(st);
    frames.delete(root);
}

// After a re-sanitize (Find, text size, Load images): new anchors and, for Find, go to the first hit.
export function frameUpdate(root, anchors, scrollToHit) {
    const st = root && frames.get(root);
    if (!st) return;
    st.anchors = anchors || [];
    st.pendingHit = !!scrollToHit;
    // Usually the new srcdoc is still loading and 'load' applies this; if it already loaded, apply now.
    const f = root.querySelector(FRAME_SEL);
    if (f && st.frame === f && st.doc && st.hookedSrc === f.getAttribute('srcdoc')) hookFrame(f, st);
}

export function frameScrollToAnchor(root, anchor) {
    const st = root && frames.get(root);
    if (!st) return false;
    const f = root.querySelector(FRAME_SEL);
    const doc = f && f.contentDocument;
    if (!doc || doc.readyState !== 'complete' || st.frame !== f) { st.pendingAnchor = anchor; return true; }
    return goAnchor(doc, anchor);
}

export function frameScrollTop(root) {
    const f = root && root.querySelector(FRAME_SEL);
    const w = f && f.contentWindow;
    if (w) { try { w.scrollTo(0, 0); } catch { } }
}

function goAnchor(doc, anchor) {
    if (!anchor) return false;
    let t = doc.getElementById(anchor);
    if (!t) { try { t = doc.querySelector(`[name="${cssEscape(anchor)}"]`); } catch { t = null; } }
    if (!t) return false;
    t.scrollIntoView({ block: 'start' });
    return true;
}

function unhookFrame(st) {
    try { if (st.win) st.win.removeEventListener('scroll', st.onScroll); } catch { }
    try { if (st.doc) st.doc.removeEventListener('click', st.onClick, true); } catch { }
    try { if (st.doc) st.doc.removeEventListener('keydown', st.onKey, true); } catch { }
    if (st.raf) cancelAnimationFrame(st.raf);
    st.frame = null; st.win = null; st.doc = null; st.raf = 0;
}

function hookFrame(frame, st) {
    let doc, win;
    try { doc = frame.contentDocument; win = frame.contentWindow; } catch { return; }
    // Not loaded yet (or still the initial about:blank): the 'load' listener comes back here.
    if (!doc || !win || doc.readyState !== 'complete' || !doc.body || !frame.getAttribute('srcdoc')) return;
    unhookFrame(st);
    st.frame = frame; st.doc = doc; st.win = win; st.last = -1;
    st.hookedSrc = frame.getAttribute('srcdoc');
    st.onScroll = () => {
        if (st.raf) return;
        st.raf = requestAnimationFrame(() => { st.raf = 0; frameReport(st); });
    };
    st.onClick = (e) => frameClick(st, e);
    st.onKey = (e) => frameKey(frame, e);
    win.addEventListener('scroll', st.onScroll, { passive: true });
    doc.addEventListener('click', st.onClick, true);
    doc.addEventListener('keydown', st.onKey, true);
    if (st.pendingAnchor) { goAnchor(doc, st.pendingAnchor); st.pendingAnchor = null; }
    else if (st.pendingHit) {
        const m = doc.querySelector('mark.chvd-hit');
        if (m) { st.pendingHit = false; m.scrollIntoView({ block: 'center' }); }
    }
    frameReport(st);
}

function frameReport(st) {
    const doc = st.doc, win = st.win;
    if (!doc || !win) return;
    let idx = 1;
    const ids = st.anchors;
    for (let i = 0; i < ids.length; i++) {
        const h = doc.getElementById(ids[i]);
        if (!h) continue;
        if (h.getBoundingClientRect().top <= 48) idx = i + 1;
        else break;
    }
    const se = doc.scrollingElement || doc.documentElement;
    if (ids.length && se.scrollTop > 0 && se.scrollTop + win.innerHeight >= se.scrollHeight - 2) idx = ids.length;
    if (idx !== st.last) {
        st.last = idx;
        st.dotnet.invokeMethodAsync('OnViewerScroll', idx).catch(() => { });
    }
}

// Keys pressed while focus is inside the page still reach the app (Ctrl F, Ctrl K, Esc, 1–4 …).
// Scrolling keys stay with the page so it scrolls as usual.
const FRAME_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'PageUp', 'PageDown', 'Home', 'End', ' ', 'Tab']);
function frameKey(frame, e) {
    if (!e.ctrlKey && !e.metaKey && !e.altKey && FRAME_KEYS.has(e.key)) return;
    const copy = new KeyboardEvent('keydown', {
        key: e.key, code: e.code, ctrlKey: e.ctrlKey, shiftKey: e.shiftKey, altKey: e.altKey, metaKey: e.metaKey,
        repeat: e.repeat, bubbles: true, cancelable: true,
    });
    const handled = !frame.dispatchEvent(copy);
    if (handled || e.ctrlKey || e.metaKey) e.preventDefault(); // e.g. Ctrl F opens Chuvadi's Find, not the browser's
}

function frameClick(st, e) {
    const t = e.target;
    const a = t && t.closest ? t.closest('a[href]') : null;
    if (!a) return;
    // Nothing inside the frame may navigate it, open a window or leave the app on its own.
    e.preventDefault();
    e.stopPropagation();
    const href = a.getAttribute('href') || '';
    if (href.startsWith('#')) {
        let id = href.slice(1);
        try { id = decodeURIComponent(id); } catch { }
        if (st.doc) goAnchor(st.doc, id);
        return;
    }
    if (/^(https?:|mailto:)/i.test(href)) {
        st.dotnet.invokeMethodAsync('OnExternalLink', href).catch(() => { });
    }
}
