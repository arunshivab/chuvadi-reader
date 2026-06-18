# Chuvadi Reader — Developer Notes

Engineering reference for the Chuvadi Reader codebase: what it is, how it's put
together, the rules it lives by, and a running history of every delta. This is the
in-repo source of truth — keep it current as the app changes.

---

## 1. What it is

Chuvadi Reader is a Windows desktop PDF **reader** and page **workbench**, built
entirely on first-party, zero-dependency .NET libraries (chuvadi-pdf for PDF work,
LiPicons for icons). It is a pure-.NET application: a WPF window hosts a
**BlazorWebView** (Blazor running inside WebView2), so the UI is Razor/HTML/CSS while
the shell, windowing, and OS integration are WPF. Target framework: **.NET 10**.

There are no third-party UI frameworks and no third-party NuGet packages beyond
Microsoft's Blazor / BlazorWebView and the Chuvadi libraries.

---

## 2. Solution layout

`ChuvadiReader.slnx` at the repo root. Projects under `src/`:

- **ChuvadiReader.Core** — services, models, and interfaces. No UI. Targets `net10.0`.
  Owns the reader/bench domain logic (`TabsService`, `BenchService`, `BenchComposer`,
  `ExportService`, `PressService`, `DocumentPropertiesService`, `OpenDocumentService`,
  `SaveFolderService`, storage and picker interfaces, the PDF reader abstraction).
- **ChuvadiReader.Ui** — a Razor Class Library: pages, components, scoped CSS, and the
  JS interop modules. Targets `net10.0`. This is where `Reader.razor`, `Bench.razor`,
  `MainLayout.razor`, `PageSvg.razor`, `wwwroot/js/*.js`, and `wwwroot/css/*.css` live.
- **ChuvadiReader.Windows** — the WPF host: `App.xaml.cs` (DI registration and
  composition root), `MainWindow`, window controls, and the platform implementations
  of Core interfaces (e.g. `WpfFilePicker`, app storage).

Repo-root files: `Directory.Build.props`, `Directory.Build.targets`, `nuget.config`,
`localpackages/` (a local NuGet feed for the Chuvadi distribution kits), and this
`docs/` folder.

---

## 3. Hard architectural rules

These are non-negotiable and predate this document.

1. **Discuss before changing code.** No code change — addition, modification, or
   removal — happens without explicit agreement after discussion. Reported bugs and
   explicit "build it / go" instructions count as agreement for the thing discussed.
2. **Strict project independence.** Every Chuvadi project is independent. No
   cross-project source references, no `ProjectReference` into another Chuvadi product,
   no copying or vendoring source between projects.
3. **Shared libraries only via packaged kits.** chuvadi-pdf and LiPicons are consumed
   only as packaged distribution kits (NuGet `.nupkg` / DLL bundles) that Arun
   provides, resolved through the local feed `./localpackages` and pinned in
   `ChuvadiReader.Core.csproj` (the `Chuvadi.Pdf` meta-package). Never pull shared code
   any other way.

---

## 4. Build, run, and delta workflow

- **Build (CI-style check):** Core and the Ui RCL build on any platform
  (`dotnet build src/ChuvadiReader.Core/...` and `.../ChuvadiReader.Ui/...`). The WPF
  host (`ChuvadiReader.Windows`) builds and runs on Windows only and is tested by Arun.
- **Run:** `dotnet run --project src/ChuvadiReader.Windows`.
- **Delta application:** changes are shipped as a zip mirroring the project structure.
  Apply with `Expand-Archive <zip> -DestinationPath <repo root> -Force`, then run.
- **Force-touch (no clean needed):** `Directory.Build.targets` defines a
  `ChvdForceTouchAssets` target that touches every `.cs/.razor/.razor.css/.css/.js/.xaml`
  before build, so the incremental compiler always recompiles extracted files even
  though `Expand-Archive` stamps them with older timestamps. This is why a clean is no
  longer required after applying a delta. A full clean is only needed in rare cases:
  `dotnet clean; Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force`.
- **Startup errors** are logged to `%USERPROFILE%\AppData\Roaming\Chuvadi\startup-error.log`.

---

## 5. Design system

All colour, type, and geometry come from `src/ChuvadiReader.Ui/wwwroot/css/tokens.css`
as `--chvd-*` variables; hardcoded colours/fonts/px are forbidden outside that file
(enforced by a token-audit script). There are light and dark themes via
`[data-theme="…"]`. Key tokens: binding red `--chvd-binding`, surfaces
`--chvd-surface-hi/mid/lo`, ink `--chvd-ink/-soft/-muted`, borders, radii
(`--chvd-radius`, `--chvd-radius-lg`), `--chvd-stroke` (0.5px hairline), and the type
stacks (`--chvd-serif-display`, `--chvd-sans-display`, `--chvd-mono`). Component styles
are Blazor scoped CSS (`*.razor.css`); cross-cutting styles live in `app.css`.

