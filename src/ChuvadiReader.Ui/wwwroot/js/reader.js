// Reader interop. Pages live as stable elements in a scroll container; this module
// (1) lazily renders a page as it nears the viewport, (2) tracks the page most in
// view for the toolbar indicator, (3) scrolls to a page on prev/next/jump, (4)
// reads/writes scroll position for per-tab scroll memory, and (5) wires the tab
// keyboard shortcuts. During a toolbar-driven scroll the indicator tracking is
// suppressed so it does not flicker through the pages it passes.

// The primary shortcut modifier: ⌘ on macOS (data-platform set by MainLayout), Ctrl everywhere else.
function isPrimary(e) {
    return document.documentElement.getAttribute('data-platform') === 'macos'
        ? (e.metaKey === true && e.ctrlKey !== true)
        : (e.ctrlKey === true && e.metaKey !== true);
}

let lazy = null;
let spy = null;
let dotnet = null;
let ratios = {};
let suppressUntil = 0;
let keyHandler = null;
let dragHandlers = null;
let drag = null;
let dragTip = null;
let scrollTarget = null;
let scrollHandler = null;
let scrollTimer = 0;

// view tools (zoom / pinch / hand / marquee / loupe / fit)
let deskRef = null;
let currentTool = 'none';
let wheelHandler = null;
let deskPointer = null;
let panning = null;
let penSplit = null;
// A pen has touched the desk in this run (AT-15); kept across documents.
let penSeen = false;
// The view was scrolled (by the person, not by a jump) since the anchor was last taken.
let anchorStale = false;
let lastInputAt = -1e9;   // the last wheel, touch, pointer or key on the desk
let inputHandler = null;
let marq = null;
let marqEl = null;
let loupeEl = null;
let loupePage = null;
let resizeObs = null;
let resizeTimer = 0;
let zoomNotifyTimer = 0;
// Nineteenth patch (Linux finding 2): the page in view stays in view when page heights change (the fit zoom after
// the first drawing lands, a page's drawing replacing its placeholder). anchor = { page, frac }: the page at the top
// of the view and how far into it the view starts. A jump sets it to the page asked for.
let anchor = null;
let anchorObs = null;
let ownZoomUntil = 0;       // a gesture zoom places the view itself; the anchor is re-read after it
let reportTimer = 0;
let chromeClickHandler = null;
let loupeR = 84;
let loupeMag = 2.2;

// touchscreen pinch-zoom + double-tap-zoom (canvas only; never the app chrome)
let touchHandlers = null;
let pinch = null;          // active 2-finger gesture: { startDist, startZoom, lastZoom }
let pinchActive = false;
let tapCand = null;        // single-finger tap candidate: { t, x, y, moved }
let lastTapTime = 0, lastTapX = 0, lastTapY = 0;
let sawPinch = false;      // did the current finger-sequence involve 2 fingers?
let dblZoomedIn = false, dblBaseZoom = 1;

// The page the reader is looking at: the one covering the most of the view, measured now from the
// page boxes. (Ratios are kept by the observer only at threshold crossings and are relative to each
// page's own height, so at a high zoom a page filling the view, ~1/3 of a tall page, lost to a stale
// ratio of a page long gone: Android round 3, double-tap on page 2 showed "1 / 6".)
function reportVisiblePage() {
    if (!scrollTarget) return;
    const best = mostVisiblePage(scrollTarget);
    if (best >= 0) invoke('OnPageVisible', best + 1);
}

// AT-16: measured from where the pages are now, not from the observer's table. The table is filled a moment after a
// scroll or a resize, so right after a jump the page filling the view could still be missing from it, and a sliver of
// the page before won the count (tablet after Find: page 3 on screen, "2 / 12" in the box). Pages run top to bottom,
// so the first one reaching into the view is found by halving, and only the ones in view are measured.
// AT-17: the unseen field that holds the caret (and a touch keyboard) between a Text tap and its box.
let caretHold = null;
let caretHoldTimer = 0;
let textTapHandler = null;

function dropCaretHold() {
    if (caretHoldTimer) { clearTimeout(caretHoldTimer); caretHoldTimer = 0; }
    if (caretHold) { caretHold.remove(); caretHold = null; }
}

function holdCaret(x, y) {
    dropCaretHold();
    const ta = document.createElement('textarea');
    ta.className = 'mk-caret-hold';
    ta.setAttribute('aria-hidden', 'true');
    ta.tabIndex = -1;
    // 16 px text: a smaller field makes mobile browsers zoom the page to it.
    ta.style.cssText = 'position:fixed;left:' + x + 'px;top:' + y + 'px;width:1px;height:1px;opacity:0;border:0;padding:0;margin:0;resize:none;font-size:16px;pointer-events:none;';
    document.body.appendChild(ta);
    caretHold = ta;
    try { ta.focus({ preventScroll: true }); } catch { }
    caretHoldTimer = setTimeout(dropCaretHold, 2000);   // no box came of the tap
}

// The text box just made (the selected one) takes the caret, and what was typed while it was on its way.
export function focusNewText(deskEl) {
    const ta = deskEl && deskEl.querySelector('.mk-text.sel .mk-ta');
    const typed = caretHold ? caretHold.value : '';
    if (ta) {
        try { ta.focus({ preventScroll: true }); } catch { }
        if (typed) { ta.value += typed; ta.dispatchEvent(new Event('input', { bubbles: true })); }
    }
    dropCaretHold();
}

// After a jump's quiet time, say which page is on screen: a jump that ends before its quiet time is over sends no
// scroll event after it, so nothing else would (AT-16).
function reportAfterQuiet() {
    if (reportTimer) clearTimeout(reportTimer);
    reportTimer = setTimeout(() => { reportTimer = 0; reportVisiblePage(); }, Math.max(0, suppressUntil - performance.now()) + 60);
}

function mostVisiblePage(deskEl) {
    const wraps = deskEl.querySelectorAll('.rpage-wrap');
    const dr = deskEl.getBoundingClientRect();
    let lo = 0, hi = wraps.length;
    while (lo < hi) {
        const mid = (lo + hi) >> 1;
        if (wraps[mid].getBoundingClientRect().bottom > dr.top) hi = mid; else lo = mid + 1;
    }
    let best = -1, bestPx = 0;
    for (let i = lo; i < wraps.length; i++) {
        const r = wraps[i].getBoundingClientRect();
        if (r.top >= dr.bottom) break;
        const px = Math.min(r.bottom, dr.bottom) - Math.max(r.top, dr.top);
        if (px > bestPx) { bestPx = px; best = pageOf(wraps[i]); }
    }
    return best;
}

