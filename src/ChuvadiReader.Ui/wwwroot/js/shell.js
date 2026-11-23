// D-10 shell keyboard: Ctrl K (command bar), Ctrl O (open), bare 1–4 (modes) and any
// shortcut a registered command declares (e.g. "Ctrl P", "Ctrl B", "F11", "T").
// The Reader's own keys (Ctrl F/Z/W/Tab/±/0, arrows, Esc) stay in reader.js.
let ref = null;
let handler = null;

const READER_OWNED = new Set(['Ctrl F', 'Ctrl Z', 'Ctrl W', 'Ctrl Shift W', 'Ctrl Alt W', 'Ctrl Tab', 'Ctrl Shift Tab',
    'Ctrl =', 'Ctrl +', 'Ctrl -', 'Ctrl _', 'Ctrl 0', 'Escape', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown',
    'PageUp', 'PageDown', 'Home', 'End', ' ']);

function combo(e) {
    let k = e.key;
    if (k === 'Control' || k === 'Shift' || k === 'Alt' || k === 'Meta') return null;
    if (k.length === 1) k = k.toUpperCase();
    const parts = [];
    if (e.ctrlKey || e.metaKey) parts.push('Ctrl');
    if (e.altKey) parts.push('Alt');
    if (e.shiftKey && k.length > 1) parts.push('Shift');
    else if (e.shiftKey && /[A-Z]/.test(k) && (e.ctrlKey || e.metaKey)) parts.push('Shift');
    parts.push(k);
    return parts.join(' ');
}

// A radio group moves with the arrow keys, Home and End, and the row moved to is chosen (the usual radio
// behaviour). Every role="radiogroup" in the app gets it: the encoding list, the segmented choices, Print's pages.
function radioKey(e) {
    if (e.ctrlKey || e.metaKey || e.altKey || e.shiftKey) return false;
    const step = { ArrowUp: -1, ArrowLeft: -1, ArrowDown: 1, ArrowRight: 1 }[e.key];
    if (step === undefined && e.key !== 'Home' && e.key !== 'End') return false;
    const radio = e.target && e.target.closest ? e.target.closest('[role="radio"]') : null;
    const group = radio && radio.closest('[role="radiogroup"]');
    if (!group) return false;
    const rows = [...group.querySelectorAll('[role="radio"]')].filter(r => !r.disabled);
    const at = rows.indexOf(radio);
    if (at < 0 || rows.length < 2) return false;
    const to = e.key === 'Home' ? 0 : e.key === 'End' ? rows.length - 1 : (at + step + rows.length) % rows.length;
    e.preventDefault();
    e.stopPropagation();
    if (to !== at) {
        try { rows[to].focus({ preventScroll: false }); } catch { }
        rows[to].click();
    }
    return true;
}

export function init(dotnetRef) {
    ref = dotnetRef;
    if (handler) document.removeEventListener('keydown', handler, true);
    handler = (e) => {
        const t = e.target;
        const tag = t && t.tagName;
        const editing = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable);
        const c = combo(e);
        if (!c) return;
        if (radioKey(e)) return;
        if (c === 'Ctrl K') { e.preventDefault(); e.stopPropagation(); ref.invokeMethodAsync('OnShellKey', c); return; }
        if (editing) return;
        // AT-17: a selected text box takes plain typing — the key goes into the box (the caret is moved there before
        // the key is applied), not to a one-key command ("i" opened the image picker while typing a note).
        if (e.key.length === 1 && !(e.ctrlKey || e.metaKey || e.altKey)) {
            const box = document.querySelector('.mk-text.sel .mk-ta');
            if (box) { try { box.focus({ preventScroll: true }); } catch { } return; }
        }
        if (READER_OWNED.has(c)) return;
        if (c === 'Ctrl O' || c === 'Ctrl P' || c === 'Ctrl S' || c === 'Ctrl Shift S' || c === 'Ctrl B' || c === 'F11' || c === 'Ctrl Y') {
            e.preventDefault();
        }
        // Bare digits 1–4, letters and function keys go to the shell; it runs whichever
        // registered command declares that key (and ignores the rest).
        const bare = !(e.ctrlKey || e.metaKey || e.altKey);
        if (bare && /^(?:[1-4A-Z]|F\d{1,2})$/.test(c) === false) return;
        ref.invokeMethodAsync('OnShellKey', c);
    };
    document.addEventListener('keydown', handler, true);
    if (!wheel) { wheel = tabWheel; document.addEventListener('wheel', wheel, { passive: false }); }
    watchTabs();
}

export function focusSelect(el) { try { el.focus(); el.select(); } catch { } }

export function scrollIntoView(el) { try { el.scrollIntoView({ block: 'nearest' }); } catch { } }

// The tab strip has no scroll bar: the active tab is kept in view, and the mouse wheel over the strip moves it
// sideways.
let wheel = null;
function tabWheel(e) {
    const strip = e.target && e.target.closest ? e.target.closest('.topbar .rtabs') : null;
    if (!strip || strip.scrollWidth <= strip.clientWidth || e.ctrlKey) return;
    const d = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
    if (!d) return;
    strip.scrollLeft += d;
    e.preventDefault();
}

let lastActive = null;
function revealActiveTab() {
    const tab = document.querySelector('.topbar .rtabs .rtab.active');
    if (!tab || tab === lastActive) return;
    lastActive = tab;
    try { tab.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch { }
}

// Watches the page for a different active tab (a switch, an open, a close) and brings it into view.
let tabWatch = null;
function watchTabs() {
    if (tabWatch) return;
    tabWatch = new MutationObserver(() => revealActiveTab());
    tabWatch.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class'] });
    revealActiveTab();
}

export function dispose() {
    if (handler) document.removeEventListener('keydown', handler, true);
    if (wheel) document.removeEventListener('wheel', wheel, { passive: false });
    if (tabWatch) tabWatch.disconnect();
    handler = null; ref = null; wheel = null; tabWatch = null; lastActive = null;
}
