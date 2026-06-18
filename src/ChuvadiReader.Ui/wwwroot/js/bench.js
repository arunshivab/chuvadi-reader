// Bench interop. One module instance per Bench page. Responsibilities:
// (1) lazily render shelf and desk thumbnails as they near the viewport,
// (2) drag a shelf page (or a whole file) into a desk, or move a desk page
//     within / across desks,
// (3) translate a plain press on a desk page into a click-select.
// All ordering and selection state lives in .NET; this module reports intent.

let observer = null;
let root = null;
let dotnet = null;
let down = null;        // { type:'shelf'|'source'|'desk', el, x, y, srcIdx?, pageIdx?, id? }
let dragging = false;
let ghost = null;
let escHandler = null;
let peek = null;
const THRESHOLD = 6;     // px of movement before a press becomes a drag
const GHOST_W = 180;     // drag-preview width in px (~22% of a 96-DPI A4 page) — readable

export function init(gridEl, dotnetRef, lazyThumbs) {
    teardown();
    root = gridEl;
    dotnet = dotnetRef;

    if (lazyThumbs) {
        observer = new IntersectionObserver((entries) => {
            for (const e of entries) {
                if (!e.isIntersecting) continue;
                const t = e.target;
                const benchId = t.getAttribute('data-bench-id');
                if (benchId) { invoke('RenderThumb', benchId); continue; }
                const shelf = t.getAttribute('data-shelf');
                if (shelf) {
                    const [s, p] = shelf.split(':');
                    invoke('RenderShelfThumb', parseInt(s, 10), parseInt(p, 10));
                }
            }
        }, { root: gridEl, rootMargin: '500px 0px', threshold: 0 });

        gridEl.querySelectorAll('[data-bench-id],[data-shelf]').forEach(el => observer.observe(el));
    }

    gridEl.addEventListener('pointerdown', onPointerDown);
    gridEl.addEventListener('pointerover', onPeekOver);
    gridEl.addEventListener('pointerout', onPeekOut);

    escHandler = (e) => { if (e.key === 'Escape') invoke('CloseTransient'); };
    document.addEventListener('keydown', escHandler);
}

// Hover-peek: hovering a page's peek button shows an enlarged preview of that page.
function onPeekOver(e) {
    const btn = e.target.closest('[data-peek]');
    if (btn) showPeek(btn);
}

function onPeekOut(e) {
    const btn = e.target.closest('[data-peek]');
    if (btn && !btn.contains(e.relatedTarget)) hidePeek();
}

function showPeek(btn) {
    hidePeek();
    const id = btn.getAttribute('data-peek');
    const esc = (window.CSS && CSS.escape) ? CSS.escape(id) : id;
    const page = root.querySelector(`[data-bench-id="${esc}"]`);
    const thumb = page && page.querySelector('.bthumb');
    if (!thumb) return;

    const box = document.createElement('div');
    const clone = thumb.cloneNode(true);
    clone.style.width = '100%';
    clone.style.height = '100%';
    clone.querySelectorAll('svg,img').forEach(el => { el.style.width = '100%'; el.style.height = '100%'; });
    box.appendChild(clone);

    const pw = 240, ph = Math.round(pw * 1.414);
    // Styled inline: this element is appended to <body>, so Blazor's scoped
    // .bench-peek rule (which carries a scope attribute) would never match it.
    box.style.position = 'fixed';
    box.style.zIndex = '210';
    box.style.width = pw + 'px';
    box.style.height = ph + 'px';
    box.style.boxSizing = 'border-box';
    box.style.background = 'var(--chvd-surface-hi, #fff)';
    box.style.border = '1px solid var(--chvd-border, #d8cdbb)';
    box.style.borderRadius = '10px';
    box.style.boxShadow = '0 18px 48px rgba(0,0,0,0.45)';
    box.style.overflow = 'hidden';
    box.style.pointerEvents = 'none';
    document.body.appendChild(box);

    const r = page.getBoundingClientRect();
    let left = r.left + r.width / 2 - pw / 2;
    let top = r.top - ph - 10;
    if (top < 8) top = Math.min(r.bottom + 10, window.innerHeight - ph - 8);
    left = Math.max(8, Math.min(left, window.innerWidth - pw - 8));
    box.style.left = left + 'px';
    box.style.top = top + 'px';
    peek = box;
}

function hidePeek() {
    if (peek && peek.parentNode) peek.parentNode.removeChild(peek);
    peek = null;
}

function onPointerDown(e) {
    if (e.button !== 0) return;
    // Let buttons (collapse/icons/rotate/remove/menus) and the desk-name input keep native behaviour.
    if (e.target.closest('button') || e.target.closest('input')) return;

    const shelf = e.target.closest('[data-shelf]');
    const shelfAll = e.target.closest('[data-shelf-all]');
    const page = e.target.closest('[data-bench-id]');

    if (shelf && root.contains(shelf)) {
        const [s, p] = shelf.getAttribute('data-shelf').split(':');
        down = { type: 'shelf', el: shelf, x: e.clientX, y: e.clientY, srcIdx: parseInt(s, 10), pageIdx: parseInt(p, 10) };
    } else if (shelfAll && root.contains(shelfAll)) {
        down = { type: 'source', el: shelfAll, x: e.clientX, y: e.clientY, srcIdx: parseInt(shelfAll.getAttribute('data-shelf-all'), 10) };
    } else if (page && root.contains(page)) {
        down = { type: 'desk', el: page, x: e.clientX, y: e.clientY, id: page.getAttribute('data-bench-id') };
    } else {
        return;
    }

    dragging = false;
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
}

