// Redact destination interop. A focused subset of the Reader's machinery: lazy-render
// pages as they near the viewport (essential — redaction documents are often 500+ pages),
// track the page most in view for the toolbar, scroll to a page on jump, and report the
// redaction layer's rect so C# can map pointer positions to normalised page fractions.

let lazy = null;
let spy = null;
let dotnet = null;
let deskEl = null;
let ratios = {};
let suppressUntil = 0;

export function init(desk, dotnetRef) {
    dotnet = dotnetRef;
    deskEl = desk;
    teardown();
    ratios = {};

    // Render pages a bit before they reach the viewport.
    lazy = new IntersectionObserver((entries) => {
        for (const e of entries) {
            if (e.isIntersecting) {
                const n = pageOf(e.target);
                if (n >= 0) invoke('RenderPage', n);
            }
        }
    }, { root: desk, rootMargin: '1200px 0px', threshold: 0 });

    // Track the most-visible page for the page indicator.
    spy = new IntersectionObserver((entries) => {
        for (const e of entries) ratios[pageOf(e.target)] = e.isIntersecting ? e.intersectionRatio : 0;
        if (performance.now() < suppressUntil) return;
        let best = -1, bestRatio = -1;
        for (const k in ratios) {
            if (ratios[k] > bestRatio) { bestRatio = ratios[k]; best = parseInt(k, 10); }
        }
        if (best >= 0) invoke('OnPageVisible', best + 1);
    }, { root: desk, threshold: [0, 0.25, 0.5, 0.75, 1] });

    observe();
}

// (Re)observe page wrappers — call after the page list is first rendered.
export function observe() {
    if (!deskEl || !lazy || !spy) return;
    deskEl.querySelectorAll('[data-page]').forEach(el => { lazy.observe(el); spy.observe(el); });
}

export function scrollToPage(index) {
    const el = deskEl && deskEl.querySelector('[data-page="' + index + '"]');
    if (!el) return;
    suppressUntil = performance.now() + 1200;
    const top = deskEl.scrollTop + (el.getBoundingClientRect().top - deskEl.getBoundingClientRect().top);
    deskEl.scrollTo({ top, behavior: 'smooth' });
}

export function dispose() {
    teardown();
    deskEl = null;
    dotnet = null;
}

function teardown() {
    if (lazy) { lazy.disconnect(); lazy = null; }
    if (spy) { spy.disconnect(); spy = null; }
}

function pageOf(el) {
    const v = el.getAttribute('data-page');
    return v == null ? -1 : parseInt(v, 10);
}

function invoke(method, arg) {
    try { dotnet.invokeMethodAsync(method, arg); } catch (e) { }
}

// Report a page's redaction layer rect (viewport pixels) so C# can convert pointer
// positions to normalised page fractions regardless of zoom.
window.chuvadiRedactMkRect = function (page) {
    const el = document.querySelector(".mk-layer[data-page='" + page + "']");
    if (!el) { return null; }
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
};
