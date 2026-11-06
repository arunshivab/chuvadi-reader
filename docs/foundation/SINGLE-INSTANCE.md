# Single instance (tabbed) — decided W-A + L-A + F-1 (coordinator, 7 Nov); Windows side and kit built

**Goal:** opening a file while Chuvadi runs hands it to the running copy as a new tab, on every desktop platform. **Rule:** the hand-off must not slow the first launch.

## What happens today
- **Windows ignores the file it is opened with.** `App.OnStartup` never reads `e.Args`, so "Open with Chuvadi" or a double-click starts a new, empty copy. A drop of several files opens only the first (`MainWindow.OnFileDrop`: `supported[0]`).
- **Mac:** already single instance (LaunchServices hands the file to the running app; `MacIncomingFiles`, `platform/mac` bf9651c).
- **Android:** `LaunchMode.SingleTask`; a new file arrives as a new intent in the running activity.
- **Linux:** a second copy starts (plain `gtk_init`, no `GtkApplication`).
- **Web:** not applicable (each browser tab is its own session).

## Contract and shared pieces

```csharp
// ChuvadiReader.Platform — the running copy's side.
public interface IAppInstance
{
    /// Files another launch handed over. Raised on a background thread; the kit's router marshals.
    event Action<IReadOnlyList<string>>? FilesReceived;
}
```

In the kit (`ChuvadiReader.Hosting`, BCL only):
1. **`DesktopSingleInstance.Claim(args)`**, called first thing in `Main`/`OnStartup`, before DI and before any window. It returns `Primary`, or `HandedOver`, in which case the process exits at once without showing a window.
2. **`StartListening()`**, called after the first paint (`IAppReadySignal.Ready`), on a background thread. The listening part loads the sockets/pipes assembly (≈12 ms measured), so it stays off the critical path. A second copy that arrives earlier waits up to 3 s for it.
3. **`IncomingFiles`**: one router for every way a file arrives (command line, hand-off, drop, Mac open-URL, Android intent). It is a generalised `MacIncomingFiles`:
   - It holds files until the UI is ready.
   - It keeps only readable, openable files (`DocumentFormatExtensions.IsOpenable`).
   - It sends them to the Bench shelf when the Bench is open, otherwise one Reader tab each, in order (the F-3 queue makes this safe).
   - It brings the window forward.

## Options per host

### Windows (I build this side once you pick)
| Option | How | Pros | Cons |
|---|---|---|---|
| **W-A (recommended)** | Election by a **named mutex** (`Local\Chuvadi.Reader.<user SID hash>`, one per user per session). Hand-off over a **named pipe** with the same name, current user only (`PipeOptions.CurrentUserOnly`), created after the first paint | The mutex lives in the core library: microseconds, no extra assembly at startup. The OS frees it if Chuvadi crashes (abandoned mutex = taken over), so there are no stale locks. The pipe is per user, so other users can't send paths. Pure .NET | Two mechanisms (mutex + pipe), about 150 lines |
| W-B | Pipe only: creating the server with `FirstPipeInstance` is the election | One mechanism | Loads `System.IO.Pipes` before the window (a few ms on every launch) |
| W-C | `WM_COPYDATA` to the main window (`FindWindow`/`SendMessageTimeout`) | Frozen-window detection built in (the timeout) | Needs the window to exist (race at startup); window-title matching is fragile; P/Invoke; files during the splash need extra handling |
| W-D | VB's `WindowsFormsApplicationBase.IsSingleInstance` | Ready-made | Pulls in WinForms and VB assemblies at startup (slower), takes over the WPF startup model, no control over the frozen case |

