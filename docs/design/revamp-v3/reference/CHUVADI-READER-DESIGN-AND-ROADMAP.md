# Chuvadi Reader — design & feature roadmap

Consolidated from the decisions taken through 2026-11-02. Companion to
`CHUVADI-READER-LAYOUT-DECISION.md` (D-10) and `CHUVADI-LAYOUT-REQUIREMENTS.md`.

---

## 1. Product direction

Chuvadi Reader becomes a **multi-format document workspace**: full **PDF editing** plus
**view-in-exact-layout** of docx, xlsx, HTML, Markdown, TXT, and Google Docs/Sheets (as exported).
It ships across **Windows, Mac, Linux, Web, and Android** from one shared Blazor UI + processing
core, with **Sangam** sign-in and an embedded viewer/editor inside **Anjal Mail**. The app stays
UI-only; all document processing lives in the Chuvadi libraries.

---

## 2. Design decisions (locked)

- **D-07 — Office is view-only in Reader.** Editing stays PDF-only; to edit an Office file you
  convert it to PDF first, then edit as PDF.
- **D-08 — new `Chuvadi.Layout` library.** It paginates docx/xlsx (print-faithful: Word/Excel page
  breaks) and emits through `Chuvadi.Pdf.Authoring`, with a documented tolerance when fonts are
  substituted. One engine feeds Reader-view, Print-to-PDF, Anjal, and LiPi export.
- **D-09 — HTML / MD / TXT viewing is Reader-side** (in the WebView); their print/export goes
  through Layout later.
- **D-10 — Reader chrome layout: side rails** (see §3). Supersedes the old "floating leaf bar"
  (1b-ii).

---

## 3. Reader chrome layout (D-10)

**Why:** a portrait page fits a landscape screen by height, so the page is ~40% of screen width and
leaves ~30% empty gutter each side; every top toolbar row steals scarce height. Move controls into
the gutters, keep the top to one thin line → page gains height (~74% → ~90%). Icon-only at rest, so
nothing scrolls.

| Zone | Contents | Behaviour |
|------|----------|-----------|
| **Top bar** (one thin line) | open tabs · page nav `‹ 2 / 13 ›` · `Document ▾` menu | Always visible. Page nav is **not** hover-only. Document ▾ holds whole-document actions: Properties, Export, Compress, Merge, Split. |
| **Left bar** (fixed) | 4 modes (Read · Mark · Arrange · Protect) + global (Open, Find, Settings) | Never changes with mode. Icon-only; **hover expands to labels**. |
| **Right · Tools** (nearest page) | contextual to the mode — light in Read; markup tools in Mark; redaction/stamp/flatten in Protect; desk actions in Arrange | Follows the active mode. Hover-expands. |
| **Right · View** (outermost) | zoom, fit, single/two-page, rotate, hand, loupe | Fixed set. Hover-expands. |
| **Under the page** (on hover) | rotate this page, send to desk, snapshot, bookmark, peek | Centered strip below the page. Only per-page/occasional actions; essentials stay in the bars. |

Responsive: on a narrow or portrait window the bars collapse to thin icon strips; the thin top bar
persists. Polished mock: the "Reader Side Rails" design canvas.

---

## 4. Platform architecture

Two reusable pieces — the **Blazor UI component library** and the **processing core**
(chuvadi-pdf + Chuvadi.Docs/Sheets + Chuvadi.Layout) — wrapped in per-platform shells:

- **Desktop (Win/Mac/Linux):** Blazor Hybrid — MAUI Blazor Hybrid for Win/Mac (and iOS/Android),
  Photino.Blazor for Linux. Processing in-process, on-device — preserves "nothing leaves this device".
- **Android:** MAUI Blazor Hybrid (reuses the same UI). Depends on chuvadi-pdf running on the Android
  .NET runtime — to confirm (native-free, so likely OK).
- **Web portal:** Blazor Server — processing server-side, browser renders. Trades the on-device
  privacy promise; needs auth (Sangam), per-user isolation, encryption at rest.
- **Anjal Mail:** headless, server-side, memory-only. Browser shows server-rendered PNG pages and
  posts simple actions (fill field, place signature, redact rect); Chuvadi applies them server-side,
  returns a new PDF for the reply. Scope: fill forms, sign/stamp, redact, save-as-new.
