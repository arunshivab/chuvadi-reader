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

## Increment 1b-i — mode bar + Document menu  ✅ (built clean, 52/52)

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

## Roadmap
- **1b-ii:** single floating leaf bar — move all view controls there, remove the toolbar +
  View-drawer duplicates (the triplication fix). This is the big toolbar surgery.
- **1b-iii:** ⌘K command bar + the 1–4 mode keys.
- **1c:** Arrange = re-parent Bench (decision C — shelf = open tabs).
- **1d:** Protect = re-parent Redact + Stamp + Flatten + Password.
- **Phase 2:** visual — `tokens.css` (+ the 5-token reconciliation), themes, Home/Library/
  Settings polish, the deferred icon/label audit.
- **Phase 3:** library-gated features (Office→PDF, signature verification, OCR, layers,
  measurement) as the library delivers each.

## 129-feature matrix
Built from `Feature Audit.dc.html` as the re-parenting increments (1c/1d) land — that's where
features actually change home. 1a/1b-i only added and relocated-within-reach, so there is
nothing re-homed to tick yet.
