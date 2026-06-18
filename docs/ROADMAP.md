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

- **Touchscreen pinch-zoom (reader, v14).** Built but not yet tested on real touch
  hardware. To be verified before deployment. The proper WebView2 pinch story (versus the
  current custom canvas-only handling) is also still open — native WebView2 pinch flags
  remain off as a deliberate stopgap.

---

## 5. Roadmap (app-side, buildable now unless noted)

**Reader**
- **Send individual pages from the Reader to a desk.** Whole-document send already exists
  ("Open in Bench"); this is finer — "send this page → Desk N". *Agreed; next to build.*
- **Snapshot tool** — capture a chosen region of a page as an image, to save or drop into a
  desk. *Not yet built.*
- **Text selection / copy on pages** — select and copy text from a rendered page. Pages
  render as SVG text, so this must stay working; it is the reason any app-wide
  "non-selectable chrome" must **exclude** the page area. *Verify / finish.*

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

> Cleanup note: a few now-unreachable redaction members remain inert in `Reader.razor`
> (`_redactions`, `RedactionCount`→0, `BoxFill`, `RemoveBox`, `CheckTaggedAsync`) to keep the v40
> overlay save-path untouched. Safe to delete in a later tidy pass with the tests guarding it.

**Next (Redact features delta).** Build on the destination:
- **Find-text-and-redact** — *headline item.* Type a name/account number, redact every occurrence
  across all pages. The destination already leaves a seam for a redaction-marks model to receive
  matches, and the page-jump/lazy-render supports landing on far pages.
- Whole-page redaction; a redaction-marks list/panel (review before applying); redact-by-area
  repeated across pages.

## 6. How this list is maintained

When an item ships, move it out of this file and add a line to the version history in
`docs/DEVELOPER-NOTES.md`. When a new idea is agreed but deferred, add it here with a
one-line note on why it's waiting (blocked on library, parked by choice, or simply
queued). Discussion-first remains the rule: nothing here is a licence to build without
agreement.