function onPointerMove(e) {
    if (!down) return;
    if (!dragging) {
        if (Math.abs(e.clientX - down.x) + Math.abs(e.clientY - down.y) < THRESHOLD) return;
        dragging = true;
        down.el.classList.add('dragging');
        makeGhost(down);
    }
    e.preventDefault();
    moveGhost(e.clientX, e.clientY);
    paintMarks(dropTarget(e.clientX, e.clientY));
}

function onPointerUp(e) {
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', onPointerUp);
    if (!down) return;

    if (dragging) {
        const hit = dropTarget(e.clientX, e.clientY);
        clearMarks();
        removeGhost();
        down.el.classList.remove('dragging');
        if (hit) {
            if (down.type === 'shelf') {
                invoke('DropShelfPage', down.srcIdx, down.pageIdx, hit.deskId, hit.index);
            } else if (down.type === 'source') {
                invoke('DropSourceAll', down.srcIdx, hit.deskId, hit.index);
            } else {
                invoke('DropDeskPage', down.id, hit.deskId, hit.index);
            }
        }
    } else if (down.type === 'desk') {
        invoke('SelectBenchPage', down.id, e.shiftKey === true);
    }

    down = null;
    dragging = false;
}

// Find the desk under the pointer and the insertion index among its pages.
function dropTarget(x, y) {
    if (ghost) ghost.style.display = 'none';
    const under = document.elementFromPoint(x, y);
    if (ghost) ghost.style.display = '';
    if (!under) return null;

    const dropEl = under.closest('[data-desk]');
    if (!dropEl || !root.contains(dropEl)) return null;

    const deskId = dropEl.getAttribute('data-desk');
    const pages = Array.from(dropEl.querySelectorAll('[data-bench-id]'));
    let best = -1, bestDist = Infinity, after = false;
    for (let i = 0; i < pages.length; i++) {
        const r = pages[i].getBoundingClientRect();
        const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
        const d = Math.hypot(x - cx, y - cy);
        if (d < bestDist) { bestDist = d; best = i; after = x > cx; }
    }
    const index = best < 0 ? 0 : (after ? best + 1 : best);
    return { deskId, dropEl, pages, index };
}

function paintMarks(hit) {
    clearMarks();
    if (!hit) return;
    hit.dropEl.classList.add('desk-over');
    const { pages, index } = hit;
    if (!pages.length) return;
    if (index >= pages.length) pages[pages.length - 1].classList.add('drop-after');
    else pages[index].classList.add('drop-before');
}

function clearMarks() {
    if (!root) return;
    root.querySelectorAll('.desk-over').forEach(el => el.classList.remove('desk-over'));
    root.querySelectorAll('.drop-before,.drop-after').forEach(el => el.classList.remove('drop-before', 'drop-after'));
}

function makeGhost(d) {
    removeGhost();
    let node, w, h = null;
    if (d.type === 'source') {
        // whole-file drag: a small chip of the file header
        node = d.el.cloneNode(true);
        w = 150;
    } else {
        const thumb = d.el.querySelector('.bthumb, .thumb') || d.el;
        node = thumb.cloneNode(true);
        w = GHOST_W;                    // readable A4 preview, same for shelf and desk pages
        h = Math.round(w * 1.414);
    }
    node.style.position = 'fixed';
    node.style.left = '0';
    node.style.top = '0';
    node.style.margin = '0';
    node.style.width = w + 'px';
    node.style.height = h ? h + 'px' : 'auto';
    node.style.pointerEvents = 'none';
    node.style.opacity = '0.85';
    node.style.zIndex = '200';
    node.style.overflow = 'hidden';
    node.style.transform = 'translate(-9999px,-9999px)';
    node.style.boxShadow = '0 6px 16px rgba(0,0,0,0.4)';
    // Force any inner SVG/image to fill the shrunk ghost regardless of scoped CSS.
    node.querySelectorAll('svg,img').forEach(el => { el.style.width = '100%'; el.style.height = '100%'; });
    ghost = node;
    document.body.appendChild(ghost);
}

function moveGhost(x, y) {
    if (!ghost) return;
    const gw = ghost.offsetWidth || GHOST_W;
    const gh = ghost.offsetHeight || Math.round(GHOST_W * 1.414);
    // Place the preview to the right of the cursor (flip left near the right edge),
    // vertically centred and clamped — so the drop point under the cursor stays visible.
    let gx = x + 18;
    if (gx + gw > window.innerWidth - 4) gx = x - 18 - gw;
    gx = Math.max(4, gx);
    let gy = Math.max(4, Math.min(y - gh / 2, window.innerHeight - gh - 4));
    ghost.style.transform = `translate(${gx}px, ${gy}px)`;
}

function removeGhost() {
    if (ghost && ghost.parentNode) ghost.parentNode.removeChild(ghost);
    ghost = null;
}

function invoke(method, ...args) {
    if (dotnet) dotnet.invokeMethodAsync(method, ...args);
}

function teardown() {
    if (observer) observer.disconnect();
    if (root) {
        root.removeEventListener('pointerdown', onPointerDown);
        root.removeEventListener('pointerover', onPeekOver);
        root.removeEventListener('pointerout', onPeekOut);
    }
    if (escHandler) { document.removeEventListener('keydown', escHandler); escHandler = null; }
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', onPointerUp);
    clearMarks();
    removeGhost();
    hidePeek();
    observer = null; root = null; down = null; dragging = false;
}

export function dispose() {
    teardown();
    dotnet = null;
}
