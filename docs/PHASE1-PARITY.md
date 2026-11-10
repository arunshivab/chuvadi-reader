# Phase 1 — parity log & progress

Living record for the UI revamp. Rule #11: **no existing feature is lost.** Every change
is logged here as ADD, FIX, or MOVE — and MOVEs record old home → new home so nothing can
silently vanish.

Bench→Arrange decision (locked): **Option C** — Arrange is one shared workspace whose shelf
auto-loads all open-tab documents (current tab focused/expanded, others collapsed; sync is
additive; non-PDF tabs greyed until the Office→PDF converter lands; desks persist via the
existing bench session).

---

## Increment 1a — the 5 verified bugs  ✅ merged

| Bug | Fix | Removed? |
|-----|-----|----------|
| #1 `/merge` `/split` unreachable | Added Merge/Split Dashboard tiles (`→/merge`, `→/split`) | No — ADD |
| #2 Properties/Export never called | Added Properties + Export buttons to the Reader toolbar | No — ADD |
| #3 blank `image` icon | Reader add-image → `file-image`; Docs/Sheets open → `upload`; disabled Sign → `edit` | No — icon swap |
| #4 shipped features shown as "v2" | Redact → `/redact`; Compress → opens a PDF (Press in toolbar); Sign/OCR left as v2 (genuinely unshipped) | No — enabled |
| #5 (partial) reused icons | Dashboard Bench icon `library` → `grid-view` | No — icon swap |

---

## Increment 1b-i — mode bar + Document menu  ✅ merged

The shell takes shape: a **mode bar** (Read · Mark · Arrange · Protect) under the toolbar,
and a **Document menu** off the filename.

| Change | Detail | Parity |
|--------|--------|--------|
| ADD | **Mode bar** — Read · Mark · Arrange · Protect (keys shown 1–4; key-binding comes with ⌘K). Read/Mark drive the existing markup toggle. Arrange/Protect reuse the existing `OpenInBench` / `SendToRedact` hand-offs (navigate to Bench/Redact **until** they re-parent in 1c/1d). | pure ADD |
| ADD | **Document menu** off the filename + chevron: Properties · Export · Press · Flatten · Merge · Split. | pure ADD |
| MOVE | **Flatten · Press · Properties · Export** buttons: Reader toolbar → **Document menu**. Still reachable, same methods. | MOVE — toolbar → doc menu, nothing lost |

**Not touched yet (on purpose):** the view controls (zoom/fit/layout/rotate/hand/loupe) and
the Edit / Bench / Redact toolbar buttons still live in the toolbar — so for now the mode bar
and those buttons overlap. That's the intermediate state; **1b-ii** moves the view controls
into the single floating leaf bar and removes the duplicates, which is what actually slims the
top. Splitting it this way keeps the risky toolbar surgery separate from the mode structure.

**Files:** `Reader.razor`, `Reader.razor.css`.

**Test on Windows:** the mode bar appears under the toolbar; **Read ↔ Mark** switches the
markup tools on/off; **Arrange** opens Bench, **Protect** opens Redact; the **filename
chevron** opens the Document menu and every item works (Properties, Export, Press, Flatten,
Merge, Split). Toolbar still has its old buttons — expected until 1b-ii.

---

## Design handoff v3 adopted (2026-11-02)

The full revamp handoff (`PDF_Viewer_Design_Revamp_3`) is now the build spec. Its
**D-10 side rails supersede** the 1b-i mode bar and the planned floating leaf bar, so
**1b-ii is dropped** and the remaining work is re-sequenced below. Source of truth for the
chrome: `Reader Side Rails.dc.html`; for everything else: the handoff README, plus
`Feature Audit.dc.html` as the 129-feature Rule #11 checklist.

---

## Increment P0 — library 3.30 + 5-theme tokens