export function init(deskEl, dotnetRef, newDocument) {
    dotnet = dotnetRef;
    teardownObservers();
    ratios = {};

    // Render pages a bit before they reach the viewport.
    // Pages asked for and not yet left: only these are reported when they leave, so a long document opening
    // doesn't send one message per far page (fifteenth patch: a page that left the view isn't drawn any more).
    const asked = new Set();
    lazy = new IntersectionObserver((entries) => {
        for (const e of entries) {
            const n = pageOf(e.target);
            if (n < 0) continue;
            if (e.isIntersecting) {
                asked.add(n);
                invoke('RenderPage', n);
            } else if (asked.delete(n)) {
                invoke('PageLeft', n);
            }
        }
    }, { root: deskEl, rootMargin: '1000px 0px', threshold: 0 });

    // Track the most-visible page for the indicator.
    spy = new IntersectionObserver((entries) => {
        for (const e of entries) ratios[pageOf(e.target)] = e.isIntersecting ? e.intersectionRatio : 0;
        if (performance.now() < suppressUntil) return;
        reportVisiblePage();
    }, { root: deskEl, threshold: [0, 0.25, 0.5, 0.75, 1] });

    deskEl.querySelectorAll('[data-page]').forEach(el => { lazy.observe(el); spy.observe(el); });

    // Keep the anchored page in view when page heights change. Another document starts with none: the last one's
    // page would pull the new one to it (Android round 5: a shared PDF opened on page 1 and was on 4 of 4 with no input).
    if (anchorObs) anchorObs.disconnect();
    anchor = !newDocument && anchor && deskEl.querySelector('.rpage-wrap[data-page="' + anchor.page + '"]') ? anchor : null;
    anchorObs = new ResizeObserver(() => {
        // The view was scrolled since the anchor was taken (it is taken 200 ms after scrolling stops): the anchor is
        // where the view was, so a page landing now must not pull the view back there. Take it afresh instead.
        if (performance.now() < ownZoomUntil || anchorStale) { recordAnchor(); return; }
        reanchor();
    });
    deskEl.querySelectorAll('.rpage-wrap').forEach(el => anchorObs.observe(el));

    // Report scroll position (throttled) so the active tab's ScrollTop stays current in
    // memory — this makes returning from the dashboard restore the exact position.
    if (scrollTarget && scrollHandler) scrollTarget.removeEventListener('scroll', scrollHandler);
    scrollTarget = deskEl;
    scrollHandler = () => {
        // Scrolling the person is doing (wheel, finger, keys, scrollbar) — not a jump's own (its anchor is its
        // target), and not the browser moving the view because the pages changed size.
        if (performance.now() >= suppressUntil && performance.now() - lastInputAt < 400) anchorStale = true;
        if (scrollTimer) return;
        scrollTimer = setTimeout(() => {
            scrollTimer = 0;
            invoke('OnScroll', scrollTarget ? scrollTarget.scrollTop : 0);
            // A zoomed-in page is taller than the view, so scrolling within it crosses no visibility
            // threshold; re-check the page here too.
            if (performance.now() >= suppressUntil) { recordAnchor(); reportVisiblePage(); }
        }, 200);
    };
    deskEl.addEventListener('scroll', scrollHandler, { passive: true });
    if (!inputHandler) inputHandler = () => { lastInputAt = performance.now(); };
    for (const type of ['wheel', 'touchmove', 'pointerdown', 'pointermove', 'keydown']) {
        deskEl.removeEventListener(type, inputHandler, true);
        deskEl.addEventListener(type, inputHandler, { passive: true, capture: true });
    }

    // AT-17: a tap with the Text tool makes a text box a moment later (through .NET). The caret has to be taken
    // inside the tap itself for a touch keyboard to open, so the tap puts it in an unseen field at once, and the new
    // box takes it over when it is drawn (focusNewText), with anything typed in between.
    if (textTapHandler) deskEl.removeEventListener('pointerup', textTapHandler, true);
    textTapHandler = (e) => {
        const t = e.target;
        if (!t || !t.closest || !t.closest('.mk-layer.textmode') || t.closest('.mk-text')) return;
        holdCaret(e.clientX, e.clientY);
    };
    deskEl.addEventListener('pointerup', textTapHandler, true);

    // Reader keyboard shortcuts (registered once; re-bound to the current ref).
    if (keyHandler) document.removeEventListener('keydown', keyHandler);
    keyHandler = (e) => {
        // Don't hijack typing in fields (page-jump box, future find box) — nor a key the shell just handed to a
        // selected text box (AT-17: the event's target is still the page, the caret is in the box).
        const tag = e.target && e.target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target && e.target.isContentEditable)) return;
        const at = document.activeElement && document.activeElement.tagName;
        if (at === 'INPUT' || at === 'TEXTAREA') return;

        if (e.key === 'Escape') { invoke('OnEscape'); return; }

        // Next/previous tab: ⌃Tab / ⌃⇧Tab on every platform. On macOS this is the only way —
        // ⌘Tab is the system app switcher and never reaches the page (Safari, Finder, Xcode use ⌃Tab).
        if (e.key === 'Tab' && e.ctrlKey === true && e.metaKey !== true && e.altKey !== true) {
            e.preventDefault();
            invoke('CycleTab', e.shiftKey ? -1 : 1);
            return;
        }

        // Tab-close shortcuts: handle the Alt/Shift variants of W before the no-Alt block.
        if (isPrimary(e) && e.key.toLowerCase() === 'w') {
            e.preventDefault();
            if (e.altKey) invoke('CloseOtherTabs');
            else if (e.shiftKey) invoke('CloseAllTabs');
            else invoke('CloseActiveTab');
            return;
        }

        if (isPrimary(e) && !e.altKey) {
            const k = e.key.toLowerCase();
            if (k === 'z') { e.preventDefault(); invoke('Undo'); }
            else if (e.key === 'Tab') { e.preventDefault(); invoke('CycleTab', e.shiftKey ? -1 : 1); }
            else if (e.key >= '1' && e.key <= '9') { e.preventDefault(); invoke('ActivateTabIndex', parseInt(e.key, 10)); }
            else if (k === '=' || k === '+') { e.preventDefault(); invoke('ZoomKey', 1); }
            else if (k === '-' || k === '_') { e.preventDefault(); invoke('ZoomKey', -1); }
            else if (k === '0') { e.preventDefault(); invoke('ZoomKey', 0); }
            else if (k === 'f') { e.preventDefault(); invoke('OpenFind'); }   // app find, not WebView2's native find
            return;
        }
        if (e.altKey || e.metaKey || e.ctrlKey) return;

        // Below here are bare navigation keys; don't steal them from focused controls.
        if (tag === 'BUTTON' || tag === 'A' || tag === 'SELECT') return;

        const desk = scrollTarget;
        if (!desk) return;
        const screenful = Math.max(120, desk.clientHeight * 0.9);
        switch (e.key) {
            case 'ArrowDown': e.preventDefault(); desk.scrollBy({ top: 90 }); break;
            case 'ArrowUp': e.preventDefault(); desk.scrollBy({ top: -90 }); break;
            case 'PageDown': e.preventDefault(); desk.scrollBy({ top: screenful }); break;
            case 'PageUp': e.preventDefault(); desk.scrollBy({ top: -screenful }); break;
            case ' ': e.preventDefault(); desk.scrollBy({ top: e.shiftKey ? -screenful : screenful }); break;
            case 'Home': e.preventDefault(); desk.scrollTo({ top: 0 }); break;
            case 'End': e.preventDefault(); desk.scrollTo({ top: desk.scrollHeight }); break;
            case 'ArrowRight': e.preventDefault(); invoke('NavPage', 1); break;
            case 'ArrowLeft': e.preventDefault(); invoke('NavPage', -1); break;
        }
    };
    document.addEventListener('keydown', keyHandler);

    // Tab drag-to-reorder via pointer events. Embedded WebView2 delivers native
    // HTML5 drag-and-drop unreliably (drop targets never register -> permanent
    // "no-drop" cursor), so we track the pointer ourselves: press a tab, move past
    // a small threshold to pick it up, release over another tab to reorder. A plain
    // click (no movement) still falls through to Blazor's @onclick to switch tabs.
    if (dragHandlers) {
        document.removeEventListener('pointerdown', dragHandlers.down, true);
        document.removeEventListener('pointermove', dragHandlers.move, true);
        document.removeEventListener('pointerup', dragHandlers.up, true);
        document.removeEventListener('pointercancel', dragHandlers.cancel, true);
    }
    dragHandlers = {
        down: (e) => {
            if (e.button !== 0) return;                       // left button only
            const t = e.target;
            if (t && t.closest && t.closest('.rtab-x')) return; // not the close button
            const tab = t && t.closest && t.closest('[data-tab-id]');
            if (!tab) return;
            drag = { id: tab.getAttribute('data-tab-id'), el: tab, startX: e.clientX, startY: e.clientY, active: false, target: null };
        },
        move: (e) => {
            if (!drag) return;
            if (!drag.active) {
                if (Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) < 5) return;
                drag.active = true;
                drag.el.classList.add('rtab-dragging');
                document.body.classList.add('rtab-dragging-active');
                drag.name = tabName(drag.el);
            }
            e.preventDefault();
            clearInsertMarkers();

            const strip = document.querySelector('.rtabs');
            const tabsEls = strip ? Array.from(strip.querySelectorAll('[data-tab-id]')) : [];

            // Insertion gap = number of tabs whose midpoint is left of the cursor.
            let insertIndex = tabsEls.length;
            for (let i = 0; i < tabsEls.length; i++) {
                const r = tabsEls[i].getBoundingClientRect();
                if (e.clientX < r.left + r.width / 2) { insertIndex = i; break; }
            }
            drag.insertIndex = insertIndex;

            // Which tab is the cursor physically over (for "before/after this tab" wording)?
            let hovered = null, hoveredAfter = false;
            for (const el of tabsEls) {
                const r = el.getBoundingClientRect();
                if (e.clientX >= r.left && e.clientX <= r.right) {
                    hovered = el;
                    hoveredAfter = e.clientX >= r.left + r.width / 2;
                    break;
                }
            }

            // Draw the insertion bar.
            if (insertIndex >= tabsEls.length) {
                if (tabsEls.length) tabsEls[tabsEls.length - 1].classList.add('rtab-insert-after');
            } else {
                tabsEls[insertIndex].classList.add('rtab-insert-before');
            }

            // Hint text.
            let hint;
            if (insertIndex >= tabsEls.length) {
                hint = 'Drop here to place at end';
            } else if (hovered) {
                hint = (hoveredAfter ? 'Place after ' : 'Place before ') + tabName(hovered);
            } else {
                hint = 'Place before ' + tabName(tabsEls[insertIndex]);
            }
            updateTip(e.clientX, e.clientY, drag.name, hint);
        },
        up: () => {
            if (!drag) return;
            const d = drag;
            drag = null;
            if (!d.active) return;                            // was a click, let it switch
            d.el.classList.remove('rtab-dragging');
            document.body.classList.remove('rtab-dragging-active');
            clearInsertMarkers();
            removeTip();
            if (typeof d.insertIndex === 'number') invoke2('ReorderTabTo', d.id, d.insertIndex);
            suppressNextClick();                              // don't let the drop become a switch
        },
        cancel: () => {
            if (!drag) return;
            if (drag.active) {
                drag.el.classList.remove('rtab-dragging');
                document.body.classList.remove('rtab-dragging-active');
                clearInsertMarkers();
                removeTip();
            }
            drag = null;
        },
    };
    const opts = { capture: true, passive: false };
    document.addEventListener('pointerdown', dragHandlers.down, opts);
    document.addEventListener('pointermove', dragHandlers.move, opts);
    document.addEventListener('pointerup', dragHandlers.up, opts);
    document.addEventListener('pointercancel', dragHandlers.cancel, opts);

    setupViewTools(deskEl);
}