---

## 6. Reader architecture

Files: `Pages/Reader.razor` (+ `.razor.css`), `wwwroot/js/reader.js`,
`Components/PageSvg.razor`, `Core/Reader/TabsService.cs`, `ReaderViewState.cs`,
`OpenDocumentService.cs`, plus the shared `Layout/MainLayout.razor` (the tab strip and
title bar) and global `app.css`.

- Pages render as inline SVG (`<PageSvg>`) into `.rpage { zoom: var(--rpage-zoom,1) }`
  inside `.rpage-wrap[data-page=n]` inside `.rdesk` (the scroll container). The
  `.zoomed` class disables paged snapping for free scrolling above 1×.
- `reader.js` sets up Ctrl+wheel anchored zoom, the hand/marquee/loupe pointer tools,
  touch pinch / double-tap, a chrome click handler (blurs buttons only, so the
  page-number input keeps focus), and the keyboard handler.
- Tabs: `MainLayout` renders the title-bar tab strip and the "⋯" actions menu;
  `TabsService` is a singleton tracking open tabs, the active tab, view state, reorder,
  and restore.

---

## 7. Bench architecture

The Bench is a **shelf + desks** workspace. Files:
`Pages/Bench.razor` (+ `.razor.css`), `wwwroot/js/bench.js`,
`Core/Reader/BenchModels.cs`, `BenchService.cs`, `BenchComposer.cs`.

### Models (`BenchModels.cs`)
- `BenchSource` — a loaded PDF on the shelf: `Index` (drives the colour stripe), `Path`,
  `FileName`, an open `Session` for thumbnails, `PageCount`, and a mutable `Collapsed`
  flag for shelf expand/collapse.
- `BenchPage` — one page in a desk. Usually points back at a source page
  (`SourceIndex`, `OriginalIndex`) and carries a `Rotation`. Two non-source kinds:
  `IsBlank` (+ optional `BackgroundHex`) and `IsImage` (+ `ImagePath`, the raw image on
  disk). Stable `Id` (Guid) for selection and drag.
- `DeskWatermark` — `{ Text, Opacity (0..1), AllPages }`.
- `Desk` — `{ Id, Name, NameLocked, List<BenchPage> Pages, Watermark?, IsEmpty }`.

### Service (`BenchService.cs`) — a singleton, shared so the workspace survives navigation
Shelf: `AddSourceAsync` (PDF only), `ToggleSourceCollapsed`, source-thumbnail helpers.
Desks: `EnsureDesk`, `AddDesk` (capped at `MaxDesks = 30`, `CanAddDesk`), `RemoveDesk`,
`RenameDesk`, `AutoNameDesk` (names a desk after its single PDF source; ignores blanks
and images). Populate: `AddPageToDesk`, `AddSourceAllToDesk` (whole-file drop),
`AddImageToDesk` (raw image page), `AddBlankToDesk`, `MovePage`, `RemovePage`,
`TurnPage`. Selection (global across desks, range within a desk): `ToggleSelect`,
`SelectRangeTo`, `TrimSelected`, `TurnSelected`. Output: `BindDeskAsync` (all pages),
`LiftDeskAsync` (selected), `BindAllAsync` (one PDF per desk → folder),
`ScatterDeskAsync`, `ExportDeskAsync`, `SetDeskWatermark`. Image preview:
`ImageDataUri` (base64 data URI so an image page renders as `<img>` without conversion).
`Reset` disposes sessions and clears everything.

### Composer (`BenchComposer.cs`)
Turns an arrangement into one PDF, building only on the verified `PageOperations`
pipeline plus `PdfDocumentBuilder` and `ImagePdfConverter`. A straight full-file bind of
a single source is a file copy (the fast identity path). Any other arrangement — subset,
reorder, or **duplicate** (the same source page placed more than once) — is built by
extracting each wanted page individually (`ExtractPages`, cached per index) and merging
the singles in order (`Merge`); this is the only approach that is correct when a page
repeats, which a single `ReorderPages` permutation cannot express. Blank pages are
materialised into tiny temp PDFs (reused per background colour); image pages are
converted to temp one-page PDFs via `ImagePdfConverter` **only at compose time**
(Bind/Lift/Scatter/Export). All temp files are deleted in a `finally`. The conversion of
an image therefore happens at the moment of binding and never leaves a converted file on
disk. Rotations are applied per distinct angle on the final page positions.

