// Reader interop. Pages live as stable elements in a scroll container; this module
// (1) lazily renders a page as it nears the viewport, (2) tracks the page most in
// view for the toolbar indicator, (3) scrolls to a page on prev/next/jump, (4)
// reads/writes scroll position for per-tab scroll memory, and (5) wires the tab
// keyboard shortcuts. During a toolbar-driven scroll the indicator tracking is
// suppressed so it does not flicker through the pages it passes.

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
let marq = null;
let marqEl = null;
let loupeEl = null;
let loupePage = null;
let resizeObs = null;
let resizeTimer = 0;
let zoomNotifyTimer = 0;
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

export function init(deskEl, dotnetRef) {
    dotnet = dotnetRef;
    teardownObservers();
    ratios = {};

    // Render pages a bit before they reach the viewport.
    lazy = new IntersectionObserver((entries) => {
        for (const e of entries) {
            if (e.isIntersecting) {
                const n = pageOf(e.target);
                if (n >= 0) invoke('RenderPage', n);
            }
        }
    }, { root: deskEl, rootMargin: '1000px 0px', threshold: 0 });

    // Track the most-visible page for the indicator.
    spy = new IntersectionObserver((entries) => {
        for (const e of entries) ratios[pageOf(e.target)] = e.isIntersecting ? e.intersectionRatio : 0;
        if (performance.now() < suppressUntil) return;
        let best = -1, bestRatio = -1;
        for (const k in ratios) {
            if (ratios[k] > bestRatio) { bestRatio = ratios[k]; best = parseInt(k, 10); }
        }
        if (best >= 0) invoke('OnPageVisible', best + 1);
    }, { root: deskEl, threshold: [0, 0.25, 0.5, 0.75, 1] });

    deskEl.querySelectorAll('[data-page]').forEach(el => { lazy.observe(el); spy.observe(el); });

    // Report scroll position (throttled) so the active tab's ScrollTop stays current in
    // memory — this makes returning from the dashboard restore the exact position.
    if (scrollTarget && scrollHandler) scrollTarget.removeEventListener('scroll', scrollHandler);
    scrollTarget = deskEl;
    scrollHandler = () => {
        if (scrollTimer) return;
        scrollTimer = setTimeout(() => {
            scrollTimer = 0;
            invoke('OnScroll', scrollTarget ? scrollTarget.scrollTop : 0);
        }, 200);
    };
    deskEl.addEventListener('scroll', scrollHandler, { passive: true });

    // Reader keyboard shortcuts (registered once; re-bound to the current ref).
    if (keyHandler) document.removeEventListener('keydown', keyHandler);
    keyHandler = (e) => {
        // Don't hijack typing in fields (page-jump box, future find box).
        const tag = e.target && e.target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target && e.target.isContentEditable)) return;

        if (e.key === 'Escape') { invoke('OnEscape'); return; }

        // Tab-close shortcuts: handle the Alt/Shift variants of W before the no-Alt block.
        if (e.ctrlKey && !e.metaKey && e.key.toLowerCase() === 'w') {
            e.preventDefault();
            if (e.altKey) invoke('CloseOtherTabs');
            else if (e.shiftKey) invoke('CloseAllTabs');
            else invoke('CloseActiveTab');
            return;
        }

        if (e.ctrlKey && !e.altKey && !e.metaKey) {
            const k = e.key.toLowerCase();
            if (k === 'z') { e.preventDefault(); invoke('Undo'); }
            else if (e.key === 'Tab') { e.preventDefault(); invoke('CycleTab', e.shiftKey ? -1 : 1); }
            else if (e.key >= '1' && e.key <= '9') { e.preventDefault(); invoke('ActivateTabIndex', parseInt(e.key, 10)); }
            else if (k === '=' || k === '+') { e.preventDefault(); invoke('ZoomKey', 1); }
            else if (k === '-' || k === '_') { e.preventDefault(); invoke('ZoomKey', -1); }
            else if (k === '0') { e.preventDefault(); invoke('ZoomKey', 0); }
            return;
        }
        if (e.altKey || e.metaKey) return;

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
            } else if (currentTool === 'area') {
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
                marq = null; removeMarqEl();
                if (x1 - x0 > 12 && y1 - y0 > 12) zoomToRect(x0, y0, x1 - x0, y1 - y0);
            }
        },
        leave: () => { if (currentTool === 'loupe') hideLoupe(); },
    };
    deskRef.addEventListener('pointerdown', deskPointer.down);
    deskRef.addEventListener('pointermove', deskPointer.move);
    deskRef.addEventListener('pointerup', deskPointer.up);
    deskRef.addEventListener('pointerleave', deskPointer.leave);

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
    deskRef.style.setProperty('--rpage-zoom', s);
    // Above 100% the page is larger than the view: drop paged snap/slides so it
    // scrolls freely and marquee-zoom lands exactly where it was drawn (snap can't
    // yank the scroll back to a page boundary). Restored at 100% or less.
    deskRef.classList.toggle('zoomed', s > 1.001);
}
function clampZoom(s) { return Math.max(0.1, Math.min(8, Math.round(s * 1000) / 1000)); }

function anchorZoom(cx, cy, oldS, newS) {
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
export function measureFit(deskEl, mode) {
    const page = deskEl && deskEl.querySelector('.rpage');
    if (!page) return 0;
    const z = readZoom();
    const rect = page.getBoundingClientRect();
    const natW = rect.width / z, natH = rect.height / z;
    if (!natW || !natH) return 0;
    const cols = deskEl.classList.contains('two') ? 2 : 1;
    const gap = 18, padX = 60, padY = 44;
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

export function scrollToPage(deskEl, index) {
    const el = deskEl && deskEl.querySelector('[data-page="' + index + '"]');
    if (!el) return;
    suppressUntil = performance.now() + 1400;
    // Scroll the desk itself — NOT scrollIntoView, which also scrolls every
    // scrollable ancestor and would drag the toolbar/page up with it.
    const top = deskEl.scrollTop + (el.getBoundingClientRect().top - deskEl.getBoundingClientRect().top);
    deskEl.scrollTo({ top, behavior: 'smooth' });
}

export function getScrollTop(deskEl) {
    return deskEl ? deskEl.scrollTop : 0;
}

export function setScrollTop(deskEl, top) {
    if (!deskEl) return;
    // Suppress the spy briefly so restoring position doesn't fire spurious page updates.
    suppressUntil = performance.now() + 400;
    deskEl.scrollTop = top || 0;
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
    wheelHandler = null; deskPointer = null; panning = null; marq = null;
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