// ── zoom / pinch / tools ─────────────────────────────────────────────────────
function setupViewTools(deskEl) {
    deskRef = deskEl;

    // Cursor-anchored zoom: trackpad pinch and Ctrl+wheel both arrive as wheel+ctrlKey.
    if (wheelHandler) deskEl.removeEventListener('wheel', wheelHandler);
    wheelHandler = (e) => {
        if (!e.ctrlKey) return;
        e.preventDefault();
        const old = readZoom();
        const next = clampZoom(old * Math.exp(-e.deltaY * 0.0015));
        if (next === old) return;
        anchorZoom(e.clientX, e.clientY, old, next);
        notifyZoom(next);
    };
    deskEl.addEventListener('wheel', wheelHandler, { passive: false });

    // Hand (pan), Marquee (area zoom) and Loupe pointer handling on the desk.
    if (deskPointer) {
        deskRef.removeEventListener('pointerdown', deskPointer.down);
        deskRef.removeEventListener('pointermove', deskPointer.move);
        deskRef.removeEventListener('pointerup', deskPointer.up);
        deskRef.removeEventListener('pointerleave', deskPointer.leave);
    }
    deskPointer = {
        down: (e) => {
            if (e.button !== 0) return;
            if (currentTool === 'hand') {
                panning = { x: e.clientX, y: e.clientY, sl: deskRef.scrollLeft, st: deskRef.scrollTop };
                try { deskRef.setPointerCapture(e.pointerId); } catch (_) { }
                e.preventDefault();
            } else if (currentTool === 'area' || currentTool === 'snapshot') {
                marq = { x0: e.clientX, y0: e.clientY };
                ensureMarqEl();
                positionMarq(e.clientX, e.clientY, e.clientX, e.clientY);
                try { deskRef.setPointerCapture(e.pointerId); } catch (_) { }
                e.preventDefault();
            }
        },
        move: (e) => {
            if (pinchActive) return;
            if (panning) {
                deskRef.scrollLeft = panning.sl - (e.clientX - panning.x);
                deskRef.scrollTop = panning.st - (e.clientY - panning.y);
            } else if (marq) {
                positionMarq(marq.x0, marq.y0, e.clientX, e.clientY);
            } else if (currentTool === 'loupe') {
                moveLoupe(e.clientX, e.clientY);
            }
        },
        up: (e) => {
            if (panning) { panning = null; return; }
            if (marq) {
                const x0 = Math.min(marq.x0, e.clientX), x1 = Math.max(marq.x0, e.clientX);
                const y0 = Math.min(marq.y0, e.clientY), y1 = Math.max(marq.y0, e.clientY);
                const tool = currentTool;
                marq = null; removeMarqEl();
                if (x1 - x0 > 12 && y1 - y0 > 12) {
                    if (tool === 'snapshot') captureSnapshotRect(x0, y0, x1, y1);
                    else zoomToRect(x0, y0, x1 - x0, y1 - y0);
                }
            }
        },
        leave: () => { if (currentTool === 'loupe') hideLoupe(); },
    };
    deskRef.addEventListener('pointerdown', deskPointer.down);
    deskRef.addEventListener('pointermove', deskPointer.move);
    deskRef.addEventListener('pointerup', deskPointer.up);
    deskRef.addEventListener('pointerleave', deskPointer.leave);

    // Tablets with a pen (<html data-input~="pen">, design 12e, D-42): the pen draws on the Mark
    // layer, a finger pans. A finger on a Mark layer never reaches its (Blazor) stroke handler and
    // pans the desk instead. data-input is absent on Windows, so nothing changes there.
    // AT-15: a host says "pen" when the system supports one (Android always does), not when one is
    // there, so the split starts only after a pen has touched the desk. Until then a finger marks.
    if (penSplit) deskRef.removeEventListener('pointerdown', penSplit, true);
    penSplit = (e) => {
        if (e.pointerType === 'pen') { penSeen = true; return; }
        if (e.pointerType !== 'touch' || !penSeen) return;
        if (!/(^|\s)pen(\s|$)/.test(document.documentElement.getAttribute('data-input') || '')) return;
        const layer = e.target && e.target.closest ? e.target.closest('.mk-layer') : null;
        if (!layer) return;
        e.stopPropagation();
        panning = { x: e.clientX, y: e.clientY, sl: deskRef.scrollLeft, st: deskRef.scrollTop };
        try { deskRef.setPointerCapture(e.pointerId); } catch (_) { }
    };
    deskRef.addEventListener('pointerdown', penSplit, true);

    // Touchscreen: two-finger pinch zooms the page canvas only (chrome never moves);
    // double-tap toggles a readable zoom. One finger is left alone for native panning.
    if (touchHandlers) {
        deskRef.removeEventListener('touchstart', touchHandlers.start);
        deskRef.removeEventListener('touchmove', touchHandlers.move);
        deskRef.removeEventListener('touchend', touchHandlers.end);
        deskRef.removeEventListener('touchcancel', touchHandlers.end);
    }
    touchHandlers = {
        start: (e) => {
            if (e.touches.length === 2) {
                // Begin pinch; drop any single-finger tool interaction in progress.
                panning = null; marq = null; tapCand = null; sawPinch = true;
                const d = touchDist(e.touches[0], e.touches[1]);
                const z = readZoom();
                pinch = { startDist: d, startZoom: z, lastZoom: z };
                pinchActive = true;
                e.preventDefault();
            } else if (e.touches.length === 1) {
                // Fresh single-finger sequence: arm a tap candidate for double-tap.
                const t = e.touches[0];
                tapCand = { t: performance.now(), x: t.clientX, y: t.clientY, moved: false };
                sawPinch = false;
            }
        },
        move: (e) => {
            if (pinch && e.touches.length === 2) {
                e.preventDefault();
                if (pinch.startDist > 0) {
                    const d = touchDist(e.touches[0], e.touches[1]);
                    const mid = touchMid(e.touches[0], e.touches[1]);
                    const next = clampZoom(pinch.startZoom * (d / pinch.startDist));
                    // Re-anchor every frame at the live midpoint so the page tracks the
                    // fingers (this also gives pan-while-pinching for free).
                    anchorZoom(mid.x, mid.y, pinch.lastZoom, next);
                    if (next !== pinch.lastZoom) { pinch.lastZoom = next; notifyZoom(next); }
                }
            } else if (tapCand && e.touches.length === 1) {
                const t = e.touches[0];
                if (Math.hypot(t.clientX - tapCand.x, t.clientY - tapCand.y) > 12) tapCand.moved = true;
            }
        },
        end: (e) => {
            if (pinch && e.touches.length < 2) {
                notifyZoom(pinch.lastZoom);
                pinch = null;
                pinchActive = false;
                dblZoomedIn = false; // a pinch ends the double-tap toggle
            }
            if (e.touches.length === 0) {
                // All fingers up: evaluate the tap for a double-tap (ignore if a pinch happened).
                if (tapCand && !tapCand.moved && !sawPinch && (performance.now() - tapCand.t) < 260) {
                    const now = performance.now();
                    const near = Math.hypot(tapCand.x - lastTapX, tapCand.y - lastTapY) < 30;
                    if (now - lastTapTime < 300 && near) {
                        doubleTapZoom(tapCand.x, tapCand.y);
                        lastTapTime = 0;
                    } else {
                        lastTapTime = now; lastTapX = tapCand.x; lastTapY = tapCand.y;
                    }
                }
                tapCand = null;
                sawPinch = false;
            }
        },
    };
    deskRef.addEventListener('touchstart', touchHandlers.start, { passive: false });
    deskRef.addEventListener('touchmove', touchHandlers.move, { passive: false });
    deskRef.addEventListener('touchend', touchHandlers.end);
    deskRef.addEventListener('touchcancel', touchHandlers.end);

    // Recompute fit on resize (throttled).
    if (resizeObs) resizeObs.disconnect();
    resizeObs = new ResizeObserver(() => {
        if (resizeTimer) return;
        resizeTimer = setTimeout(() => { resizeTimer = 0; invoke('OnDeskResize'); }, 150);
    });
    resizeObs.observe(deskEl);

    // After clicking any toolbar / drawer / hover / rail button, hand focus back to
    // the document so the keyboard and wheel always target the page, not the control.
    if (chromeClickHandler) document.removeEventListener('pointerup', chromeClickHandler);
    chromeClickHandler = (e) => {
        const b = e.target && e.target.closest ? e.target.closest('button') : null;
        if (b && deskRef && b.closest('.rtop, .rdrawer, .rfloat, .rail')) {
            setTimeout(() => { try { b.blur(); deskRef.focus({ preventScroll: true }); } catch (_) { } }, 0);
        }
    };
    document.addEventListener('pointerup', chromeClickHandler);
}

