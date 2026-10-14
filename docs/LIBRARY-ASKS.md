# chuvadi-pdf / Chuvadi.Docs / Chuvadi.Sheets — library asks

Chuvadi Reader is a **viewer/UI only**. All document *processing* must live in the
library. This file lists processing the app currently can't delegate because the
library doesn't expose the capability yet. Each is something the app either (a) had to
re-implement and should hand back once the library can do it, or (b) a feature the app
can't offer at all until the library adds it.

Captured during the deep architecture review on 2026-10-14.

## Removed from the app in this PR (already delegated / stopped)

- **Excel number/date format-code emulation** — `XlsxViewService` was parsing Excel
  format codes (`%`, `#,##0`, decimal counts, date masks) to format cell text. That is
  format-engine work. **Removed**; the app now shows the library's plain value.
  → **ASK (SHEETS-FMT):** expose a formatted display string per cell
  (`Cell.FormattedText` / `Cell.DisplayText`) computed by the library's number-format
  engine, honouring the cell's format code. The viewer will map that string directly.

## Asks the app is waiting on (feature left in place, or degraded, until delivered)

### chuvadi-pdf
- **FLATEDECODE too strict — throws on Adler-32 mismatch (PDF-12). [CRITICAL — blocks viewing]**
  Real journal PDF (`PIIS0167814007000138`, 10pp) renders page 1 only; pages 2–10 throw
  `FilterException: FlateDecode: Adler-32 checksum mismatch`. Opens fine in Acrobat/pdf.js/
  PDFium. Deterministic; present in 3.15.0–3.18.0 (3.18 does NOT fix it). Checksum pattern
  (`Expected 0x7B25270A, got 0xD17B2527`) shows an off-by-one: the 4-byte Adler-32 is read
  one byte too late, swallowing the trailing `0x0A` newline. Data decompresses fine — only
  verification throws. No app workaround (`Open` has no options; `DeflateFilter.Decode` has
  no leniency flag; `Adler32.Verify` is unconditional). `PdfRepairer.Repair` recovers 9/10
  and rewrites the doc — partial/lossy stopgap only. **Ask:** (1) lenient decode by default —
  on checksum/trailing-byte mismatch, recover decompressed data + warn instead of throwing
  (as every mainstream reader does); (2) fix the off-by-one when a trailing EOL is present;
  (optional) a `StrictFilters=false` switch on `Open`.
- **CROP — real crop/clip (PDF-01).** `BenchComposer` "crops" by painting white bands
  over the area outside the kept region (`StampMaskBands`). The content is **still under
  the paint** (a data-leak) because `PageComposer` can't clip on place. The library *has*
  `PageCropper` + `PageCropMode.Scrub`, but switching to it rendered these pages blank and
  corrupted uncropped pages in testing — so it needs library-side validation of Scrub's
  coordinate/MediaBox behaviour before the app can adopt it. **Priority: high (security).**
- **REDACTION over-removal (PDF-02).** A redaction rect deletes the *whole* vector
  fill/stroke it merely intersects, not just the covered part. Confirmed still present in
  3.17.1. Redaction of shaded/filled regions is unsafe to rely on until fixed.
- **TAGGED-PDF redaction + verify (PDF-03).** The library can't strip text from tagged
  (structured) PDFs, so the app hand-rolls a post-redaction "did it actually go?" check
  (`RedactService.CountSurvivingText`) that hit-tests extracted fragments against the
  rects, and infers "tagged" by scanning catalog key names (`IsTagged`). Need: real
  tagged-content redaction, a `Document.IsTagged`/`HasStructTree` property, and an
  "extract-within-rect / verify-removal" API.
- **ARROW primitive (PDF-04).** No arrow in the authoring API, so the app computes
  arrowhead geometry by hand (`RedactService.BuildArrow`, and the preview twin
  `Reader.ArrowHeadPoints`). Need `DrawArrow`/`Path.ArrowHead`.
- **MULTI-OVERLAY compositing + image opacity (PDF-05).** `PageStamper` can't composite
  several overlays in one pass, forcing a per-item chained-stamp loop
  (`RedactService.StampPageOverlay`); and image opacity < 1 is recorded but not honoured
  (`StampOneImage`). Need multi-overlay place + per-image alpha.
- **IMPOSITION in the library (PDF-06).** The app computes N-up grid geometry and booklet
  fold order by hand (`BenchComposer.ImposeNup/Booklet`). The library has
  `Imposition.NUp/Booklet`; adopt after validating options parity (treated like crop:
  validate before switching).
- **SVG view-rotation option (PDF-07).** The UI rotates a rendered page by regex-editing
  the SVG string (`PageSvg.razor`). `SvgRenderer`/`SvgExportOptions` should take a display
  rotation (0/90/180/270) and emit the correct `viewBox`/size itself.
- **Content-inventory / dynamic-XFA flag (PDF-08).** To detect a blank dynamic-XFA page
  the app renders SVG and string-matches `<text>/<image>/<path>`
  (`ChuvadiPdfReader.PageHasDrawableContent`). Need `Page.HasDrawableContent` and/or a
  proper "dynamic-XFA, not renderable" flag.
- **COMPRESS vector-only PDFs to target (PDF-09). [HIGH — recent CAD blocker]**
  `PdfCompressor.Compress` only recompresses images, so it can't reduce a vector-heavy
  (CAD) PDF — no images to shrink (maxed ~5.4 MB on an 8 MB CAD file). The app hand-rolls
  the target search in `PressService.PressToTargetAsync` (step JPEG quality 85→25, re-run
  `Compress`, keep smallest); on a vector-only file this **never converges** because quality
  changes nothing. Need a real `CompressToTarget(byteBudget)` that targets a budget and
  falls back to down-convert/rasterise-to-target for vector-only content, so the app can
  drop its hand-rolled stepping loop.
- **XFA geometry completeness (PDF-10, minor).** `XfaDataField.Geometry` was populated for
  15 of 19 fields on the test certificate; 4 null. Low priority.

### Chuvadi.Sheets
- **SHEETS-FMT** (above) — formatted cell display string.
- **COLUMN WIDTH in a real unit (SHEETS-01).** The UI converts width with a hand `w*7`
  factor (`Sheets.razor`). Expose width in px or points.
- **COLUMN LETTER (SHEETS-02, minor).** The UI computes A/AA/AB… labels; a
  library-provided column label would remove it.
- **CELL STYLE on read (SHEETS-03).** `Cell.Style` is null for real files — no fonts,
  fills, borders, number formats surfaced. (Raised earlier; still open.)
- **MERGED RANGES on read (SHEETS-04).** Not exposed; merged labels can't span.

### Chuvadi.Docs
- **RUN FONT (DOCS-01).** `TextFormat.Font` empty for all runs.
- **STYLE-INHERITED FORMATTING (DOCS-02).** Only direct run formatting surfaces; style
  (heading/named-style) bold/size/colour is lost.
- **HEADERS/FOOTERS (DOCS-03).** Header/footer collections return empty.
- **ANCHORED IMAGES (DOCS-04, minor).** Floating/anchored images not placed (inline ones
  render fine).

### Header/footer preview (cross-cutting)
- **TOKEN RESOLVER (PDF-11).** `StampPreview.razor` re-implements `{page}/{total}/
  {filename}/{date}` substitution for the live preview, which can drift from the library's
  bind-time output. A library "resolve header/footer tokens for page N" helper would make
  preview == output.