| Change | Detail | Parity |
|--------|--------|--------|
| FIX | chuvadi-pdf **3.18.1 → 3.30.0** (Core + Tests pins; `ci/packages` mirror swapped 33 × 3.18.1 → 34 × 3.30.0; the new 34th package is `Chuvadi.Pdf.Fonts.Bundled`, pulled in by Authoring). No API changes needed. Brings the library-ready features (layers, measurement, signature verification), the Anjal safety APIs, and font-name validation (`DrawText` with an unknown font now throws; the LiPi font name is `"Lipi"`). | no UI change |
| ADD | **`tokens.css`** from the handoff: 5 themes (Cream paper · Night paper · Palm leaf · Indigo ink · High contrast) + new tokens (`on-accent`, `danger`, `page`, `canvas`, highlights, `find-hit`, `scrim`, type scale, layout sizes, elevation). Radii grow slightly (4 → 6 px buttons). The four old `--chvd-shadow-soft/-medium/-deep/-rim` tokens are dropped; nothing used them. | look only |
| FIX | Windows `index.html` pre-load dark background matches the new Night paper (`#100b08`). | — |

Behaviour to expect from 3.30 (correct, not regressions): layers that are OFF in a file are now
hidden; `Warnings` can appear after a page renders. The three new themes are inert until the
Settings increment adds the picker.

---

## Increments P1–P7 — the D-10 shell, all modes, viewers, library features (one delivery)

Built in one pass on chuvadi-pdf 3.30.0, then moved to **3.30.1** and **3.30.2** (same 34 packages, no API changes). 3.30.2 fixes the text drawing that put find highlights one space off (embedded Type 1C fonts); re-checked on 3.30.2: `PdfSigner.Sign` still fails and `EncryptionOptions.Permissions` is still init-only. Full feature-by-feature check: **`docs/REVAMP-V3-PARITY-MATRIX.md`**
(121 audit rows: KEPT 79 · MOVED 40 · GREYED 1 · N/A 1 · **LOST 0**).

| Increment | What changed | Parity |
|---|---|---|
| **P1 shell** | New `ShellService` (Core). `MainLayout`: 40 px top bar (wordmark → Home · tabs · ‹ n / N › · Document ▾ · Ctrl K field · theme moon · window controls), 62 px left bar (Read · Mark · Arrange · Protect with 3 px marker, Open · Find · Library · Home, Settings), 26 px status bar (offline · user · activity · progress · version · live clock), messages bottom-right. Rails (`Components/Rails`), Pages & outline drawer, floating cards, hover strip. Fixed the `@onauxclick` crash (middle-click now via `mouseup`). | MOVE: old 8-item nav rail → left bar (Reader/Bench/Redact → modes; Docs/Sheets → tabs + Ctrl K). Reader toolbar, View drawer, 1b-i mode bar, `rfloat` → rails / View card / top bar. Document menu → top bar. |
| **P2 Mark** | Tools rail: Text, Image, Rectangle, Ellipse, Line, Arrow, Draw, Signature; Undo/Redo (new markup undo, 100 steps); Clear; Save; Save as new PDF. Selection settings → floating cards (Text box / shape / Image / Signature) with Duplicate + Delete (Duplicate is new). | MOVE: `mk-rail` bar → rail + cards. Highlight, Note, Fill form shown greyed (“Coming soon”). |
| **P3 Protect** | Protect lives in the Reader: Redact box, Find & redact (open the redaction view), Stamp + Watermark (card with live preview, Save / Save as new), Flatten, Password (new), Sign with certificate (new, invisible signature). Redaction view gets the same Protect rail + View rail. | MOVE: Reader stamp drawer → Protect card; Redact `rd-top` → shell; Save redacted → rail primary. |
| **P4 Arrange** | Bench keeps all behaviour; toolbar → Arrange Tools rail (Shelf, Fetch, Add desk, Blank page, Undo, Redo, Session…, Reset, To new desk, Compare, Combine, Bind all) + View rail (desks per row, shelf page size, collapse shelf). Option C: entering Arrange puts every open PDF tab on the shelf. | MOVE: `bench-bar` → rails. |
| **P5** | Home and Settings rebuilt (see agent notes in the matrix); 5 themes (Cream, Night, Palm, Indigo, High contrast) with live cards; UI scale 100/125/150 and Reduce motion (`PreferencesService`); Ctrl K command bar (every feature, greyed ones listed with a reason); mode keys 1–4; narrow-window layout (< 1000 px); messages after actions. | Settings items all kept; Home tools tile → modes / Document menu / Ctrl K. |
| **P6 viewers** | Word, Excel, Markdown, plain text and sandboxed HTML open as tabs (read-only; Mark/Arrange/Protect dimmed). Section/line/sheet counter in the top bar, outline drawer, Find, Copy all, Source view, text size, reading width, wrap. Convert to PDF greyed until the Layout library. | `/docs`, `/sheets` pages kept (Ctrl K). |
| **P7 library features** | Layers (view-only hiding), Measure (distance / angle / file scale; click two points), signature verification (banner + card), Print (native dialog, WPF). | ADD |