// Return keyboard focus to the document (e.g. after committing a page jump).
export function focusDesk() {
    if (deskRef) { try { deskRef.focus({ preventScroll: true }); } catch (_) { } }
}

function readZoom() {
    const v = deskRef ? getComputedStyle(deskRef).getPropertyValue('--rpage-zoom') : '';
    const n = parseFloat(v);
    return isFinite(n) && n > 0 ? n : 1;
}
function setZoomVar(s) {
    if (!deskRef) return;
    ownZoomUntil = performance.now() + 400; // a gesture zoom places the view itself (anchorZoom, zoomToRect)
    deskRef.style.setProperty('--rpage-zoom', s);
    // Above 100% the page is larger than the view: drop paged snap/slides so it
    // scrolls freely and marquee-zoom lands exactly where it was drawn (snap can't
    // yank the scroll back to a page boundary). Restored at 100% or less.
    deskRef.classList.toggle('zoomed', s > 1.001);
}
function clampZoom(s) { return Math.max(0.1, Math.min(8, Math.round(s * 1000) / 1000)); }

function anchorZoom(cx, cy, oldS, newS) {
    // Keep the point under the finger / cursor on the same spot of the same page. Scaling the whole
    // scroll offset (below) also scales the fixed gaps between pages, which drifts by a page or more
    // at a high zoom (Android round 3: double-tap on page 2 zoomed into page 3). Vertically the page's
    // .rpage-wrap is measured (not zoomed itself, so its box is right in every WebView); sideways the
    // page itself, as zoomToRect does.
    const hit = document.elementFromPoint(cx, cy);
    const wrap = hit && hit.closest ? hit.closest('.rpage-wrap') : null;
    const subj = wrap ? (wrap.querySelector('.rpage svg') || wrap.querySelector('.rpage')) : null;
    if (wrap && subj && deskRef.contains(wrap)) {
        const w1 = wrap.getBoundingClientRect(), p1 = subj.getBoundingClientRect();
        const fy = w1.height ? (cy - w1.top) / w1.height : 0;
        const fx = p1.width ? (cx - p1.left) / p1.width : 0;
        setZoomVar(newS);
        const w2 = wrap.getBoundingClientRect(), p2 = subj.getBoundingClientRect();
        deskRef.scrollTop += (w2.top + fy * w2.height) - cy;
        deskRef.scrollLeft += (p2.left + fx * p2.width) - cx;
        return;
    }
    const r = deskRef.getBoundingClientRect();
    const x = deskRef.scrollLeft + (cx - r.left);
    const y = deskRef.scrollTop + (cy - r.top);
    const k = newS / oldS;
    setZoomVar(newS);
    deskRef.scrollLeft = x * k - (cx - r.left);
    deskRef.scrollTop = y * k - (cy - r.top);
}