### JS interop (`bench.js`)
`init(grid, dotnetRef, lazyThumbs)` wires an IntersectionObserver over `[data-bench-id]`
and `[data-shelf]` for lazy thumbnails, and a pointer-based drag system. Drag sources:
a shelf page (`[data-shelf]`), a whole file (`[data-shelf-all]` on the source header),
or a desk page (`[data-bench-id]`). Drop targets are `[data-desk]` regions; the insert
index is the nearest page by centre distance. A small ghost (~25% of the thumbnail,
`GHOST_SCALE`) follows the cursor. On drop it invokes the matching .NET method
(`DropShelfPage`, `DropSourceAll`, `DropDeskPage`); a press without movement on a desk
page becomes `SelectBenchPage`. Buttons and the desk-name input are ignored by the drag
handler so they keep native behaviour.

The desk "⋯" menu is closed by a full-screen `.menu-overlay` click-catcher rendered
while a menu is open.

---

## 8. The page-number formula (do not change without discussion)

Each page chip in a desk shows a label, computed by `PageLabel(BenchPage)` in
`Bench.razor`:

- A **normal source page** shows its **original page number within its own source
  document**: `OriginalIndex + 1` (`OriginalIndex` is 0-based).
- A **blank page** shows `(blank)`.
- An **image page** shows `(image)`.

Rules and rationale:

- The number is a property of the **page**, not of its **slot**. Reordering a page,
  moving it to another desk, or rotating it never changes the label. If page 5 of a
  book is dragged in, it reads "5" wherever it lands — even as the first page of a desk.
- The label communicates **provenance** (which page of which source), not sequence. Two
  pages from different sources may legitimately both read "5". The desk's left-to-right
  order is what communicates the **bind sequence**.
- Blanks and images have no source page, so they are labelled in words rather than
  numbered. The image case is a **deliberate exception** to the strict formula: a
  converted image would technically be "page 1" of its one-page conversion, but a lone
  "1" under a scan is meaningless, so `(image)` is shown instead.

This formula is the one that shipped in the original bench list view (`OriginalIndex + 1`)
and is considered finalised. The `(blank)`/`(image)` labels are the only additions, made
because those page kinds did not exist when the formula was first set.

---

## 9. chuvadi-pdf — what's available vs. what's requested

Confirmed available in **chuvadi-pdf 3.6.0** and used by the Bench:
`PageOperations` (Merge/ReorderPages/ExtractPages/DeletePages/RotatePages/SplitPages);
`PdfDocumentBuilder` + `PageBuilder` (blank and coloured-blank pages via `AddPage` +
`DrawRectangle`); `ImagePdfConverter` (image → PDF); `WatermarkStamper.ApplyText` with
`TextWatermarkOptions` (centre-only, `PageIndices`, `Opacity`); `PdfDocument`
(read-only metadata). Note: `Chuvadi.Pdf.Graphics` contains a `Path` type — fully
qualify `ColorF` (do not `using` that namespace where `System.IO.Path` is in scope).

Not yet available — tracked in `chuvadi-pdf-bench-requests.md` for the library chat:
(1) composite an existing page onto a new page with background fill + content opacity
(delivers both per-page transparency and page background colour); (2) document outline /
bookmarks; (3) a metadata setter on an existing/merged document; (4) a positioned
(footer/corner) text stamp for true page numbering. See `docs/ROADMAP.md`.

---

## 10. Version history (delta log)

Newest first. Each delta from here on gets an entry. (Entries before this log was
started are reconstructed from the development history and may be summarised.)

