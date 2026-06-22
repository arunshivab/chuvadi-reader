# Chuvadi Reader — Roadmap & Deferred Items

One place for everything not yet done: work that's parked, work that's blocked on the
library, known issues, and the direction ahead. Kept in one file (rather than splitting
"deferred" and "roadmap") because the two overlap heavily — most deferred items *are*
the near-term roadmap.

Last updated alongside **v40** (Add Image + shapes — Core).

> **Incoming library package:** a new chuvadi-pdf build is ready that reportedly fixes some
> bugs and adds **footer / page-number** support (unblocks §1.4). To pull it in: drop the
> new `.nupkg`(s) into `localpackages/`, bump the `Chuvadi.Pdf` version pin in
> `ChuvadiReader.Core.csproj`, restore, then verify — re-run the tagged-PDF repro (§2) and
> inspect the new footer/page-number (and redaction) APIs by reflection.

---

## 1. chuvadi-pdf capabilities — status

The four long-standing library requests, with their status after the **3.11.1** bump
(v25). Paste-ready write-ups remain in `chuvadi-pdf-bench-requests.md`.

1. **Composite an existing page with background fill + content opacity** — *likely covered*
   in 3.11.1 by `PageStamper.Place` / `PlaceOnAll` (stamp a source page onto a target with
   a transform + placement) and `WatermarkStamper.ApplyImage`. Verify the opacity +
   background story when wiring overlays.
2. **Document outline / bookmarks** — *not yet confirmed* in 3.11.1 (no outline type seen in
   a first pass). Re-check before relying on it.
3. **Metadata setter on an existing document** — *not yet confirmed* in 3.11.1. Re-check.
4. **Positioned text stamp / page numbers** — ✓ **available in 3.11.1.** `HeaderFooter` +
   `HeaderFooterOptions` give header/footer bands; `TextStamper.Apply` + `StampTokens`
   give positioned text with `{page}`/`{total}`/file/timestamp tokens. This unblocks footer
   page numbers.

Also newly available and useful: `Redactor` / `RedactionOptions` (true redaction by
rectangle or regex — underpins the Redact tool).

**New request — watermark custom / Indic fonts.** `TextWatermarkOptions.FontName` only
resolves the standard-14 PDF fonts (Helvetica/Times/Courier families). Verified: any other
name (Calibri, Arial, "LiPi", …) **silently falls back to Helvetica**, and Devanagari/Tamil
text renders nothing (the standard fonts have no Indic glyphs). The library *does* have
TrueType embedding in the authoring layer (`TrueTypeFontEmbedder`, `TrueTypeLoader`), so the
ask is to let the watermark use it — either bundle a LiPi/Indic font resolvable by name, or
let `TextWatermarkOptions` accept an embedded TrueType font/stream. Until then the watermark
font picker can only honestly offer the three standard families.

**New requests (v40 — from building Add Image + shapes).** Building the overlay tools surfaced
four library gaps. All four are confirmed empirically (full API dumped by reflection; behaviour
verified by stamping onto a real tagged PDF and rendering the result):