### Linux (Linux chat builds; the kit supplies the shared part)
| Option | How | Pros | Cons |
|---|---|---|---|
| **L-A (recommended)** | The kit's Unix path: election by an exclusive **lock file** in `$XDG_RUNTIME_DIR` (the kernel releases it if the process dies). The winner deletes any stale socket and listens on a **Unix socket** there after the first paint | Same kit code and protocol as Windows. Measured here: crash recovery works; election 4.3 ms in a cold test process (less inside the app, where file I/O is already loaded); the 12 ms socket set-up happens after the paint | On Wayland, raising the running window needs the launcher's activation token (`XDG_ACTIVATION_TOKEN`): the second copy forwards it and the Linux host passes it to GTK. A small host addition |
| L-B | Switch the native host to `GtkApplication` with `G_APPLICATION_HANDLES_OPEN` (D-Bus) | Desktop-standard; the activation token and window raising are handled by GTK | Reworks the Linux host's startup; needs a D-Bus session bus (missing in some kiosk set-ups); a different code path from Windows |
| ✗ | .NET named pipes as on Windows | — | **Tested and rejected:** on Linux a pipe is a socket file left behind after a crash. Every later launch then believes a copy is running and loses 500 ms timing out |

### Mac and Android
Nothing new: both are single-instance natively. They can route through the kit's `IncomingFiles` so every platform behaves the same (optional for the Mac chat, which already does this).

## If the running copy is frozen
The listener runs on its own thread, so it answers even when the UI thread is stuck. It acknowledges a file only after the UI thread has taken it.

| Option | Behaviour | Pros | Cons |
|---|---|---|---|
| **F-1 (recommended)** | The new copy waits up to **1.5 s** for the acknowledgement, then opens the file in its own window, as today. The frozen copy gives up its own attempt at 1.0 s and drops the file, so it can never open it a second time if it recovers | No new UI (the design is final); the file always opens; no duplicate tab | A 1.5 s delay in that rare case; two windows until the frozen one is closed |
| F-2 | A message: "Chuvadi isn't responding — Open in a new window / Cancel" | Tells the person what happened | A new dialog that isn't in the design |
| F-3 | Keep waiting | — | The file may never open |

Tested on Linux with a deliberately frozen primary: the hand-off normally completes in 31 ms; when frozen, the new copy gave up at 1.5 s and could open its own window.

## Startup speed (the rule)
- First launch, critical path: one mutex (Windows) or one lock file (Linux) before the window. The pipe or socket starts after the first paint. Target: under 1 ms added; it will be measured in the app with the startup probe before and after, as before.
- A launch that hands over exits before building DI or any window, so it is faster than a launch is today.

## Safety
- Per user (Windows pipe ACL is current-user only; Linux `$XDG_RUNTIME_DIR` is 0700).
- Paths are resolved to full paths by the sender. The receiver accepts at most 64 files and 64 KB per message, and only existing, openable files.
- The protocol is a length-prefixed UTF-8 list of paths plus a one-byte acknowledgement, with a version byte first so the format can change later.

## Behaviour changes for Arun's list
1. Windows opens the file it was started with (today it ignores it). A bug fix.
2. A second launch hands its file to the running copy as a new tab (the approved feature).
3. Dropping several files opens each in its own tab (today only the first). Recommended for consistency; it can stay first-file-only if preferred.


---

## Built (7 Nov)

### Kit (shared by every host)
| Piece | Where | What |
|---|---|---|
| `IAppInstance` | `ChuvadiReader.Platform/AppInstance.cs` | `FilesReceived` (UI thread; an empty list means "come to the front"). Kit default `NoAppInstance` |
| `IncomingFiles` | `ChuvadiReader.Hosting/IncomingFiles.cs` (registered by the kit) | One router for command line, hand-over, drop, macOS open-file and Android intents: only existing, openable files; Bench shelf when the Bench is open, else one Reader tab each, in order; holds files until the first render (`IAppReadySignal.IsReady`, new); `Delivered` event → bring the window forward |
| `DesktopSingleInstance` | `ChuvadiReader.Hosting/Desktop/DesktopSingleInstance.cs` | `Claim(args)` → `Primary` / `HandedOver` / `Standalone`; `ClaimAsync` (same, on a background thread); `StartListening(runOnUi)` after the first paint; `ActivationToken` (Wayland) |
| `InstanceChannel` | `…/Desktop/InstanceChannel.cs` | Windows named pipe (current user only) / Unix socket; versioned, length-prefixed message (≤ 64 files, ≤ 64 KB, full paths only, + activation token); reply byte. Loaded only when a hand-over happens or the running copy starts listening |

