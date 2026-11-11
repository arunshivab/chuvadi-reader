# Open questions from the Foundation (for the coordinator and Arun)

Updated 11 Nov 2026. Each question has its options, pros, cons and the Foundation's recommendation. Decide here; the
Foundation acts on the answer and removes the question when it is done.

---

## Q1. Who moves bookmark saving to the exact write-back (chuvadi-pdf 3.33.0, PDF-17)?

3.33.0 lets an untouched bookmark be written back exactly (destination, named destination, action, colour, style,
open or closed). The Reader's bookmark saving (`OutlineService`, last changed by the Windows Reader chat in its PR 15)
has to move to it. It must be done once.

| Option | Pros | Cons |
|---|---|---|
| **A. The Foundation does it, in the library patch (recommended)** | Lands with the library move, so main never has 3.33.0 with the old saving. One set of tests. It is shared Core, which is the Foundation's. | The Windows Reader chat wrote the current code and knows it best; it reviews the change. |
| B. The Windows Reader chat does it | The author of the current code changes it. | It needs the library patch merged first, so the exact write-back arrives one PR later, and main is on 3.33.0 with the old saving in between. |

**Recommendation: A.** Size: S (under half a day), plus the Windows Reader chat's review.

---

## Q2. `global.json` (D-178): which lines?

There is no `global.json` in the repo. Android writes one in its CI only (SDK 10.0.403, roll-forward disabled) and now
wants the workload version pinned too. Proposed, at the repo root:

```json
{
  "sdk": {
    "version": "10.0.403",
    "rollForward": "disable",
    "workloadVersion": "<the workload set Android installs>"
  }
}
```

**Needed from Android:** the exact workload set number. The Foundation will not guess it.

**For Arun: how strict?**

| Option | Pros | Cons |
|---|---|---|
| **A. `rollForward: disable` (recommended)** | Every build, on every machine and runner, uses the same SDK. This is what stopped Android's runtime drifting. | Arun's PC and every runner must have exactly 10.0.403 installed. Moving up is a deliberate one-line change in this file. |
| B. `rollForward: latestPatch` | A machine with a newer patch still builds. | Builds can differ between machines again, which is the fault D-178 closes. |

**Recommendation: A.** With A, the Foundation's own CI changes from "10.0.x" to reading this file. Size: S.

---

## Q3. Should marks that aren't saved yet survive a page reload? (asked by Web)

Today unsaved marks (text boxes, shapes, pictures, redaction boxes) live only in the open Reader page. A reload, or a
dropped connection on the Web portal, loses them. Nothing is written anywhere.

| Option | Pros | Cons | Cost |
|---|---|---|---|
| A. Leave as is | Nothing unsaved is ever stored. Simplest and most private. | A reload loses work, and on the Web portal a reload is easy to trigger. | None |
| **B. Keep them with the tab, in memory (recommended)** | Survives a reload on the Web portal once tabs belong to the session (D-174). Nothing touches disk. Gone when the session ends. Also keeps marks when the Reader page is left and returned to. | Does not survive closing the app or a server restart. Pictures added as marks stay in memory until the tab closes. | M: about two days, mostly moving state out of the page and re-proving Mark |
| C. Save drafts to storage | Survives closing the app and restarts, on every host. | Unsaved redaction boxes and added pictures would sit on disk or on the server. Needs rules for when drafts are deleted and a "restore your marks?" prompt. | L: four to five days |

**Recommendation: B.** It gives Web what it asked for at about half the cost and keeps unsaved redaction marks off disk.
C can follow later if drafts should outlive the app.

---

## Q4. Chuvadi.Fonts.Liberation with chuvadi-pdf 3.33.0 (Arun's call under the no-externals rule)

3.33.0 will not restore without Chuvadi.Fonts 1.1.0 and Chuvadi.Fonts.Liberation 1.0.0. The second is new: the
Liberation fonts 2.1.5 (12 font files, SIL OFL 1.1), about 4.4 MB in every install.

