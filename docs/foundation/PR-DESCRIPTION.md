# Platform layer (Foundation) — Mac, Linux, Android, Web groundwork

Lands the Foundation edits in chuvadi-reader first (Arun's merge plan (a)), so the Reader chat builds on them. **Do not merge before the Windows checks below.**

Four commits: code (`src`, `tests`, `ChuvadiReader.Platforms.slnx`, `Directory.Build.props`, `LICENSE`, `legal/`, `.github/workflows/ci.yml`), packages (`ci/packages`: chuvadi-pdf 3.31.2 and Chuvadi.Print 1.0.0-rc.2), dev tools (never shipped), docs (`docs/foundation`).

## What changes
**New projects**
- `ChuvadiReader.Platform` — the contract every host implements: platform kind, form factor, input, window chrome, capabilities, key labels, `ShellLayout` (desktop / tablet / phone / portal), file write-back, app folders, account, clipboard, file manager, share, lifecycle, menu-bar commands.
- `ChuvadiReader.Hosting` — `AddChuvadiReader(lifetime)` registers every Core service once for all hosts (per-connection for Web); defaults for each seam; menu-bar commands from `ShellService`; desktop defaults (folders, account, Ctrl/⌘ labels, settings file storage).
- `tests/ChuvadiReader.Hosting.Tests` — 28 tests (service graph, per-connection isolation, Windows registration drift guard, key labels, command bridge, every save delivers, openable types vs the Open dialog, settings write, host folders, save-folder fallback, tab restore).
- `tests/ChuvadiReader.MobileUi.Tests` (3) and `tests/ChuvadiReader.WebUi.Tests` (14); `tests/ChuvadiReader.MobileUi.Preview` — a testing-only browser preview of the real Ui (never shipped).

**Ui / Core (Windows looks and behaves the same, except where marked)**
- Window buttons follow the platform: right on Windows (original markup), left on macOS, none on Android/Web; title-bar drag only where there is a native window.
- Shortcut labels per platform (⌘ on macOS); command keys stay canonical. **Visible:** Bench "Select all" tooltip "Ctrl+A" → "Ctrl A".
- **Bug fix (macOS):** Reader shortcuts required Ctrl; ⌘ now works on Mac. Windows test unchanged.
- Text copy through the host clipboard (Windows: WPF clipboard with retries) instead of WebView2's.
- `theme.js` moves from the Windows host into the Ui (content unchanged plus one setter Windows never calls).
- Every save delivers through `IFileWriteBack` (no-op on desktop); a test fails if a save site skips it.
- Temp files, default save folder, desk templates and user name come from the host. **Visible on disk:** desk templates now live in `%AppData%\Chuvadi`; existing ones are copied over from `%AppData%\ChuvadiReader` on first use.
- Controls a platform can never support are hidden (folder pickers, watched folders, Show in folder / Copy file path) — all stay on Windows.
- **New on Windows:** Document ▾ › Show in folder works (opens Explorer with the file selected).
- Core uses the 17 individual chuvadi-pdf 3.31.2 packages instead of the umbrella (drops PdfA, Color, Fonts.Woff2, Fonts.Bundled, Xfa, Reader — none used). All from `ci/packages`; nothing added. **chuvadi-pdf 3.31.1 → 3.31.2** (packed from the unchanged v3.31.2 tag; hashes in `docs/foundation/PACKAGES-3.31.2.md`; Chuvadi.Fonts stays 1.0.0, the tag pins it).
- **Bug fix:** Settings › About showed chuvadi-pdf 3.30.2; versions now read from the loaded libraries (shows 3.31.2).
- **Bug fix:** window drop accepts exactly the Open dialog's types (it accepted .doc/.xls, which then failed, and refused .md/.markdown/.txt/.log/.html/.htm).
- **Bug fix:** settings are written to a temp file that then replaces `settings.json`.
- Web wording only on the Web portal (Windows text unchanged).
- **Phone, tablet and portal chrome** (not rendered on Windows): phone shell, touch support, portal account menu and status user, "Upload", server status dot; server paths and folder-only sections hidden where the platform has no folders; Phase 0 samples off on the server only.
- The status-bar clock is its own component: only its text re-renders each second, not the whole shell.
- 0.5px dividers drawn as borders on non-Windows hosts (WebKit dropped them at some positions).
- Pen draws / finger pans on the markup layer when a pen is present; tablet desk counter on the Bench.
- **Bug fix:** reopening the app could show the same file in two tabs (tab restore now skips duplicates; test added).
- Mac Catalyst treated like macOS for signing-key storage and the temp-path guard.
- **Bug fix:** a PDF whose permissions forbid printing no longer prints — the Print card says why (tests added).
- **Bug fix:** the Home greeting no longer adds a second full stop after a name ending in "." (the name is shown as typed).
- **Bug fix:** open requests queue instead of overwriting each other (only reachable with several at once, e.g. a multi-file upload).
- **Single instance, tabbed:** opening a file while Chuvadi runs hands it to the running window as a new tab; Windows now opens the file it is started with (it ignored it before); every dropped file opens (before: only the first). A frozen running copy can't block a launch: after 1.5 s the new launch opens its own window.
- Print results show as errors when printing failed or was refused (before: decided from the wording).
- Web and Android wording for the Home summary, the drop zone, Appearance and the Preferred-name hint (Windows text unchanged).
- Shared kit: the default save folder no longer falls back to the temp folder when Documents doesn't exist yet (Linux).
- `ci/packages`: Chuvadi.Print and Chuvadi.Print.Ipp 1.0.0-rc.2 (used by the Linux host; nothing in this PR references them).

**Windows host:** registers through `AddChuvadiReader`; adds `WpfClipboard`, `WpfFileManager`; `FileAppStorage` moves to the shared kit. **`dotnet publish` is now ReadyToRun** (win-x64 default, `-r win-arm64` for Arm); build, F5 and tests are unchanged.

**Licence (Arun, 7 Nov: proprietary, free to use):** `LICENSE`, `legal/EULA.txt` and `legal/THIRD-PARTY-NOTICES.txt` (every bundled package's licence with full texts: chuvadi-pdf Apache-2.0, Chuvadi libraries MIT, LiPi Sans OFL-1.1, Microsoft MIT) — **all three are drafts for legal review**. `Directory.Build.props` sets Company, Product and Copyright on every assembly; the Windows app carries the three files in its output. Settings › About › Licences now opens them in the app (also at `/licence`).

**CI:** `ci.yml` moves to `actions/checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7` (Node 24).

## Startup (published Release; `docs/foundation/STARTUP.md`)
Core/Ui startup measured with `tools/StartupProbe` (services + first render of the real App), main c7b9e44 vs this branch, medians:

| Build | Services | First render | Total | Methods JIT-compiled |
|---|---|---|---|---|
| main, JIT | 26.0 ms | 198.4 ms | 256.3 ms | 1,372 |
| branch, JIT | 28.0 ms | 204.5 ms | 263.8 ms | 1,415 |
| main, ReadyToRun | 22.7 ms | 124.1 ms | 177.5 ms | 351 |
| branch, ReadyToRun | 23.1 ms | 126.3 ms | 177.3 ms | 357 |

- ReadyToRun: no added cost. JIT: +43 small methods (~7 ms, ~3 %), after removing a module fetch, a root cascading component and generic registrations from the first version.
- Rendered HTML identical to main on Windows (apart from the clock and whitespace); screenshots before vs after the phone/tablet/portal chrome identical on 6 routes at 100/125/150 % (`docs/foundation/COMBINED-HOOKS.md`).
- **In this PR:** the Windows app publishes ReadyToRun — −80 ms (−31 %) of Core/Ui startup measured here; publish grows 43 → 52 MB. Time the WPF + WebView2 part on Windows with `pwsh scripts/measure-startup.ps1` and `-ReadyToRun`.
- Re-checked after the phone/tablet/portal chrome landed: ReadyToRun +1 method, +1.2 ms (noise); see STARTUP.md.
- Measured on Linux x64, 2-core sandbox (A/A noise ≈ 7 ms); the WPF host compiles here with `-p:EnableWindowsTargeting=true` but can only run on Windows.

## Verified
- `ChuvadiReader.Platforms.slnx` and `ChuvadiReader.Windows` build with `-warnaserror`: 0 warnings, 0 errors.
- Tests: 229/229 (161 existing + 68 new).
- Second round of screenshots (9 screens incl. the Print card and two Settings sections, 100/125/150 %): identical (`docs/foundation/COMBINED-HOOKS-2.md`).
- Working-repo CI (Sthraas/chuvadi-reader-platforms, `platform/foundation`): shared code on Ubuntu x64/arm64, macOS, Windows; the Windows app on windows-latest; package check.

## Windows checks for Arun before merging
1. `dotnet build ChuvadiReader.slnx -c Release` → 0 warnings; `dotnet test` → all pass.
2. Start the app: maximised as before, top bar unchanged, window buttons at the right; minimise / maximise-restore (icon flips) / close; drag the top bar; double-click it to maximise.
3. Settings › Appearance: all 5 themes, interface size 100 / 125 / 150, reduce motion.
4. Reader: Ctrl W, Ctrl Shift W, Ctrl Alt W, Ctrl Tab, Ctrl 1-9, Ctrl + / − / 0, Ctrl F, Ctrl Z; Ctrl K command bar shows "Ctrl …" keys.
5. Document ▾ › Copy file path, then paste somewhere.
6. Status bar still says "offline · on this device".

Also for this PR's other changes:

7. Settings › About: chuvadi-pdf 3.31.2.
8. Drop a .md and a .docx on the window: both open; a .doc is refused.
9. Arrange: Apply template still lists your templates; Save template works.
10. Change a setting, restart: kept.
11. Bind a desk, Merge, Split, Stamp in place, Save marks in place: files written as before.
12. Document ▾ › Show in folder: Explorer opens with the file selected.
13. Status-bar clock ticks; Home, Library and Settings › About still show folder paths and the Watched folder card.
14. Open two files, close the app, reopen: each file appears once.
15. `dotnet publish src/ChuvadiReader.Windows -c Release`: the published app starts and works.
16. Settings › Language › Preferred name "Arun S." → Home: "Good morning, Arun S." (one full stop).
17. A PDF whose permissions forbid printing: the Print card is greyed with "This file's permissions don't allow printing."; an ordinary PDF still prints.
18. Explorer › Properties › Details on `ChuvadiReader.Windows.exe`: Company "Sthraas Intelligence Pvt Ltd", Product "Chuvadi Reader", Copyright "© 2026 Sthraas Intelligence Pvt Ltd. All rights reserved."; `LICENSE.txt`, `EULA.txt`, `THIRD-PARTY-NOTICES.txt` sit beside the exe.
19. Settings › About › Licences › Open: the licence agreement, the licence and the third-party notices; "‹ About" goes back.
20. With Chuvadi open, double-click (or Open with) a PDF in Explorer: it opens as a new tab in the running window, which comes to the front — no second window.
21. With Chuvadi closed, double-click a PDF: Chuvadi starts with that file open.
22. Select three PDFs in Explorer and open them, or drag three onto the window: three tabs.
23. Print a PDF whose permissions forbid printing: the message shows as an error, not with a tick.

## Known gaps (not changed here)
- Windows printing is raster (`WpfPrintService`); move to Chuvadi.Print's vector XPS route when rc.3 is released.
- Layout not released (Word/Excel exact view, Save as PDF); Docs stays 1.1.0; Sheets 1.1.2 (the Reader chat moves it).