### Windows (built)
- `App.OnStartup`: `Claim(e.Args, beforeHandOff: AllowSetForegroundWindow(any))` first; `HandedOver` → `Shutdown()` before building anything. Registers the instance as `IAppInstance`; routes `e.Args` (**Windows now opens the file it is started with**); listens after the first render; `Delivered` restores and activates the window.
- `RunOnUi`: `Dispatcher.InvokeAsync`, waits `UiWait` (1.0 s), aborts if not started — so a frozen UI never takes the files late (F-1).
- `MainWindow.OnFileDrop`: through the router — **every dropped file opens in its own tab** (approved).

### Tests (13, `SingleInstanceTests`)
Hand-over; a launch while the running copy is still starting (waits up to 3 s); a frozen copy (new launch goes `Standalone` after ~1.5 s, the frozen copy never takes the file); the next launch becomes primary after the running copy ends; no running copy → primary at once; background claim; malformed messages refused (6 cases); round trip with a Tamil file name and a token; the router (waits for the first render, filters, order; Bench; empty hand-over). They run the **real** Windows path on Windows runners and the real Unix path on Linux/macOS runners. Here (Linux): 48/48 kit tests, stable over 5 repeats. **The Windows path has compiled but not run yet** — CI is stopped (GitHub billing); Arun's Windows checks 20–23 cover it.

### Startup cost (first launch)
- Windows: one named mutex in the core library; no pipe code loaded until after the first paint. To be confirmed on Windows with `scripts/measure-startup.ps1`.
- Linux (measured here, cold process, ReadyToRun): the claim costs **1–3.5 ms** (median ~3 ms; this sandbox is noisy). It was ~7.7 ms in the first version: culture-aware formatting, `string.Create<T>` (JIT-compiled) and the user database lookup were removed. The rest is the first native file open + lock. **Linux: use `ClaimAsync`** at the top of `Main` and await it just before showing the window — it overlaps the host's own start-up, so nothing is added to the critical path.

## For the Linux chat
```csharp
// Program.Main, first line: the claim runs on its own thread while the host starts GTK etc.
var claim = DesktopSingleInstance.ClaimAsync(args);
// … host start-up that doesn't need services …
var instance = await claim;               // before building services and showing the window
if (instance.Role == InstanceRole.HandedOver) return 0;
services.AddSingleton<IAppInstance>(instance);
// … build the provider, create the window …
var incoming = provider.GetRequiredService<IncomingFiles>();
incoming.Delivered += () => { /* present the window; pass instance.ActivationToken to GTK (xdg-activation) */ };
incoming.Receive(args);
ready.Ready += () => instance.StartListening(RunOnGtkMainLoop);
// RunOnGtkMainLoop(action, wait): g_main_context_invoke the action, wait up to `wait`; if it hasn't
// started by then, cancel it so it never runs, and return false.
// On exit: instance.Dispose().
```
- Delete `LinuxAppPaths` (kit now has the same fallback) and route drops through `IncomingFiles`.

## For the Mac chat
- Nothing to elect (LaunchServices). Optionally replace `MacIncomingFiles` with the kit's `IncomingFiles`: raise `FilesReceived` from a small `IAppInstance` fed by `SceneOpenUrl`/`OpenUrls`, or call `IncomingFiles.Receive(paths)` directly — same rules (Bench / one tab each / wait for first render).

## For the Android chat
- `singleTask` already delivers new intents; call `IncomingFiles.Receive(localPaths)` from `DocumentImport` (or keep its own routing — the rules are the same).
