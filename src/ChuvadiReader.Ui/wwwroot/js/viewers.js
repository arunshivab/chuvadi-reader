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
