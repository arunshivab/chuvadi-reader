# Handoff: Chuvadi Reader — full UI revamp

## Overview
Chuvadi Reader is an offline Windows/macOS/Linux/web PDF reader and editor. It also reads Word and Excel files (read-only, with Convert to PDF). It is built with Blazor (`ChuvadiReader.Ui`) in a WPF host. This revamp replaces the left navigation rail and its 8 destinations (Home, Reader, Bench, Redact, Library, Docs, Sheets, Settings) with **one document-centric shell**:

- Every file opens as a **tab** (PDF, DOCX, XLSX, images).
- Each PDF tab has **four modes**: **Read · Mark · Arrange · Protect**.
  - Arrange = the old Bench (shelf + desks).
  - Protect = the old Redact page, plus Stamp, Flatten and Password.
- Only the active mode's tools are visible.
- **Home** is opened by clicking the wordmark; **Library** is a permanent button beside it; **Settings** is the gear icon.
- **Ctrl K / ⌘K** opens a command bar that can find and run any feature.

> ## ⚠ Read this first: D-10 supersedes the chrome below
> The locked decisions in `reference/CHUVADI-READER-DESIGN-AND-ROADMAP.md` (D-07 to D-10) **override** parts of this README. Where they conflict, follow the list below and **`Reader Side Rails.dc.html`**:
> - **D-10 side rails replace the mode bar and the floating leaf bar.**
>   - **Top bar:** one thin 40 px line: wordmark · tabs · **‹ 2 / 13 ›** (centred, always visible) · **Document ▾** · Ctrl K · window controls.
>   - **Document ▾** holds Properties, Export, Compress, Merge, Split and Print.
>   - The 52 px mode bar is gone.
> - **Left bar (62 px, fixed):** Read · Mark · Arrange · Protect (icons; 3 px `binding` marker on the active mode), then Open · Find · Library · Home, with Settings at the foot. It **never changes with mode**. On hover it expands over the canvas to show labels and shortcuts.
> - **Right · Tools rail (64 px, nearest the page):** follows the mode.
>   - Read: Pages & outline, Bookmark, Snapshot, Layers, Measure, Signatures, Print, Immersive.
>   - Mark: markup tools, then Undo/Redo, with Save and **Save as new** at the foot.
>   - Arrange: Shelf, Fetch, Add desk, Blank page, Undo/Redo, Session, Reset, then Combine and **Bind all**.
>   - Protect: Redact box, Find & redact, Stamp, Watermark, Flatten, Password, Sign, then **Save redacted copy**.
> - **Right · View rail (54 px, outermost, fixed set):** zoom ± and %, fit width/page, single/two-page/cover, rotate view, hand, loupe. In Arrange: desks per row 1/2/3 and page size.
> - **Settings cards:** the object settings that used to live in the 296 px **margin panel** become **floating cards** in the right gutter beside the Tools rail (text box, Find & redact, Layers + Measure). They never cover the page. The **Pages/outline drawer** (216 px) and the **Shelf** (Arrange) slide out beside the left bar.
> - **Under the page:** a strip that appears on hover: Rotate page · To desk · Snapshot · Bookmark · Peek.
> - **Narrow or portrait window (< 1000 px):** the left and Tools rails shrink to 48 px; the View rail folds into one View button in the Tools rail; the tabs collapse to the active tab plus a "+N" count.
> - **D-07:** Office files stay view-only; **Convert to PDF** is the way to edit them.
> - **D-08:** fidelity comes from the new `Chuvadi.Layout` library. Render the Office view from Layout output.
> - **D-09:** HTML, Markdown and TXT are viewed in the WebView (`Reader Side Rails` 11j–l).
>   - The views are read-only; Mark, Arrange and Protect are dimmed until the file is converted.
>   - The page counter becomes a section counter (Markdown/HTML) or a line counter (TXT).
>   - The Tools rail: Outline · Find · Copy all · Print · Source/rendered, with **Convert to PDF (via Layout)** as the primary action.
>   - The View rail: text size, reading width, wrap lines.
>   - HTML runs sandboxed: no scripts, no network, no forms, and remote images only on request, explained by a banner.
> - **Platforms:** see `Portal & Android.dc.html`.
>   - **Web portal:** Blazor Server. Sangam sign-in is required. The status bar says "processed on the server · encrypted at rest". An account menu replaces the window controls.
>   - **Android tablet:** the same rails at 72/72/60 px with 48 px targets.
>   - **Android phone:** the left bar becomes a bottom bar; the Tools rail becomes a tool strip (Mark/Protect only); the View rail becomes pinch gestures plus a View sheet.
>   - **Sangam gating:** free and local for reading, markup, arrange and protect. Identity is needed for certificate signing, sync, sharing and the portal.
> - **Out of scope here:** Anjal Mail embedding (it is designed in its own project).
> - **Still valid below:** themes and tokens, Home, Library, Settings, Arrange desk/shelf behaviour, dialogs, states, the feature audit and the bug list. Where the sections below say "mode bar", "leaf bar" or "margin", map them to the top bar, the View rail and the settings cards.

