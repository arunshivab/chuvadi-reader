# Foundation — existing-file edits for Arun's approval

**Groups A, B and C are applied** (approved 6 Nov). Startup refinements after approval (same behaviour, less startup work): `App.razor` is back to the original — no `PlatformRoot`/cascade; MainLayout sets the `<html>` platform attributes only on non-Windows hosts; right-hand window buttons and the status text are inline in MainLayout again (original Windows markup); no `platform.js` module. See `STARTUP.md`. Approver: the coordinator (delegated by Arun, 6 Nov); nothing reaches `arunshivab/chuvadi-reader` before Arun's final check. Group A is one patch with exact diffs: `docs/foundation/proposal/foundation-edits-1.patch` (apply with `git apply`). It assumes the recommended answers to decisions F-2 (a), F-3 (switch) and F-6 (keep both); if you choose differently I regenerate it.
Groups B and C depend on decision F-1 (file model) and are listed by site; their diffs are written after you decide, so nothing is built for an option you don't pick.

## How Group A was verified
Applied on a local scratch branch (never pushed), then on Linux (.NET SDK 10.0.403):
- `ChuvadiReader.Platforms.slnx` build: **0 warnings, 0 errors**; tests **172/172 pass**.
- `ChuvadiReader.Windows` **compiled** with `-p:EnableWindowsTargeting=true`: **0 warnings, 0 errors**. It cannot be *run* here (WPF) — see "What to check on Windows".
- Static web assets: `_content/ChuvadiReader.Ui/js/theme.js` and `js/platform.js` are in the Windows host's asset manifest.

## Group A — needed by every platform; Windows looks and behaves the same

Windows output is designed to be identical: `CtrlKeyLabels` returns "Ctrl …" text unchanged, `WindowButtons Side=Right` renders the exact markup it replaces, `StatusLocation` renders the exact text on non-server hosts, and `isPrimary(e)` is the same Ctrl test on Windows. The exceptions are marked **visible** or **behaviour**.

