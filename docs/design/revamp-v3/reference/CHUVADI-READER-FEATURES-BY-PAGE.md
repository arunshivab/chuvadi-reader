# Chuvadi Reader — feature list by page

Built from the current code (every page read in full). Flat checklist per screen.
Status tags: **(v2)** built-but-disabled / coming soon · **(gated)** needs an entitlement ·
**(locked)** waiting on a library fix · everything else is live.

---

## Dashboard (home)
- Open/fetch a document (drop-tile "Drop a document, or click to fetch"; opens in Reader)
- See greeting + library stats (doc count, encrypted count, "nothing leaves this device")
- Watched-folder tile: path, document count, last-scan time; "change" / "+ add folder"
- Tools launcher: Reader, Bench, Library, Merge, Split, Compress, Redact
- Sign **(v2)** · OCR **(v2)**
- Storage-map tiles per category (count, size, usage bar) → open Library filtered
- Pinned list; Recent list (click to open, shows lock icon + relative time)

---

## Reader

### Page navigation
- Go to next / previous page (step = 2 in two-page mode)
- Jump to a page by typing its number ("/ {total}")
- Scroll continuously or page-by-page; remembers scroll + zoom per tab

### Zoom & view size
- Zoom in / zoom out (10%–800%, live %)
- Fit width · Fit page · Actual size (100%)
- Zoom presets (50/75/100/125/150/200/400%)
- Ctrl + mouse-wheel zoom toward cursor; +/–/0 keys; Undo view changes

### Page layout
- Single page · Two-page (facing)
- Show cover alone
- Rotate view left / right, scope = this page / this page onward / whole document

### Reading tools
- Hand (pan) tool
- Marquee zoom (drag a box to zoom to it)
- Loupe magnifier (lens size S/M/L, 1.5×/2×/3×)
- Snapshot a region → Save as PNG / Copy to clipboard / Send to a desk
- Lock scrolling
- Immersive / full-screen reading mode (hover bar brings controls back)
- Hover floating controls at bottom edge; page-curl corner
- Pinch-to-zoom, one-finger pan, double-tap zoom (touch; untested on hardware)

### Find
- Find in document (Ctrl+F); whole-word chip; match count; next (Enter) / previous (Shift+Enter); seeds from selected text

### Bookmarks / outline
- Toggle bookmarks panel
- Add bookmark at current page; move up/down; indent/nest; outdent; delete
- Go to a bookmark's page; double-click to rename
- Save bookmarks into the file

### Markup / annotate (Edit mode)
- Text box — place or drag to size; font (Helvetica/Times/Courier), bold, italic, size, align L/C/R/justify, colour, rotation, delete
- Image — add via picker, paste, or drop; aspect lock, replace, opacity, rotation, send behind page, delete
- Shapes — rectangle, line, ellipse, arrow, freehand draw; stroke colour + width, fill toggle+colour, rotation, send behind, delete; draggable line/arrow endpoints
- Signature — type (name + Script/Elegant/Classic font + ink colour) or upload an image; placed as an overlay
- Move/resize any overlay by its handles
- Clear all overlays · Save (overwrite) · Save as new PDF · Done
- Overlay counter (n text · n shapes · n images)

### Watermark & header/footer (Stamp)
- Watermark — text, font, bold/italic, colour, size, opacity, angle (0/45/90/315°); **all pages or a page range**
- Header and Footer — enable each; left/centre/right slots; size, text colour, band fill; **all pages or a page range**
- Save (overwrite) · Save as new PDF

### Whole-document actions (top bar / menu)
- Flatten form fields & markup to a static PDF (incl. XFA → static)
- Compress (Press) — Light/Balanced/Strong/Custom; optional target MB; before→after stats; opens result
- Document properties (title, author, dates, page size, encryption, XFA, fast-web-view, file size…)
- Export pages as images — SVG/PNG/JPG/BMP, this page or all, 96/150/300/600 dpi, choose folder
- Page colour for viewing only (White/Sepia/Gray/Mint/Pale-blue/custom; remembered, not written to file)
- Send current page to a desk (existing desk or new desk; capped at 30 desks)
- Open in Bench (adds doc as a Bench source)
- Send to Redact **(gated)**
- Recovery banner "Try recovery" — opt-in, writes a "(recovered)" copy (error-only)

### Tabs & misc
- Multi-document tabs: close active / others / all, cycle, activate by index 1–9, drag to reorder, restored on relaunch
- Open by drag-and-drop onto the window
- Toast notifications

---

## Redact **(gated — redirects out if not entitled; always saves a new file)**
- Receive a doc from Reader, or fetch another
- Next/previous page, jump-to-page, zoom in/out
- Draw a redaction box by dragging on a page; remove a box (×)
- Colour: Black / White / custom
- Box counter + found-count; tagged-PDF warning (save blocked for tagged PDFs)
- Find & redact text — case-sensitive, whole-word, all pages or range; accept/exclude each match, select all/none, "Redact checked (n)", "Redact all matches"; Box vs Glyph-only
- Category/regex presets (Financial · Medical · PII), "+regex", "Replace" — all **(locked)**
- Clear all · Save redacted PDF → · Open in Reader

---

## Library
- Search a word across every PDF in the folder → results (name, page, snippet); click a hit to open at that page
- Filter by category chips and manual tag chips; clear all
- View switch: Table / Library (shelf; A–Z, thickness = page count)
- Table row: select, open, category tag, add/remove manual tags, pages, size, updated
- Shelf view: books grouped by category, thickness = pages; click to open
- Press (compress) selected files — presets + custom, optional per-file target MB, choose folder, total % saved