## About the design files
The `.dc.html` files in this bundle are **design references created in HTML**. They are prototypes showing the intended look and behaviour, not production code to copy. The task is to **recreate them in the existing Blazor codebase** (`src/ChuvadiReader.Ui`), using its patterns:
- Razor components
- global `app.css`
- `tokens.css` (`--chvd-*` variables)
- the `<Icon>` adapter over **LiPicons.Blazor**

Open any `.dc.html` file directly in a browser. It needs `support.js`, `lipicons.js` and `fonts/` next to it.

## Fidelity
**High-fidelity.** Colours, type, spacing, radii and copy are final unless a section says otherwise. Recreate them pixel-for-pixel using the tokens in `tokens.css`.

`lipicons.js` is a **subset copied verbatim from the LiPicons 1.1.0 manifest** (Regular variant, stroke 1.75). The names match the manifest, so use `<LipiIcon Name="…">` or the `Icon` adapter.

---

## Files (open in this order)
| File | What it shows |
|---|---|
| **`Reader Side Rails.dc.html`** | **D-10, the current chrome. Start here.** At the top is a **live, clickable prototype**: switch modes from the left bar; tools open their drawer or settings card (Pages & outline, Layers/Measure, Text, Find & redact, Shelf); rails expand on hover; hovering the page shows the strip under it. Below it are the static states: Read at rest, hover, drawer + View rail, Mark, Mark with an object selected, Arrange, Protect, Layers/Measure/signature check, narrow window, and the **D-09 viewers**: **Markdown** (11j), **plain text** (11k) and sandboxed **HTML** (11l). |
| **`Portal & Android.dc.html`** | Web portal: Sangam sign-in, Reader in the browser, Sangam gating. Android tablet: Read, Mark with a pen, Arrange in portrait. Android phone: Read, Mark, Arrange, Protect + View sheet. |
| `reference/*.md` | The user's roadmap (D-07 to D-10, phases, library status) and the feature-by-page list. **These are the source of truth.** |
| `Chuvadi App.dc.html` | **Pre-D-10 prototype; its layout is superseded by `Reader Side Rails`.** Still the reference for behaviour and screen content: **The interactive prototype**: the shell, all modes, Home, Library, Settings › Appearance, Word/Excel, the locked-workbook unlock, Ctrl K. Its props (theme, platform, startScreen, startMode, desksPerRow, signedIn, kioskClock, menus) appear in the Tweaks panel. |
| `Chuvadi Revamp.dc.html` | Round 1: review of today's Reader (numbered problems) and the first proposal. |
| `Chuvadi Revamp v2.dc.html` | Home before/after, Library, Arrange with all desks visible, password-protected Excel, Mac build, desk ⋯ menu, View menu + document menu. |
| `Chuvadi Revamp v3.dc.html` | Settings (Appearance), the 5 themes, and the Windows / macOS / Linux / web frames. |
| `Bench Dialogs.dc.html` | Watermark, Header & footer, Numbering, Impose, Crop, Margins, Export, Compare. |
| `Settings Sections.dc.html` | Reading, Library & folders, Account & roles, Kiosk, Language, Accessibility, Shortcuts, About. |
| `States & Feedback.dc.html` | First run, empty states, errors (damaged file, XFA), messages after an action, progress, page loading, unsaved-changes prompt, blocked actions. |
| `Missing Features.dc.html` | 15 new features: annotations & comments, forms, print, kiosk, users/roles/audit, OCR\*, password\*, certificate signatures\*, compare documents\*, links & attachments, Tamil UI, accessibility & UI scale, Library batch actions, autosave & recovery, edit properties. (\* already in development) |
| `Chuvadi Mobile.dc.html` | Phone (Home, Read, Mark, Arrange, Protect), iOS vs Android, tablet. |
| `Walkthrough.dc.html` | 6-step clickable flow: scan → OCR → redact → certificate sign → bind → print. |
| `Brand Assets.dc.html` | App icon (256 → 16 px, night and high-contrast versions), splash screens (light and dark), file-type icons. |
| `Feature Audit.dc.html` | **Checklist**: all 129 existing features, today's location and new home, plus bugs found in the current code. |
| `tokens.css` | Drop-in replacement for `wwwroot/css/tokens.css`: 5 themes + new tokens. |