**Greyed — waiting on the PDF library (no app workarounds):** Sign with certificate (`PdfSigner.Sign` fails in 3.30.1) and the print / copy / change limits on Password (`EncryptionOptions.Permissions` can't be set). A password-protected copy itself works.

**Greyed until built:** Highlight, Note, Fill form, Peek, Attachments tab, Show in folder, Convert to PDF, Excel “As printed”, OCR (Ctrl K), Account & roles, Kiosk, Language, and the Settings rows listed as “Coming soon”.

**Library asks raised by this work** (for the chuvadi-pdf / Sheets teams): `PdfSigner.Sign` fails in 3.30 (“Could not locate /Contents…”), so signing uses `PdfCounterSigner`; no visible signature appearance; `EncryptionOptions.Permissions` is init-only; no “opened with owner password” flag; `OptionalContentGroup` lacks locked state and an id; Chuvadi.Sheets 1.1.2 fails on workbooks whose relationship targets are absolute (`/xl/worksheets/…`, as written by openpyxl and some exporters).

---

## Three decisions (2026-11-06)

Each option was set out with pros and cons and Arun chose:

| # | Question | Chosen | What changed |
|---|---|---|---|
| 1 | How Settings › Interface size (100 / 125 / 150 %) is applied | **B — whole-window zoom by the host** | New `IUiScale` (Core, `Window/IUiScale.cs`). The Windows app registers `WpfUiScale`, which sets the WebView2 `ZoomFactor`, so layout, breakpoints, viewport units and pointer positions all scale together. Hosts without it (tests, the preview, a future web portal) register `NoopUiScale` and keep the CSS `zoom` on `<html>` as the fallback (`data-ui-zoom="css"` turns on the `100vh` correction in `prefs.css`). The 44 px hit targets still follow `data-ui-scale`. Ctrl + wheel / pinch zoom stay off. |
| 2 | HTML viewer: what the app may do inside the sandboxed frame | **B — `sandbox="allow-same-origin"`, never `allow-scripts`** | Nothing in the page can run, submit, open windows or navigate (sanitizer + CSP unchanged). The app now reaches into the frame (`viewers.js` `frame*`): outline go-to, the § counter while scrolling, Find scrolls to the first match, keyboard shortcuts work with focus inside the page (Ctrl K, Ctrl F, Esc, 1–4). Every link click is stopped: `#` links scroll within the page; web and mail links show “Open … in your browser?” and open only on **Open** (new `IExternalLinks`; Windows: `WpfExternalLinks`, http / https / mailto only); anything else does nothing. Status reads “HTML · scripts off · …”. |
| 3 | Removing a password from a file whose permissions forbid changes | **C — keep refusing; say why** | Same rule (the library can't say whether the owner or the open password was used). The refusal now adds: “If you're the owner, you'll be able to remove it once the PDF library can tell which password opened the file.” The flag stays on the library ask list. |

Tests: 161 (was 149) — link allow-list, no-op hosts, the refusal text.

---

## chuvadi-pdf 3.31.1 (2026-11-06)

Moved from 3.30.2 to **3.31.1** (34 packages) plus a new dependency, **Chuvadi.Fonts 1.0.0** (LiPi Sans faces as embedded resources, pulled in by Authoring, Forms and Rendering.Raster). No app code changes were needed: build 0 warnings, 161 tests pass, and PDF pages render pixel-identical to 3.30.2 in the preview (cover and contents pages checked); Find highlights still line up.

What 3.31 adds (not used by the app yet): annotation appearances generated for comments and form fields that have none (`AnnotationMode` on rendering, SVG export and text extraction), the Trim / Bleed / Art page boxes, and a font-embedding report.

Re-checked library asks — **none fixed in 3.31.1**: `PdfSigner.Sign` still fails (“Could not locate /Contents value in the signed PDF output”), `EncryptionOptions.Permissions` is still init-only, and there is still no which-password-opened-the-file flag, outline position within a page, or `OptionalContentGroup` lock state / id. Those features stay greyed.

---

## chuvadi-pdf 3.32.0 — greyed-out features switched on (2026-11-10)

The library now supplies everything that was greyed out for it, so each feature below uses the library call directly (no app-side workaround anywhere):

| Feature | What changed | Library call |
|---|---|---|
| **Protect › Sign with certificate** | On. Signs a copy with a .pfx / .p12 (RSA or ECDSA); the copy is verified before it is kept; an already-signed file gets its next signature appended so earlier ones stay valid. Invisible for now — the visible signature box needs Arun's design first. | `PdfSigner.Sign`, `PdfCounterSigner.AddSignature` |
| **Protect › Password › Once opened, allow** | The Printing / Copying text / Changing the document switches work, and the optional permissions password is back. Reading for accessibility always stays allowed. | `EncryptionOptions.WithPermissions(PdfPermissions…)` |
| **Remove password** | The owner (permissions) password can always remove protection. The open password can when the file allows changes; otherwise the card says so and asks for the permissions password. | `EncryptionInfo.EffectiveFlags` |
| **Outline** | A bookmark lands on the position the file gives (its heading), not the top of the page — in continuous scrolling and when zoomed; the page-at-a-time layout shows the whole page as before. Turned pages and cropped pages are handled. | `OutlineItem.Destination` |
| **Layers** | Lock state and layer identity come from the library; layers that share a name are listed and switched separately. The app's own reading of `/Locked` is removed. | `OptionalContentGroup.IsLocked` / `.Id`, `LayerVisibilityById` |
| **Measure** | No change in behaviour; the note about viewports being rebuilt is gone (the library reads them once per page). | `PdfPage.Viewports` |

Checked with outside tools (testing only, never shipped): poppler `pdfsig` reports the app's signature valid and covering the whole document; `pypdf` opens a protected copy with the open password as *user* and the permissions password as *owner*, with exactly the chosen limits.

Also confirmed while here: comments and form fields that have no stored appearance are drawn (squares, highlights, free text, field values); `.xlsx` files with absolute part paths open on Chuvadi.Sheets 2.0.4; "Try recovery" already uses `PdfRepairer.Repair`.

Not in this change (each needs a decision or the library):
- **Visible signature box** — the library can draw it; placement design goes to Arun first.
- **Links inside a PDF page** — the Reader has never followed them (a click on a link does nothing). The library now gives every link's target (`LinkAnnotation.Destination`), so this can be built; it is a new feature and needs its own design.
- **Saving edited bookmarks keeps the page but not the position** — the library's outline writer takes a page only (`OutlineEntry`), so "Save bookmarks" writes page-top destinations for every bookmark, including ones the person didn't touch. Library ask PDF-17.
- **Text files without a byte-order mark** — shown as UTF-8 when valid, otherwise Latin-1. A file in another legacy encoding (most commonly Windows-1252: curly quotes, dashes, €) shows some characters wrong. Options are with Arun.

Tests: bookmark positions and same-named layers are new; the signing, permissions and remove-password tests now exercise the real feature.

---

## Roadmap (revised)
- **P1 — shell:** 40 px top bar (wordmark · tabs · `‹ 2 / 13 ›` · Document ▾ · Ctrl K ·
  window controls), fixed left bar (modes + Open/Find/Library/Home + Settings), Tools rail and
  View rail (Read), Pages & outline drawer, hover strip under the page, 26 px status bar.
  Removes the old nav rail, Reader toolbar, View drawer and the 1b-i mode bar — every control
  MOVEs to a rail.
- **P2 — Mark** on the Tools rail + floating settings cards.
- **P3 — Protect** = Redact re-parented + Stamp + Flatten + Password.
- **P4 — Arrange** = Bench re-parented (shelf drawer, desks on the canvas; Option C).
- **P5 — Home, Library, Settings** (sections + theme picker), Ctrl K, mode keys 1–4, narrow
  window, states & feedback.
- **P6 — Word/Excel tabs** in the shell; **HTML/MD/TXT** viewers (D-09).
- **P7 — library-ready features:** Layers, Measure, signature verification; Print.
- **Later:** web portal + Android, the Missing Features set, Office rendering via
  Chuvadi.Layout (D-08) when the library lands.

## Feature matrix
Done — see `docs/REVAMP-V3-PARITY-MATRIX.md`. Note: the README says 129 features; the
`Feature Audit.dc.html` data block actually holds 121 rows, all audited.