function touchDist(a, b) { return Math.hypot(b.clientX - a.clientX, b.clientY - a.clientY); }
function touchMid(a, b) { return { x: (a.clientX + b.clientX) / 2, y: (a.clientY + b.clientY) / 2 }; }

// Double-tap toggles between the pre-tap zoom and a readable zoom, anchored at the tap.
function doubleTapZoom(cx, cy) {
    const cur = readZoom();
    if (!dblZoomedIn) {
        dblBaseZoom = cur;
        const target = clampZoom(Math.max(cur * 2.2, 2));
        if (target !== cur) { anchorZoom(cx, cy, cur, target); notifyZoom(target); }
        dblZoomedIn = true;
    } else {
        const target = clampZoom(dblBaseZoom);
        if (target !== cur) { anchorZoom(cx, cy, cur, target); notifyZoom(target); }
        dblZoomedIn = false;
    }
}

function notifyZoom(s) {
    if (zoomNotifyTimer) clearTimeout(zoomNotifyTimer);
    zoomNotifyTimer = setTimeout(() => { zoomNotifyTimer = 0; invoke('OnZoomChanged', s); }, 110);
}

// Returns the absolute scale that fits the page width / whole page; 0 if not measurable.
// A page's size at 100 %. From the rendered SVG's own width / height (e.g. "595pt") when there is one,
// else offsetWidth / offsetHeight (layout size, unzoomed). Never getBoundingClientRect ÷ zoom: older
// WebViews (Android API 35) report a CSS-zoomed box unzoomed, which made Fit page 147 px wide on a
// 402 px phone (round 3).
function naturalSize(page) {
    const svg = page.querySelector('svg');
    const toPx = (v) => {
        const m = /^\s*([\d.]+)\s*(pt|px)?\s*$/.exec(v || '');
        return m ? parseFloat(m[1]) * (m[2] === 'pt' ? 96 / 72 : 1) : 0;
    };
    let natW = svg ? toPx(svg.getAttribute('width')) : 0;
    let natH = svg ? toPx(svg.getAttribute('height')) : 0;
    if (!natW || !natH) { natW = page.offsetWidth; natH = page.offsetHeight; }
    return { natW, natH };
}

// A newly opened document is fitted right after init, often before any page has rendered. The
// placeholder (.rpage-load) is sized in points (595 × 842), 3/4 of a rendered page (96/72 px per pt),
// so measuring it fitted pages 4/3 too large on a slow device (Android emulator: Fit page 456 px on a
// 411 px screen). Wait briefly for a rendered page and measure that; fall back to the placeholder.
// drawnOnly: measure a drawn page or nothing (0) — a placeholder isn't the page's size; the Reader fits again when a
// page lands (Linux fresh-open stall: a heavy first sheet on a cold app took longer than the 2 s wait).
export async function measureFit(deskEl, mode, drawnOnly) {
    for (let i = 0; i < 40 && deskEl && !deskEl.querySelector('.rpage svg'); i++) {
        await new Promise(r => setTimeout(r, 50));
    }
    const rendered = deskEl && deskEl.querySelector('.rpage svg');
    if (!rendered && drawnOnly) return 0;
    const page = rendered ? rendered.closest('.rpage') : deskEl && deskEl.querySelector('.rpage');
    if (!page) return 0;
    const { natW, natH } = naturalSize(page);
    if (!natW || !natH) return 0;
    const cols = deskEl.classList.contains('two') ? 2 : 1;
    // Side padding the fit leaves: 60 px on the desktop; a shell can set --rfit-pad-x on the desk
    // (the phone: 24 px, so the page fills the screen's width as in Portal & Android 12g).
    const padX = parseFloat(getComputedStyle(deskEl).getPropertyValue('--rfit-pad-x')) || 60;
    const gap = 18, padY = 44;
    const perPageW = (deskEl.clientWidth - padX - (cols - 1) * gap) / cols;
    const s = mode === 'width'
        ? perPageW / natW
        : Math.min(perPageW / natW, (deskEl.clientHeight - padY) / natH);
    return clampZoom(s);
}

// Full-screen / immersive: collapse all app chrome to just the document.
export function setImmersive(on) {
    document.body.classList.toggle('chvd-immersive', !!on);
}

export function setTool(deskEl, tool) {
    currentTool = tool || 'none';
    if (!deskEl) return;
    deskEl.classList.toggle('tool-hand', currentTool === 'hand');
    deskEl.classList.toggle('tool-area', currentTool === 'area');
    deskEl.classList.toggle('tool-snapshot', currentTool === 'snapshot');
    deskEl.classList.toggle('tool-loupe', currentTool === 'loupe');
    if (currentTool !== 'loupe') hideLoupe();
}

function zoomToRect(sx, sy, w, h) {
    const old = readZoom();
    const cx = sx + w / 2, cy = sy + h / 2;
    const s = clampZoom(old * Math.min(deskRef.clientWidth / w, deskRef.clientHeight / h));

    // Anchor to the page under the selection centre (zoom-consistent geometry,
    // same approach as the loupe) so the region you drew is what fills the view.
    const hit = document.elementFromPoint(cx, cy);
    const page = hit && hit.closest ? hit.closest('.rpage') : null;
    if (page) {
        const subj = page.querySelector('svg') || page;
        const r1 = subj.getBoundingClientRect();
        const fx = (cx - r1.left) / r1.width;
        const fy = (cy - r1.top) / r1.height;
        setZoomVar(s);
        requestAnimationFrame(() => {
            const r2 = subj.getBoundingClientRect();
            const dr = deskRef.getBoundingClientRect();
            deskRef.scrollLeft += (r2.left + fx * r2.width) - (dr.left + deskRef.clientWidth / 2);
            deskRef.scrollTop += (r2.top + fy * r2.height) - (dr.top + deskRef.clientHeight / 2);
            notifyZoom(s);
        });
        return;
    }

    const dr = deskRef.getBoundingClientRect();
    const docX = deskRef.scrollLeft + (cx - dr.left);
    const docY = deskRef.scrollTop + (cy - dr.top);
    const k = s / old;
    setZoomVar(s);
    deskRef.scrollLeft = docX * k - deskRef.clientWidth / 2;
    deskRef.scrollTop = docY * k - deskRef.clientHeight / 2;
    notifyZoom(s);
}

