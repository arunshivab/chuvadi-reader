# Foundation — inventory of host-specific code in Core and Ui

Checked against chuvadi-reader `main` **c7b9e44** (6 Nov 2026), which already carries chuvadi-pdf 3.31.1 + Chuvadi.Fonts 1.0.0.
Method: full read of the host seams and the WPF host; pattern sweep of `ChuvadiReader.Core`, `ChuvadiReader.Ui` (C#, Razor, JS, CSS); build and test of Core + Ui + tests on Linux x64 (.NET SDK 10.0.403): **0 warnings, 161/161 tests pass**. Mac, Android and a real Blazor Server run have not been tried yet — rows marked *verify* need a device run.

Legend for "Breaks on": **M** macOS (WKWebView) · **L** Linux (WebKitGTK) · **A** Android (Android WebView) · **W** Web (Blazor Server, many users).

## 1. What is already in good shape

- Core and Ui are plain `net10.0`, with no `System.Windows`, WPF, WinRT, registry or P/Invoke. All of that lives in `ChuvadiReader.Windows`.
- The app already talks to the host through interfaces registered by the host: `IFilePicker`, `IPrintService`, `IImageClipboard`, `IWindowControls`, `IUiScale`, `IExternalLinks`, `IAppStorage`, `IAppReadySignal` (+ `IEntitlements`). Most of the platform layer is "implement these per host".
- No mutable static state in Core or Ui (only computed read-only statics), so nothing leaks between web users through statics.
- Fonts are self-hosted in `wwwroot` (LiPi Sans, and Instrument Serif / Fraunces / Tiro Tamil / IBM Plex Mono under `fonts/google`) — no CDN, works offline everywhere.
- HTML viewer frame is `sandbox="allow-same-origin"` + `srcdoc`, no scripts (`viewers.js:163`) — supported by every engine.

## 2. Inventory

### 2.1 Paths and the file model (the main cross-platform issue)

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| F1 | `Core/Reader/IFilePicker.cs` (whole interface) | Every picker returns a **local path** | A, W | Android pickers give content URIs; a web server never sees the client's paths. |
| F2 | `Core/Reader/IPrintService.cs:59` | `PrintAsync(string pdfPath, …)` | A, W | Same: needs a path the host can read. |
| F3 | `Windows/MainWindow.xaml.cs:57-87` → `OpenDocumentService.Request(path)`, `BenchDropService.Drop(paths)` | Dropped files enter as paths | A, W | Web drops arrive as browser `File` bytes; Android as URIs / share intents. |
| F4 | 7 × `PickDocumentAsync`, 2 × `PickDocumentsAsync`, 3 × `PickImagesAsync`, 1 × `PickOpenSessionAsync`, 1 × `PickCertificateAsync` in Ui (MainLayout.razor:480, Dashboard.razor:403, Docs.razor:48, Reader.razor:3333, Redact.razor:357, Sheets.razor:48, Split.razor:152, Bench.razor:1336/1359/1519, Merge.razor:76, Reader.razor:1574/2689, Reader.Shell.cs:736) | Open flows | — | Work unchanged if the host returns a readable local path (option C). |
| F5 | 12 × `PickSavePdfAsync` (PasswordCard.razor:5, Bench.razor:2048/2055/2078/2664, Merge.razor:128, Reader.Shell.cs:770, Reader.razor:1768/3025/3140, Redact.razor:563, Split.razor:229), `PickSaveImageAsync` (Reader.razor:1881), `PickSaveSessionAsync` (Bench.razor:1497) | Save-as flows write to the returned path | A, W | Need a **write-back** after the write: Android → content URI, Web → per-user storage / browser download. |
| F6 | In-place saves: Reader.razor:1802-1808, 2197-2203, 2988-2994 (`File.Replace` / `Move` / `Copy`) | "Save" overwrites the opened file | A, W | Same write-back need. |
| F7 | 13 × `PickFolderAsync` (LibrarySection.razor:75/86, SavingSection.razor:48, Bench.razor:2062/2617/2656/2730, Dashboard.razor:439/448, Library.razor:376, Reader.razor:1251/1302, Split.razor:200) | Output folders, watched folders, Library roots | W (no folders), A (SAF tree URIs only) | Web: no folder concept → capability off; output-to-folder becomes a write-back of the folder. |
| F8 | `Core/Documents/LibraryService.cs:31,151` | `FileSystemWatcher` | W | Meaningless on a server; capability `WatchFolders`. |
| F9 | `Ui/Layout/MainLayout.razor:353` | `File.Exists(t.Path)` on tabs | — | Fine with working paths. |

### 2.2 Folders, temp files, user name

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| P1 | `Core/Reader/SessionService.cs:46`, `Ui/Components/Settings/AppInfo.cs:46` | `SpecialFolder.ApplicationData` + "Chuvadi" | M (wrong place: .NET gives `~/.config` on macOS, not `~/Library/Application Support`), W (one folder for **all** users) | Route through `IAppPaths`. Windows value unchanged. |
| P2 | `Core/Documents/SaveFolderService.cs:31` | `SpecialFolder.MyDocuments` default save folder | A (empty/odd), W (server's folder) | `IAppPaths.DefaultSaveFolder`. |
| P3 | `Core/Documents/UserProfileService.cs:39` | `Environment.UserName` for the user chip | A (app uid), W (server account) | `IAccount.DisplayName` (Sangam on Web). |
| P4 | Temp files: StampService.cs:30,63; PressService.cs:85; BenchService.cs:1134-1135, 1160-1161, 1367, 1427; BenchComposer.cs:150, 159, 241, 388, 456, 491, 549; Ui Reader.razor:1191, 1934, 2191; Bench.razor:1607 | `Path.GetTempPath()` / `GetTempFileName()` | W | Works everywhere technically, but on Web every user's intermediate PDFs land in one shared server temp folder. Needs a per-user `IAppPaths.WorkingFolder` (and cleanup) before Web goes live. |
| P5 | `Ui/Pages/Reader.razor:1802`, 2197, 2988 | `File.Replace(temp-in-/tmp, target)` | L, M *verify* | Across file systems (`/tmp` is often tmpfs) `File.Replace` throws; the existing `catch` falls back to Copy+Delete, so it works but loses atomicity. Same on Windows across drives today. |

### 2.3 Window chrome, form factor, wording

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| C1 | `Ui/Layout/MainLayout.razor:123-127` | Window buttons always drawn, at the right | M (must be left, native), A, W (must not exist; Web gets the account menu) | Needs `IPlatformInfo.Chrome`. |
| C2 | `Ui/Layout/MainLayout.razor:645-661` | Title-bar press → `BeginDrag`, double-press → maximise | A, W (no-ops already via Noop, but the 350 ms double-press logic still runs) | Gate on `Chrome.TitleBarDrag`. |
| C3 | `Ui/Layout/MainLayout.razor:163` | Status text "offline · on this device" | W | Design 12b: "processed on the server · encrypted at rest · only you can see it". |
| C4 | `Ui/Layout/MainLayout.razor:165` | User chip falls back to "This computer" | A, W | `IAccount`. |
| C5 | `Ui/Layout/MainLayout.razor:205-210` + left bar markup | Desktop left bar only | A phone | Design 12g-12j: bottom bar, tool strip, View sheet. Not in the Ui yet (DESIGN-NOTE "Gaps"). |
| C6 | All rails | Desktop sizes 62/64/54 px | A tablet | Design 12d-12f: 72/72/60 px, 48 px targets. |
| C7 | ~50 hard-coded "Ctrl …" labels: Reader.Shell.cs (9), Reader.razor (11), MainLayout.razor (7), ShortcutsSection.razor (20), Bench.razor (2), ReadingSection.razor (1), Dashboard.razor:45 | Shortcut text | M | Design: "⌘ K on Mac". Needs `IKeyLabels.Format`. |
| C8 | `Ui/Components/Settings/AboutSection.razor:14`, `Settings.razor:37`, `LibrarySection.razor:50` | "nothing leaves this device" / "Stored on this device only" | W | Untrue on the portal. Copy for Web needs Arun's wording. |
| C9 | Left bar "Open" (folder icon) | — | W | Design 12b uses `upload` for Web, `folder` for tablet. |
| C10 | `Ui/Pages/Reader.Shell.cs:424` | "Show in folder" stubbed "Coming soon" | — | New `IFileManager.RevealAsync` makes it possible on desktop. Wiring it is a behaviour change → listed. |

### 2.4 Keyboard, pointer, gestures (JS)

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| K1 | `Ui/wwwroot/js/reader.js:98, 106, 117` | Reader shortcuts require `ctrlKey && !metaKey`, and bail out on `metaKey` | M | **Bug on Mac:** ⌘W, ⌘Z, ⌘F, ⌘+/−/0, ⌘1-9, ⌘Tab-cycle do nothing (and ⌘W would close the window). `shell.js:16,41` and `bench.js:59` already accept ⌘. |
| K2 | `Ui/wwwroot/js/reader.js:247-258` | Trackpad pinch zoom assumed to arrive as `wheel` + `ctrlKey` (Chromium behaviour) | M, L *verify* | WebKit delivers trackpad pinch as `gesturestart/gesturechange` on macOS; WebKitGTK zooms the whole page unless the host disables it. Likely needs a gesture handler in reader.js (edit) plus host settings. |
| K3 | `Ui/wwwroot/js/reader.js:313-382` | Touch pinch + double-tap on the desk only | A | Good. Host must disable WebView page zoom (Windows does this in MainWindow.xaml.cs:142-143); hosts' `index.html` should use `user-scalable=no`. |
| K4 | `Ui/wwwroot/js/reader.js` (tools) | No pen/finger split | A tablet | Design 12e: "Pen draws, finger pans" (`pointerType`). Edit to reader.js. |
| K5 | `Ui/wwwroot/js/viewers.js:282-288` | Frame keys forwarded with ctrl/meta | — | Already handles ⌘. |
| K6 | Android back button | Not handled | A | `IAppLifecycle.BackRequested`; Ui must close the open card/sheet/drawer first (hook). |

### 2.5 Clipboard, drag and drop, paste

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| B1 | `Ui/Pages/Reader.Shell.cs:517` | `navigator.clipboard.writeText` | M *verify* (MAUI on Apple serves from `app://0.0.0.0`; a custom scheme is not a secure context, so `navigator.clipboard` may be undefined → "Couldn't copy") | Route through `IClipboard.SetTextAsync`. Works in WebView2, Android WebView (`https://0.0.0.0`) and HTTPS web. |
| B2 | `Core/Reader/IImageClipboard.cs` | Image copy, host-implemented | — | Kept; `IClipboard` adds text. |
| B3 | `Ui/wwwroot/js/redact.js:93-95` | No clipboard read (by design) | — | Fine. |
| B4 | `Ui/wwwroot/js/reader.js:788-857` | Image paste + drop onto a Mark layer, read in JS as base64 and sent to .NET | W | Over Blazor Server the base64 travels through SignalR: default `MaximumReceiveMessageSize` is 32 KB, so any real image fails. Web host must raise the limit or stream. Same for the snapshot path (`reader.js:537`). Host setting, no Ui edit. |
| B5 | `Windows/MainWindow.xaml.cs:89-102` | Host turns off WebView2's own drop so window-level file drop works | M, L, A | Each host must do its equivalent; Web keeps the browser's drop and uploads. |

### 2.6 WebView / page host

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| H1 | `Windows/wwwroot/index.html`, `Windows/wwwroot/theme.js` | Host page and `window.chuvadiTheme` live in the **Windows** project | M, L, A, W | Every host needs the same `theme.js` (Ui calls `chuvadiTheme.set`, PreferencesInterop). Copying it per host risks drift → proposal to move it into Ui. |
| H2 | `Windows/MainWindow.xaml.cs:124-135` (`WebViewCache`) | WebView2 profile folder per build | — | Windows-only, stays in host. |
| H3 | `Windows/Platform/WpfUiScale.cs` | Interface size = WebView zoom | — | Other hosts: WKWebView `pageZoom`, Android `setInitialScale`/text zoom, or `IsSupported=false` → CSS fallback already in `theme.js`. |
| H4 | `Ui/wwwroot/css/*.css` | `-webkit-` prefixes, `::-webkit-scrollbar` | — | All four engines accept them (Firefox on Web shows default scrollbars — cosmetic). |
| H5 | `Ui/ChuvadiReader.Ui.csproj` | `<SupportedPlatform Include="browser" />` | — | Only an analyzer hint; Web is server-side, harmless. |

### 2.7 Services and lifetime (Web)

| # | Where | What | Breaks on | Notes |
|---|---|---|---|---|
| S1 | `Windows/App.xaml.cs:60-106` | ~45 services registered as **singletons** in the host | W | On Blazor Server a singleton is shared by every user → tabs, recents, settings, bench would leak across people. Must be per-connection (scoped). The new host kit `AddChuvadiReader(AppStateLifetime.PerConnection)` does this. |
| S2 | same | Registration list lives in the Windows host only | all | Each new host would copy it and drift. Kit centralises it; a test guards drift. |
| S3 | `Core/Storage/IAppStorage.cs` doc + `Windows/Platform/FileAppStorage.cs` | JSON file storage implementation is pure .NET but sits in the Windows project | M, L, A | Reusable as is; proposal to move it to the kit. Web needs per-user, encrypted-at-rest storage (design 12a). |

### 2.8 Libraries and OS-specific branches already in Core

| # | Where | What | Notes |
|---|---|---|---|
| O1 | `Core/Documents/CertificateSignService.cs:117` | `X509KeyStorageFlags` by OS (macOS can't use EphemeralKeySet) | Correct; Android *verify* (EphemeralKeySet supported there). |
| O2 | `Core/Documents/PasswordService.cs:185` | Path comparison case-insensitive on Windows/macOS, ordinal elsewhere | Correct. |
| O3 | `Core/ChuvadiReader.Core.csproj` | Umbrella `Chuvadi.Pdf` 3.31.1; `Chuvadi.Docs` **1.1.0**; `Chuvadi.Sheets` **1.1.2** | Library report recommends individual Pdf packages (−6.7 MB, matters on Android), Sheets 2.0.1, Docs 2.0.0 when released. Docs 1.1.0 is a lightweight (unsigned) tag. These are Reader-chat / Arun decisions; Foundation does not change them. |
| O4 | `Windows/Platform/WpfPrintService.cs` | Printing = WPF PrintDialog + XPS writer over chuvadi-pdf raster; **not** Chuvadi.Print | Other desktops will use Chuvadi.Print (IPP/CUPS) or the OS print panel; see interface §6. |

## 3. Other findings (not platform-specific)

- **CI** (`.github/workflows/ci.yml`) builds Core, Ui and tests on Ubuntu, macOS and Windows, plus the WPF host on Windows, from the committed `ci/packages` feed — but only for pushes and PRs to `main`, so it does not run on the platform branches. Foundation adds a separate workflow (new file) instead of editing it.
- `ci/nuget.config` comment says "the chuvadi-pdf 3.14.1 set" but the folder holds 3.31.1 — stale comment.
- `Directory.Build.targets` touches every source file's timestamp on each build. Harmless for CI, but it makes every build a full rebuild.
- `App.xaml.cs` (Windows) ignores command-line arguments, so "Open with Chuvadi" / double-click on a PDF doesn't open the file. Not a Foundation item, noted for Arun.
- Sandbox-packed chuvadi-pdf 3.31.1 DLLs are not byte-identical to the ones committed in `ci/packages` (embedded symbols carry build paths). Versions and contents match by name; this only matters if Arun wants reproducible packs.
