# Phase 1 — parity log & progress

Living record for the UI revamp. Rule #11: **no existing feature is lost.** Every change
is logged here as either ADD, FIX, or MOVE — and MOVEs record the old home → new home so
nothing can silently vanish.

Bench→Arrange decision (locked): **Option C** — Arrange is one shared workspace whose
shelf auto-loads all open-tab documents (current tab focused/expanded, others collapsed;
sync is additive; non-PDF tabs greyed until the Office→PDF converter lands; desks persist
via the existing bench session).

---

## Increment 1a — the 5 verified bugs  ✅ (built clean, 52/52 tests)

| Bug | What was wrong | Fix | Removed anything? |
|-----|----------------|-----|-------------------|
| #1 | `/merge` and `/split` had routes but **no way to reach them** | Added **Merge** and **Split** tiles to the Dashboard tools (`→/merge`, `→/split`). (Also folding into Arrange + ⌘K in 1b.) | No — pure ADD |
| #2 | Reader `ShowProperties` / `ShowExport` existed but **nothing called them** | Added **Properties** (`info`) and **Export** (`download`) buttons to the Reader toolbar, next to Press. (They move into the Document menu in 1b.) | No — pure ADD |
| #3 | `<Icon Name="image">` isn't in LiPicons → **blank icons** in the Reader image tool and the Docs/Sheets "Open" buttons | Reader add-image tool → `file-image`; Docs/Sheets "Open file" → `upload`. Also found `sign` is blank → swapped the (disabled) Sign tile to `edit`. | No — icon swap only |
| #4 | Dashboard showed **shipped** features as disabled "v2" | **Redact** → enabled (`→/redact`). **Compress** → enabled (opens a PDF; Press lives in the Reader toolbar). **Sign / OCR** kept as "v2" — they genuinely aren't shipped (no route), so that label is accurate. | No — enabled existing |
| #5 (partial) | Icons reused for different things | Dashboard **Bench** tile shared the `library` icon with Library → changed Bench to `grid-view`. | No — icon swap only |

**Files touched:** `Dashboard.razor`, `Reader.razor`, `Docs.razor`, `Sheets.razor`.
**Parity:** increment 1a only ADDs and FIXES — nothing removed.

**Still open from bug #5 (deferred, not forgotten):** a full icon-name audit across the app
(e.g. the Docs empty-state uses `Name="reader"`, the tools tile `Name="tools"` — unverified
against the manifest), and the Settings theme-label mismatch. These land in Phase 2 (visual
pass) with the token reconciliation, so they're done once, properly.

---

## Roadmap (each is its own testable delta → you verify on Windows → next)

- **1b — the shell:** tab + mode bar (Read · Mark · Arrange · Protect), the single floating
  leaf bar (removing the duplicate toolbar/drawer view controls), the Document menu
  (Properties/Export/Press/Flatten/Password + merge/split), and ⌘K. Wraps the *existing*
  Read + Mark; Arrange/Protect appear as modes that (for now) host the current Bench/Redact.
- **1c — Arrange:** re-parent Bench as the Arrange mode per decision C (shelf = open tabs).
- **1d — Protect:** re-parent Redact + Stamp + Flatten + Password as the Protect mode.
- **Phase 2 — visual:** `tokens.css` (with the 5-token reconciliation), 5 themes,
  Home/Library/Settings polish, status bar, the deferred icon/label audit.
- **Phase 3 — new features:** library-gated (Office→PDF, signature verification, OCR,
  layers, measurement, …) — built as the library delivers each.

## Full 129-feature matrix
Built incrementally from `Feature Audit.dc.html` as each shell increment lands — every
feature ticked off against its new home before that increment is called done. (1a touched
no existing feature's home, so there's nothing to re-home yet.)
