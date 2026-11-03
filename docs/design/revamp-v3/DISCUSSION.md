# Chuvadi Reader revamp: decisions and discussion log

This is the record of the design conversation: what was asked, what was decided and why, and what is still open. Read it with `README.md` (the build spec) and `Feature Audit.dc.html` (the feature checklist).

---

## Round 1 — "The design feels less friendly as features grow; I want a unique revamp"
**Review of today's build** (`Chuvadi Revamp.dc.html`, section 0). The identity is good; the structure is the problem.
1. Eight sidebar destinations, two of them duplicates: Docs and Sheets are file formats, not places.
2. About 25 unlabelled icons in one Reader toolbar row.
3. Fit, zoom, layout, rotate and hand appear three times: toolbar, View drawer and floating bar.
4. One document has three homes (Reader, Bench, Redact). Handing off between them loses your tab and position.
5. Mac traffic lights appear on the Windows build. *(Reversed in round 2 — see below.)*
6. Drawers and rows pile up: bookmarks, view, stamp, signature, find and the markup rail.
7. 124 px of chrome before the page, plus a 196 px rail.
8. The dashboard shows shipped features as "v2".

**Proposal**:
- No rail. Every format opens as a tab.
- Four modes per PDF: **Read · Mark · Arrange · Protect**.
- One floating leaf bar for viewing; one right-hand margin for whatever is selected.
- A page ribbon in which bookmarks appear as chapter dividers.
- Ctrl K command bar.
- Keep the manuscript identity: cream paper, binding red, Instrument Serif and the ச wordmark.

## Round 2 — the user's 7 questions
1. **Desks must stay visible together for drag and drop.** *Agreed.* Tabbed desks were dropped. Arrange shows every desk at once, with the 1 / 2 / 3-per-row toggle, a drop marker, a ghost card and a "new desk" drop tile.
2. **No sidebar + four verbs.** *Approved by the user.*
3. **Status bar with more information (signed-in user in future; kiosk use; show time).** *Agreed.* It shows offline status · user (or Guest · kiosk) · current activity · date · live clock, with an optional large kiosk clock.
4. **Traffic lights: right on Windows, left on Mac.** *Agreed; no objection.* They are part of the identity. Close sits at the far right on Windows; keep a 28 px hit area.
5. **Word/Excel: no editing, but convert to PDF; open all kinds, including protected, in exactly the original format.** *Agreed.* Read-only tabs with **Convert to PDF** as the primary action, an Excel **As printed** view, and an unlock card for protected files.
   - *Caveat:* the current `DocxViewService` re-creates text block by block, so it cannot be exact. Render Word and Excel through the convert-to-PDF pipeline and show that PDF.
   - *Caveat:* opening protected Office files needs support in the PDF library; confirm it exists.
6. **Home before/after.** Done (`v2` 2a/2b). The tools tile is removed; resume cards remember the mode you were in; Recent shows lock state; the Library gets a shelf preview.
7. **"I loved the Library — why remove it?"** *It was never removed.* In round 1 it was tucked into Home; it is now a permanent button beside the wordmark, with the bookshelf view intact.

## Round 3 — "Check every page; are all Bench features accounted for? What's missing?"
- *Admission:* round 1 had read only part of the code. A full read followed: 13 pages and 12 Bench dialogs, 129 features in all. The result is `Feature Audit.dc.html`.
- **Bugs found in the current code:**
  - `/merge` and `/split` cannot be reached.
  - The Reader's Properties, Export and Press dialogs exist but nothing opens them.
  - The `image` icon is blank because it isn't in LiPicons.
  - The dashboard shows shipped features as "v2".
  - Icons are reused for different things.
  - Settings' theme labels don't match the code.
- Every Bench feature now has a home in Arrange: desk header chips, the ⋯ menu, the page hover strip, shelf search / range / reorder / close, Session, Bind all / Combine and the selection bar.
- **Missing features**, ranked:
  - **Now:** annotations & comments, forms, print, kiosk, users/roles/audit.
  - **Next:** OCR, password, certificate signatures, compare, links/attachments.
  - **Later:** Tamil UI, accessibility & UI scale, Library batch actions, autosave & recovery, edit properties.

