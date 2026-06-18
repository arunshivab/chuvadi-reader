# Chuvadi Reader — Features

A guided tour of what Chuvadi Reader does, written for anyone who wants to
understand the app without reading the code. Features are grouped into the two
halves of the app — the **Reader** (where you read documents) and the **Bench**
(where you assemble, edit, and bind documents) — and numbered continuously.

Status tags: **(shipped)** is in the build and confirmed working; **(new)** landed
in the most recent delta and is awaiting your testing; **(untested)** is built but
needs a specific environment (e.g. a touchscreen) to verify.

---

## The Reader

### 1. Multi-document tabs in the title bar (shipped)
Open several documents at once, each in its own tab. The tabs live in the window's
title bar rather than a separate strip, so no vertical space is lost to chrome. You
can drag tabs to reorder them, middle-click a tab to close it, and use a "⋯" menu to
close the active tab, close the others, or close all. Open tabs are remembered and
restored the next time you launch the app.

### 2. Document rendering as crisp vector pages (shipped)
Every page is rendered as inline SVG rather than a rasterised bitmap, so text stays
sharp at any zoom and the page is light to draw. Pages render on demand as you scroll
toward them, so even large documents open quickly.

### 3. Zoom, fit, and navigation controls (shipped)
Zoom with the toolbar buttons, with Ctrl + mouse-wheel (which zooms toward the cursor),
or with keyboard shortcuts. Snap to **Fit width**, **Fit page**, or **Actual size**
(100%). Jump straight to a page by typing its number in the page box. Standard
shortcuts are wired throughout (Escape, page numbers 1–9, +/-/0 for zoom, Ctrl+Tab to
cycle tabs, and more).

### 4. Reading tools — marquee zoom, loupe, and hand (shipped)
Beyond plain zoom, the reader offers a **marquee** tool (drag a rectangle to zoom into
exactly that region), a **loupe** (a magnifier that follows the pointer for close
inspection without changing the overall zoom), and a **hand** tool for click-drag
panning when a page is larger than the viewport.

### 5. Single-page and two-page (facing) layouts (shipped)
Read one page at a time or two side by side like an open book, with an option for the
cover page to stand alone. Switch between paged scrolling and continuous scrolling to
match how you prefer to move through a document.

### 6. Immersive reading mode (shipped)
Hide the toolbar and surrounding chrome to give the page the full window. A slim hover
bar brings the controls back when you need them, then gets out of the way again.

### 7. Touchscreen pinch-to-zoom (untested)
On a touchscreen, pinch with two fingers to zoom the page canvas only — the title bar,
sidebar, and status bar stay put. One finger pans or scrolls naturally, and a
double-tap toggles a quick zoom. This is built but has not yet been verified on real
touch hardware; it will be tested before deployment.

### 8. Open by drag-and-drop (shipped)
Drag a file from the desktop or a folder straight onto the window to open it in a new
tab — no need to go through a file dialog.

---

## The Bench (the page workbench)

The Bench is where Chuvadi Reader stops being a reader and becomes a tool for
*building* documents. The model is a **shelf** of source files on the left and a set
of independent **desks** on the right. You drag pages from the shelf into desks,
arrange them, and each desk binds to its own finished PDF.

### 9. The shelf — your loaded source files (new)
Fetch one or more PDFs and they line up on the shelf, each with a coloured dot that
identifies it, its page count, and a strip of page thumbnails you can drag from. Each
source can be **collapsed** to just its header (to tidy a busy shelf) or **expanded**
to show its pages again.

### 10. Per-source Properties and Press (new)
Every file on the shelf carries two inline actions. **Properties** opens the document's
metadata (title, author, page count, size, dates, producer, encryption, and more).
**Press** compresses that file — choose Light (lossless), Balanced, or Strong — and
reports how much space was saved.

### 11. Desks — independent compositions (new)
Each desk is its own ordered arrangement of pages that binds to its own PDF. You can
have up to **30** desks. Add a desk from the toolbar (always visible) or from the tile
at the end of the desk area. A desk names itself after its source file when all of its
pages come from one document, otherwise it stays "Desk 1, 2, …"; the name is always
editable and becomes the suggested filename when you save.

### 12. View density — one, two, or three desks per row (new)
A toggle in the toolbar lays the desks out one, two, or three per row. It defaults to
**one per row** (the most spacious working view) and your choice is remembered between
sessions.

### 13. Dragging pages and whole files into desks (new)
Drag any page thumbnail from the shelf into a desk to copy it there — the same source
page can live in several desks at once. Drag a file's **header** to drop *all* of its
pages into a desk in order. Within a desk you can drag pages to reorder them, or drag
them from one desk to another. A small thumbnail follows the cursor while you drag so
you can see what you're moving.

### 14. Page labels — provenance at a glance (new)
Under each page in a desk is its **original page number from its source file** — not
its position in the desk. So a page that was page 5 of its book always reads "5",
wherever it sits. This tells you where a page came from; the left-to-right order tells
you the bind sequence. Inserted blanks read **(blank)** and dropped images read
**(image)**, since neither has a source page number.

### 15. Select, rotate, trim, rearrange (new)
Click a page to select it, Shift-click to select a range within a desk. Rotate a page
(hover and use its rotate button), remove it, or trim a whole selection. Each desk page
also has a **peek** button (a loupe) — hover it to see an enlarged preview of that page
without opening it. Everything happens per desk, so a page rotated in one desk is
unaffected in another.

The six desk actions (Fetch image, Add blank, Watermark, Scatter, Export, Remove desk)
are reachable both from the desk's "⋯" menu and as quick icon-buttons in the desk footer
next to Lift and Bind. At three desks per row the footer tightens to icons with tooltips;
at one or two per row the Lift/Bind buttons show their text.

### 16. Blank and coloured-blank pages (new)
Insert a blank page into a desk from its menu — useful as a separator or a cover. A
blank can carry a background colour. Blanks are created fresh at bind time, so they add
no weight until you save.

### 17. Image pages — drop a scan or photo in (new)
Fetch one or more images straight into a desk (PNG, JPG, and the usual formats). An
image stays a raw image right up until you bind — it is **not** pre-converted to a PDF
on disk — and is woven into the output only when the desk is bound. This keeps a
dropped scan lightweight and avoids leaving stray converted files around.

### 18. Per-desk watermark (new)
Give a desk a text watermark with adjustable opacity. It applies to the whole desk or
to just the pages you've selected, and is stamped onto that desk's output when you bind
or lift.

### 19. Bind and Lift — saving a desk (new)
**Bind** saves all of a desk's pages as one PDF. **Lift** saves only the pages you've
selected in that desk. Both open a save dialog pre-filled with the desk's name. After
saving, an **Open** action opens the result in the Reader.

### 20. Bind all (new)
Bind every non-empty desk in one go: pick a folder and each desk is written as its own
PDF, named after the desk.

### 21. Scatter and Export per desk (new)
**Scatter** writes every page of a desk as its own single-page PDF into a folder — the
inverse of binding. **Export** renders a desk to images (SVG, PNG, JPG, or BMP) at a
chosen resolution, for all pages or just the selected ones.

### 22. Reset the bench (new)
Clear every source and desk in one action (with a confirmation), to start a fresh
assembly. Files you've already saved are untouched.