- **Sangam sign-in:** value-based gating — free local viewing/basic markup; identity required for
  signing, sync, sharing, cloud storage, and anything on the web portal.

---

## 5. Feature roadmap

### Phase 1 — shell (UI-only, library-independent)
- **1a** ✅ merged — bug fixes (Merge/Split tiles, Properties/Export, icons, enabled Redact/Compress).
- **1b-i** ✅ merged — mode bar (Read/Mark/Arrange/Protect) + Document menu.
- **D-10 side rails** — the chrome rebuild (replaces 1b-ii).
- **1b-iii** — ⌘K / Ctrl-K command bar + mode keys 1–4.
- **1c** — Arrange = re-parent Bench (shelf auto-loads open tabs; Option C).
- **1d** — Protect = re-parent Redact + Stamp + Flatten + Password.
- First step when resuming: bump library pins from 3.18.1 to current (3.28) and re-run build + tests.

### Phase 2 — visual
- Implement D-10 as real tokens/themes (light + dark), density, hover-expand.
- Home/Library/Settings polish; icon/label audit.

### Phase 3 — library-gated features (each waits on a library capability)

| Feature | Library dependency | Status |
|---------|-------------------|--------|
| Office viewer in exact layout (docx/xlsx) | **Chuvadi.Layout** + Docs/Sheets read-fidelity | Layout to be built; Sheets 1.2.0 shipped read-side style/merged/dims |
| Print: to-PDF | Chuvadi.Layout (`ToPdf`) | with Layout |
| Print: real printer driver + print-to-device | OS/platform print (app-side) + Layout page model | app-side, after Layout |
| Signature **verification** (show who signed, validity) | chuvadi-pdf PDF-13 | **library ready** (3.27) |
| Layers panel (show/hide OCG) | chuvadi-pdf PDF-14 | **library ready** (3.21+) |
| Measurement tool (distance, `/Measure`) | chuvadi-pdf PDF-15 | **library ready** (3.21+); renderer doesn't rescale by `/UserUnit` (measurement readout only) |
| OCR (searchable scans) | OCR engine (library) | not yet; currently v2/disabled |
| Digital signing (apply a signature) | identity (Sangam) + signing | Sangam-gated |

### Already in the app but not fully live
- **Sign** and **OCR** tiles — v2/disabled.
- **Redact** regex/category presets (Financial/Medical/PII) + Replace — locked pending a library fix.
- Stamp/header-footer **"shrink to fit"** — locked pending a library fix.

### Multi-platform
- Mac/Linux desktop, Web portal, Android — after the shared UI + core stabilise; sequencing TBD.

---

## 6. Library dependencies & status (snapshot)

- **chuvadi-pdf** — all 15 asks closed; verified intact through **3.28**. Anjal embedding **A1–A4
  delivered** (3.27): password-required signal, sanitize/disarm, CancellationToken on render/extract,
  input limits. **Open:** A5 (written no-disk guarantee) and the A2 no-network clause.
- **Chuvadi.Sheets 1.2.0** — read-side style API, merged ranges, column/row dimensions shipped;
  building auto-fit + NumberFormatter display-text (SHEETS-FMT).
- **Chuvadi.Docs** — DOCS-01..04 (run fonts, style-inherited formatting, headers/footers on read,
  anchored images) still open; feed Chuvadi.Layout.
- **Chuvadi.Layout** — new; the linchpin for the Office viewer, Print, Anjal, LiPi export. Needs the
  read-fidelity asks under it and the display-list render entry (see requirements doc).
- **Open bug flagged:** library-authored **LiPi Sans Latin Latin-script text renders invisible**
  (raster + SVG) in 3.26–3.28 — matters for anything that authors with LiPi (Layout/Anjal/LiPi HIS).

---

## 7. Open items
- LiPi invisible-text render bug → raise with the PDF library chat (priority).
- Capital-spacing check still unverified — needs a real LiPi-rendered PDF (and 3.25 for the
  "matches 3.25" clause).
- Chuvadi.Layout: fix the **print-faithful tolerance bands** and expose a display-list render entry.
- Web portal privacy/compliance model (processing leaves the device).