1. **A transform / CTM on `PageBuilder`.** The authoring `PageBuilder` exposes only absolute
   draw calls (`DrawRectangle`, `DrawLine`, `DrawImage`, `DrawTextBlock`, …) — there is **no**
   `SaveState`/`Concat`/`Rotate`/`RestoreState`. So content can only be drawn axis-aligned.
   Combined with #2 below, this makes **per-item rotation of filled rectangles and images
   impossible** from the app. Ask: add a graphics-state transform (push/concat-matrix/pop, or a
   rotation parameter on the draw calls). This single capability unlocks image rotation, filled-
   rect rotation, *and* text rotation — all on one shared overlay. (Lines don't need it; we rotate
   their endpoints. Rect/image can't be faked without a filled-polygon primitive, which also
   doesn't exist.)
2. **`PageStamper.Place` cannot composite multiple overlays.** Stamping a second overlay onto
   already-stamped bytes **silently drops the first** (verified twice: two rects → only the last
   survives). So today the rule is *one stamp per page*, which is why every shape and image for a
   page must be drawn onto a single overlay (the app now does this). Ask: either let `Place`
   accept multiple source overlays / be safely chainable, or — better — provide the CTM in #1 so
   one overlay can hold everything at any angle. This limitation also affects multi-item text.
3. **Image opacity.** `DrawImage(byte[]|ImageFrame, x, y, w, h)` has no alpha/opacity parameter,
   so overlay images can only stamp at full opacity. Ask: a `DrawImage` overload (or builder
   state) honouring an `/ca` constant-alpha so watermarks/translucent logos are possible. (Ties to
   the SvgRenderer `/ca,/CA` drop noted elsewhere.)
4. **Coordinate convention — please confirm/document.** All authoring draw methods
   (`DrawRectangle`, `DrawLine`, `DrawImage`, **and** `DrawTextBlock`) place content in a
   **top-left, y-DOWN** space (origin at the page's top-left, y increasing downward), not the
   PDF-native bottom-left y-up. Verified with raw-coordinate probes. This is fine once known, but
   it's undocumented and the opposite of what the page geometry (`Width`/`Height`, `/Rotate`)
   implies — worth a doc note, or an option to choose origin. (This is also the root cause of the
   Add-Text vertical-flip — see §2.)

---

## 2. Known issues

- **Reader floating toolbar (`.rfloat`) colour — FIXED (stamp/watermark delta).** The bottom
  zoom/tool bar was a hardcoded near-black `rgba(26,20,16,0.95)`; it now uses `--chvd-surface-hi` +
  `--chvd-ink` with a softened shadow, so it reads correctly over the page in both themes.
- **One "Oncology" highlight (of 16) is off on page 2 (find-redact UI — open, minor).** Search finds
  all 16 matches and box placement is correct after the Y-flip, but one match on page 2 shows an
  off/missing highlight. Likely a multi-`BoundingBox` match where the UI only renders `Boxes[0]`
  (a line-wrapped or fragment-split occurrence), or a page-2-specific fragment. Investigate when
  next in the find-redact area; not blocking (search + redact otherwise correct).

- **Pattern / regex redaction is UNRELIABLE (library bug — open; blocks the regex feature).**
  `RedactionOptions.Patterns` (`PatternRule` + `CommonPatterns`) should find and remove regex
  matches, but it does not do so dependably. Repro (untagged authored page, single `PatternRule`,
  no rectangles): a literal `amount`, `CommonPatterns.UsSsn` over `123-45-6789`,
  `CommonPatterns.Email` over both `a@b.com` and `john@example.com`, and `CommonPatterns.CreditCard`
  over a test card all extract straight back out after `Redactor.Apply` — nothing was removed.
  Behaviour is erratic (one early combined boxes+pattern run removed an email; isolated runs remove
  nothing). **A redaction that silently leaves SSNs / card numbers / emails in is a safety failure**,
  so the app gates the whole regex/preset surface OFF (Financial · Medical · PII and custom-regex
  chips render disabled, "needs library") until this is fixed. The explicit-box path (manual draws +
  text-search matches) is unaffected and ships enabled. **Library fix needed:** make pattern matching
  apply to the page's extracted text and remove every match's glyphs reliably, independent of whether
  explicit rectangles are present. `FindRedactTests.Pattern_Redaction_IsKnownUnreliable_PendingLibraryFix`
  pins the current behaviour and will start failing (alerting us) once the library is fixed — then the
  UI chips can be re-enabled.
- **Rasterizer crashes on some redacted output (library bug — open).** `PdfRenderExtensions
  .RenderPageToPng/Bmp` can throw `IndexOutOfRangeException` in `ScanlineRasterizer.AddSpanRaw`
  (via `PaintStrokeOp`) when rendering certain redacted PDFs. The app renders pages via the SVG
  path, not the rasterizer, so it is unaffected at runtime; redaction tests assert via `TextExtractor`
  (not rendering) to avoid the crash. **Library fix needed:** guard the scanline span bounds.
- **Add Text lands vertically flipped (under separate investigation).** `StampText` maps the
  on-screen box to a y-UP rect (`MapToPdf`) before calling `DrawTextBlock`, but `DrawTextBlock`
  actually expects **top-left, y-DOWN** coordinates (§1 new-request 4). Net effect: text placed
  near the top of the page is stamped near the bottom. The shape/image path (v40) was built with
  the correct y-down mapping (`MapToPdfTopLeft`) and is unaffected. The text path is intentionally
  left untouched here pending the owner's library-side fix; the clean resolution is the
  `PageBuilder` CTM (§1 new-request 1), after which text can join the shared single overlay.
- **Redaction does NOT remove text on tagged PDFs (library bug — open).** `Redactor.Apply`
  draws the overlay and rewrites the plain content stream, but text inside marked-content
  (BDC/EMC) on tagged pages survives and extracts straight back out. Repro: open a file with
  `/StructTreeRoot`, add a `RedactionRect` covering the whole page, apply, reopen, run
  `TextExtractor` — text remains (tagged CV: 1632→1155 chars, "Dr ARUN SHIVA B…" recoverable;
  same strings in raw bytes). Untagged input correctly goes to 0. **App mitigation (v36):**
  `RedactService` verifies after save and blocks/deletes any output where text still falls inside
  a redaction rect, and the reader warns on tagged docs. **Library fix needed:** remove show-text
  operators within marked-content on tagged pages (and prune the affected StructElem text) so B15
  holds regardless of tagging. The verify-after-save guard auto-clears once the library is fixed.
- **~~Tagged PDFs break page operations and watermarking~~ — FIXED in chuvadi-pdf 3.11.1.**
  Tagged PDFs (with `StructTreeRoot` / `MarkInfo` / `Marked`) used to throw "Page tree
  /Kids[0] is not a dictionary" on any reorder / subset / duplicate / watermark. Verified
  fixed against 3.11.1 on the MRDDFF CV: all four operations now succeed. Resolved by the
  v25 library bump.
- **~~PNG/JPG export — dark vertical line~~ — FIXED in chuvadi-pdf 3.11.1.** Rasterised image
  exports (PNG/JPG) used to show a dark vertical line where a table border rendered too
  heavily. Confirmed resolved by the library in 3.11.1 (reported fixed by Arun, Jun 2026).
- **SVG render ignores transparency (ExtGState ca/CA) — library, reported.** The Reader
  shows pages via `RenderPageToSvg` (`Chuvadi.Pdf.Svg`), and the SVG exporter emits fill
  colour but **not** opacity. A 50%-opacity text watermark (verified `/ca 0.5` `/CA 0.5` in
  the PDF) appears **fully opaque / dark on screen**, while the bound PDF is correct — Adobe
  renders it light at 50%. So output files are fine; only the in-app SVG view is wrong.
  Notably the library *does* capture the alpha: the rendering display list carries an
  `OpacityOp.Alpha` (and `ImageOp.SoftMaskAlpha`), but `SvgRenderer.Render(PageDisplayList)`
  doesn't translate it into SVG `fill-opacity`/`opacity`. No app-side toggle exists
  (`SvgExportOptions` = InlineImages, TextStrategy, FontStrategy, Precision, PrettyPrint,
  Background — nothing for alpha), so this is a library fix in the SVG serializer. Reported
  to the library chat (Jun 2026). **Correction (Jun 2026):** an earlier note here claimed
  `SvgExportOptions.Background` "has no effect" — that is now wrong. In 3.11.1 it **works and
  defaults to opaque white**: the renderer emits a full-page `<rect … fill="#FFFFFF"/>` behind
  the content unless `Background` is explicitly set to `null`. So the library, not the app, now
  supplies the page's white background. The reader deliberately passes `Background = null` (for
  the view-only page tint, v33); the bench and export keep the white default. The remaining gap
  is alpha only: a non-opaque `Background` still emits `fill="#RRGGBB"` with no `fill-opacity`,
  same serializer issue as the watermark `/ca` case above.
- **Header/footer "Shrink" fits blank the page — library, reported.** `HeaderFooterOptions.Fit`
  has three values; only `Overlay` preserves content. Both `ReserveAndScale` ("Shrink page to
  make room") and `ScaleIfIntruding` ("Shrink only if overlapping") **drop all original page
  content**, leaving only the header/footer band. Verified by rendering the result to SVG: on
  the CV, drawn paths fall from 814 (Overlay) to **0** (either Shrink mode); reproduced on a
  plain untagged PDF too, so it is not tagged-PDF-specific. No app-side workaround — the page
  body is gone in the produced file. The two Shrink options are **kept in the UI on purpose**
  (per Arun) so they light up the moment the library fixes the reserve/scale path; until then,
  only Overlay should be used. Band background also depends on a Shrink fit, so it stays dimmed
  under Overlay. Reported to the library chat (Jun 2026).

---

## 3. Parked design decisions

- **Bench icon and drawer-pin icon.** Candidate icons were explored but none were chosen
  ("think later"). The current icons stand until revisited.

---

## 4. Awaiting verification

- **Touchscreen pinch-zoom (reader, v14).** Implemented and in place: custom canvas-only
  two-finger pinch + double-tap + Ctrl/trackpad-wheel zoom, with native WebView2 zoom flags
  deliberately disabled so only the page canvas scales (never the chrome). This is the intended
  design, not a stopgap. Only open item: confirm on real touch hardware before deployment — no
  known defect.

---

## 5. Roadmap (app-side, buildable now unless noted)

**Reader**
- **Send individual pages from the Reader to a desk.** *Shipped (send-pages delta).*
- **Find in document (Ctrl+F) — SHIPPED (find delta).** A slim find bar (top-right): type a term,
  highlight every match on the rendered pages, Enter / ↑↓ to step (with a "3/17" counter), Esc to
  close, case-insensitive with a whole-word toggle — reusing `RedactService.SearchAsync`
  (`DocumentSearch`). Ctrl+F is intercepted in `reader.js` so WebView2's native find no longer hijacks
  it. Focusing the empty box pre-fills from the current on-page selection (via `selectedText` —
  selection only, no clipboard read), so the "select text → click Find → it's there" flow works.
- **Snapshot tool — SHIPPED (snapshot delta).** Toolbar Snapshot tool (crop icon) → marquee a
  region on a page → a menu offers **Save as PNG** (host save-picker), **Copy to clipboard** (host
  `IImageClipboard` → WPF `Clipboard.SetImage`), and **Send to desk** (writes a temp PNG and calls
  `BenchService.AddImageToDesk`, with a desk picker incl. "New desk"). Capture is done in JS — the
  page SVG is rasterised to a canvas at 150 DPI and the region cropped to PNG. Note: pages whose SVG
  embeds externally-referenced raster images could taint the canvas; text/vector pages capture fine.
- **Text selection / copy on pages** — selectable SVG text already works; the specific ask (auto-fill
  Find from the selection) shipped with the find delta above. The app-wide "non-selectable chrome"
  polish still must **exclude** the page area. *Mostly done; chrome polish open.*

**Reader — markup tools (Adobe-Reader-style), view-then-Save-As.** Decision (Jun 2026):
these live in the **Reader**, not the Bench — precise per-page edits need the large zoomable
page view, and the Reader gains **Save As a new file** (the original is never written). The
Bench stays papers-as-units (fetch/reorder/scatter/press/bind + desk watermark/header-footer).
All markup tools share one overlay canvas (the markup mode added with Redact) and flatten via
the library on Save As:
- **Redact** — ✅ shipped v34–v36. Click-drag boxes (any overlay colour, v35), stored as 0–1
  fractions per page, flattened with `Redactor` into `<name>-redacted.pdf`. Rotation-aware.
  **Secure on untagged PDFs; tagged PDFs are blocked** by verify-after-save until the library
  fixes tagged-content removal (see §2).
- **Add Text** — ✅ shipped v37. Click/drag to place a text box, type inline, full styling
  (font family, bold, italic, size, colour, alignment, rotation, line height), move/resize/delete;
  authored + stamped on Save As. Works on tagged PDFs (overlay, not removal). Standard-14 fonts
  only — **Tamil/Indic deferred** to a font-embedding pass (the library has `TrueTypeFontEmbedder`).
- **Add Image** — overlay an image on a page at a position (distinct from the Bench's
  "fetch image as its own page"). **Core shipped v40** (`RedactService` image + shape stamping,
  one-overlay-per-page, verified on a tagged PDF). Non-rotated images + rectangles (fill/stroke)
  + lines at any angle. **Image/rect rotation deferred** to the library CTM (§1 new-request 1).
  **UI is the next delta** (toolbar Image/Rect/Line tools, property panels, place/move/resize,
  aspect-lock, clipboard paste + drag-drop). Uses `PageBuilder.DrawImage`/`DrawRectangle`/
  `DrawLine` + `PageStamper.Place`.
- **Shapes (Rectangle, Line)** — shipped alongside Add Image (v40 Core). More primitives
  (ellipse, arrow, ink/freehand, polygon) are **not in the library** — added to the asks list;
  build when delivered.
- **Signature**, **Snapshot** — also Reader-side, later.
- **Watermark / Header-footer in the Reader** — agreed to also offer these in the Reader
  (acting on the open document, Save As), as their own delta after the markup tools. They remain
  in the Bench as desk-level batch stamps.

**Bench — fetch/arrange**
- **Page ranges on fetch** — bring in only a chosen range of a source's pages.
- **Insert at a position on fetch** — drop a fetched file's pages after a chosen point
  rather than appending.
- **Image-page polish** — tune the raw-image thumbnail (currently an inline data URI) and
  the drag feel as it gets real use.

**Polish**
- **Non-selectable chrome (app-wide)** — extend the bench's `user-select: none` to the
  dashboard, toolbar, and tab strip, **excluding** the page content area (text selection on
  pages must stay). Bench chrome is already done (v24).

---

## 5b. Testing & CI

Added as its own delta after v40. Two pieces:

- **`tests/ChuvadiReader.Tests`** (xUnit) — regression guards for the overlay pipeline. Fixtures
  are authored in code (no binary PDFs); pages are rasterised with the library's own
  `PdfRenderExtensions.RenderPageToBmp` (pure-managed, no GDI/poppler) and pixels are sampled out
  of the BMP. Covers: shape/image placement, **multiple items coexisting on one page** (the
  "PageStamper can't stack" guard), line angle, redaction removal on untagged input, and the
  nothing-to-save guard. Runs identically on Linux/macOS/Windows.
- **`.github/workflows/ci.yml`** — builds Core + Ui + Tests and runs the tests on an
  **ubuntu / macos / windows** matrix; the WPF host (`ChuvadiReader.Windows`, net10.0-windows)
  additionally builds on the Windows leg only.

**Package feed for CI.** The dev tree restores chuvadi-pdf from `./localpackages` (git-ignored).
CI can't see that, so a committed mirror of just the **3.11.1** set + `LiPicons.Blazor` lives in
**`ci/packages/`**, selected via **`ci/nuget.config`**. When the library version bumps, refresh
`ci/packages/` with the new `.nupkg`s and bump the pins. (A future move to GitHub Packages would
retire this folder — parked with the post-icon-redesign distribution decision.)

A tagged-PDF redaction-block test is intentionally **not** included yet: it needs a tagged fixture,
and authored PDFs are untagged. Add it with a small committed fixture (or a library tagging API)
when convenient.

## 5c. Redact destination (shipped) + Redact features (next)

**Shipped (relocation delta).** Redaction is now its own top-level sidebar destination (`/redact`,
peer of Reader/Bench) rather than a Reader markup tool. It opens its own session via `IPdfReader`,
renders pages **lazily** (IntersectionObserver — redaction docs are routinely 500+ pages), and
reuses `RedactService` for the save. Two entry routes: a Fetch… picker inside Redact, and a
**Send to Redact** action in the Reader. The inline Redact tool was removed from the Reader's markup
toolbar (markup is now overlay-only: Text/Image/Shapes). A capability seam — `IEntitlements.CanRedact`
(`DefaultEntitlements` returns true) — gates the rail item, the route, and the Send button, so Redact
can later become Enterprise-only by swapping one implementation. It is plumbing, not enforcement.

> Cleanup note: **DONE (send-pages delta).** The inert redaction members in `Reader.razor`
> (`_redactions`, `RedactionCount`, `MarkupCount`, `BoxFill`, `RemoveBox`, the dead `RedactDraw`
> branches, orphaned `_mkDraft`/`_mkDraftPage`) were removed; markup is overlay-only and the v40
> overlay save-path is intact. `CheckTaggedAsync`/`_mkTagged` stay (still drive the markup tagged chip).

**Planned next (agreed):**
- **Watermark + Header/Footer in the Reader, with live preview — and the SAME preview added to the
  Bench.** Reuse the proven library calls (`WatermarkStamper.ApplyText`, `HeaderFooter.Apply`) via a
  shared Core `StampService`; a shared `<StampPreview>` component renders an approximate on-page
  live preview (rotated translucent watermark text + header/footer bands) in both surfaces; the real
  library stamp is produced on Save (Reader) / press (Bench). Fit = Overlay only (Shrink fits blank
  the page — library bug, §2). This is the current delta.
- **Redact preview — show the black box when a match is Accepted.** Deferred until the new library
  package lands (alongside re-enabling redaction once the Redactor removes text). Clicking ✓ on a
  match should render the redaction box as a preview, not just tint the highlight.

**Find &amp; redact (shipped — text path).** The Redact destination now has a right-docked Find
panel (toggled from the top bar):
- **Search** via `DocumentSearch.SearchAsync` with **case-sensitive**, **whole-word**, and
  **page-range / whole-doc** options — the standard find controls. Matches return per-page
  normalised boxes (top-left fractions, same convention as the manual draw layer) plus a context
  snippet. `RedactService.SearchAsync` does the normalisation in Core (it has page sizes).
- **Review flow** — every match is checked by default; review by ticking/crossing each match either
  in the drawer list or via the on-page highlight's hover ✓/✗ (✓ = include in redaction, ✗ =
  exclude); the two stay in sync. The jumped-to match gets a stronger highlight. **Redact checked**
  (review path) and **Redact all matches** (no-review path) both apply.
- **Redaction type** — **Box** (default, black/white overlay) and **Glyph-only** (transparent
  overlay, removes the word with no box). Both verified to remove text on untagged docs.
- **Combined save** — manual draw boxes + accepted matches feed ONE `RedactService.ApplyAsync`
  (a single `Redactor.Apply`), so a session can mix hand-drawn boxes and found text, saved once.
  The verify-after-save tagged guard and amber warning chip apply throughout.
- **Auto-paste** — focusing the empty Find box pre-fills it from the current on-page text selection,
  so select-then-Find needs no Ctrl+C. (No clipboard fallback: `navigator.clipboard.readText()`
  triggers a WebView2 permission prompt that Adobe/WPS never show, so it was removed.)
- **Reader → Redact** — **Send to Redact** is now flatten-aware: if the Reader has unsaved overlays
  (text/shapes/images), they're flattened into a temp PDF that is sent to Redact (shapes travel
  baked in); otherwise the original file is sent.