| # | File | Change | Why | For | Against | Windows risk |
|---|---|---|---|---|---|---|
| A1 | `Core/ChuvadiReader.Core.csproj` | + ProjectReference to `ChuvadiReader.Platform` | Core and Ui can use the new contracts | One-line, contracts-only project (F-2 a) | Core now builds a second small project | None |
| A2 | `Ui/_Imports.razor` | + `@using ChuvadiReader.Ui.Components.Platform` | New components usable by tag | — | — | None |
| A3 | `Ui/App.razor` | Wrap `<Router>` in `<PlatformRoot>` | Sets `data-shell / data-platform / data-form-factor / data-input` on `<html>` and cascades form-factor changes so layouts re-render (Android fold, browser resize) | One place, no per-page code | One extra JS module load at start (~15 lines) | Very low: attributes only; no CSS uses them yet |
| A4 | `Ui/Layout/MainLayout.razor` | (a) inject `IPlatformInfo`; (b) `<WindowButtons Side=Left/>` at the start of the top bar and `Side=Right` in place of the `traffic` div; (c) `<StatusLocation/>` in place of the status text; (d) `StartDrag` returns early when `Chrome.TitleBarDrag` is false; (e) 10 "Ctrl …" labels and the Document-menu key column go through `Keys.Format` | Mac buttons on the left, none on Android/Web, server wording on Web (12b), no drag logic where there's no window, ⌘ labels on Mac | Design-exact per platform without touching the desktop markup | MainLayout gains 2 component tags | Low: same markup on Windows. **Check:** buttons still at the right, maximise icon still flips, drag + double-click maximise still work |
| A5 | `Ui/Components/Rails/RailButton.razor` | Shows `Keys.Format(Keys)` in the label and tooltip | Every rail button's shortcut (≈30) is fixed in one place | Keys stay canonical in all pages | — | None |
| A6 | `Ui/Components/Shell/CommandBar.razor` | Shows `Keys.Format(cc.Keys)` | Same, for Ctrl K results | Command `Keys` stay canonical — they are also the dispatch keys from `shell.js` | — | None |
| A7 | `Ui/Components/Settings/ShortcutsSection.razor` | `<kbd>` shows `Keys.Format(...)` | Settings › Shortcuts lists ⌘ on Mac | — | — | None |
| A8 | `Ui/Pages/Reader.razor` | inject `IPlatformInfo` and `IClipboard`; 2 bookmark tooltips via `Keys.Format` | ⌘ labels; text copy (A10) | — | — | None |
| A9 | `Ui/Pages/Bench.razor`, `Ui/Pages/Dashboard.razor` | inject `IPlatformInfo`; "Select all" tooltip and the Home tip via `Keys.Format` | ⌘ labels | — | — | **Visible (tiny):** Bench tooltip "Ctrl+A" becomes "Ctrl A", matching every other label |
| A10 | `Ui/Pages/Reader.Shell.cs:517` | `navigator.clipboard.writeText` → `IClipboard.SetTextAsync` | WKWebView on Mac may not expose `navigator.clipboard` (custom-scheme page) → "Couldn't copy"; one host-implemented path works everywhere | Same result on every platform | Copy now goes through the host | **Behaviour (equivalent):** Windows copies via the WPF clipboard (new `WpfClipboard`, 5 retries if another app holds the clipboard) instead of WebView2's. **Check:** "Copy file path" and other text copies |
| A11 | `Ui/wwwroot/js/reader.js:10, 98, 106, 117` | Import `isPrimary` from `platform.js`; use it for the tab/undo/zoom/find shortcuts; bare-key navigation also ignores Ctrl combos | **Bug fix (Mac):** today ⌘W, ⌘Z, ⌘F, ⌘±0, ⌘1-9, ⌘Tab do nothing on Mac because the code requires `ctrlKey && !metaKey` | One rule for all keys | — | None by construction: on Windows `isPrimary` = `ctrlKey && !metaKey`, exactly today's test; the added `ctrlKey` check on line 117 is only reached when Alt or Win is also held, which already returned. **Check:** Ctrl W/Tab/1-9/+/−/0/F/Z in the Reader |
| A12 | `Ui/wwwroot/js/theme.js` (moved from `Windows/wwwroot/theme.js`, content unchanged) + `Windows/wwwroot/index.html` script path | Every host needs `window.chuvadiTheme` | One copy, no drift | Windows page loads it from `_content/ChuvadiReader.Ui/js/theme.js` | Low. **Check:** theme switch, interface size 125/150 %, reduce motion |
| A13 | `Windows/ChuvadiReader.Windows.csproj` | + ProjectReference `ChuvadiReader.Hosting` | Shared registration (F-3) | — | — | None |
| A14 | `Windows/App.xaml.cs` | Replace the 40-line service list with `services.AddChuvadiReader(AppStateLifetime.Process)`; register `IClipboard` (`WpfClipboard`) and `IPlatformInfo` (Windows, buttons right, drag on, desktop capabilities, Ctrl labels) | One registration list for all five hosts | No drift; Web gets per-user lifetime from the same list | Windows now depends on the kit | Low: the kit registers the same 37 services as singletons (a test proves the set); host registrations still win. **Check:** app starts, every page opens |
| A15 | new `Windows/Platform/WpfClipboard.cs` | `IClipboard` for Windows | needed by A10/A14 | — | — | — |
| A16 | new `Ui/Components/Platform/` (`PlatformRoot`, `PlatformState`, `WindowButtons` + `.css`, `StatusLocation`) and `Ui/wwwroot/js/platform.js` | The platform switch itself | New files, but they need A1 to compile, so they ship in this patch | — | — | — |

### What to check on Windows after applying Group A
1. `scripts/setup-feed.sh` (or your local-nuget), then `dotnet build ChuvadiReader.slnx -c Release` → 0 warnings; `dotnet test` → 161 pass.
2. Start the app: window maximised as before, top bar unchanged, buttons at the right; minimise / maximise-restore (icon flips) / close; drag the top bar; double-click it to maximise.
3. Settings › Appearance: all 5 themes, interface size 100/125/150, reduce motion.
4. Reader: Ctrl W, Ctrl Shift W, Ctrl Alt W, Ctrl Tab, Ctrl 1-9, Ctrl + / − / 0, Ctrl F, Ctrl Z; Ctrl K command bar shows "Ctrl …" keys.
5. Document ▾ › Copy file path → paste somewhere.
6. Status bar still says "offline · on this device".

