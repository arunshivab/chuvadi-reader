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
        const a = e.target && e.target.closest ? e.target.closest('a[data-anchor-link]') : null;
        if (!a || !el.contains(a)) return;
        e.preventDefault(); // keeps Blazor's router from treating "#slug" as a navigation
        scrollToAnchor(el, a.getAttribute('data-anchor-link'));
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