## Round 4 — mockups of every missing feature, phone/tablet, web/Mac/Linux, 5 themes, Settings
- **In development:** OCR, password-protect, certificate signatures and compare documents (marked in `Missing Features`).
- **Web is the same interface as desktop.** Only the frame differs:
  - **Windows:** reference build.
  - **macOS:** traffic lights left, menu bar in the system bar, ⌘ shortcuts.
  - **Linux:** like Windows, with softer corners, honouring the system's button layout.
  - **Web:** no window controls, "Install app", files stay in the browser.
- **5 themes:** Cream paper (default) · Night paper · Palm leaf · Indigo ink · High contrast. The PDF page stays white in all of them.
- **Phone:** the modes become a bottom bar and the margin becomes a sheet; desks stack and scroll sideways.
- **Tablet:** the desktop layout with touch-sized controls.
- iOS and Android variants are shown.

## Round 5 — "Prepare everything else, and a final set for the coding chat"
Added: Bench dialogs (8), Settings sections (8), states & feedback (10), iOS vs Android, a 6-step walkthrough, brand assets (icon + splash), and this handoff package (README, tokens.css, all files).

## Round 6 — roadmap documents (D-07 to D-10) brought in
The user attached `CHUVADI-READER-DESIGN-AND-ROADMAP.md` and `CHUVADI-READER-FEATURES-BY-PAGE.md` (now in `reference/`).
- **D-10 side rails** replace the mode bar and the floating leaf bar.
  - **Reasoning:** a portrait page on a landscape screen leaves about 30% empty gutter on each side, so the controls move there. The top shrinks to one line, and the page grows from about 74% to about 90% of the window height.
  - **Built in `Reader Side Rails.dc.html` (11a–i):**
    - The left bar keeps the modes plus global actions and expands on hover.
    - The Tools rail follows the mode; the View rail is a fixed set.
    - Settings cards float in the right gutter; drawers slide out for Pages/outline and the Shelf; a strip appears under the page on hover.
    - A narrow-window version is included.
    - Phase 3's library-ready features (Layers, Measurement, signature verification) live on the existing rails.
- **Platforms:** web portal (Blazor Server, Sangam sign-in, status bar shows that processing happens on the server), Android tablet (same rails at touch size) and Android phone (rails fold into a bottom bar, a tool strip and a View sheet). Built in `Portal & Android.dc.html` (12a–j).
- **Anjal Mail** was dropped from this project at the user's request; it has its own design session.
- **D-07 / D-08 / D-09** (Office view-only, the Chuvadi.Layout library, HTML/MD/TXT in the WebView) were noted in the README. No new screens were needed beyond the existing Word/Excel views.

---

## Open items / caveats
- **Icon and splash** are built from type and CSS. Redraw them as SVG/ICO in a vector tool.
- **Tamil mode labels** (படி · குறி · அடுக்கு · காப்பு) are my translation. Have a native speaker check them.
- **The `.chvd` bench-session extension** is only a suggestion; sessions save as `.json` today.
- **Library-dependent items** stay locked or flagged until the library supports them: regex redaction (#62), Replace redaction, shape primitives (#70), image alpha (#72), raster export of CFF fonts (LA-29), password API (LA-26).
- **Not designed yet:** dark-theme versions of the dialogs, phone versions of Settings and Library, and a full Library table view on phone.
- **Tablet mockups:** the old ones in `Chuvadi Mobile` are the desktop layout scaled down. The D-10 Android tablet in `Portal & Android` supersedes them.
- **`Chuvadi App.dc.html`** still uses the pre-D-10 mode bar. The clickable side-rail prototype is now the **live frame at the top of `Reader Side Rails`**, which supersedes it for layout. Chuvadi App remains the reference for Home, Library, Settings and the Word/Excel views.
- **HTML / Markdown / TXT viewers:** done (`Reader Side Rails` 11j–l).
