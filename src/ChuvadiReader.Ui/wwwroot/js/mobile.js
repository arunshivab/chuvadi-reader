// Touch for the tablet and phone shells (Portal & Android 12d-12j). Loaded by TouchSupport.razor and
// PhoneShell.razor only when <html data-shell> is "tablet" or "phone"; never on desktop or portal.
//
// It translates touch into what the desktop code already understands, so reader.js and bench.js
// stay unchanged:
//   * two-finger pinch on the page desk → Ctrl + wheel, which reader.js turns into its own
//     cursor-anchored zoom (the same path as a trackpad pinch)
//   * double-tap on the page → Fit width ⇄ Fit page (Read mode; .NET decides)
//   * horizontal swipe when the page has no sideways overflow → next / previous page (Read mode)
//   * tap on the canvas → shows the per-page strip for a few seconds (desktop shows it on hover)
//   * Arrange: long-press a page (or a shelf page) to drag it; a quick swipe scrolls instead.

let net = null;          // DotNetObjectReference<TouchSupport>
let attached = false;
const touches = new Map();   // pointerId → {x, y}
let pinch = null;            // {dist, desk}
let tap = null;              // {x, y, t, id, moved}
let lastTap = null;          // {x, y, t}
let stripTimer = null;
let press = null;            // Arrange long-press state
let guardMove = null;

const DOUBLE_TAP_MS = 300;
const TAP_SLOP = 10;
const SWIPE_MIN = 60;
const LONG_PRESS_MS = 450;
const ZOOM_K = 0.0015;       // reader.js: next = old * exp(-deltaY * 0.0015)

export function initTouch(dotnetRef) {
    net = dotnetRef || net;
    if (attached) return;
    attached = true;
    document.addEventListener('pointerdown', onDown, true);
    document.addEventListener('pointermove', onMove, true);
    document.addEventListener('pointerup', onUp, true);
    document.addEventListener('pointercancel', onCancel, true);
    document.addEventListener('contextmenu', onContextMenu, true);
}

export function disposeTouch() {
    if (!attached) return;
    attached = false;
    document.removeEventListener('pointerdown', onDown, true);
    document.removeEventListener('pointermove', onMove, true);
    document.removeEventListener('pointerup', onUp, true);
    document.removeEventListener('pointercancel', onCancel, true);
    document.removeEventListener('contextmenu', onContextMenu, true);
    endPress();
    touches.clear();
    pinch = null; tap = null; lastTap = null;
    clearTimeout(stripTimer);
}

function isTouch(e) { return e.pointerType === 'touch'; }

function deskOf(el) { return el && el.closest ? el.closest('.rdesk') : null; }

function interactive(el) {
    return !!(el && el.closest && el.closest('button, input, select, textarea, a, [contenteditable], .mk-layer, .fcards, .pstrip, .mdrawer'));
}

// ── pointer routing ─────────────────────────────────────────────────────────
function onDown(e) {
    if (!isTouch(e) || e.__chvdSynthetic) return;
    touches.set(e.pointerId, { x: e.clientX, y: e.clientY });

    if (touches.size === 2) {
        const desk = deskOf(e.target);
        if (desk) {
            const [a, b] = [...touches.values()];
            pinch = { dist: Math.hypot(a.x - b.x, a.y - b.y), desk };
            tap = null;
            cancelPress();
        }
        return;
    }

    if (touches.size === 1 && beginPress(e)) return;

    tap = { x: e.clientX, y: e.clientY, t: performance.now(), id: e.pointerId, moved: false, target: e.target };
}

function onMove(e) {
    if (!isTouch(e) || !touches.has(e.pointerId)) return;
    touches.set(e.pointerId, { x: e.clientX, y: e.clientY });

    if (press && !press.armed && e.pointerId === press.id) {
        if (Math.hypot(e.clientX - press.x, e.clientY - press.y) > TAP_SLOP) cancelPress();  // a swipe: let it scroll
    }

    if (pinch && touches.size >= 2) {
        const [a, b] = [...touches.values()];
        const dist = Math.hypot(a.x - b.x, a.y - b.y);
        if (pinch.dist > 0 && dist > 0) {
            const ratio = dist / pinch.dist;
            if (Math.abs(ratio - 1) > 0.01) {
                const deltaY = -Math.log(ratio) / ZOOM_K;
                pinch.desk.dispatchEvent(new WheelEvent('wheel', {
                    bubbles: true, cancelable: true, ctrlKey: true, deltaY,
                    clientX: (a.x + b.x) / 2, clientY: (a.y + b.y) / 2,
                }));
                pinch.dist = dist;
            }
        }
        e.preventDefault();
        return;
    }

    if (tap && tap.id === e.pointerId && Math.hypot(e.clientX - tap.x, e.clientY - tap.y) > TAP_SLOP) tap.moved = true;
}