---

## Shell layout (desktop, 1440 × 900 reference)
Top to bottom:

1. **Title bar**, 46 px, `surface-hi`, bottom border 0.5 px `border`. Contents, left to right (gap 12 px, padding 0 14 px):
   - [Mac only: traffic lights, 15 px dots, gap 9 px]
   - Wordmark button: "C**ச**huvadi". Instrument Serif italic 21 px, with ச in Tiro Tamil `binding`. Opens Home.
   - **Library** button: 30 px tall, radius 7, border 0.5 `border-soft`, LiPicon `library` 15, label 13 px.
   - 0.5 px divider.
   - **Tabs** (flex 1). Each tab: height 28, min-width 116, max-width 210, radius 8, padding 0 6 0 10, gap 7. Contents: format icon 13 px coloured by format (PDF `binding`, DOCX `#1f3b73`, XLSX `olive`), name in IBM Plex Mono 11.5, optional lock 11 px `warn`, close button 17 px at 55 % opacity.
     - Inactive: bg `surface-lo`, text `ink-muted`, border `border-soft`.
     - Active: bg `surface-hi`, text `ink`, border `binding`, `box-shadow: inset 0 -2px 0 binding`.
   - "+" button: 28 × 26.
   - **Command field**: 250 × 30, bg `surface-mid`, border `border`, radius 6. Search icon 14, text "Find or do anything…" 13 px `ink-muted`, kbd chip "Ctrl K" (⌘ K on Mac) in mono 10 px on `surface-lo`.
   - Settings (gear 17) and theme (moon 17) icon buttons, 30 × 30.
   - [Windows/Linux: traffic lights, 17 px, gap 10, order min (#febc2e), max (#28c840), close (#ff5f57), with close at the far right. Keep a 28 px hit area.]
   - [Web: no window controls; an "Install app" button instead.]
2. **Mode bar**, 52 px, `surface-mid`, bottom border `border-soft`. A 3-column grid:
   - Left: document title button (Instrument Serif 20 + chevron; opens the **Document menu**) and meta text (mono 11 `ink-muted`).
   - Centre: **mode switch**. Track `surface-lo`, radius 9, padding 3. Each segment is 36 px tall, padding 0 18, with the verb in Instrument Serif italic 21 and its shortcut key (1–4) in mono 9.5. Active segment: bg `surface-hi`, text `binding`, shadow `0 1px 3px rgba(26,19,16,.12)`. Word/Excel tabs: Mark, Arrange and Protect at 38 % opacity, with the tooltip "Convert to PDF first".
   - Right: the mode's main actions (see each mode below).
3. **Body**, flex row:
   - **Page ribbon** (left), 176 px (214 px in Arrange), `surface-mid`. Header 36 px with a mono uppercase label. In Read/Mark/Protect: thumbnails 84 × 118 (current page: outline 2 px `binding`, offset 3), with **bookmarks shown as italic serif chapter dividers between them** ("§ Summary"). Arrange: the **Shelf**. Word: headings. Excel: sheets.
   - **Canvas** (centre), bg `surface-lo`. The page is white, `shadow-page`, centred, 28 px from the top.
   - **Margin** (right), 296 px, `surface-hi`, border-left `border`. Shown only in Mark, Arrange and Protect; it holds the settings for whatever is selected.
4. **Status bar**, 30 px, `surface-hi`, mono 11. Contents:
   - Offline dot (8 px `olive`) + "offline · on this device" (web: "local · files stay in this browser").
   - User chip: 18 px avatar in `binding` + "Arun S. · Records" (or "Guest · kiosk").
   - Activity text, e.g. "Arrange · 3 desks · 20 pages · 2 selected".
   - Date.
   - **Live clock HH:MM:SS** at weight 500. Kiosk option: 15 px.

### Floating "leaf bar" (Read / Mark / Protect)
Centred, 16 px from the bottom of the canvas. bg `surface-hi`, border `border`, radius 12, `shadow-float`, padding 5 8.
- Contents: ‹ · page box (34 × 26, `surface-lo`) + "/ 12" · › | − · 100 % · + | Fit width · Fit page · Two-page | **View ▴**.
- The **View menu** (300 px popover) holds:
  - **Tools**: Hand H, Marquee Z, Loupe L (size / magnification), Snapshot S.
  - **Layout**: Continuous, Cover alone, Lock scrolling, Rotate view ▸ (page / onward / document).
  - **Look**: Page colour ▸, Actual size 0, Full screen F11.
- **These controls exist only here.** Remove the toolbar copies and the View-drawer copies.

### Document menu (title chevron)
Properties (Alt ↵) · Reduce file size (Press) · Export pages as images · Print (Ctrl P) | Flatten forms & marks · Password-protect a copy | Show in folder · Copy file path · Tags… | Close (Ctrl W). The Properties, Export and Press dialogs **already exist in Reader.razor but nothing opens them**; wire them here.

---

## Modes

### Read
- Right side of the mode bar: Find (Ctrl F), Print, Immersive.
- Hovering a page shows a "To desk" chip at the top right of the page (existing send-to-desk feature).

### Mark
- Right side of the mode bar: Undo · Redo · **Save** (overwrite, secondary button) · **Save as new PDF** (primary).
- **Tool column** floating at the left of the canvas (6 px padding, radius 12). Each tool is 52 × 50, icon 18 + label 10: Text, Image, Box, Ellipse, Line, Arrow, Draw, Sign. Active tool: bg `binding-soft`, colour `binding`.
- New tools (see `Missing Features`): Highlight, Note, Fill form.
- **Margin** shows the selection's settings:
  - Text: font segmented (Helvetica / Times / Courier), B, I, size (pt), rotation, colour swatches, "Place behind page text", and an "On this page" list.
  - Image and shape panels follow the same pattern.
- **Footer**: Duplicate, Delete.

### Arrange (was Bench)
- **Right side of the mode bar**: desks-per-row toggle (1 / 2 / 3, remembered), **Session ▾** (Save / Open / Reset bench), Undo, Redo, and the **Bind all** split button (▾ Combine into one).
- **All desks are visible at once** in a grid (`repeat(n, 1fr)`, gap 16), so pages can be dragged between them. A dashed "Drop here for a new desk" tile ends the grid.
- **Desk card**: bg `surface-hi`, 3 px top border in the desk colour; the focused desk also gets a 1 px `binding` ring.
  - Header 42 px: colour dot (14 px), name (Instrument Serif 18, rename inline), page count (mono), **chips** (mono 9.5, radius 4: WM, HF, №, BM, A4/LTR), Bind button, and the ⋯ menu.
  - **⋯ menu**: Select all (Ctrl A) · Invert · Move left / right · Duplicate · Colour… · Open in Read · Send to Protect · Save as template… · Apply template ▸ · Clear all pages · Delete desk.
- **Page cards**, 60 × 84:
  - Under each card: the **source page number** (provenance), plus "(blank)" or "(image)" where relevant.
  - Bottom-left: a 7 px dot in the source's colour.
  - Badges: 90°, crop, margin.
  - Selected: 2 px `binding` outline + ✓ badge.
  - Hover: a dark strip above the card — Peek · Duplicate · Rotate · Split desk here · Crop · Margins.
  - While dragging: 35 % opacity at the origin, a ghost card rotated −5°, a 3 px `binding` drop marker in the target desk, and a label like "p 3 → Desk 2".
- **Selection bar** (dark, bottom centre): "N selected · Desk 1" · Rotate · Duplicate · To new desk · Compare (exactly 2) · Lift · Remove.
- **Shelf** (ribbon), from the top:
  - Header icons: thumbnail size slider, Collapse all, Interleave A+B.
  - Per file: chevron, colour dot, name, "8/12 pp", ↑ · Properties · Press · ×. Closing is **refused while desks still use the file** (see `States 8j`).
  - "Search this file…" (results become page chips) and a page-range field ("1-5, 8-10", plus All).
  - 4-column thumbnails.
  - "Add files or images".
- **Margin** (focused desk):
  - **Pages**: Crop, Margins, Blank page, Add image, Reverse, Sort by source.
  - **Whole desk**: Number pages, Watermark, Header & footer, Size: A4.
  - **Print layout**: N-up, Booklet.
  - **Footer**: "Add bookmarks on bind" toggle; Scatter / Export / Lift N; **Bind this desk** (primary).
- **Dialogs**: see `Bench Dialogs.dc.html`. Every option from the current Bench modals is kept.

### Protect (was the Redact page)
- Margin tabs: **Redact · Stamp · Flatten · Password**. Signing with a certificate is planned for Protect too (5h).
- Redact tab:
  - Hint row "Drag on the page to draw a box".
  - Find field showing the "16 found" count, plus chips: Aa match case, whole word, all pages.
  - Match list: p#, snippet, and a ✓ / ✗ toggle (✓ = ink background).
  - "Remove as": Black box / Text only / Replace… (Replace stays locked until the library supports it).
  - Footer counts.
  - The regex presets stay **visible but locked**, with an explanation.
- Mode bar: "Always writes a new file" + **Save redacted copy** (ink-coloured primary button).
- On the page: matches are marked with `find-hit` (marigold outline); accepted redactions show as black boxes.

### Word / Excel tabs
- Read-only.
- Mode bar right: Print + **Convert to PDF** (primary).
- Excel: sheet tabs along the bottom, and a **Grid / As printed** toggle. As printed paginates the sheet exactly as the PDF will.
- Protected files show an unlock card: lock icon in a 44 px circle, a password field and an **Open** button. The password lives only in memory for that tab.
- Fidelity note: `DocxViewService` re-creates the document block by block. To get **"exactly as made"**, render Word and Excel through the same pipeline as Convert to PDF, then show that PDF.

---

## Home, Library, Settings
- **Home**: grid `1fr / 420px`.
  - Left: eyebrow (mono 11, with clock); greeting (Instrument Serif 54 + வணக்கம். in Tiro Tamil 42 `binding`); a Fraunces 15 blurb; a 180 px dashed drop zone; **resume cards** (3 across — file, where you stopped, and the mode as an italic serif word, e.g. "Marking"); Recent list (lock state shown in mono `warn`).
  - Right panel, `surface-lo`: Pinned, a **Library shelf preview**, Watched folder.
  - Remove the "tools" tile.
- **Library**: kept as it is today (search, category chips, Table / Shelf, wooden bookends, books sized by page count). Reach it from the title bar.
- **Settings**: a 244 px nav + content area. Sections: Appearance, Reading, Saving & files, Library & folders, Account & roles, Kiosk, Language, Accessibility, Shortcuts, About. Changes save as soon as they are made.
  - Appearance: 5 theme cards (live preview; clicking applies the theme).

## Themes
`data-theme` = `light` (Cream paper, default) · `dark` (Night paper) · `palm` (Palm leaf) · `indigo` (Indigo ink) · `contrast` (High contrast). The rendered page stays **white in every theme** (`--chvd-page`).

## Platforms
- **Windows**: the reference build.
- **macOS**: traffic lights on the left; the app menu moves to the system menu bar (File, Edit, View, Document, Window, Help); ⌘ shortcuts.
- **Linux**: same as Windows, with 12 px window corners, honouring GNOME/KDE button layout.
- **Web**: no window controls, an "Install app" (PWA) button, square corners, browser print. Files are processed in the browser.

## Mobile
- **Phone**: the mode bar moves to the bottom (icon + italic verb); the margin becomes a bottom sheet; the ribbon becomes a swipe-up strip; desks stack and scroll sideways (long-press to drag). Touch targets are at least 44 px.
- iOS/Android differences are listed in `Chuvadi Mobile.dc.html` (4h/4i).

## Interactions & behaviour
- **Mode keys**: 1–4 switch modes. Ctrl K command bar; Ctrl F find; Ctrl W close tab; Ctrl P print. Arrange keeps Ctrl Z/Y/A, Del, R/Shift R, Ctrl D. The full list is in `Settings Sections` › Shortcuts.
- **Menus and popovers**: radius 10, `shadow-popover`, items 30–32 px tall, hover bg `surface-mid`. Close on Esc or outside click.
- **Dialogs**: an 800 px card on `--chvd-scrim`. Header 58 px (icon + serif 25 + mono context); body is settings on the left and a **live page preview** (300 px, `surface-lo`) on the right; footer 58 px with secondary + primary buttons.
- **Messages after an action**: bottom-right, above the status bar. Success messages fade after 6 s; errors stay until closed. Each has an action (Open, Show in folder, Find file…).
- **Long jobs**: a status-bar progress bar, plus a progress card that can be hidden, with Stop.
- **Motion**: `--chvd-quick` 120 ms / `--chvd-base` 180 ms, both on `--chvd-ease`. No motion when "Reduce motion" is on.

## State (new or changed)
- `activeTab`, `mode` per tab (`read | mark | arrange | protect`), `deskPerRow` (persisted), `focusedDeskId`.
- `viewMenuOpen`, `docMenuOpen`, `deskMenuFor`, `commandOpen`.
- `theme` (persisted, or follows the OS), `uiScale` (100 / 125 / 150), `platform` (from the host).
- `user` + `role` (from the future sign-in), `kiosk` config, `autosave` drafts.
- Existing services (BenchService, TabsService, RedactService, StampService…) keep their logic. The Bench and Redact *pages* become mode views inside the Reader tab, and `OpenInBench` / `SendToRedact` become plain mode switches on the same tab.

## Bugs found in the current code (fix with the rebuild)
1. `/merge` and `/split` cannot be reached from anywhere. Fold them into Arrange (drop files → Bind; Scatter / Lift by range) and Ctrl K.
2. Reader `ShowProperties` / `ShowExport` / `ShowPress` are never called. Wire them into the Document menu.
3. The `image` icon name is not in LiPicons, so it renders blank. Use `file-image`.
4. The Dashboard shows Redact and Sign as disabled "v2", but both have shipped.
5. Icons are reused for different things (Reader = Docs, Bench = Sheets, Flatten = Compress), and Settings' theme labels ("Mono lobby / Grid lobby") don't match the code.

## Assets
- Fonts are in `fonts/`, copied from `wwwroot/lipi-sans` and `wwwroot/fonts/google`: LiPi Sans (Latin + Tamil), Instrument Serif (regular + italic), IBM Plex Mono 400/500, Tiro Tamil, Fraunces.
- Icons: `lipicons.js` holds the LiPicons 1.1.0 paths for every icon used. In the app, use `LiPicons.Blazor`.
- App icon / splash: see `Brand Assets.dc.html`. They are built from type and CSS; produce the final SVG/ICO in a vector tool.
- No raster images are used. PDF pages in the mockups are HTML stand-ins.