function ensureMarqEl() {
    if (!marqEl) { marqEl = document.createElement('div'); marqEl.className = 'rmarquee'; document.body.appendChild(marqEl); }
    marqEl.style.display = 'block';
}
function positionMarq(x0, y0, x1, y1) {
    if (!marqEl) return;
    marqEl.style.left = Math.min(x0, x1) + 'px';
    marqEl.style.top = Math.min(y0, y1) + 'px';
    marqEl.style.width = Math.abs(x1 - x0) + 'px';
    marqEl.style.height = Math.abs(y1 - y0) + 'px';
}
function removeMarqEl() { if (marqEl) { marqEl.remove(); marqEl = null; } }

// Snapshot: rasterise the marquee'd region of the page under the selection to a PNG at 150 DPI
// and hand the base64 back to .NET (which offers Save / Copy / Send to desk).
async function captureSnapshotRect(x0, y0, x1, y1) {
    const left = Math.min(x0, x1), top = Math.min(y0, y1);
    const w = Math.abs(x1 - x0), h = Math.abs(y1 - y0);
    if (w < 8 || h < 8) return;

    const cx = left + w / 2, cy = top + h / 2;
    const hit = document.elementFromPoint(cx, cy);
    const page = hit && hit.closest ? hit.closest('.rpage') : null;
    const wrap = page && page.closest ? page.closest('[data-page]') : null;
    const svg = page ? page.querySelector('svg') : null;
    if (!svg || !wrap) { invoke('SnapshotFailed', 'Point the marquee at a page to snapshot.'); return; }

    const pageIndex = parseInt(wrap.dataset.page || '0', 10);
    const r = svg.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return;

    // Region as a fraction of the page, clamped to the page bounds.
    let fx = (left - r.left) / r.width;
    let fy = (top - r.top) / r.height;
    let fw = w / r.width, fh = h / r.height;
    fx = Math.max(0, Math.min(1, fx)); fy = Math.max(0, Math.min(1, fy));
    fw = Math.min(fw, 1 - fx); fh = Math.min(fh, 1 - fy);
    if (fw <= 0 || fh <= 0) return;

    // Page size in points (from the SVG viewBox) → pixels at 150 DPI.
    const vb = svg.viewBox && svg.viewBox.baseVal;
    const ptW = (vb && vb.width) ? vb.width : r.width;
    const ptH = (vb && vb.height) ? vb.height : r.height;
    const scale = 150 / 72;
    const fullW = Math.max(1, Math.round(ptW * scale));
    const fullH = Math.max(1, Math.round(ptH * scale));

    const xml = new XMLSerializer().serializeToString(svg);
    const svgUrl = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);

    const dataUrl = await new Promise((resolve) => {
        const img = new Image();
        img.onload = () => {
            try {
                const full = document.createElement('canvas');
                full.width = fullW; full.height = fullH;
                const fctx = full.getContext('2d');
                fctx.fillStyle = '#ffffff'; fctx.fillRect(0, 0, fullW, fullH);
                fctx.drawImage(img, 0, 0, fullW, fullH);

                const cw = Math.max(1, Math.round(fw * fullW));
                const ch = Math.max(1, Math.round(fh * fullH));
                const crop = document.createElement('canvas');
                crop.width = cw; crop.height = ch;
                crop.getContext('2d').drawImage(
                    full, Math.round(fx * fullW), Math.round(fy * fullH), cw, ch, 0, 0, cw, ch);
                resolve(crop.toDataURL('image/png'));
            } catch (err) { resolve(null); }
        };
        img.onerror = () => resolve(null);
        img.src = svgUrl;
    });

    if (!dataUrl) { invoke('SnapshotFailed', 'Could not capture this region.'); return; }
    invoke2('SnapshotCaptured', dataUrl.substring(dataUrl.indexOf(',') + 1), pageIndex);
}

function ensureLoupe() {
    if (!loupeEl) {
        loupeEl = document.createElement('div');
        loupeEl.className = 'rloupe';
        loupeEl.innerHTML = '<div class="rloupe-in"></div>';
        document.body.appendChild(loupeEl);
    }
    loupeEl.style.width = loupeEl.style.height = (loupeR * 2) + 'px';
}
function hideLoupe() { if (loupeEl) { loupeEl.remove(); loupeEl = null; } loupePage = null; }
function moveLoupe(cx, cy) {
    const hit = document.elementFromPoint(cx, cy);
    const page = hit && hit.closest ? hit.closest('.rpage') : null;
    if (!page) { if (loupeEl) loupeEl.style.display = 'none'; return; }
    ensureLoupe();
    loupeEl.style.display = 'block';
    const inner = loupeEl.querySelector('.rloupe-in');
    if (loupePage !== page) {
        loupePage = page;
        const svg = page.querySelector('svg');
        inner.innerHTML = svg ? svg.outerHTML : '';
    }
    const LR = loupeR, MAG = loupeMag;
    const innerSvg = inner.firstElementChild;
    const subject = page.querySelector('svg') || page;
    const pr = subject.getBoundingClientRect();
    const px = cx - pr.left, py = cy - pr.top;
    if (innerSvg) { innerSvg.style.width = pr.width + 'px'; innerSvg.style.height = pr.height + 'px'; }
    inner.style.transformOrigin = '0 0';
    inner.style.transform = `translate(${LR - px * MAG}px, ${LR - py * MAG}px) scale(${MAG})`;
    loupeEl.style.left = (cx - LR) + 'px';
    loupeEl.style.top = (cy - LR) + 'px';
}

export function configLoupe(radius, mag) {
    if (radius > 0) loupeR = radius;
    if (mag > 0) loupeMag = mag;
    if (loupeEl) loupeEl.style.width = loupeEl.style.height = (loupeR * 2) + 'px';
}

// frac (0..1, optional): how far down the page to land — a bookmark that points at a heading
// part-way down a page goes to that heading, with a little room above it, not to the page top
// (continuous scrolling and zoomed pages; the page-at-a-time layout shows the whole page).
export function scrollToPage(deskEl, index, frac) {
    const el = deskEl && deskEl.querySelector('[data-page="' + index + '"]');
    if (!el) return;
    // In the page-at-a-time layout the desk snaps to whole pages, so the page is the target there.
    const snaps = /^(y|both|block)/.test(getComputedStyle(deskEl).scrollSnapType || '');
    const f = !snaps && typeof frac === 'number' && frac > 0 ? Math.min(frac, 1) : 0;
    const r = el.getBoundingClientRect();
    const lead = f > 0 && r.height ? Math.min(f, 12 / r.height) : 0;   // 12px above the target
    suppressUntil = performance.now() + 1400;
    anchor = { page: index, frac: f - lead }; // stays the target while heights change under the jump
    anchorStale = false;
    // Scroll the desk itself — NOT scrollIntoView, which also scrolls every
    // scrollable ancestor and would drag the toolbar/page up with it.
    const top = deskEl.scrollTop + (r.top - deskEl.getBoundingClientRect().top) + (f - lead) * r.height;
    deskEl.scrollTo({ top, behavior: 'smooth' });
    reportAfterQuiet();
}