function onUp(e) {
    if (!isTouch(e) || e.__chvdSynthetic) return;
    touches.delete(e.pointerId);
    if (pinch) {
        if (touches.size < 2) pinch = null;
        tap = null;
        return;
    }

    if (press && e.pointerId === press.id) {
        if (!press.armed) {
            // A quick tap on a page: hand bench.js the press it never saw, so it selects the page
            // exactly as a click does (its pointerup listener runs after this one).
            replayDown(press);
            cancelPress();
        } else {
            endPress();
        }
        return;
    }

    const t = tap;
    tap = null;
    if (!t || t.id !== e.pointerId) return;
    const dx = e.clientX - t.x, dy = e.clientY - t.y;
    const desk = deskOf(t.target);

    if (t.moved) {
        const sh = t.target.closest ? t.target.closest('.bench-shelf') : null;
        if (sh && document.documentElement.getAttribute('data-shell') === 'phone' && Math.abs(dy) > SWIPE_MIN && Math.abs(dy) > 2 * Math.abs(dx)) {
            // Swipe up on the peek opens the Shelf; swipe down on its header closes it.
            if (dy < 0) sh.classList.add('chvd-open');
            else if (t.target.closest('.shelf-head') || sh.scrollTop <= 0) sh.classList.remove('chvd-open');
            return;
        }
        // Swipe paging: horizontal, fast enough, and the page isn't wider than the view.
        if (desk && Math.abs(dx) > SWIPE_MIN && Math.abs(dx) > 2 * Math.abs(dy)
            && performance.now() - t.t < 600 && desk.scrollWidth <= desk.clientWidth + 1) {
            call('OnSwipe', dx < 0 ? 1 : -1);
        }
        return;
    }

    // Arrange on a phone: the Shelf rests as a peek; a tap on its header (or the peek) opens it.
    const shelf = t.target.closest ? t.target.closest('.bench-shelf') : null;
    if (shelf && document.documentElement.getAttribute('data-shell') === 'phone'
        && (!shelf.classList.contains('chvd-open') || t.target.closest('.shelf-head'))) {
        shelf.classList.toggle('chvd-open');
        return;
    }
    if (interactive(t.target)) return;
    const now = performance.now();
    if (desk && lastTap && now - lastTap.t < DOUBLE_TAP_MS && Math.hypot(e.clientX - lastTap.x, e.clientY - lastTap.y) < 30) {
        lastTap = null;
        call('OnDoubleTap');
        return;
    }
    lastTap = { x: e.clientX, y: e.clientY, t: now };
    showStrip(t.target);
}

function onCancel(e) {
    if (!isTouch(e)) return;
    touches.delete(e.pointerId);
    if (touches.size < 2) pinch = null;
    if (tap && tap.id === e.pointerId) tap = null;
    if (press && e.pointerId === press.id && !press.armed) cancelPress();
}

function onContextMenu(e) {
    // Android shows a context menu / text selection on long-press; not on a page being dragged.
    if (press && press.armed) e.preventDefault();
}

function call(method, arg) {
    if (!net) return;
    try { arg === undefined ? net.invokeMethodAsync(method) : net.invokeMethodAsync(method, arg); } catch { /* circuit gone */ }
}

// ── per-page strip: tap shows it (hover on desktop) ─────────────────────────
function showStrip(target) {
    const canvas = target && target.closest ? target.closest('.mview-canvas') : null;
    if (!canvas) return;
    canvas.classList.add('chvd-strip-on');
    clearTimeout(stripTimer);
    stripTimer = setTimeout(() => canvas.classList.remove('chvd-strip-on'), 4000);
}

// ── Arrange: long-press to drag ─────────────────────────────────────────────
function beginPress(e) {
    const card = e.target.closest ? e.target.closest('[data-bench-id], [data-shelf], [data-shelf-all]') : null;
    if (!card || e.target.closest('button') || e.target.closest('input')) return false;
    // bench.js would start its press now; hold it back until we know this isn't a scroll.
    e.stopPropagation();
    press = { id: e.pointerId, x: e.clientX, y: e.clientY, target: e.target, armed: false, timer: 0 };
    press.timer = setTimeout(armPress, LONG_PRESS_MS);
    return true;
}

function armPress() {
    if (!press) return;
    press.armed = true;
    try { navigator.vibrate && navigator.vibrate(12); } catch { /* not allowed */ }
    // From here on a moving finger drags the page, not the desks: stop the browser panning.
    guardMove = (ev) => { if (ev.cancelable) ev.preventDefault(); };
    document.addEventListener('touchmove', guardMove, { passive: false, capture: true });
    replayDown(press);
}

function replayDown(p) {
    const ev = new PointerEvent('pointerdown', {
        bubbles: true, cancelable: true, composed: true,
        pointerId: p.id, pointerType: 'touch', isPrimary: true, button: 0, buttons: 1,
        clientX: p.x, clientY: p.y,
    });
    ev.__chvdSynthetic = true;
    p.target.dispatchEvent(ev);
}

function cancelPress() {
    if (!press) return;
    clearTimeout(press.timer);
    press = null;
}

function endPress() {
    cancelPress();
    if (guardMove) {
        document.removeEventListener('touchmove', guardMove, { capture: true });
        guardMove = null;
    }
}

// ── orientation (phone View sheet: "Two-page · landscape only") ──────────────
let orientRef = null;
let orientHandler = null;

export function watchOrientation(dotnetRef) {
    orientRef = dotnetRef;
    if (orientHandler) window.removeEventListener('resize', orientHandler);
    orientHandler = () => {
        if (!orientRef) return;
        try { orientRef.invokeMethodAsync('SetLandscape', window.innerWidth > window.innerHeight); } catch { /* gone */ }
    };
    window.addEventListener('resize', orientHandler);
    orientHandler();
}

export function unwatchOrientation() {
    if (orientHandler) window.removeEventListener('resize', orientHandler);
    orientHandler = null;
    orientRef = null;
}
