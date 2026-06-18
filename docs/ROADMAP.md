# Chuvadi Reader — Roadmap & Deferred Items

One place for everything not yet done: work that's parked, work that's blocked on the
library, known issues, and the direction ahead. Kept in one file (rather than splitting
"deferred" and "roadmap") because the two overlap heavily — most deferred items *are*
the near-term roadmap.

Last updated alongside **v24**.

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

---

## 2. Known issues

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
  "fetch image as its own page"). *Next in Batch C* — same canvas, `PageBuilder.DrawImage` +
  `PageStamper`.
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

## 6. How this list is maintained

When an item ships, move it out of this file and add a line to the version history in
`docs/DEVELOPER-NOTES.md`. When a new idea is agreed but deferred, add it here with a
one-line note on why it's waiting (blocked on library, parked by choice, or simply
queued). Discussion-first remains the rule: nothing here is a licence to build without
agreement.