// Find: bring the current match itself into view, not only the top of its page — on a zoomed or tall
// page the match can sit a screen or more below it (Android Test AT-09). The highlight renders with the
// page, so after bringing the page in, wait for it (up to 1.5 s), then put it a third of the way down.
export async function scrollToMatch(deskEl, index) {
    if (!deskEl) return;
    scrollToPage(deskEl, index);
    for (let i = 0; i < 30; i++) {
        const hl = deskEl.querySelector('[data-page="' + index + '"] .rfind-hl.cur');
        if (hl) {
            suppressUntil = performance.now() + 1400;
            const d = deskEl.getBoundingClientRect(), h = hl.getBoundingClientRect();
            const hTop = deskEl.scrollTop + (h.top - d.top), view = deskEl.clientHeight;
            let top = hTop - Math.max(0, (view - h.height) / 3);
            const left = deskEl.scrollWidth > deskEl.clientWidth
                ? deskEl.scrollLeft + (h.left - d.left) - Math.max(0, (deskEl.clientWidth - h.width) / 2)
                : deskEl.scrollLeft;
            const w = deskEl.querySelector('.rpage-wrap[data-page="' + index + '"]');
            // Snapping pages place themselves; otherwise (a zoomed or tall page) the view is the match's.
            if (w && !/^(y|both|block)/.test(getComputedStyle(deskEl).scrollSnapType || '')) {
                const r = w.getBoundingClientRect(), wTop = deskEl.scrollTop + (r.top - d.top), wEnd = wTop + r.height;
                // Stay on the match's page where the match allows (AT-09: a match at a page's foot, a third of the
                // way down, left the next page covering most of the view, and the page box said that page).
                if (top + view > wEnd && wEnd - view <= hTop) top = Math.max(wTop, wEnd - view);
                if (top < wTop && wTop + view >= hTop + h.height) top = wTop;
                // The match, not its page's top, stays in view while page heights change (AT-09: a page drawn
                // during the scroll put the view back on the page's top, the match below it off screen).
                if (r.height) { anchor = { page: index, frac: Math.max(0, (top - wTop) / r.height) }; anchorStale = false; }
            }
            // The highlight can arrive late (its page had to be drawn): this second scroll gets its own quiet time,
            // and the page on screen is reported when it ends (AT-16).
            suppressUntil = Math.max(suppressUntil, performance.now() + 900);
            deskEl.scrollTo({ top, left, behavior: 'smooth' });
            reportAfterQuiet();
            return;
        }
        await new Promise(r => setTimeout(r, 50));
    }
}

export function getScrollTop(deskEl) {
    return deskEl ? deskEl.scrollTop : 0;
}

export function setScrollTop(deskEl, top) {
    if (!deskEl) return;
    // Suppress the spy briefly so restoring position doesn't fire spurious page updates.
    suppressUntil = performance.now() + 400;
    deskEl.scrollTop = top || 0;
    recordAnchor();
}

// Current on-page text selection, for auto-filling the Find box (selection only; no clipboard
// read, which would trigger a WebView2 permission prompt).
export function selectedText() {
    try {
        const sel = window.getSelection ? window.getSelection().toString() : '';
        return (sel || '').trim();
    } catch (e) { return ''; }
}

export function dispose() {
    disableImageDrops();
    teardownObservers();
    if (scrollTarget && scrollHandler) scrollTarget.removeEventListener('scroll', scrollHandler);
    scrollTarget = null; scrollHandler = null;
    if (scrollTimer) { clearTimeout(scrollTimer); scrollTimer = 0; }
    if (keyHandler) { document.removeEventListener('keydown', keyHandler); keyHandler = null; }
    if (dragHandlers) {
        document.removeEventListener('pointerdown', dragHandlers.down, true);
        document.removeEventListener('pointermove', dragHandlers.move, true);
        document.removeEventListener('pointerup', dragHandlers.up, true);
        document.removeEventListener('pointercancel', dragHandlers.cancel, true);
        dragHandlers = null;
    }
    if (drag && drag.el) drag.el.classList.remove('rtab-dragging');
    document.body.classList.remove('rtab-dragging-active');
    clearInsertMarkers();
    removeTip();
    drag = null;

    if (deskRef && wheelHandler) deskRef.removeEventListener('wheel', wheelHandler);
    if (deskRef && deskPointer) {
        deskRef.removeEventListener('pointerdown', deskPointer.down);
        deskRef.removeEventListener('pointermove', deskPointer.move);
        deskRef.removeEventListener('pointerup', deskPointer.up);
        deskRef.removeEventListener('pointerleave', deskPointer.leave);
    }
    if (deskRef && penSplit) deskRef.removeEventListener('pointerdown', penSplit, true);
    if (deskRef && touchHandlers) {
        deskRef.removeEventListener('touchstart', touchHandlers.start);
        deskRef.removeEventListener('touchmove', touchHandlers.move);
        deskRef.removeEventListener('touchend', touchHandlers.end);
        deskRef.removeEventListener('touchcancel', touchHandlers.end);
    }
    if (resizeObs) { resizeObs.disconnect(); resizeObs = null; }
    if (chromeClickHandler) { document.removeEventListener('pointerup', chromeClickHandler); chromeClickHandler = null; }
    if (resizeTimer) { clearTimeout(resizeTimer); resizeTimer = 0; }
    if (zoomNotifyTimer) { clearTimeout(zoomNotifyTimer); zoomNotifyTimer = 0; }
    removeMarqEl(); hideLoupe();
    document.body.classList.remove('chvd-immersive');
    wheelHandler = null; deskPointer = null; penSplit = null; panning = null; marq = null;
    touchHandlers = null; pinch = null; pinchActive = false; tapCand = null; sawPinch = false;
    deskRef = null; currentTool = 'none';
}

function clearInsertMarkers() {
    document.querySelectorAll('.rtab-insert-before, .rtab-insert-after')
        .forEach(el => el.classList.remove('rtab-insert-before', 'rtab-insert-after'));
}

function tabName(el) {
    const n = el && el.querySelector('.rtab-name');
    return n ? n.textContent : '';
}

// Floating label that follows the cursor while dragging a tab.
function updateTip(x, y, name, hint) {
    if (!dragTip) {
        dragTip = document.createElement('div');
        dragTip.className = 'rtab-dragtip';
        document.body.appendChild(dragTip);
    }
    dragTip.textContent = '';
    const n = document.createElement('div');
    n.className = 'rtab-dragtip-name';
    n.textContent = name;
    const h = document.createElement('div');
    h.className = 'rtab-dragtip-hint';
    h.textContent = hint;
    dragTip.appendChild(n);
    dragTip.appendChild(h);
    dragTip.style.left = (x + 14) + 'px';
    dragTip.style.top = (y + 18) + 'px';
}

