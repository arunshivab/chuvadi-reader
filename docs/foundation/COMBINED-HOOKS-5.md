# Fifth combined patch — on `platform/foundation` (7 Nov)

| Item | From | Where | Desktop |
|---|---|---|---|
| **D-89 A — portal drag-and-drop.** New seam `IDroppedFiles` (Platform; kit default `NoDroppedFiles`): `ReceiveAsync(files)` with name, size and a stream per file, plus `MaxFileBytes`. The Ui's `PortalDropCatcher` (an invisible full-window `InputFile`, Blazor's own component; small script `portal-drop.js`) switches on only while documents are dragged over the window. Image-only drags pass through, so the Reader still places a dropped image on a page. Any other drop is stopped, so the browser never opens the file and leaves the app. Failures show as error toasts | Coordinator (D-89) | `Services.cs`, kit registration, `Portal/PortalDropCatcher.razor(+.css)`, `wwwroot/js/portal-drop.js`, `MainLayout` (rendered only when portal and `DropFiles`) | Never rendered |
| **D-89 B — wording where a drop isn't possible** (`DropFiles` false: Android, and the portal until its catcher is on). First-run box: "Open your first document" (portal: "Upload your first document"). Drop zone: "Open a document" (portal: "Click to upload a document"). Tooltip: "Choose a document to open" (portal: "…to upload") | Coordinator (D-89) | `Dashboard.razor` (`CanDrop`) | Unchanged text |
| **Fix: Windows single-instance mutex** — the running copy creates the named mutex and never acquires it. Acquiring ties it to a thread: re-entrant on that thread, abandoned when the thread ends (e.g. a pool thread under `ClaimAsync`). Found by the first Windows run of the tests (4 failures there) | Foundation | `DesktopSingleInstance.cs` | Same behaviour, correct in every case |

## Proof
- Windows pixel-identical: 9 screens × 3 scales, before `e5802e6` vs after — **27/27 identical**.
- Portal (preview with a stand-in `IDroppedFiles` that stores the file and opens it through `IncomingFiles`, as the Web host will):
  1. catcher idle when nothing is dragged;
  2. on when a PDF is dragged in;
  3. off when the drag leaves;
  4. off for an image-only drag;
  5. a PDF dropped elsewhere has the browser's default stopped;
  6. a dropped PDF opens in the Reader as a tab with its name;
  7. a dropped `.exe` shows "Couldn't upload setup.exe: Chuvadi doesn't open this kind of file." as an error. No page errors.
- B wording checked on the portal (catcher on and off), the Android tablet and the Android phone layouts.
- Mutex fix: named-mutex semantics checked here (creator first; the same thread and other threads see it exists; recreated after all handles close). The Windows tests need one Windows run (manual "full" once D-90 is decided).
- Build 0 warnings; tests 265/265; startup (ReadyToRun) 187.9 → 188.1 ms, 353 → 358 methods JIT-compiled at startup.

## For the Web chat
Register an `IDroppedFiles`. In `ReceiveAsync`:
- read each `OpenRead(ct)` stream once, inside the call;
- store it as the session's working copy (as your upload does);
- refuse what you don't take, with one short message each (size limit, type);
- call `IncomingFiles.Receive(paths)`.

Then set `DropFiles = true`. Until then keep `DropFiles = false`, so the portal shows B's wording. Blazor's limit on the browser-to-server stream (`MaximumReceiveMessageSize` doesn't apply to `InputFile` streams, but your `MaxFileBytes` does) is your setting.
