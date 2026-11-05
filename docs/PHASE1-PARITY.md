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