| Option | Pros | Cons |
|---|---|---|
| A. Accept it | The Reader moves to 3.33.0: exact bookmark write-back, damaged pages shown with a warning, Indic text in form fields copies correctly. Text set in Helvetica, Times or Courier that a file doesn't embed is drawn in metric-matched fonts. | Third-party font files in every install (inside one of Arun's own packages, under OFL, with a notices entry). About 4.4 MB more. Startup effect: see `PACKAGES-3.33.0-NOTES.md` when measured. |
| B. Stay on 3.32.0 until the library can do without it | Nothing third-party added. | No bookmark fix, no damaged-page warning, no Indic copy fix until then. |

**No recommendation from the Foundation on the rule itself.** Hashes the Foundation computed (to be confirmed against
the release records): Sheets 2.0.5 `338cfdafdcf79fb8247a73c6d2ef2732b01f17e06a6e38817a22dc39f7fd6ad2`,
Fonts 1.1.0 `e6f5ffa6061dbab3d472c100e1e91846b33af485d2d2cb7a956902834d1f742c`,
Fonts.Liberation 1.0.0 `6f4eb33e671dd8d9d5a2c2b4c238f333ac6b1216a8de3b8ad868a2a1d6540ba2`.
The work is on `platform/foundation-lib33`, not for merge.

---

# Embedding the Reader in Anjal Mail (for Arun's write-up; no code written)

The idea: a mail page opens an attachment in the viewer or the PDF editor inside the page, and the saved result goes
back as a reply attachment. Sizes: **S** under half a day, **M** one to two days, **L** three days or more.

## 1. Packaging Core + Ui + Hosting + Platform as a kit another Blazor Server app can host

**What is already there**
- The four projects are separate from every host. A host calls one registration (`AddChuvadiReader`) and supplies its
  own seams (file picker, write-back, storage, paths, clipboard and so on). The Web portal is a Blazor Server host
  built exactly this way, with one set of services per person (scoped), so "another Blazor Server app hosts it" is a
  path that already runs.
- The Ui is a Razor class library: its scripts, styles and fonts are served from its own content folder.

**What is missing**

| Item | Why | Size |
|---|---|---|
| Publish the four as packages (ids, versions, licence, notices carried inside) | Today they are project references in one repo; nothing is packed | S |
| A version and compatibility rule between the kit and its hosts | The seams change with combined patches; a second product needs to know what a kit version promises | S (a document and a version number), then discipline |
| The kit's own dependencies delivered to the host (chuvadi-pdf, Docs, Sheets, Fonts from a local feed, never nuget.org) | Anjal's build needs the same local feed and source mapping | S |
| A registration that doesn't take over the host (see section 2) | `AddChuvadiReader` registers everything, including Home, Library, recent files, settings, the watched folder | M |
| Tests that host the kit in a second, minimal app | Proves the kit works outside the Reader's own shell and keeps working | M |

**Total for packaging alone: about one week (L).** It gives a kit that runs as the whole Reader inside another app's
process. It does not yet give a panel inside a mail page; that is section 2.

## 2. What in the Ui assumes it owns the whole page