- **Regex / category presets — GATED OFF pending a library fix.** Financial · Medical · PII and
  custom-regex chips render disabled ("needs library") because the library's pattern redaction is
  unreliable (see §2). The Core + UI seam is fully built (`RedactService.PatternPresets`,
  `BuildPatternRules`, `ApplyAsync` already accepts patterns), so re-enabling is flipping the chips
  back on once the library is fixed.

**Library asks raised by this delta (for the chuvadi-pdf chat):**
- **Ask A — replace-text redaction mode.** A third redaction type beyond box/glyph: remove a match
  and draw a shorter replacement string in its place (font/size/baseline from context), as a true
  removal. No `Redactor` replace API exists today. The "Replace" type is shown but locked until this
  lands. We won't build an app-side remove-then-stamp workaround (architecture rule).
- **Ask B — expand `CommonPatterns` for complete Financial / Medical coverage.** Current set is
  SSN/phone/email/ICD-10/card/ISO-date/ZIP/NHS. Wanted (grouped if possible): Financial — IBAN,
  routing (ABA), account, SWIFT/BIC, ITIN, EIN; Medical — MRN, insurance/policy ID, DOB, NPI; PII —
  passport, licence, IP. Lets every Chuvadi product share one canonical preset set.
- **Ask C — fix pattern/regex redaction reliability (blocks Asks B's value).** `RedactionOptions
  .Patterns` removes matches erratically / not at all (see §2). Must reliably remove every regex
  match's glyphs. Until then the regex feature stays gated.
- **Ask D — guard the rasterizer crash** (`ScanlineRasterizer.AddSpanRaw` IndexOutOfRange on some
  redacted output; see §2).

## 6. How this list is maintained

When an item ships, move it out of this file and add a line to the version history in
`docs/DEVELOPER-NOTES.md`. When a new idea is agreed but deferred, add it here with a
one-line note on why it's waiting (blocked on library, parked by choice, or simply
queued). Discussion-first remains the rule: nothing here is a licence to build without
agreement.

---

## 7. Bench v1 feature backlog (numbered — work page by page)

The full Bench feature set folded into v1 scope (agreed Jun 2026). ✅ = already in,
◐ = partial, unmarked = to build. We work through the unmarked items one at a time.

**Page arrangement on the desk**
1. Drag to reorder pages ✅
2. Move pages between desks ✅
3. Rotate a page ✅
4. Add blank page ✅
5. Add image ✅
6. Delete a page from a desk ✅ (select page(s) → desk-bar Delete; per-page × removed so the hover strip fits the thumbnail)
7. Duplicate a page ✅ (per-page Duplicate → clone inserted after)
8. Multi-select + bulk move / delete / rotate
9. Reverse page order ✅ (desk toolbar Reverse)
10. Sort pages ✅ (desk toolbar Sort → source order, blanks/images last)
11. Extract selected pages → new desk ✅ (bench-bar Extract, moves selection into a new desk)
12. Split a desk in two ✅ (per-page Split → this page onward to a new desk)
13. Interleave two sources (odd/even merge for double-sided scans) ✅ (shelf "Interleave" → pick file A + B → weaves A1,B1,A2,B2… into a new desk, appends the longer source's remainder)
14. Crop a page ✅ (Bench — hover-strip Crop → drag a rectangle → **four apply modes** (#332/#333): Blank rest (mask outside white, page size kept), Crop to size (page = rect), Fit to page (rect scaled centered into the original size), Fit + margin (fit inside page minus 0.5in default). Fit/Mask whiten outside the kept region via a stamped band overlay so neighbour content can't bleed when placed. Un-rotates the displayed crop into page space. Geometry unit-tested incl. letterbox-no-leak; rotated/intrinsic-`/Rotate` pages to field-verify)
15. Resize / normalize page size (force all to A4 or Letter) ✅ (per-desk toolbar "Size" → A4/Letter/original; every page scaled-to-fit, top-anchored, at Bind via PageComposer.ScaleToFit)

**Source shelf**
16. Fetch a PDF ✅
17. Collapsible sources ✅
18. Page thumbnails ✅
19. Page-range fetch (bring in only pp. X–Y)  — open (follow-up)
20. Fetch multiple files at once ✅ (FetchFile loops PickDocumentsAsync)
21. Drag files in from Explorer  — open (follow-up; WebView2 file-path limits)
22. Reorder sources ✅ (BenchService.MoveSource + ↑/↓ on each source header)
23. Remove a source ✅ (shelf 'Close file', delivered #317)
24. Collapse-all / expand-all ✅ (shelf toolbar toggle + BenchService.SetAllCollapsed)
25. Search a source and jump to a page  — open (follow-up; needs library search)
26. Larger page preview on hover
27. Thumbnail size slider

**Desk management**
28. Multiple desks ✅
29. Rename desk ✅
30. Delete desk ✅
31. Per-desk watermark + header/footer ✅
32. Reorder desks ✅ (#335 — ←/→ on the desk header)
33. Duplicate a desk ✅ (#335 — deep copy of pages + size/colour/watermark/header-footer, inserted after the original)
34. Desk colour / label ✅ (#335 — label via rename; colour swatch + 8-colour palette, drives the desk-card top accent)
35. Per-desk page-size & margin normalization ✅ (size via #15; **Add Margins** #332: per-page hover-strip Margins dialog — cm/in toggle, Default 0.5in preset, per-side T/R/B/L, apply to This page / All pages on the desk; content scales-to-fit-centered inside the page minus margins, page size unchanged. v1 limitation: page-space margins on rotated pages)
36. Desk templates / presets
37. Save / restore a whole bench session (arrangement, not just output)

**Imposition / layout (print-shop style)**
38. N-up ✅ (#336 — desk “Impose” dialog: 2/4/6/9-up + custom rows×cols, A4/Letter, gutter; flattens the desk then lays pages scaled-fit-centered per cell → new source + new desk) (2-up, 4-up) onto one sheet
39. Booklet imposition ✅ (#336 — saddle-stitch: pad to ×4, fold order, 2-up per sheet side → new desk; print double-sided + fold) (saddle-stitch ordering)
40. Numbering / Bates ✅ (#338 — desk “Number” dialog: Page X / Page X of Y / Bates with prefix + start + zero-pad; 5 corner positions; font size; all-pages or range; counts blanks; sequential across desk; applied at Bind/Export; № chip on desk header; #339 added first-page handling: Number it / Skip-keep-count / Skip-&-renumber for cover pages)
41. TOC / bookmarks ✅ (#340 — Reader bookmarks panel: reads the document outline, click-to-navigate, plus full editing — add at current page, rename, delete, move up/down, indent/outdent for nesting — and Save writes the outline back into the file)

**Output / bind / export**
42. Bind a desk → PDF ✅
43. Scatter / export desks ✅
44. Combine all desks into one PDF ✅ (#335 — “Combine” toolbar button: binds each desk with its own settings, then object-level merge via PageOperations.Merge; distinct from “Bind all” one-per-desk)
45. Export selected pages only
46. Export pages as images (PNG / JPG)
47. Export with bookmarks ✅ (#340 — desk “BM” toggle, default on: Bind writes a per-source-section outline, each source's own bookmarks nested + remapped to output pages)
48. Encrypt / password-protect on export
49. PDF/A export
50. Compress & downsample images on export
51. Flatten annotations on export
52. Print directly

**Quality-of-life**
53. Undo / redo for desk edits
54. Select-all / invert selection
55. Keyboard shortcuts for arrange
56. Open a desk's result in the Reader
57. Send a desk to Redact
58. Page badges (source colour, rotation indicator) ◐
59. Compare two pages side by side
60. Image-page thumbnail polish (downsize the inline data-URI, tidy drag feel)

> Held: not started. Dr. Arun has another job first; we resume here page by page.

---

## 8. Backlogs (numbered — everything else pending, continues from the 60)

Consolidated master list of all pending work outside the Bench-60 (agreed Jun 2026).
Numbering continues so any item is addressable by number. Status: **verify** = re-check
against the new library package first; **build** = app-side, buildable now;
**needs-lib** = depends on a library capability (confirm it's in the new package, else
write the ask).

**A. Library uplift — verify the new package, then re-enable** *(do these first)*
61. Tagged-PDF text removal in `Redactor` — re-enable redaction on tagged PDFs; verify-after-save guard auto-clears; add a tagged-PDF redaction test. *(verify)*
62. Pattern / regex redaction reliable — re-enable regex + presets (SSN, email, …) in Redact. *(verify)*
63. Rasterizer no longer crashes on redacted output — move redaction tests from text-extraction back to real rendering. *(verify)*
64. `PageStamper.Place` stacks multiple overlays — drop the one-overlay-per-page workaround; multi-item pages become a positive test. *(verify)*
65. SVG renderer keeps stamped overlays — re-enable the SVG overlay path; do the parked visual testing of Image/Shapes overlays. *(verify)*
66. SVG renderer honours transparency (ExtGState `ca`/`CA`) — show content opacity in the rendered view. *(verify)*
67. Header/footer Shrink/Reserve fits preserve content — light up the two Shrink fits + band background (kept dimmed for exactly this). *(verify)*
68. Indic / custom watermark fonts — enable Tamil/Indic watermark via the bundled font / `TrueTypeFontEmbedder`. *(verify)*
69. Redact "black box on Accept" preview — the UI piece deferred to this package. *(build)*

**B. Reader markup / drawing capabilities** *(confirm the primitives are in the new package; if not, these become written library asks)*
70. Shapes beyond rectangle — ellipse, circle, arrow, polygon, ink/freehand (needs a filled-polygon primitive + CTM). *(needs-lib)*
71. Per-item rotation of images & filled rects (needs a CTM/transform on `PageBuilder`). *(needs-lib)*
72. Image overlay opacity / RGBA transparency — `DrawImage` alpha/`ca` ("draw image is RGB only" today). *(needs-lib)*
73. Change page background colour — app feature over `SvgExportOptions.Background` / a `PageCompositor`. *(needs-lib + build)*
74. Change page content transparency — per-page content opacity. *(needs-lib + build)*
75. Content layering — draw arbitrary content **behind** existing page content (z-order / behind the text). *(needs-lib)*

**C. Redact features**
76. Redaction "Replace" type — overlay replacement text where redacted (needs a library `Redactor` replace API; the type is shown but locked). *(needs-lib)*

**D. App-wide polish / misc** *(buildable now)*
77. Non-selectable chrome (app-wide) — extend `user-select: none` to dashboard/toolbar/tab strip, **excluding** the page area. *(build)*
78. One-off "Oncology" highlight off on page 2 (find-redact minor) — likely a multi-`BoundingBox` match where only `Boxes[0]` renders. *(build)*
79. Bench icon + drawer-pin icon — revisit (parked "think later"). *(build)*
80. Reader Signature tool (Reader-side, later). *(build)*

> **Sequencing (confirmed Jun 2026):** all **needs-lib** items (70–76, and any other new
> chuvadi-pdf primitive) are the **LAST** phase — batched as library asks and done at the very
> end, after every app-buildable item ships. Do not treat them as blockers mid-stream. Order:
> work the app-buildable items feature-by-feature down this list → then batch the library asks.

---

## 9. v3.14.0 verification sweep (results)

Upgraded 3.11.1 → **3.14.0** (drop-in: Core+Ui build clean, 21/21 tests green, no API breaks).
Empirically verified against repros + reflection:

**VERIFIED FIXED**
- **#61 Tagged-PDF redaction removal** — full-page redact on the tagged CV → page text 0 chars, "ARUN" gone (was 1155 chars in 3.11.1). ✅
- **#64 Multi-overlay stacking** — `PageStamper.Place` twice → both overlays survive (raster confirms red+blue rects). The one-stamp-per-page rule is lifted. ✅
- **#66 SVG renderer honours opacity** — a 0.4 watermark now emits `opacity` in the SVG. ✅
- **#67 Header/footer Shrink fits** — `ReserveAndScale` keeps the body AND draws the band (body was blanked in 3.11.1). ✅

**CONFIRMED AVAILABLE (API)**
- **#71 Per-item rotation** — `PageStamper.Place` now takes a `Graphics.Transform` (`CreateRotationDegrees`, etc.). ✅
- **#75 Behind-content layering** — `StampPlacement.Underlay` exists (stamp under the page content). ✅
- **#76 Redaction "Replace"** — `RedactionRect(pageIndex, bounds, replacementText)`. ✅
- Bonus new surface: `PageStamper.ReplaceStamp`/`RemoveStamp` (named stamps), `DrawHyperlink`,
  `DrawTable`/`ReportBuilder`, `FormFiller.Fill`, `PdfDocumentBuilder.AddTrueTypeFont(name,data)`.

**STILL OPEN (stay as library asks)**
- **#62 Pattern / regex redaction** — STILL removes nothing. Tested string + Regex ctor, `_=>true`
  validator, all-pages + page-0 scope, and even a literal word — none redacted, while rect redaction
  works. So the pattern→rect matching step is still broken. *Library ask remains.*
- **#70 Shapes beyond rect/line** — `Graphics.Path` (with `Ellipse`/`CubicBezierTo`) exists but only
  feeds the internal rasterizer/glyphs; `PageBuilder` has no draw-path. *Ask: expose Path fill/stroke
  on `PageBuilder` (or a `DrawEllipse`/`DrawPolygon`).*
- **#72 Image alpha** — `PageBuilder.DrawImage` still has no alpha; opacity exists only on the
  watermark image path (`ImageWatermarkOptions.Opacity`). *Ask: `DrawImage` alpha overload.*

**PARTIAL**
- **#68 Indic/custom fonts** — `AddTrueTypeFont(name,data)` enables Indic **text overlays** (authored
  pages); **watermark** Indic still needs a font-data path (`TextWatermarkOptions` has `FontName` only).
- **#73/#74 page background / content transparency** — render side now supports it (SVG `Background` +
  opacity). Save-side bake still wants the `PageCompositor.Recolor` ask (no such API yet).
- **#65 SVG keeps stamped overlays** — raster path confirmed; SVG path needs a quick visual confirm.

---

## 10. Field-test findings (3.14.0, real-doc testing)

Method note: an earlier diagnosis pass used a scratch render sampler that assumed bottom-up
bitmaps. `PdfRenderExtensions.RenderPageToBmp` emits **top-down** bitmaps, so that sampler
inverted the y-axis and produced several wrong conclusions (a "y-up redaction frame", "mirrored
shapes/images") that were investigated and **discarded**. The test project's `TestPdf.SampleBmp`
reads the height-sign correctly and is the trustworthy sampler. Ground truth, re-verified with it:
**all authoring primitives (`DrawText`, `DrawRectangle`, `DrawTextBlock`) and `RedactionRect.Bounds`
are TOP-LEFT / y-down.** `MapToPdfTopLeft` is therefore correct for shapes, images, and redaction.

Working: watermark render; image/logo gradient (export + reader); shape/image/redaction placement.

- **#81 Text overlay vertically MIRRORED (app-side) — FIXED.** `StampText` was the only overlay
  using the y-up `MapToPdf`; `DrawTextBlock` is top-left/y-down like the other primitives, so the
  text was mirrored top↔bottom (placed high → saved low). Fix: `yTop = h - (rect.Y + rect.Height)`
  (top-down top edge), keeping the y-up `rect` for the rotation centre. Verified end-to-end: text
  placed at norm Y=0.10 now renders at fy≈0.11 (top, ~8pt natural top-padding). 21/21 tests green.
- **#82 HF band Background floods the page (library bug).** With `Fit=ReserveAndScale` or
  `ScaleIfIntruding`, `HeaderFooterOptions.Background` paints the ENTIRE page, not just the band rect
  (≈99% painted, confirmed headless); with `Fit=Overlay` it paints nothing. → library ask. App-side
  stopgap (pending): gate the band-background control off + drop band-fill from `StampPreview`.
- **#83 Shelf: add an option to close/remove files from the source shelf.** (Future; user-requested.)

---

## 11. Redaction findings (3.14.0)

The new `Chuvadi.Pdf.Redaction` package ships real additions worth adopting: `PatternSets`
(Financial/Medical/GeneralPii), `PatternValidators` (Luhn/Verhoeff/Iban/AbaRouting/Npi),
`CommonPatterns.LabeledValue`, optional-arg `PatternRule` ctors, and `RedactionRect.ReplacementText`.
Wire `PatternSets`/`PatternValidators` into the deferred find-and-redact SSN/PII presets.

- **App mapping is correct.** Search boxes are bottom-left y-up (verified: "CONTENTS" near a page
  top → Y≈766/842); `SearchAsync` converts them to top-left normboxes that demonstrably contain the
  rendered glyphs; `ApplyAsync` maps via `MapToPdfTopLeft` (y-down) — the frame `RedactionRect` wants.
  The search→redact round-trip removes text in the unit tests.
- **#84 (LIBRARY) `Redactor` rect-removal is position-unreliable.** EMPIRICALLY CONFIRMED with the
  library alone (no app code): redacting a rectangle that *exactly covers the rendered glyphs* removes
  the text only for certain anchor positions and not others — e.g. on a 595×842 page, an authored word
  was NOT removed at y=60/200/400/600/780 in either y-up OR y-down rect framing; on a 460×160 page it
  removed at one position (y=100) but not another (y=60). This is why "Father" (sitting high on the
  page) wasn't redacted while the one unit-test configuration passes. Root cause is inside the library
  Redactor's rect↔glyph matching, not the app's coordinate mapping. → **library ask, with this repro.**
- **#85 "Box vs Glyph" shows a box in both.** `glyphOnly` maps to `OverlayColor = ColorF.Transparent`
  (boxless removal); when removal succeeds, glyph mode is correctly boxless. The "box only" the user
  sees is entangled with #84 — when removal fails, the overlay box is drawn but the text remains, and
  on tagged docs the verify guard then deletes the output. Re-test glyph mode once #84 is resolved.

---

## 12. Outstanding library asks (consolidated)

- **#84 Redactor rect-removal position-unreliable** (above) — highest priority; breaks core redaction.
- **#82 HeaderFooter `Background` floods whole page** under ReserveAndScale/ScaleIfIntruding.
- **#62 Pattern/regex redaction removes nothing** (rect path aside; presets gated off until fixed).
- **#70 Shapes:** `Graphics.Path` exists but `PageBuilder` has no draw-path / ellipse / polygon.
- **#72 `DrawImage` has no alpha** (image opacity recorded but not honoured).
- **#68 Watermark-Indic** needs the font-data path (overlay Indic works via `AddTrueTypeFont`).

---

## 13. Reader-side sweep (status corrections + small deltas)

Stale roadmap entries corrected against the actual code (June 2026):
- **Add Image / Shapes UI — already SHIPPED**, not "next delta": Image/Rect/Line tools, shape +
  image property panels (stroke/fill, aspect-lock, replace, delete), select/move/resize, canvas
  render, Save-As stamping, **and** clipboard-paste + drag-drop (reader.js). No work needed.
- **Watermark + Header/Footer in the Reader with live preview — already SHIPPED**: stamp toolbar
  button + `<StampPreview>` + config panel on the active tab (Reader.razor). (§5c item was stale.)
- **Pinch-zoom — already SHIPPED (v14)**, canvas-only. The "temporary stopgap" note was stale memory.

Shipped this delta:
- **#83 Shelf: close/remove a source file.** Per-source "Close file" (×) button on the shelf header
  → `BenchService.TryRemoveSource`, which **refuses while any desk still holds pages from that source**
  and shows a status notification telling the user to clear those pages first. Source `Index` made
  collision-safe (max+1) so reuse after a close can't alias a desk page's `SourceIndex`. Session
  disposed on close.
- **Desk "Delete all".** `BenchService.ClearDesk` + a per-desk **Clear** button (shown when the desk
  is non-empty) removes all pages from a desk while keeping the desk — the intended way to unblock a
  source close.
- **#6 Non-selectable chrome (app-wide).** `user-select: none` on `.titlebar` (toolbar + tab strip)
  and `.dash` (dashboard); Reader page SVG and Bench page content are outside these, so on-page text
  selection is preserved.

Shipped (follow-up delta):
- **#78 Find-redact highlight covers wrapped matches.** Reader.razor rendered only `Boxes[0]`; now
  loops every box in the match, so a match that wraps across lines is fully highlighted (the stray
  "Oncology" case).
- **#77 Non-selectable chrome — completed app-wide.** Replaced the partial titlebar+dashboard rule
  with `user-select: none` on `.shell` (whole app) and `user-select: text` re-enabled on `.rpage`
  (the rendered PDF page) so copy-from-page + Find auto-paste still work; inputs stay editable.
- **#80 Reader Signature tool — SHIPPED (first cut).** "Sign" tool in the markup rail opens a drawer:
  **Type** (name → rendered to a transparent PNG on a JS canvas in a Script/Elegant/Classic font + ink
  colour, live preview) or **Upload** (PNG/JPG). Either becomes a normal image overlay via the existing
  `AddImageBox` — so it rides the WORKING image-stamp path, NOT text stamping (sidesteps the text
  mirror entirely). Placed signatures drag/resize/delete like any image; Save-As stamps them.
  Freehand-draw still deferred to a library ink/path primitive. Dashboard "Sign" button left as a v2
  stub (entry point is the in-Reader tool).
