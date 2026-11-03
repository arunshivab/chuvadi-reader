# Chuvadi Reader: revamp-v3 feature parity audit

> **Status after fixes (same branch, after this audit):** both BROKEN rows and all five LOST/partial items in section 2 were fixed before delivery:
> 1. ‹ › now step by the spread (`ShellService.CounterStep`, set from the Reader's `PageStep`).
> 2. Immersive brings back the bottom-edge bar (zoom, fit, single/two-page, rotate, hand, exit).
> 3. The redaction view names the file it is redacting in the status bar.
> 4. "Pin to right" (old View drawer): deliberate removal — the View card is a floating card that stays open until closed, so pinning is built in.
> 5. Narrow windows: the View card has Single / Two-page / Cover; the redaction view's Tools rail gets Zoom in / out when the View rail folds; **R** now selects Redact box there.
> Final tally: 121 audit rows — KEPT 79 · MOVED 40 · GREYED 1 · N/A 1 · LOST 0 · BROKEN 0.


Baseline `24bc97b` (main), compared with HEAD `7908a06` (feat/revamp-v3, clean working tree). No source files were changed.

Method: for each feature I traced the new markup element to its `@onclick` handler and checked that the method still exists and still does the same work. The `@code` blocks of Reader/Bench/Redact are almost unchanged: the old-vs-new Reader diff only adds undo hooks, Shell calls and toasts. The checks were:
- every method-group handler from the old markup must be referenced by the new markup or the `.Shell.cs` files;
- the reader.js, bench.js and redact.js keyboard handlers are unchanged;
- shell.js leaves reader-owned keys alone (`READER_OWNED`) and never calls `stopPropagation`, except for Ctrl K.

Paths are relative to `src/ChuvadiReader.Ui/` unless stated otherwise. Line numbers are HEAD unless marked "old". In this report, "Reader" means `Pages/Reader.razor`, "Shell.cs" means `Pages/Reader.Shell.cs` and "Layout" means `Layout/MainLayout.razor`.

> **Count discrepancy:** README.md says "129 existing features", but the data block in `Feature Audit.dc.html` contains **121** rows (13 areas: 9+19+5+9+12+9+11+11+5+11+6+7+7). All 121 are audited below.

Status key:
- KEPT: same place.
- MOVED: new place, still reachable.
- GREYED: visible but disabled with a reason, and it was also not working before.
- BROKEN: still reachable, but behaviour regressed.
- LOST: no longer reachable.
- N/A: had no UI before either.

## 1. Audit checklist (121 rows)

| # | Feature | Old location | New location | Status | Evidence |
|---|---|---|---|---|---|
| **Shell & navigation** |||||
| 1 | Tabs: drag-reorder, middle-click close, restored on launch | Title bar | Top bar tabs | KEPT | Layout:36-47, OnTabMouseUp :414; drag js/reader.js:139-238 → `ReorderTabTo` Reader:3334; restore Reader:3145, Dashboard:237. Drag only works while the Reader page is mounted, as before |
| 2 | Tab ⋯ menu: close active / others / all | Title bar ⋯ | Top bar ⋯ + Ctrl K + Ctrl W keys | KEPT | Layout:55-67, :533-535; reader.js:98-104 |
| 3 | Open by drag-and-drop | Window | Same | KEPT | Layout:372 (`OpenDoc.Requested`); Bench `BenchDrop` unchanged |
| 4 | Light / dark theme | Title bar | Top bar moon + Settings › Appearance (5 themes) + Ctrl K | KEPT | Layout:113, :525-529 |
| 5 | Find pill (decorative) | Title bar | Ctrl K field + left bar Find | MOVED | Layout:110, :140 |
| 6 | Rail: Home, Reader, Bench, Redact, Library, Docs, Sheets, Settings | Left rail | Wordmark/left-bar Home; Read/Mark/Arrange/Protect; Library; Settings; Docs/Sheets as tabs and Ctrl K "Word/Excel viewer page" | MOVED | Layout:31, :124-144, :522-523 |
| 7 | Traffic-light window controls | Right | Right | KEPT | Layout:114-118 |
| 8 | Status bar | "offline · ready · version" | Offline · user · activity · progress · version · clock | KEPT | Layout:152-170 |
| 9 | Redact entitlement gate (`CanRedact`) | Rail item, Protect segment and Send button hidden (old Layout:76, old Reader:92,127) | Protect always shown in the left bar; Redact tools show an error toast; Ctrl K items disabled with a reason; /redact page guard kept | MOVED (gate weakened) | Layout:279-288 (no entitlement check); Shell.cs:766-772, :453-454 |
| **Read: viewing & navigating** |||||
| 10 | Prev / next page, page-number box | Toolbar `rtop` | Top bar ‹ n / N › | **BROKEN** | Layout:76,79 step `CounterCurrent ± 1`; old Reader:35,40 stepped `± PageStep` (2 in two-page). `PageStep` (Reader:3551) is now unused |
| 11 | Zoom ±, Ctrl+wheel, presets | Toolbar, drawer, float bar | View rail ±/%, View card presets, Ctrl ±/0, Ctrl+wheel | MOVED | Reader:1117-1119, :890-896; reader.js:111-113, :249 |
| 12 | Fit width · Fit page · Actual size | Toolbar, drawer, float bar | View rail (W/P), View card "100%", Ctrl K, Ctrl 0 | MOVED | Reader:1121-1122, :884-889; Shell.cs:462-464 |
| 13 | Single / two-page | Toolbar, drawer, float bar | View rail | MOVED | Reader:1124-1125. In a narrow window the View rail is hidden (css/shell.css:283) and the View card has no single/two-page control, so only Ctrl K `v.single`/`v.two` remains (see §2) |
| 14 | Cover alone · Continuous | Drawer | View card toggles, View rail Cover, Ctrl K | MOVED | Reader:898-899, :1126; Shell.cs:467-468 |
| 15 | Rotate view (page / onward / document) | Toolbar + drawer | View card (scope + Left/Right); View rail (right only); Ctrl [ / Ctrl ] | MOVED | Reader:901-910, :1128; Shell.cs:469-470 |
| 16 | Hand · Marquee · Loupe (lens size, magnification) | Toolbar + drawer | View rail Hand/Loupe; View card Hand/Marquee/Loupe + lens/mag; H/Z/L | MOVED | Reader:1129-1130, :911-930; Shell.cs:471-473 |
| 17 | Lock scrolling | Toolbar | View card; Ctrl K | MOVED | Reader:900; Shell.cs:474 |
| 18 | Page colour (view only) | Toolbar popover | View card › Page colour; Ctrl K | MOVED | Reader:931-939; Shell.cs:475 |
| 19 | Page-curl corner | Drawer | View card › Display (not Settings › Reading as planned) | MOVED | Reader:942 |
| 20 | Immersive full screen with hover bar | Float bar `rfloat` (stayed visible in immersive) | Immersive: Read rail, F11, View card. **No on-screen controls in immersive** | **BROKEN** | css/shell.css:387-393 hide topbar, rails, fcards and pstrip in immersive. Old `rfloat` (old Reader:958-977) was not hidden (Reader.razor.css:274 hid only `.rtop`). Only `rexit` remains (Reader:1010-1013) |
| 21 | Touch pinch-zoom of the canvas only | Canvas | Same | KEPT | reader.js:247-258 (unchanged) |
| 22 | Page thumbnails | None | Pages & outline drawer › Pages | KEPT (new) | Reader:58-84; rail :1100 |
| 23 | Bookmarks: view, go to | Bookmarks panel (toolbar icon) | Drawer: § chapter dividers + Outline tab (p# go) | MOVED | Reader:64-68, :102-115 |
| 24 | Bookmarks: add, rename, move, indent, delete, save | Bookmarks panel | Drawer › Outline; Add bookmark on the rail, strip and Ctrl B | MOVED | Reader:87-121, :1101, :1005; Shell.cs:417, :547-559 |
| 25 | Find (whole word, n of N, ↑↓, Esc, fills from selection) | Find bar | Same bar; opened from Read rail, left bar Find and Ctrl F | KEPT | Reader:979-996, :1106; Shell.cs:81; reader.js:114 |
| 26 | Send a page to a desk | Hover button on page | Same + strip "To desk" | KEPT | Reader:549-571, :1003 |
| 27 | Snapshot → Save PNG / Copy / Send to desk | Toolbar tool + menu | Read rail Snapshot, strip, key S; same 3-action menu | MOVED | Reader:1102, :1004, :950-977; Shell.cs:418 |
| 28 | XFA "can't display" · Flatten XFA | Empty state · Doc menu | Same message · Document ▾ › Flatten | KEPT | Reader:332-339; Shell.cs:379 |
| **Document actions** |||||
| 29 | Properties | Doc menu (baseline) · shelf ⓘ | Document ▾ › Properties · shelf ⓘ | KEPT | Shell.cs:374 → Reader:3085; dialog :127-153; Bench:119 |
| 30 | Press (presets, custom, target size) | Doc menu · shelf · Library batch | Document ▾ · shelf · Library | KEPT | Shell.cs:376; Reader:203-274; Bench:128; Library:93 |
| 31 | Export pages as images | Doc menu | Document ▾ | KEPT | Shell.cs:375; Reader:155-201 |
| 32 | Print | Missing | Document ▾, Read rail, Ctrl P (PrintCard + WpfPrintService) | KEPT (new) | Shell.cs:377, :422; Reader:1109, :748-750 |
| 33 | Show in folder · copy path · tags | Library only (no reveal/copy existed) | Document ▾: Show in folder **greyed "Coming soon"**; Copy file path works; Tags… opens Library | MOVED (+GREYED part) | Shell.cs:390-392; tag editing still Library:122-134 |
| **Mark: overlays** |||||
| 34 | Text: font, B, I, size, colour, rotation | 2nd toolbar row | Mark rail Text (T) → Text box card | MOVED | Reader:1061, :627-668 |
| 35 | Text: alignment ×4, line height | 2nd toolbar row | Text card alignment. Line height has no UI now and had none before (model only) | MOVED | Reader:642-648; `LineHeight` Reader:1587 |
| 36 | Image: replace, aspect lock, opacity, rotation, behind | 2nd toolbar row | Image card ("Add image" = old "Replace / add another", same `AddImageViaPicker`) | MOVED | Reader:702-721, :1062 |
| 37 | Shapes: box, line, ellipse, arrow, freehand + properties | 2nd toolbar row | Mark rail + Shape card | MOVED | Reader:1063-1067, :672-698 |
| 38 | Signature: type (3 styles, ink) or upload | Drawer | Mark rail Signature (G) → card | MOVED | Reader:1068, :588-624 |
| 39 | Paste / drop an image onto the page | Canvas | Same | KEPT | `ToggleMarkup` → enableImageDrops; Reader:2679-2697 |
| 40 | Move / resize / delete handles | Canvas | Same | KEPT | Reader:389-392, :414-415, :453-456, :495-496 |
| 41 | Tagged-PDF warning chip (Mark) | Never rendered (`_mkTagged` computed only, old Reader:1517) | Still not rendered | N/A | Reader:1662, :2189-2198 (no markup) |
| 42 | Save (overwrite) · Save as new PDF | 2nd toolbar row | Mark rail foot; Ctrl S / Ctrl Shift S | MOVED | Reader:1078-1079; Shell.cs:447-448 |
| **Arrange: shelf** (Bench markup below the old `bench-bar` is unchanged) |||||
| 43 | Fetch PDFs · add images | Shelf + toolbar | Shelf "Fetch a file", Tools rail Fetch, desk Image | KEPT | Bench:60, :65, :1085, :354 |
| 44 | Drag files in from Explorer | Window drop | Same (BenchDrop) | KEPT | Bench.razor OnInitialized `SetBenchActive` |
| 45 | Collapse / expand · Collapse all | Shelf | Same + View rail | KEPT | Bench:122, :76, :1116 |
| 46 | Thumbnail size slider | Shelf | Same + View rail ± | KEPT | Bench:70-71, :1113-1114 |
| 47 | Page-range filter · All | Each file | Same | KEPT | Bench:144-147 |
| 48 | Search a file → chips → jump · Only matches | Each file | Same | KEPT | Bench:136-161 |
| 49 | Reorder files ↑↓ | Each file | Same | KEPT | Bench:126-127 |
| 50 | Close a file (refused while used) | Each file | Same | KEPT | Bench:129 `CloseSource` |
| 51 | Properties · Press per file | Each file | Same | KEPT | Bench:119, :128 |
| 52 | Interleave A+B | Shelf tool | Same | KEPT | Bench:80-108 |
| 53 | Drag a page or the file name | Shelf | Same | KEPT | Bench:116, :172; bench.js |
| 54 | Hover preview of a shelf page | bench.js | Same | KEPT | bench.js:46-47, :73 |
| **Arrange: desks** |||||
| 55 | Up to 30 desks · Add desk | Toolbar + tile | Tools rail Add desk; Ctrl K | MOVED | Bench:1087; Bench.Shell.cs:69 |
| 56 | 1/2/3 desks per row (remembered) | Toolbar | View rail; narrow: "Desks per row" cycle | MOVED | Bench:1108-1111, :1102 |
| 57 | Rename · colour | Desk header | Same | KEPT | Bench:198-200, :259-263 |
| 58 | Badges WM · HF · № · BM · A4/LTR | Desk header | Same | KEPT | Bench:202-223 |
| 59 | Select all · Invert | Desk header | Same; Ctrl A | KEPT | Bench:218-219; bench.js:61 |
| 60 | Move desk left/right · Duplicate | Desk header | Same | KEPT | Bench:225-227 |
| 61 | Clear all pages · Delete desk | Desk header | Same | KEPT | Bench:230 ff. |
| 62 | Open in Reader · Send to Redact | ⋯ menu | Same | KEPT | Bench:238-239 |
| 63 | Save / Apply template | ⋯ menu | Same | KEPT | Bench:241-251 |
| **Arrange: pages** |||||
| 64 | Drag reorder / between desks / copy from shelf | Desks | Same | KEPT | bench.js (unchanged) |
| 65 | Click / Shift-click selection | Desks | Same | KEPT | unchanged Bench markup |
| 66 | Hover strip: peek, duplicate, rotate, split, crop, margins | Each page | Same | KEPT | Bench:338-343 |
| 67 | Crop dialog | Crop dialog | Same | KEPT | Bench:342 `OpenCrop` |
| 68 | Margins dialog | Margins dialog | Same | KEPT | Bench:343 `OpenMargins` |
| 69 | Badges: rotation, crop, margin, source colour | Each page | Same | KEPT | Bench:322-330 |
| 70 | Provenance label | Each page | Same | KEPT | Bench:346 |
| 71 | Blank page · Image page | Desk footer | Same + Tools rail Blank page | KEPT | Bench:354-355, :1088 |
| 72 | Reverse · Sort by source | Desk footer | Same | KEPT | Bench:356-357 |
| 73 | Delete selected · Extract to new desk · Compare 2 | Desk footer / bench-bar | Delete in footer; Extract and Compare on the Tools rail (conditional, as before) | MOVED | Bench:370, :1094-1101 |
| 74 | Compare pages side by side | Compare dialog | Same | KEPT | Bench:1675 |
| **Arrange: output** |||||
| 75 | Bind desk · Lift selected | Desk footer | Same | KEPT | Bench:377-378 |
| 76 | Bind all · Combine into one | bench-bar | Tools rail foot; Ctrl K | MOVED | Bench:1105-1106; Bench.Shell.cs:76-77 |
| 77 | Scatter | Desk footer | Same | KEPT | Bench:375 |
| 78 | Export desk | Export dialog | Same | KEPT | Bench:376 |
| 79 | Watermark dialog | Dialog | Same | KEPT | Bench:373, :396-469 |
| 80 | Header & footer dialog | Dialog | Same | KEPT | Bench:374, :476 ff. |
| 81 | Numbering / Bates | Dialog | Same | KEPT | Bench:359 |
| 82 | Normalise size | Desk select | Same | KEPT | Bench:360-363 |
| 83 | N-up · Booklet | Impose dialog | Same | KEPT | Bench:358 |
| 84 | Bookmarks on bind | BM chip | Same | KEPT | Bench:214 |
| 85 | Open result after saving | Status strip | Same | KEPT | Bench `_status` strip (unchanged) |
| **Arrange: session** |||||
| 86 | Save / Open bench session | bench-bar Session ▾ | Tools rail "Session…" → popover | MOVED | Bench:1091, :26-34 |
| 87 | Reset bench (confirm) | bench-bar | Tools rail "Reset bench" → same `ConfirmReset` | MOVED | Bench:1092 |
| 88 | Undo / Redo | bench-bar | Tools rail; Ctrl Z/Y | MOVED | Bench:1089-1090 |
| 89 | Shortcuts Ctrl Z/Y/A/D, Esc, Del, R/Shift R | Keyboard | Same (bench.js unchanged; shell.js `preventDefault` on Ctrl Y doesn't block, because bench.js ignores `defaultPrevented`) | KEPT | bench.js:54-69; shell.js:36 |
| 90 | Busy overlay · status strip | Overlay + strip | Same + status bar activity | KEPT | Bench:20-24; Bench.Shell.cs:52 |
| **Protect: redact, stamp, flatten** |||||
| 91 | Draw redaction boxes · colours | Redact page | Protect › Redact box → redaction view (unchanged) | KEPT | Reader:1085 → Shell.cs:766 → `SendToRedact` |
| 92 | Find: case, whole word, page range | Find panel | Same; Tools rail Find & redact | KEPT | Redact:136-174, :225; Reader:1086 |
| 93 | Review ✓/✗ per match | Find panel + page | Same | KEPT | Redact find panel (unchanged) |
| 94 | Box · Glyph-only · Replace (locked) | Find panel | Same | KEPT | Redact:178-180 |
| 95 | Redact checked · Redact all | Find panel | Same | KEPT | Redact:212-213 |
| 96 | Financial/Medical/PII/regex presets (locked) | Locked chips | Same | KEPT | Redact:167-174 |
| 97 | Tagged-PDF warning · verify | Rail | Same | KEPT | Redact:38-40 |
| 98 | Save redacted PDF (new file) | Rail strip button | Tools rail "Save redacted copy"; Ctrl K | MOVED | Redact:235; Redact.Shell.cs:36 |
| 99 | Watermark + header/footer on open doc, live preview | Read toolbar stamp drawer | Protect › Stamp / Watermark card | MOVED | Reader:1087-1088, :772-851, :538-543 |
| 100 | Flatten forms & markup | Doc menu | Protect rail Flatten + Document ▾ | MOVED | Reader:1089; Shell.cs:379 |
| 101 | Password-protect a copy (new) | — | Protect › Password + Document ▾ | KEPT (new) | Reader:1090, :751-753; Shell.cs:380 |
| **Library** (Library.razor unchanged apart from the "← dashboard" link; reached from left bar Library, Layout:141) |||||
| 102 | Search a word across PDFs → open at page | Library | Same | KEPT | Library:22-52 |
| 103 | Category filters + manual tags | Chips | Same | KEPT | Library:65-77 |
| 104 | Shelf view | Library | Same | KEPT | Library:84 |
| 105 | Table view | Library | Same | KEPT | Library:83 |
| 106 | Add / remove tags | Table | Same | KEPT | Library:122-134 |
| 107 | Multi-select → batch Press | Table | Same | KEPT | Library:93 |
| **Home** |||||
| 108 | Greeting + Tamil · workspace line | Home | Same (Tamil now optional, on by default) | KEPT | Dashboard:22-31; Core PreferencesService:73 |
| 109 | Drop / fetch | Home | Same | KEPT | Dashboard:49-59 |
| 110 | Watched folder: change / add | Tile (both buttons had no handler, old Dashboard:35-36) | change now works; add folder greyed "Coming soon" | KEPT (+GREYED part) | Dashboard:189-190 |
| 111 | Pinned · Recent (lock badge) | Tiles | Side panel + Recent list | KEPT | Dashboard:130-155, :86-107 |
| 112 | Storage map | Tile | Same | KEPT | Dashboard:109-127 |
| 113 | Tools tile (Reader, Bench, Library, Merge, Split, Compress, Redact) | Tile | Removed by design. All targets are reachable: left bar, Ctrl K Merge/Split, Document ▾ Press, Protect | MOVED | old Dashboard:40-51; Layout:520-521 |
| 114 | Resume where you left off (new) | — | Resume cards | KEPT (new) | Dashboard:62-84 |
| **Word, Excel, Merge, Split, Settings** |||||
| 115 | Word: headings, formatting, tables, images, header/footer | Docs page (rail) | Word tab (DocxView) + Outline drawer; /docs page via Ctrl K | MOVED | Reader:304-306; Docs.razor; Layout:522; DocxView:27-47 |
| 116 | Excel: grid, sheets, frozen header rows | Sheets page (rail) | Excel tab (XlsxView); /sheets via Ctrl K | MOVED | Reader:307-309; Layout:523; XlsxView:47, :58-70 |
| 117 | Convert Word / Excel to PDF (new) | — | Greyed "Coming with the Layout library" | GREYED | Reader:1041; Shell.cs:386 |
| 118 | Open protected Word / Excel (new) | — | Unlock card | KEPT (new) | DocxView:13, :119; XlsxView:13 |
| 119 | Merge | Doc menu + Dashboard tile | Document ▾, Ctrl K (global and Arrange) | MOVED | Shell.cs:382; Layout:520; Bench.Shell.cs:83 |
| 120 | Split | Doc menu + Dashboard tile | Same as Merge | MOVED | Shell.cs:383; Layout:521; Bench.Shell.cs:84 |
| 121 | Settings: theme, splash, stepper, save folder + month/date, name | Settings page | Settings › Appearance / Reading / Saving / Language (same storage keys `splash`, `page-stepper`) | MOVED | AppearanceSection:34-35; ReadingSection:24-25; SavingSection:10-16; LanguageSection:47 |

**Tally (121):** KEPT 79 · MOVED 38 · BROKEN 2 · GREYED 1 · N/A 1 · LOST 0.

## 2. LOST / BROKEN

1. **Pager no longer steps by spread in two-page view (BROKEN, row 10).**
   - Old: ‹ › called `GoTo(CurPage - PageStep)` and `GoTo(CurPage + PageStep)`, with `PageStep => TwoPageOn ? 2 : 1` (old Reader:35,40; Reader:3551).
   - New: the top-bar pager calls `Shell.GoToAsync(Shell.CounterCurrent ± 1)` (Layout:76,79), and `PageStep` is no longer used anywhere.
   - Fix: make `Shell.GoToAsync` take a step, or have Reader publish a step and the counter handler apply `PageStep`. Alternatively, add Prev/Next commands that use `PageStep`.
2. **Immersive mode has no on-screen controls any more (BROKEN, row 20).**
   - Old: the `rfloat` hover bar (zoom ±, fit width/page, single/two-page, rotate L/R, hand, full screen) stayed available in immersive (old Reader:958-977). Only `.rtop` was hidden (Reader.razor.css:274).
   - New: css/shell.css:387-393 hide `.topbar`, `.mview-rails`, `.fcards` and `.pstrip` in immersive. Only the "exit" chip and the keyboard are left. The "Strip under the page on hover" toggle (Reader:941) now controls a different strip, and that strip is also hidden in immersive (Reader:998 `!_reading`).
   - Fix: show the View rail (or a compact copy of the old leaf bar) on bottom-edge hover in immersive.
3. **Redaction view no longer shows which file is being redacted (LOST, extra).**
   - Old: `rd-top` showed the Redact title and `_name` (old Redact.razor top bar), and the diff removes it.
   - New: nothing in the redaction view shows the name. The status bar activity (Redact.Shell.cs:28) has no file name. The top-bar tab shows the *active tab*, which can differ: overlays are flattened into a temp copy (Reader `SendToRedact`), and "Redact another file…" opens a file that isn't a tab.
   - Fix: put the file name in the status activity, or in a header line above the redaction strip.
4. **The View drawer "Pin to right" option is gone (LOST, extra; arguably obsolete).**
   - Old: `TogglePin` / `_drawerPinned` (old Reader:896).
   - New: there is no pin. The View card floats in the gutter and stays open until closed, so the need is mostly covered.
   - Record it as a deliberate removal, or add a pin to `FloatCard`.
5. **Narrow-window (< 1000 px) reachability gaps (BROKEN-partial, extra).**
   - In a narrow window the View rail is hidden (css/shell.css:283).
   - Reader: the View card has no single/two-page control (Reader:882-944), so only Ctrl K `v.single`/`v.two` remains. The old drawer had both (old Reader:915-916).
   - Redact: its Tools rail has no `rb-viewfold` button (Redact:223-236), so zoom ± is reachable only through Ctrl K `rd.zoomin`/`rd.zoomout`.
   - Fix: add Single/Two-page to the View card, and add a View fold button to Redact's Tools rail.

Behaviour changes worth knowing (not losses):
- **Entitlement gate:** Protect and the Redact tools are now visible even when `CanRedact` is false. They show an error toast instead of being hidden (Layout:279-288; Shell.cs:768-772). `CanRedact` is true today.
- **Arrange syncs every open tab:** Arrange now puts *all* open PDF tabs on the shelf (Layout:326-343). The old "Open in Bench" added only the active file (Reader:1140-1150, which no markup calls now).
- **Misleading "Save redacted copy" in the Reader:** this Protect rail button (Reader:1094) only opens the redaction view (`GoRedact(false)`); it does not save.
- **Unbound "R" hint:** the Redact Tools rail shows the key "R" on "Redact box" (Redact:224), but /redact registers no command with Keys "R" (Redact.Shell.cs:34-41), so pressing R does nothing there.
- **Left-bar Find in Arrange:** Bench sets no `FindHandler`, so Find goes to the Library (Layout:345-354).
- **Show in folder:** greyed in Document ▾ (Shell.cs:390). It had no working implementation before (Library only displays the path), so this is acceptable.

## 3. Extra features not in the audit

| # | Feature (old UI) | Old location | New location | Status | Evidence |
|---|---|---|---|---|---|
| E1 | Rail collapse / expand handle | old Layout:88 `rail-handle` | Superseded: 62 px left bar that expands on hover | MOVED (superseded) | Layout:123-145 |
| E2 | Docs and Sheets pages | Rail | Ctrl K "Word/Excel viewer page"; files open as tabs | MOVED | Layout:522-523 |
| E3 | "← dashboard" links (Library, Bench, Settings) | Page headers | Left bar Home / wordmark (Docs/Sheets keep theirs) | MOVED | Library.razor diff; Bench bar removed; Layout:31, :142 |
| E4 | Rotate view LEFT button | rtop, rfloat | View card "Left", Ctrl [ (the View rail has right only) | MOVED | Reader:908; Shell.cs:470 |
| E5 | View drawer "Pin to right" | rdrawer | none | LOST | old Reader:896 |
| E6 | `rfloat` hover bar (non-immersive) | Canvas bottom edge | View rail | MOVED | Reader:1116-1133 |
| E6b | `rfloat` hover bar in immersive | Canvas bottom edge | none | LOST (= row 20) | css/shell.css:387-393 |
| E7 | Actual-size toolbar button | rtop | View card "100%", Ctrl 0, Ctrl K | MOVED | Reader:888 |
| E8 | Zoom presets chips | rdrawer | View card | MOVED | Reader:890-896 |
| E9 | "Hover controls (bottom edge)" toggle | rdrawer | Repurposed as "Strip under the page on hover" (controls the new strip) | MOVED | Reader:941 |
| E10 | "Edit" toggle (enter Mark) | rtop | Left bar Mark / key 2 | MOVED | Layout:124-137 |
| E11 | Watermark & header/footer button (Read) | rtop | Protect › Stamp / Watermark | MOVED | Reader:1087-1088 |
| E12 | "Open in Bench" | rtop | Left bar Arrange / key 3 (syncs all tabs) | MOVED | Layout:310-314 |
| E13 | "Send to Redact" | rtop | Protect › Redact box / Find & redact (overlays still flattened in) | MOVED | Reader:1085-1086 → Reader:1152 |
| E14 | Mark "Done" | Mark row | Left bar Read / key 1 | MOVED | Layout:558 |
| E15 | Mark "Clear" | Mark row | Mark rail "Clear all marks", Ctrl K (now undoable) | MOVED | Reader:1075; Shell.cs:449 |
| E16 | Mark live counts (text/shapes/images) | Mark row | Status bar activity + Marks card meta | MOVED | Shell.cs:325, Reader:725 |
| E17 | Mark save status + "Open it" | Mark row | Marks card | KEPT | Reader:723-731 |
| E18 | Recovery banner "Try recovery" | Canvas | Same | KEPT | Reader:343-355 |
| E19 | Empty-state "Fetch…" | Canvas | Same | KEPT | Reader:276-298 |
| E20 | Redact prev/next/page box | rd-top | Top-bar pager (CounterHandler) | MOVED | Redact.Shell.cs:16-19, Layout:73-81 |
| E21 | Redact zoom ± | rd-top | View rail (narrow: Ctrl K only) | MOVED | Redact:238-241 |
| E22 | Redact Find toggle / Fetch… | rd-top | Tools rail Find & redact / "Redact another file…" | MOVED | Redact:225, :232 |
| E23 | Redact document name | rd-top | none | LOST | see §2.3 |
| E24 | Redact "Clear" | strip | Same + Tools rail "Clear all boxes" | KEPT | Redact strip; :233 |
| E25 | Bench bar: Fetch File, Bind all, Combine, Add desk, Extract (N), Compare, Session, Reset, Undo/Redo, per-row | bench-bar | Arrange Tools/View rails + Ctrl K | MOVED | Bench:1082-1117; Bench.Shell.cs:63-84 |
| E26 | reader.js keys: Esc, Ctrl W/Alt W/Shift W, Ctrl Z, Ctrl Tab, Ctrl 1-9, Ctrl ±/0, Ctrl F, arrows/PgUp/PgDn/Space/Home/End | Keyboard | Unchanged; shell.js defers via `READER_OWNED` | KEPT | reader.js:88-137; shell.js:7-9, :35 |
| E27 | Window drag + double-click maximize | Title bar | Top bar | KEPT | Layout:604-624 |
| E28 | Dashboard tools tile: Sign / OCR "v2" (disabled) | Tile | OCR greyed in Ctrl K; Sign = Mark › Signature / Protect › Sign with certificate | MOVED / GREYED | Layout:524 |
| E29 | Old Reader mode bar (Read/Mark/Arrange/Protect segments, baseline 1b-i) | rmodebar | Left bar modes + keys 1–4 | MOVED | Layout:124-137, :558-561 |
| E30 | Old Document menu (baseline: Properties, Export, Press, Flatten, Merge, Split) | Title chevron | Document ▾ (all six plus Print, Password, Copy path, Tags, Close) | MOVED | Shell.cs:370-395 |