- **v39 — Save in place for overlay-only edits; redaction still forces a new file.** The markup
  rail gains a **Save** button (overwrite the open file) that appears **only when the edit is
  overlay-only** (text present, no redactions). It writes the flattened result to a temp file
  beside the original, then — via new `TabsService.ReloadActiveAsync` — disposes the open document
  (releasing any OS file lock), swaps the file in (`File.Replace`, falling back to copy), and
  reopens the same tab on the same page; the reopen runs in a `finally` so a failed swap never
  leaves a blank tab. **Save as new PDF** remains always available and is the **only** option when
  any redaction is present (preserving the "redaction never overwrites" rule, and the tagged-PDF
  block). Files: `TabsService.cs`, `Reader.razor`, docs.

 Text boxes stored their values
  in public fields; Blazor's two-way `@bind` on the `<textarea>` only writes back to **properties**,
  so typed text displayed on screen but never reached the model — at save time every box looked
  empty and was skipped, producing a file with no text. Converted `TextBox` to properties; text is
  now captured on input and stamped correctly. Files: `Reader.razor`, docs.

 The edit canvas gains a
  **Text** tool alongside Redact. Place a text box by **click** (default-sized) or **drag**
  (sized), type into it inline, and style it from the rail when selected: **font family**
  (Helvetica / Times / Courier), **bold**, **italic**, **size** (pt), **colour**, **alignment**
  (left / center / right / justify), **rotation** (degrees), and line height. Boxes are
  selectable, movable (move handle), resizable (corner handle), and deletable, stored per page as
  0–1 fractions like redactions. On **Save As**, redactions are flattened first (via `Redactor`,
  with the verify-after-save guard) and text is then **authored and stamped** on top
  (`PdfDocumentBuilder` → `PageStamper.Place`), into one new file (`-edited.pdf`, or `-redacted.pdf`
  when only redactions). Text overlays add content rather than remove it, so they work on **tagged
  PDFs** too (no block). Core: `RedactService` gained `TextAnno`/`PageText` and a combined
  `FlattenToFileAsync(redactions, overlayHex, texts)`; `RedactToFileAsync` now delegates to it.
  Standard-14 fonts only → **non-Latin (Tamil/Indic) text won't render yet** (TrueType embedding
  is a later pass), and authored text has no opacity (library colour is RGB-only). Rotation maps
  screen-CW to the PDF's CCW/​y-up space about the box centre (folding in page view rotation) —
  math-grounded, visual sign to be confirmed. Files: `RedactService.cs`, `Reader.razor`,
  `Reader.razor.css`, docs.


  Discovered that the library's `Redactor` does **not** achieve byte-level removal on **tagged
  PDFs**: text inside marked-content survives and extracts straight back out (a full-page
  redaction of the tagged CV left ~1155 of 1632 chars recoverable, incl. name/phone/email),
  while untagged PDFs redact correctly (chars → 0). So v34/v35 could produce an insecure
  "redacted" file on tagged input. Two-part response: (1) `RedactService` now **verifies after
  save** — it re-opens the output, extracts text fragments, and if any non-blank fragment still
  lands inside a redaction rectangle it **deletes the output and throws**
  `RedactionNotRemovedException` (so an insecure file is never left on disk); the reader surfaces
  the failure in the rail and offers no "Open it". This guard is version-proof — when the library
  is fixed, tagged saves will verify clean and succeed with no app change. (2) `RedactService.IsTagged`
  detects a `/StructTreeRoot`, and the reader shows a **warning chip** in the markup rail for
  tagged docs ("text can't be fully removed yet; saving will be blocked"). The library-side fix
  (remove show-text inside BDC/EMC on tagged pages) is logged in ROADMAP §2 with a paste-ready
  repro for the library chat. Files: `RedactService.cs`, `Reader.razor`, `Reader.razor.css`, docs.

 The Redact rail gains a **colour
  control**: presets **black** (default) and **white**, plus a custom picker — one colour for the
  whole document (passed as `overlayHex` to `RedactService.RedactToFileAsync`, parsed to the
  library's `OverlayColor`). White-out redaction is the main use (removed area reads as blank
  paper instead of a black bar); content is still byte-deleted regardless of cover colour.
  On-screen boxes preview in the chosen colour with a grey visibility outline so white boxes stay
  selectable. Also: the toolbar pen button's tooltip changed from "Markup (redact)" to **"Edit"**,
  since the markup mode hosts more than redaction (Text/Image to come). Files: `RedactService.cs`,
  `Reader.razor`, `Reader.razor.css`, docs.


  This supersedes the earlier "edit canvas lives in the Bench" decision. The split is now:
  the **Bench** handles papers as units (fetch, reorder, blank, scatter, press, bind, plus
  desk-level watermark/header-footer), while the **Reader** handles a page's content up close
  (redact now; text, image, signature, snapshot later). A **Markup** button (edit icon) in the
  reader toolbar enters a markup mode with a tool rail (Redact active; Text and Image shown
  disabled/"soon" — the shared canvas skeleton). In markup mode each page gets an overlay
  (`.mk-layer`) where the user **click-drags black redaction boxes**; boxes are stored as
  normalised 0–1 fractions per page (zoom-proof), with a ✕ to remove and a Clear all. **Save as
  redacted** writes a brand-new PDF via the library's `Redactor` (true byte-level removal) and
  never touches the open file; the default name is `<original>-redacted.pdf`, and after saving
  the rail offers to open the result in a new tab. Boxes are per-document and in-session only
  (cleared when the active document changes); markup is **strictly view-only until Save As**.
  Coordinate mapping fraction→PDF points handles the page's own `/Rotate` plus the reader's
  view rotation (verified on the harness for all four rotations, and end-to-end for the common
  case). New Core `RedactService` (`Documents/RedactService.cs`, with `NormBox`/`PageRedaction`),
  registered in DI; `ChuvadiPdfSession`/`IPdfReader.OpenAsync` already render the reader with a
  transparent page background from v33, so boxes sit cleanly over content. Also: the Library
  shelf **bookend category labels** changed from the muted marigold to a high-contrast warm
  ivory (`#F4ECD8`). Files: `RedactService.cs`, `App.xaml.cs`, `Reader.razor`,
  `Reader.razor.css`, `wwwroot/js/reader.js`, `Library.razor.css`, docs.

