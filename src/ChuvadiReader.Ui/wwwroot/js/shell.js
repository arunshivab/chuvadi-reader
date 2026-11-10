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

export function init(dotnetRef) {
    ref = dotnetRef;
    if (handler) document.removeEventListener('keydown', handler, true);
    handler = (e) => {
        const t = e.target;
        const tag = t && t.tagName;
        const editing = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || (t && t.isContentEditable);
        const c = combo(e);
        if (!c) return;
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
}

export function focusSelect(el) { try { el.focus(); el.select(); } catch { } }

export function scrollIntoView(el) { try { el.scrollIntoView({ block: 'nearest' }); } catch { } }

export function dispose() {
    if (handler) document.removeEventListener('keydown', handler, true);
    handler = null; ref = null;
}