| Assumption | Where | What an embedded panel needs | Size |
|---|---|---|---|
| **Routes.** Every screen is a page at a fixed address (`/`, `/reader`, `/bench`, `/redact`, `/library`, `/settings`, …) and screens move between each other by address | Pages | The viewer and the editor as components a host places in its own page, with no address of their own; moves between Read, Mark, Arrange and Protect kept inside the panel | L |
| **The frame.** One layout draws the title bar with window buttons, the side rail, the tab strip, the status bar and Home | `MainLayout` | A panel layout: the document and its tools only. No title bar, no Home, no Library, no tab strip (or the host's own) | M |
| **The Reader page is one large component** (page, shell and state together, several thousand lines) that expects the tab service, the side rail and the command palette around it | `Reader.razor`, `Reader.Shell.cs` | Splitting the document view and Mark tools from the shell around them. This is the bulk of the work and the main risk | L |
| **The whole document element.** Theme, text scale and size class are set on the page's root element; styles use global names (`--chvd-…`, `.sb-…`, `.xv-…`) and assume the app fills the window height | Shell script and styles | Scope the theme and scale to the panel's own element; let the panel take the size its host gives it | M |
| **Keyboard.** Shortcuts (Ctrl+F, Ctrl+O, Ctrl+P, Ctrl+K, one-key Mark tools) listen on the whole page | Shell and reader scripts | Listen only while the panel has focus, and leave the host's shortcuts alone | M |
| **One set of tabs per person,** saved and brought back at the next start | `TabsService` | One document per panel, nothing saved or restored, closed when the panel closes (the dispose from patch 26 already closes documents) | S |
| **Its own storage:** preferences, recent files, pinned, tags, library, watched folder | Core services | Either off in a panel, or the host's to provide. A mail attachment should leave no trace in "recent files" | S to M |
| **Print, share, drag-and-drop, single instance, window state** | Host seams | Mostly "not available" answers from the host; print through the browser as the Web portal does | S |

**Total for an embedded panel: three to four weeks (L),** most of it in splitting the Reader page and scoping styles
and keys. A much smaller first step is possible and worth naming:

| Smaller step | What Anjal gets | What it does not get | Size |
|---|---|---|---|
| **View-only panel first** | The viewers (PDF pages, Word, Excel, Markdown, text, HTML) as a component in the mail page: open, scroll, zoom, find, copy | No Mark, Arrange or Protect; no saving back | L, about one to one and a half weeks. The Word, Excel, Markdown, text and HTML viewers are already separate components; the PDF page view is the part that has to be lifted out |
| **Open the full Reader in its own page of Anjal** (not inside the mail page) | Everything, today's Ui, with a "back to the mail" action and the result returned | It is a page change, not a panel | M, on top of section 1 |

## 3. Can a host hand in bytes and take the saved file back through `IFilePicker` and `IFileWriteBack` as they stand?

**Yes, without changing either, but through files, not bytes.**

- Both seams speak in **paths**. There is no call that takes or returns bytes or a stream.
- **Handing a document in:** the host writes the attachment's bytes to a file in its own working folder (the
  `IAppPaths` seam says where) and asks the Reader to open that path. The Web portal does this for uploads today.
- **Taking the result back:** when the person saves, the Reader asks `IFilePicker.PickSavePdfAsync` for a path. The
  host answers with a path in its working folder, without showing a dialog. After the Reader has written the file, it
  calls `IFileWriteBack.CommitAsync(path)`. The host reads the file there and attaches it to the reply. This is the
  same route the Web portal uses to turn a save into a download and Android uses to write back to a document provider.
- **What the host must take care of itself:** deleting the working files afterwards (attachments are private), and
  naming (the Reader shows the file name from the path, so the host should keep the attachment's name).

**Gaps worth closing if Anjal goes ahead**

| Gap | Why it matters | Size |
|---|---|---|
| No "this save is the answer" signal | `CommitAsync` fires for every write to a picked path (exports, pictures, split parts). The host has to tell the saved document from the rest by path | S: a small result callback for an embedded session |
| "Save" on a document that was opened by path saves in place | For an attachment the host wants "save" to mean "hand it back", never "overwrite my working copy silently" | S: a capability the host sets (`SaveInPlace` and `SavesAsDownload` already steer this) |
| Unsaved-changes prompt when the mail page closes the panel | The panel must be able to say "there are unsaved marks" before the host removes it | S |
| Bytes in memory only (no file at all) | Only if Anjal must never write attachments to disk, even briefly. Every reader and writer in Core works on paths today | L: a stream route through Core's services |

## Summary for the write-up

| Goal | Size | Depends on |
|---|---|---|
| The kit as packages another Blazor Server app can reference | about 1 week | — |
| The full Reader as its own page inside Anjal, document in and result out | about 1 week more | the packages |
| A view-only panel inside the mail page | about 1 to 1.5 weeks | the packages |
| The full editor as a panel inside the mail page | 3 to 4 weeks | the packages; best after Phase 1, since it splits the Reader page every platform uses |
| No files on disk at all | 1 week or more | a decision that it is required |

**The Foundation's view:** the cheapest useful route is the packages, then the full Reader as its own page in Anjal
with the attachment handed in and the result handed back. It reuses what the Web portal already proves. The panel inside
the mail page is real work on the shared Ui and should be planned as its own piece, not squeezed into Phase 1.
