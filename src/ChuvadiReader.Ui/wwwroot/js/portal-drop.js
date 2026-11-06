// D-89 A — web portal drag-and-drop (loaded only on the portal, by PortalDropCatcher).
// While documents are dragged over the window, the full-window file input is switched on (class "on")
// so the drop lands in it and Blazor's InputFile uploads it. Image-only drags are left alone (the Reader
// places dropped images on a page). A drop that lands anywhere else is stopped so the browser doesn't
// open the file and leave the app.

const isImageOnly = (dt) => {
    const items = dt && dt.items ? Array.from(dt.items).filter(i => i.kind === 'file') : [];
    return items.length > 0 && items.every(i => i.type && i.type.startsWith('image/'));
};
const hasFiles = (dt) => !!dt && Array.from(dt.types || []).includes('Files');

export function attach(root) {
    const input = root.querySelector('input[type=file]');
    let depth = 0;
    const on = () => root.classList.add('on');
    const off = () => { depth = 0; root.classList.remove('on'); };

    const enter = (e) => {
        if (!hasFiles(e.dataTransfer) || isImageOnly(e.dataTransfer)) return;
        depth++;
        on();
    };
    const leave = (e) => {
        if (!hasFiles(e.dataTransfer) || !root.classList.contains('on')) return;
        if (--depth <= 0) off();
    };
    const over = (e) => {
        if (!hasFiles(e.dataTransfer)) return;
        e.preventDefault();                       // a drop here is allowed (and not the browser's)
        e.dataTransfer.dropEffect = 'copy';
    };
    const drop = (e) => {
        if (!hasFiles(e.dataTransfer)) return;
        if (e.target !== input) e.preventDefault(); // never let the browser open the file
        setTimeout(off, 0);
    };
    const end = () => off();

    document.addEventListener('dragenter', enter, true);
    document.addEventListener('dragleave', leave, true);
    document.addEventListener('dragover', over, true);
    document.addEventListener('drop', drop, true);
    window.addEventListener('dragend', end, true);
    window.addEventListener('blur', end);

    return {
        dispose() {
            document.removeEventListener('dragenter', enter, true);
            document.removeEventListener('dragleave', leave, true);
            document.removeEventListener('dragover', over, true);
            document.removeEventListener('drop', drop, true);
            window.removeEventListener('dragend', end, true);
            window.removeEventListener('blur', end);
        },
    };
}