- **v33 — Reader page-colour tint (view-only, per document).** A "Page colour" control in the
  reader toolbar (contrast icon) opens a popover with preset reading shades (White, Sepia, Soft
  gray, Mint, Pale blue), a custom colour picker, and Reset-to-white. The chosen tint is
  remembered **per document** (keyed by file path in `IAppStorage`, `readerPageBg:{path}`) and
  reloaded when that file is reopened; other documents default to white. Implemented as
  **Option 2 (CSS-owned)** so colour changes are instant with no page re-render: `.rpage` now
  uses `background: var(--reader-page-bg, #fff)`, set from `_pageBg` on the `.reader` root. For
  the tint to show, the reader's render path now passes `Background = null` to the SVG renderer
  (the library otherwise stamps an opaque-white full-page rect by default in 3.11.1). This is
  isolated to the reader: `IPdfReader.OpenAsync` / `ChuvadiPdfSession` gained an optional
  `transparentBackground` flag, set true only by `TabsService` (reader tabs); `BenchService` and
  `ExportService` keep the white default, so bench previews and saved/exported files are
  unaffected. **Strictly view-only — nothing is written to any PDF.** Pages that paint their own
  white background still render white (that white is page content; the tint sits behind it).
  Also corrected the stale ROADMAP note that said `SvgExportOptions.Background` had no effect — it
  works and defaults to white in 3.11.1. Files: `ChuvadiPdfReader.cs`, `IPdfReader.cs`,
  `TabsService.cs`, `Reader.razor`, `Reader.razor.css`, docs.

- **v30–v32 — Footer editor polish.** v30: the Quick/Custom selector became a small sliding
  **toggle switch** (click switch or either label). v31: the **Page/Date/Time format choosers
  moved onto one line with the switch** (formats left, switch right) to reclaim vertical space;
  the row beneath is now a **fixed-height band** (Quick = a one-line hint, Custom = the field
  chips) so the popup no longer changes height when you flip modes; the trailing "tokens
  resolve…" note was removed. v32: the desk **"Footer" button is renamed "Stamp"** (one word,
  matches its icon-button siblings; tooltip stays "Header / footer"); **`{filename}` now
  resolves to the original source file's name** the pages came from (single real source → that
  name; mixed desks or only blanks/images → desk name + ".pdf") instead of always the desk name;
  and the **`{filepath}` / "Full path" option was removed** (a desk can mix sources, so a single
  original path is ambiguous — Arun asked for filename only). The two header/footer **"Shrink"
  fits are deliberately kept** though they currently blank the page — that's a chuvadi-pdf
  reserve/scale bug now logged in §2 for the library chat, and the options will work unchanged
  once it's fixed. Files: `BenchService.cs`, `Bench.razor`, `Bench.razor.css`.