function removeTip() {
    if (dragTip) { dragTip.remove(); dragTip = null; }
}

// After a real drag, swallow the click the browser fires on pointerup so it
// doesn't also switch to the dragged tab.
function suppressNextClick() {
    const swallow = (ev) => {
        ev.preventDefault();
        ev.stopPropagation();
        document.removeEventListener('click', swallow, true);
    };
    document.addEventListener('click', swallow, true);
    setTimeout(() => document.removeEventListener('click', swallow, true), 300);
}

function pageOf(el) {
    const v = el.getAttribute('data-page');
    return v == null ? -1 : parseInt(v, 10);
}

function invoke(method, arg) {
    try { dotnet.invokeMethodAsync(method, arg); } catch (e) { }
}

function invoke2(method, a, b) {
    try { dotnet.invokeMethodAsync(method, a, b); } catch (e) { }
}

function teardownObservers() {
    if (lazy) { lazy.disconnect(); lazy = null; }
    if (spy) { spy.disconnect(); spy = null; }
    if (anchorObs) { anchorObs.disconnect(); anchorObs = null; }
}

// The page at the top of the view, and how far into it the view starts (0 = its top edge).
function recordAnchor() {
    if (!scrollTarget) return;
    anchorStale = false;
    const top = scrollTarget.getBoundingClientRect().top;
    for (const w of scrollTarget.querySelectorAll('.rpage-wrap')) {
        const r = w.getBoundingClientRect();
        if (r.bottom > top + 1) {
            anchor = { page: pageOf(w), frac: r.height ? Math.max(0, (top - r.top) / r.height) : 0 };
            return;
        }
    }
}

// Page heights changed: put the anchored page back where it was in the view, then report the page in view once
// the jump's quiet time is over (after a jump the page box kept showing 1 while page 2 was in view).
function reanchor() {
    if (!scrollTarget || !anchor) return;
    const w = scrollTarget.querySelector('.rpage-wrap[data-page="' + anchor.page + '"]');
    if (!w) return;
    const d = scrollTarget.getBoundingClientRect(), r = w.getBoundingClientRect();
    const delta = (r.top + anchor.frac * r.height) - d.top;
    if (Math.abs(delta) > 1) scrollTarget.scrollTop += delta;
    reportAfterQuiet();
}

// Markup: report a page's redaction layer rect (viewport pixels) so the C# side
// can convert pointer positions to normalised page fractions regardless of zoom.
window.chuvadiReaderMkRect = function (page) {
    const el = document.querySelector(".mk-layer[data-page='" + page + "']");
    if (!el) { return null; }
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
};

// ── Add Image: clipboard paste + drag-and-drop ───────────────────────────────
// While markup mode is on, pasting or dropping an image onto a page hands the
// bytes (base64) to C#, which adds it as an editable overlay. Embedded WebView2
// fires these DOM events reliably; we gate on a .mk-layer being present.
let imageDrops = null;

function fileToBase64(file) {
    return new Promise((resolve, reject) => {
        const r = new FileReader();
        r.onload = () => {
            const s = String(r.result || '');
            const comma = s.indexOf(',');
            resolve({ base64: comma >= 0 ? s.slice(comma + 1) : s, mime: file.type || 'image/png' });
        };
        r.onerror = () => reject(r.error);
        r.readAsDataURL(file);
    });
}

export function enableImageDrops() {
    if (imageDrops || !dotnet) return;
    const onPaste = async (e) => {
        if (!document.querySelector('.mk-layer')) return;
        const items = e.clipboardData && e.clipboardData.items;
        if (!items) return;
        for (const it of items) {
            if (it.kind === 'file' && it.type && it.type.startsWith('image/')) {
                const file = it.getAsFile();
                if (!file) continue;
                e.preventDefault();
                try {
                    const { base64, mime } = await fileToBase64(file);
                    dotnet.invokeMethodAsync('OnImagePasted', base64, mime);
                } catch (_) { }
                return;
            }
        }
    };
    const onDragOver = (e) => {
        const layer = e.target && e.target.closest ? e.target.closest('.mk-layer') : null;
        if (layer && e.dataTransfer) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; }
    };
    const onDrop = async (e) => {
        const layer = e.target && e.target.closest ? e.target.closest('.mk-layer') : null;
        if (!layer) return;
        const dt = e.dataTransfer;
        if (!dt || !dt.files || dt.files.length === 0) return;
        const file = Array.from(dt.files).find(f => f.type && f.type.startsWith('image/'));
        if (!file) return;
        e.preventDefault();
        const page = parseInt(layer.getAttribute('data-page'), 10);
        const r = layer.getBoundingClientRect();
        const fx = r.width > 0 ? (e.clientX - r.left) / r.width : 0.5;
        const fy = r.height > 0 ? (e.clientY - r.top) / r.height : 0.5;
        try {
            const { base64, mime } = await fileToBase64(file);
            dotnet.invokeMethodAsync('OnImageDropped', page, fx, fy, base64, mime);
        } catch (_) { }
    };
    document.addEventListener('paste', onPaste);
    document.addEventListener('dragover', onDragOver, true);
    document.addEventListener('drop', onDrop, true);
    imageDrops = { onPaste, onDragOver, onDrop };
}

export function disableImageDrops() {
    if (!imageDrops) return;
    document.removeEventListener('paste', imageDrops.onPaste);
    document.removeEventListener('dragover', imageDrops.onDragOver, true);
    document.removeEventListener('drop', imageDrops.onDrop, true);
    imageDrops = null;
}

// ── Signature: render typed text to a transparent, tightly-cropped PNG ────────
// Returns a base64 PNG data URL (or null). The signature is placed as a normal
// image overlay, so it rides the (working) image stamp path, not text stamping.
export function renderSignature(text, cssFont, color) {
    text = (text || '').trim();
    if (!text) return null;
    try {
        const SCALE = 4;          // supersample for a crisp result
        const PAD = 28;           // breathing room around the glyphs
        const meas = document.createElement('canvas').getContext('2d');
        meas.font = cssFont;
        const m = meas.measureText(text);
        const asc = (m.actualBoundingBoxAscent || 52);
        const desc = (m.actualBoundingBoxDescent || 22);
        const left = (m.actualBoundingBoxLeft || 0);
        const right = (m.actualBoundingBoxRight || m.width || meas.measureText(text).width);
        const w = Math.max(8, Math.ceil(left + right)) + PAD * 2;
        const h = Math.max(8, Math.ceil(asc + desc)) + PAD * 2;
        const c = document.createElement('canvas');
        c.width = Math.round(w * SCALE);
        c.height = Math.round(h * SCALE);
        const ctx = c.getContext('2d');
        ctx.scale(SCALE, SCALE);
        ctx.font = cssFont;
        ctx.fillStyle = color || '#0b3d91';
        ctx.textBaseline = 'alphabetic';
        ctx.textAlign = 'left';
        ctx.fillText(text, PAD + left, PAD + asc);
        return c.toDataURL('image/png');
    } catch (_) {
        return null;
    }
}