## Groups B and C — one patch, for approval (`proposal/foundation-edits-2.patch`, 27 files, +451/−86)

Built on F-1 C. Verified with the patch applied (Linux, .NET 10.0.403): Platforms solution and the WPF host compile with `-warnaserror` (0 warnings); **188/188 tests** (161 existing + 27 kit, including 6 new guards). Applies cleanly to `platform/foundation` @ the commit that adds it.

### Group B — write-back at every save (F-1 C)
| # | File(s) | Change | For | Against | Windows risk |
|---|---|---|---|---|---|
| B1 | new `Ui/Components/Platform/WriteBackExtensions.cs` | `await WriteBack.DeliverOrThrowAsync(path)`: returns at once when `IsNeeded` is false; otherwise commits and throws `IOException` with a readable message on failure, so each site's existing `catch` shows it; a cancelled download is not an error | one line per site, reuses the existing error display | a site's error text may read "Couldn't save: …" for a delivery failure | None: Windows `NoFileWriteBack.IsNeeded` is false → no call is made |
| B2 | Bench.razor (session, Bind all, Combine, `RunSave` = Bind/Lift desk, Scatter, Export PDF / images, Press), Reader.razor (Export, Press, Stamp copy and in place, snapshot PNG, bookmarks in place, markup in place, markup Save as, Flatten copy), Reader.Shell.cs `OnProtectedSaved` (password copy + certificate signing), Merge, Split (to folder, range), Redact | One delivery line right after each successful write; folder outputs deliver the folder | every save reaches the person's real file (Android) or a download (Web) | — | None (see B1) |
| B3 | new test `Every_save_site_delivers_its_file` | Fails if a Ui file has more `PickSave…`/`File.Replace` sites than delivery calls (mutation-checked: removing Merge's call fails it) | a future save site can't silently skip delivery | heuristic count, not a proof | — |

### Group C — host folders, account, packages, shared lists, platform gating
| # | File(s) | Change | For | Against | Windows risk |
|---|---|---|---|---|---|
| C1 | `Core.csproj` | **F4:** umbrella `Chuvadi.Pdf` → 17 individual 3.31.1 packages (one `ChuvadiPdfVersion` property). Shipped set drops PdfA, Color, Fonts.Woff2, Fonts.Bundled, Xfa, Reader and the umbrella — none referenced by the app. All in `ci/packages`, **nothing to add**. | ~6.7 MB less (Android), no deprecated packages | 17 references kept at one version (one property does it) | Low: compiles and tests pass; nothing used was removed. **Check:** open a PDF, Mark, Arrange, Protect, sign, print |
| C2 | new `Core/Storage/ScratchFiles.cs`; StampService, PressService, BenchService, BenchComposer (4 helpers made instance methods), Reader.razor (3), Bench.razor (1) | Every temp file goes to `IAppPaths.WorkingFolder` (optional ctor parameter; fallback = system temp) | Web keeps intermediates in the person's encrypted session folder, not a shared server temp | ctor signatures gain an optional parameter (tests unchanged) | None: desktop `WorkingFolder` = `Path.GetTempPath()`; only the random temp file names differ |
| C3 | SaveFolderService (+ SavingSection) | Default save folder = `IAppPaths.DefaultSaveFolder`/"Chuvadi Documents" (Web: under the session folder); static `DefaultBaseFolder()` → instance `DefaultFolder` | right default per host | — | None: same Documents\Chuvadi Documents (test asserts it) |
| C4 | SessionService | **Mac chat F2 (bug fix):** desk templates in `IAppPaths.AppDataFolder` instead of a hard-coded `%AppData%\ChuvadiReader`; one-time copy of an existing file from the old folder (old file left in place) | one app-data folder for everything, per host | a copy, not a move, leaves the old file behind | **Behaviour (visible on disk only):** templates now live in `%AppData%\Chuvadi`; existing ones are carried over on first use. **Check:** Arrange › desk ⋯ › Apply template still lists your templates |
| C5 | UserProfileService | Name fallback from `IAccount.DisplayName` (desktop `LocalAccount` = OS user name) | Web shows the Sangam name | — | None |
| C6 | AppInfo + AboutSection | **Bug fix:** About showed "chuvadi-pdf 3.30.2" (hard-coded) while 3.31.1 ships; versions now read from the loaded assemblies. Data folder from `IAppPaths` | About can't drift again | — | **Visible:** About › chuvadi-pdf shows 3.31.1 |
| C7 | DocumentModels + MainWindow.xaml.cs | **Mac chat F1 (bug fix):** one `OpenableExtensions` list; window drop uses it. Before: drop accepted .doc/.xls (which then opened as PDF and failed) and refused .md/.markdown/.txt/.log/.html/.htm | drop = Open dialog everywhere; test guards it against the Windows filter | — | **Behaviour:** dropping .md/.txt/.html now opens them; .doc/.xls are no longer accepted. **Check:** drop a .md and a .docx |
| C8 | FileAppStorage (moved Windows → `Hosting/Desktop`) + App.xaml.cs `using` | **Mac chat F3 (bug fix):** write to `settings.json.tmp`, then replace `settings.json` | a crash mid-write can't truncate settings; one implementation for Windows/Mac/Linux/Android | file moves out of the Windows project | Low. **Check:** change a setting, restart, it is kept |
| C9 | Bench (3), Reader (2), Library (1) "change…" links; SavingSection "Where files are saved"; LibrarySection "Library folder" + "Watched folders"; Document menu "Show in folder" + "Copy file path" | **Hidden** (not greyed) when the capability is false: `PickFolders`, `WatchFolders`, `RevealInFolder` | the controls never work there; the portal design has none of them | hiding vs greying (alternative: show greyed with "Not available on the web") | None: all true on desktop |
| C10 | (contract note) | Web's `IFilePicker.PickFolderAsync` returns a fresh session folder without a dialog; write-back delivers its files as one download | every output-to-folder flow (Bind all, Split to folder, Export, Scatter, Press) works on Web with no Ui edit | — | — |

### Add to the Windows checks (HANDOFF §6) when B/C are applied
7. Settings › About: chuvadi-pdf 3.31.1.
8. Drop a .md file and a .docx on the window: both open; a .doc is refused.
9. Arrange: Apply template still lists existing templates; Save template works.
10. Change a setting, restart: kept.
11. Bind a desk, Merge, Split, Stamp in place, Save marks in place: files written as before.

### Not in this patch (decisions or other chats)
- Device-only wording (AboutSection "nothing leaves this device", Settings "Stored on this device only", Library "Stays on this device") — needs Arun's Web wording.
- "Show in folder" wiring to `IFileManager` — Arun's call (kept "Coming soon").
- Sheets 2.0.1 — waiting for the Reader chat's answer (see status).
- Portal Home/Library layout without watched folders — Web portal UI chat.

## Later hooks (when the components exist; not in this proposal)
- MainLayout: `AccountMenu` slot when `Chrome.AccountMenu` (Web, 12b); Web sign-in route (12a); `SangamGate` on certificate signing (12c).
- MainLayout + Reader/Bench/Redact: phone bottom bar, tool strip and View sheet when `FormFactor.Phone` (12g-12j); tablet sizes via `data-form-factor="tablet"` CSS (12d-12f).
- reader.js: pen draws / finger pans on tablets (12e); WebKit `gesture*` pinch for Mac/Linux (INVENTORY K2, after a device test).
- MainLayout/Reader: Android back closes the open card, sheet or drawer first (`IAppLifecycle.BackRequested`).
- ~~Ui publishes its command list to `IAppCommands`~~ — not needed: the kit fills `IAppCommands` from `ShellService` (decided 6 Nov).