- **v29 — Footer editor v2: Quick/Custom dual mode (Batch B, delta 2 refinement).**
  The header/footer editor gained a **mode toggle** at the top: **Quick** (default) gives
  each of the six slots (header & footer · Left/Centre/Right) a **single dropdown** —
  None, Page #, Page X of Y, Total, Filename, File path, Date, Time, Date+Time, or
  Custom text — with **mutual exclusion**: a real field chosen in one slot disappears from
  the other slots of the same band (None and Custom text always remain), so a field can't
  be duplicated by accident. **Custom** mode is the previous free-text boxes + insert-chips,
  for users who want the same field in several places or text mixed between tokens. A shared
  **format bar** (page 1/i/I/a/A · date · time) sits above both modes; in Quick mode changing
  a format live-rebuilds the affected slots, in Custom mode it only affects new chip inserts
  (boxes stay user-owned). New: **Page fit** dropdown (Overlay · Shrink page to make room ·
  Shrink only if overlapping) mapped to `PageContentFit`; **band background** is now tied to a
  Shrink fit (disabled with a hint under Overlay, where the library can't paint it); **separate
  Remove header / Remove footer** buttons (each clears its band and re-persists, keeping the
  modal open); a desk **HF badge** beside the WM badge; a full-screen **binding/lifting overlay**
  with spinner + label; and the **Fetch-file spinner** now paints reliably (`await Task.Yield()`
  after showing it). Fixed the Quick-grid **textbox overflow** (`minmax(0,1fr)` + `box-sizing`).
  Model: `DeskHeaderFooter` gained `QuickMode`, `PageFmt`, `DateFmt`, `TimeFmt`, `Fit`. Service:
  `ApplyHeaderFooter` maps `Fit`. Files: `BenchModels.cs`, `BenchService.cs`, `Bench.razor`,
  `Bench.razor.css`. Mode names chosen as **Quick / Custom** (friendlier than Basic/Advanced).
  Deferred: Custom mode stays free-text + chips rather than inline "pill" chips (functional,
  visual polish later); band background still needs a Shrink fit to appear (library limit §2).

- **v19 — Bench peek, footer actions, responsive.** Drag ghost resized to a legible
  ~60% (was too small at 25%). Hover-**peek**: a loupe button on each desk page; hovering
  it shows an enlarged preview via a single shared `.bench-peek` popover cloned in JS (no
  binoculars icon exists in LiPicons, so `loupe` is used). Desk **footer** now carries the
  same six actions as the "⋯" menu (Fetch image / Add blank / Watermark / Scatter / Export
  / Remove) as icon-buttons beside Lift/Bind, with the "⋯" menu kept. **Responsive**: at 3
  desks per row the desk gets a `compact` class that drops the Lift/Bind text to icons +
  tooltips; 1–2 per row keep the text. Files: `Bench.razor`, `Bench.razor.css`, `bench.js`.
  Still pending: send an individual page from the Reader to a desk (reader-side, next).
- **v27 — Header/footer editor (Batch B, delta 2).** Each desk gained a Word-style header
  and footer, applied to the bound output on Bind/Lift. The editor (a new "Footer" action on
  the desk) offers separate **Left / Centre / Right** boxes for both bands, **insert-field
  chips** that drop tokens into the focused box (Page #, Total, Page X of Y, Filename, Full
  path, Date, Time, Date+Time), font size, colour, an optional **background fill**, and a
  **page range** (all, or 1-based From–To). A live preview substitutes sample values. Model:
  new `DeskHeaderFooter` + `Desk.HeaderFooter`; service: `SetDeskHeaderFooter` and
  `ApplyHeaderFooter` (builds `HeaderFooterOptions` with a 3-section `BandText`, wired into
  bind + lift after the watermark; footer failure can never block a bind). Known v1 limits
  (by design): `{filename}`/`{filepath}` resolve to the desk name (per-source filenames are a
  later, per-page job); custom date formats and roman/letter page numbers are deferred.
  Files: `BenchModels.cs`, `BenchService.cs`, `Bench.razor`, `Bench.razor.css`. (Note: the
  format specifiers below were wrongly believed unavailable at v27; they ship in the UI at
  v28.)
- **v28 — Footer formats + watermark refinement (Batch B polish).** Two corrections/additions
  after testing. (1) **Footer formats** — the stamp tokens *do* support `{token:format}` in
  3.11.1 (verified): page numbers as `{page:roman}`/`{page:ROMAN}`/`{page:alpha}`/
  `{page:ALPHA}` (bijective) and dates/times with any .NET format string
  (`{date:dd MMM yyyy}`, `{time:hh:mm tt}`, `{datetime:…}`). The footer editor now has a
  page-style dropdown and date/time format dropdowns whose chips insert the right token, and
  the live preview resolves them (with local roman/alpha/date formatters). (2) **Watermark
  refinement** — font is now a Word-style **dropdown** (the three real families; non-standard
  names silently fall back to Helvetica, so only these are offered until the library exposes
  font embedding — see §1), **size** is a preset dropdown plus a custom number, and **colour**
  is a swatch palette (Grey/Red/Blue/Green/Orange/Yellow/Black/White) with the picker kept as
  Custom. Files: `Bench.razor`, `Bench.razor.css`.
- **v26 — Watermark expansion (Batch B, delta 1).** The per-desk watermark gained the full
  styling set requested: font face (Sans/Serif/Mono → Helvetica/Times/Courier), **bold** and
  **italic** (mapped to the standard PDF font variant via `DeskWatermark.ResolveFontName`),
  font size, colour (hex → `ColorF.FromRgb8`), opacity, and orientation — Horizontal (0°),
  Vertical (90°), Diagonal ↗ (45°), Diagonal ↘ (315°), or a custom angle. The modal now
  carries these controls plus a live CSS preview; `DeskWatermark` and `SetDeskWatermark`
  were widened accordingly and `ApplyWatermark` now feeds them into `TextWatermarkOptions`
  (all variants + rotations verified runnable against 3.11.1). Files: `BenchModels.cs`,
  `BenchService.cs`, `Bench.razor`, `Bench.razor.css`.
- **v25 — Library bump to chuvadi-pdf 3.11.1.** Pulled in the new library build (from
  3.6.0). The **tagged-PDF failure is fixed** — reorder / extract / merge / watermark now
  all succeed on tagged PDFs, verified directly on the MRDDFF CV that previously threw
  "Page tree /Kids[0] is not a dictionary". Core and Ui compile clean against 3.11.1 with
  **no app code changes**; the Windows host touches the library only through Core, so it
  should build unchanged (rebuild and report). New capabilities now available to wire up:
  `HeaderFooter`/`HeaderFooterOptions` and `TextStamper` + `StampTokens` (footer/header
  page numbers and positioned text with `{page}`/`{total}` tokens); `PageStamper` and
  `WatermarkStamper.ApplyImage` (page/image overlays); `Redactor`/`RedactionOptions`
  (true redaction by rectangle or regex). Files: `ChuvadiReader.Core.csproj` (pin → 3.11.1),
  `localpackages/` (28 × 3.11.1 nupkgs).
- **v24 — Non-selectable chrome.** Bench UI text (desk titles, page counts, labels) was
  drag-selectable like a web page because the app is HTML in WebView2. Added
  `user-select: none` on the `.bench` root for a native-app feel, with `user-select: text`
  kept on the `.desk-name` rename field so it stays editable. (App-wide chrome —
  toolbar/tabs/reader — can get the same treatment once we confirm the Reader doesn't rely
  on text selection.) File: `Bench.razor.css`.
- **v23 — Desk footer regrouped by intent.** Delete-desk moved out of the footer to a red
  trash button in the desk header (away from Bind, so it can't be fat-fingered), and the
  redundant header "⋯" menu was removed entirely now that every action is visible. The
  footer is now two intent groups: **Add** (Image, Blank) on the left and **Output**
  (Watermark, Scatter, Export, Lift, Bind) on the right, split by the flexible spacer — so
  Lift/Bind sit with the other output actions instead of as a separate island. Compact
  (3-per-row) still strips all labels to icons. Files: `Bench.razor`, `Bench.razor.css`.
- **v22 — Consistent desk footer.** Resolved the footer mismatch: all eight footer
  controls (the six desk actions plus Lift and Bind) now share one `.bt sm` button style.
  At one or two desks per row they are full **labelled buttons** (Image, Blank, Watermark,
  Scatter, Export, Remove, Lift, Bind); at three per row the `compact` class strips every
  label, leaving uniform square **icon** buttons with tooltips. Previously the six actions
  were icon-only even at one/two columns while Lift/Bind kept text. Files: `Bench.razor`,
  `Bench.razor.css`.
- **v21 — Readable drag preview.** The drag ghost is now a fixed **~180 × 254 px** (about
  22% of a 96-DPI A4 page) instead of a scaled-down thumbnail, so the page content is
  legible enough to confirm you're dragging the right page. It floats beside the cursor
  (flipping left near the right edge, vertically clamped) so it never covers the drop gap
  under the pointer. The size is one constant (`GHOST_W` in `bench.js`). File: `bench.js`.
- **v20 — Bench peek/footer fixes.** Fixed the **hover-peek** (it showed nothing and
  added a stray scrollbar): the popover is appended to `<body>`, so Blazor's *scoped*
  `.bench-peek` rule never matched it — it is now fully inline-styled in `bench.js` like
  the drag ghost, so `position:fixed` and the rest actually apply. **Fetch now shows a
  loading state**: the shelf displays an "Opening file…" spinner while a fetched PDF is
  being opened, and the Fetch buttons disable meanwhile (`_fetching`). **Footer controls
  unified**: the six desk action icons now use the same bordered button frame and height
  as Lift/Bind, so the whole footer reads as one button family. Files: `bench.js`,
  `Bench.razor`, `Bench.razor.css`, docs.
- **v19 — Bench desk peek + footer actions.** Drag ghost resized to a legible ~60%
  (`GHOST_SCALE`, 46px floor) after 25% proved too small. **Hover-peek**: each desk page
  has a small peek button (loupe icon — LiPicons has no binoculars); hovering it shows an
  enlarged preview of that page via a shared `.bench-peek` popover built in `bench.js`
  (clone-on-hover, one popover, removed on leave). **Footer actions** (#7): the six desk
  actions (Fetch image, Add blank, Watermark, Scatter, Export, Remove desk) are now also
  icon-buttons in the desk footer beside Lift/Bind; the header "⋯" menu is kept too.
  **Responsive footer** (#8): at three desks per row the desk gets a `compact` class that
  hides the Lift/Bind text labels (`.btxt`), leaving icons with tooltips; one and two per
  row keep the text. Files: `bench.js`, `Bench.razor`, `Bench.razor.css`, docs.
  Still open (agreed, next): send an individual page from the Reader to a desk.
- **v18 — Bench bug fixes.** Composer rewritten to be **duplicate-safe**: a desk page
  that repeats (same source page placed twice) used to throw "newOrder has N entries but
  document has M pages"; binding now extracts each wanted page and merges in order. Desk
  pages no longer overflow the desk card (the page grid sizes the desk instead of being
  clipped at min-height). The watermark menu item got a real icon (`watermark`; `droplet`
  was not a LiPicon). Escape now closes the desk "⋯" menu and every Bench modal
  (`CloseTransient` invokable + a document keydown listener in `bench.js`). The drag ghost
  is made robustly small (explicit size + inner SVG/image forced to fill, so it no longer
  renders full-size). Documented the **tagged-PDF library limitation** (see ROADMAP known
  issues). Files: `BenchComposer.cs`, `Bench.razor`, `Bench.razor.css`, `bench.js`, docs.
- **v17 — Bench desks refinements.** View toggle 1/2/3 desks per row (default 1,
  persisted to storage key `bench.perRow`); desk min-height 400px; page-number label
  under each desk page (`OriginalIndex + 1`, `(blank)`, `(image)`); drag ghost shrunk to
  ~25% (`GHOST_SCALE`, 22px floor); outside-click closes the desk "⋯" menu via
  `.menu-overlay`; 30-desk cap (`BenchService.MaxDesks`, `CanAddDesk`); shelf source
  expand/collapse + whole-file drag (`AddSourceAllToDesk` / `DropSourceAll`); shelf
  source actions changed from a "⋯" menu to two inline icons (Properties, Press); images
  stay raw until Bind — new `BenchPage.IsImage`/`ImagePath`, desk-level multi-select
  **Fetch image** (`IFilePicker.PickImagesAsync` + `WpfFilePicker` implementation),
  rendered as a data-URI `<img>`, converted only at compose time. Also: the bench's
  "Open" actions open results in the Reader (no reveal-in-Explorer service exists).
  Files: `BenchModels.cs`, `BenchComposer.cs`, `BenchService.cs`, `IFilePicker.cs`,
  `WpfFilePicker.cs`, `Bench.razor`, `Bench.razor.css`, `bench.js`. Plus the first
  in-repo docs (`docs/FEATURES.md`, `DEVELOPER-NOTES.md`, `ROADMAP.md`).
- **v16 — Multi-desk Bench (first cut).** Rewrote the Bench from a single composition
  into the shelf + desks model: shelf of sources, independent desks, drag from shelf into
  desks (copy semantics), per-desk Bind/Lift, Bind all, add/remove/rename desks, blank
  and coloured-blank pages, per-desk watermark, per-desk Scatter/Export, per-source
  Properties/Press. New `BenchService`/`BenchComposer`/`BenchModels`, `Bench.razor`
  (+ CSS), and a shelf→desk drag system in `bench.js`.
- **v15 — Terminology.** Renamed remaining "Stitch"/"Browse" to "Fetch" across the
  bench, reader empty states, and dashboard.
- **v14 — Touchscreen pinch-zoom (reader).** Two-finger canvas-only pinch anchored at
  the finger midpoint, one-finger pan kept native, double-tap zoom toggle. Untested on
  real touch hardware.
- **v13 / v12 — Force-touch + toolbar/tabs.** Added `Directory.Build.targets`
  force-touch so deltas recompile without a clean; reordered the bench toolbar; added
  tab "⋯" options (close active/others/all, middle-click close) and the matching
  `TabsService` methods.
- **v11 — Reader fixes.** Tab-key chrome-scroll bug (hidden hover bar removed from tab
  order); 1:1 page overlap on short viewports.
- **v10 — Bench fixes.** Scatter default folder + confirm; Reset button; height fix
  (`100vh` → `100%`); page-jump focus handling.
- **v1–v9 — Reader foundation.** The reader view system, tabs-in-title-bar,
  drag-and-drop open, marquee/loupe/zoom tools, and the first single-composition bench
  (Bind/Lift/Scatter/Export/Press/Properties), plus startup and cache fixes.
