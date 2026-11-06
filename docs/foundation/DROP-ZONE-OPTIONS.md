# Home drop zone on the portal and on mobile — **decided C (D-89); built: `COMBINED-HOOKS-5.md`**

## What happens today
| Host | `Capabilities.DropFiles` | Click on the zone | Drop on the zone |
|---|---|---|---|
| Windows | true | Open dialog | Works: the WPF window takes the drop (`MainWindow.OnFileDrop`) |
| Mac / Linux | true | Open panel/dialog | Works where the host takes window drops (Mac: yes; Linux: host's job) |
| Web portal | **true** (set by the Web host) | Upload dialog | **Does nothing useful.** No code takes a browser drop, so outside the Reader the browser's default applies: Chrome, Edge and Firefox **open a dropped PDF in the tab, leaving the app** (standard browser behaviour; a real file drag can't be scripted in headless tests, so the Web chat should confirm it by hand) |
| Android tablet / phone | false | Open (SAF) | Nothing (Android has no window drag-and-drop in practice; files arrive by Share / Open with) |

On the phone, Mobile UI's `mobile.css` already hides the regular zone (`button.drop`). The first-run box ("Drop your first document here") still shows on the phone and on tablets, and both zones show on tablets.

The handout's Web notes (Revamp v3, 3j) say: **"Drag-and-drop and Ctrl K work the same."** So on the portal the design expects a drop to work.

## Options

| Option | What | Pros | Cons |
|---|---|---|---|
| **A. Ui drop handler through the platform layer** | Ui: while a file is dragged over the window, a full-window `<InputFile>` overlay (Blazor's own component; no new JavaScript library) catches the drop and hands the files to a new host seam, `IDroppedFiles.ReceiveAsync(IReadOnlyList<IBrowserFile>) → working paths`. The Web host streams each to the session's encrypted working copy (as its upload does), then the kit's router opens them (Bench shelf or one tab each). Rendered only when `ServerSide && DropFiles`, so desktop and Android never load it | Matches the design ("drag-and-drop work the same"). Fixes the "browser opens the PDF and leaves the app" trap. One Ui piece, one host seam; the Web chat already has the upload pipeline | New seam and Web host code. Uploads stream over the Blazor connection (the Web host sets a size limit; large files take as long as an upload does). Also needs a document-level `dragover`/`drop` guard so a near miss doesn't navigate away |
| **B. Hide or reword where drop isn't available** | Ui reads `DropFiles`. When false: the first-run box says "Open your first document" (phone: "Tap to open…"), and the regular zone says "Open a document" without "Drop". Web keeps its wording only if A is built | Tiny, no host code, honest everywhere. Desktop unchanged (`DropFiles` true) | On the portal it removes a designed interaction instead of providing it. Two wording variants to maintain |
| **C. A for the portal, B for Android (recommended)** | Build A, used only where `ServerSide`; apply B where `DropFiles` is false (Android). The Web host keeps `DropFiles = true` because it will be true | The portal gets what the design promises; Android stops promising what it can't do; Windows/Mac/Linux unchanged | The most work (still small): the A seam plus Web host code, and the B wording |
| D. Leave as is | — | No work | The portal drop opens the PDF in the browser tab and leaves Chuvadi; mobile promises a drop it can't do |

**Recommendation: C.** Until A lands, the Web host should set `DropFiles = false` so B's wording applies on the portal too. That is a one-line host change that removes the trap now.

Startup: A is rendered only on the portal (not on desktop or Android), and B is a string choice. Windows stays pixel-identical; the same proof will run.