---

## Merge
- Add files (picker) · Add open documents
- Reorder list (move up/down), remove a row
- Merge & Save (needs ≥2 files) → choose output
- Open result in Reader

---

## Split
- Source = current Reader tab or chosen file; shows page count
- Every page → separate files (into a folder), or Extract a page range (single PDF)
- Range steppers (From/To); ± buttons or inline chevrons (per Settings)
- Split to folder / Extract range · Open result

---

## Docs (Word viewer, read-only)
- Open a .docx and view it — headings, formatting, tables, images, header/footer; page size, landscape flag, block count
- Rejects non-.docx; message for password-protected / open errors

---

## Sheets (Excel viewer, read-only)
- Open an .xlsx — grid with column letters + row numbers, values, styling (align, bold/italic/underline, font, size, colour, fill, wrap), frozen header rows, column widths
- Switch worksheets via sheet tabs
- Rejects non-.xlsx; message for password-protected

---

## Settings
- Theme: Mono lobby (light) / Grid lobby (dark)
- Splash screen on launch: on/off
- Page-number controls: ± buttons / inline chevrons
- Where files are saved: base folder, change / reset, month subfolder (yyyy-MM), date subfolder (yyyy-MM-dd)
- Preferred name (used in the greeting)

---

## Bench (page workbench)

### Bench-wide (top bar)
- Session: Save session… / Open session…
- Reset (clear the bench; bound files untouched)
- Undo (Ctrl+Z) / Redo (Ctrl+Y)
- View density: 1 / 2 / 3 desks per row (remembered)
- Fetch File (add PDFs to the shelf)
- Bind all (every desk → chosen folder)
- Combine (every desk → one PDF)
- Add desk (capped at 30)
- Extract (n) selected pages → new desk
- Compare (exactly 2 selected pages, side by side)

### Shelf (source files)
- Add/fetch a file; thumbnail-size slider
- Collapse all / Expand all; collapse/expand one source
- Interleave two files page-for-page (A1,B1,A2,B2…) → new desk
- Per source: name, Properties, page count, move up/down, Press (compress), Close file (× — blocked if its pages are on a desk)
- Search within one file → page-chip matches, "only matches" filter
- Page-range filter ("1-5, 8") to show only those pages; "All" clears
- Drag a whole file, or a single page, onto a desk

### Desk — header & settings
- Desk colour (8 colours / none)
- Rename desk (inline; names itself after its source when all pages are from one file)
- Toggles shown as chips: watermark, header/footer, numbering, normalise size, bookmarks-on-bind
- Page count; Select all (Ctrl+A) / Invert selection
- Move desk left/right, Duplicate desk, Clear (delete all pages), Delete desk
- More (⋯): Open in Reader, Send to Redact, Save as template…, Apply template

### Page actions inside a desk
- Drag to reorder within a desk or between desks; click to select; shift-click a range
- Per page: Peek (enlarged preview), Duplicate, Rotate, Split desk here, Crop, Margins, Remove
- Delete one page, or delete a whole selection
- Badges: blank / image / rotation° / crop / margin; label = original source page number
- Crop modal: drag a box → Blank rest / Crop to size / Fit to page / Fit + margin / Clear
- Margins modal: in/cm, Top/Right/Bottom/Left, default 0.5 in, apply this page / all pages / clear

### Desk — footer actions
- Fetch image into the desk
- Add blank page (optionally coloured)
- Reverse page order
- Sort into source order
- Impose (N-up 2/4/6/9 or custom rows×cols, or booklet; A4/Letter; gutter) → new desk
- Number (page / page X of Y / Bates) — position, font size, **all pages or range**, first-page skip options, live preview, clear
- Size: original / A4 / Letter (normalise at bind/export)
- Watermark (see modal) · Footer/header (see modal)
- Scatter (every page → its own 1-page PDF into a folder)
- Export (PDF, or SVG/PNG/JPG/BMP at 96/150/220/300 dpi; selected or all; choose folder)
- Lift (save selected pages) · Bind (save the whole desk → PDF; Open after)

### Desk modals
- Watermark: text, font + bold/italic + size, colour, orientation (horizontal/vertical/diagonal/custom angle), opacity, **all pages or selected**, live preview, remove
- Header & footer: Quick or Custom; per slot pick Page# / Page X of Y / Total / Filename / Full path / Date / Time / Date+Time / Custom; format selects; size, colour; page-fit = overlay / shrink page / shrink-if-overlapping; band background; **all pages or range**; live preview; remove
- Numbering, Scatter, Export, Impose (as above)
- Properties (per source): title, author, subject, keywords, pages, page size, size, dates, producer, creator, encrypted, linearized, XFA
- Compare: two pages side by side at true aspect

### Bench keyboard & gestures
- Ctrl+Z undo · Ctrl+Y / Ctrl+Shift+Z redo
- Ctrl+A select all in active desk · Ctrl+D duplicate selected
- R rotate clockwise · Shift+R counter-clockwise
- Delete / Backspace remove selected · Esc clear selection / close Compare
- Drop files from the file explorer onto the Bench → added to shelf (non-PDFs skipped)

---

## Notes
- **Entitlement-gated:** Redact (route + the Reader "Send to Redact" button).
- **Coming soon (v2, disabled):** Dashboard Sign and OCR.
- **Locked pending a library fix:** Redact regex/category presets + Replace; "shrink to fit" header/footer option.
- The older `docs/FEATURES.md` is a narrative tour and predates some of this (mode bar, recovery, Redact/Merge/Split, the Bench crop/margins/impose/numbering set); this list reflects the current code.
