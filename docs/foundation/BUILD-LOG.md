# Foundation — build log

## 2026-11-06 — session 1

### 1. Code and packages
- Cloned `arunshivab/chuvadi-reader` (read only). `main` = **c7b9e44** "chuvadi-pdf 3.31.1 (+ Chuvadi.Fonts 1.0.0) (#10)". The Reader chat's 3.31.1 upgrade **has landed**; Foundation builds on it.
- Cloned `arunshivab/chuvadi-pdf` at tag **v3.31.1** (38c82c2). Packed with the repo's own command (`build/pack.ps1`): `dotnet pack Chuvadi.slnx -c Release -p:Version=3.31.1` → 34 packages. Working tree clean before and after (library code not touched).
  - Note: a first pack without `-p:Version` produced 0.1.0 packages; discarded. The script uses the right command.
  - Sandbox-packed DLLs are not byte-identical to the 3.31.1 DLLs committed in chuvadi-reader `ci/packages` (embedded build paths); names and versions match.
- Downloaded release assets from `github.com/arunshivab/chuvadi/releases` and checked SHA-256:

| Package | SHA-256 | vs report |
|---|---|---|
| Chuvadi.Fonts 1.0.0 | `c15a82d1caa3d70f3e2abe16dd94201ea1d3dae91764735a19ffa7398c37f5fe` | match |
| Chuvadi.Sheets 2.0.1 | `417f537f74826deeb8cd14492441800243b45df9e714cd9ce6daf9138571c253` | match |
| Chuvadi.Zip 1.0.0 | `e019f9b61d48d705e9b8e81e3ca8bcbb3114056b2127f4e9ea2af0aa51110104` | match |
| Chuvadi.Markup 1.0.0 | `3257478005d2a159e8cf9d88899876dd75a419b8d2cd7d7d6b6754f9213a4192` | match |
| Chuvadi.Docs 1.5.1 (stop-gap, not used) | `102b682a5820110cd547ea648db6d87dedbf57906ef33d2a1a26730c4df1c265` | not in report |
| Chuvadi.Print 1.0.0-rc.2 | `252b6d10204d37069a35040ea89d58e1662a452c4e05fd90a0d8b90cd1049cbf` | not in report (record lives in the Print project) |
| Chuvadi.Print.Ipp 1.0.0-rc.2 | `82304b782ce23ac5dcb3895e1fee31898abf3c22222f9f9c65c423fba68aa562` | not in report |
| Chuvadi.Print.Windows 1.0.0-rc.2 | `df1ab37a48786ca5f1822e033b2f0fd45ba7339d9850140365360defb527b375` | not in report |
| Chuvadi.Print.Remote 1.0.0-rc.2 | `973885310888f3ec683633e1e94db1950d30ab0a7c0c2a07df11678794e7ebbf` | not in report |

- `scripts/setup-feed.sh` (new) reproduces the feed into `./localpackages` (the path the repo's `nuget.config` already uses).

### 2. Working repo
- Access to `Sthraas/chuvadi-reader-platforms` arrived mid-session. Its `main` holds `README.md` + `docs/` (design handout, coordination).
- `platform/foundation` = chuvadi-reader `main` (c7b9e44) merged with the working repo's `main` (unrelated histories), keeping chuvadi-reader's `README.md`. `src/` and `tests/` are identical to chuvadi-reader `main`.

### 3. Baseline on Linux
- .NET SDK 10.0.403 (sandbox). `dotnet build` of Core, Ui, tests: **0 warnings, 0 errors**. `dotnet test`: **161/161 pass**. (The Windows host can't build here: WPF.)

### 4. Inventory and design
- `docs/foundation/INVENTORY.md`: every host-specific item in Core and Ui with file:line.
- `docs/foundation/PLATFORM-INTERFACE.md`: the draft contract and decisions F-1…F-6.
- New projects (new files only):
  - `src/ChuvadiReader.Platform` — contracts (`IPlatformInfo`, `IFileWriteBack`, `IAppPaths`, `IAccount`, `IClipboard`, `IFileManager`, `IShare`, `IAppLifecycle`, `IAppCommands`).
  - `src/ChuvadiReader.Hosting` — `AddChuvadiReader(AppStateLifetime)`, desktop defaults, key labels, command registry.
  - `tests/ChuvadiReader.Hosting.Tests` — 11 tests: every service resolves (process and per-connection), per-connection isolation, host override wins, drift guard against `Windows/App.xaml.cs`, macOS key labels, command routing.
  - `ChuvadiReader.Platforms.slnx` — programme solution; the existing `ChuvadiReader.slnx` is unchanged.
- Build of `ChuvadiReader.Platforms.slnx`: **0 warnings, 0 errors**; tests **172/172 pass** (161 + 11).

### 5. Existing-file edits — proposal only
- Made the proposed edits on a local scratch branch (never pushed); exported as `docs/foundation/proposal/foundation-edits-1.patch` (23 files, +290/−66), explained in `docs/foundation/EDITS-FOR-APPROVAL.md` (Group A diffs; Groups B/C wait for decision F-1).
- Verified with the patch applied: Platforms solution 0 warnings, 172/172 tests; WPF host **compiled** on Linux with `-p:EnableWindowsTargeting=true` (0 warnings) — running it needs Arun's Windows machine.
- `git apply --check` of the patch on `platform/foundation`: clean.
- Fixed in my own code while doing this: WPF has no `SetDataObject` retry overload (WinForms-only) → own retry loop in `WpfClipboard`; `DesktopAppPaths.WorkingFolder` now equals `Path.GetTempPath()` so moving Core onto it later changes nothing on desktop; drift test lists the two new host seams.

### 6. CI
- Existing `.github/workflows/ci.yml` (Ubuntu/macOS/Windows, `main` only) left unchanged.
- New `.github/workflows/platform-build.yml` (reusable job: runner, build, test, build-args, workloads, label, full-feed) and `.github/workflows/platforms.yml` (on `platform/**`): shared code on ubuntu x64, ubuntu arm64, macOS, Windows; the Windows app on Windows; the full feed reproduced from the tag. All builds `-warnaserror`.
- Local check of the CI restore path (`--configfile ci/nuget.config`, clean package cache) + `-warnaserror`: 0 warnings, 0 errors.

### 7. First CI run (ae24bcf) and fixes
- Passed: shared code on ubuntu-latest, ubuntu-24.04-arm, macos-latest, windows-latest; the unchanged Windows app on windows-latest (`ChuvadiReader.slnx`, `-warnaserror`, 161 tests).
- Failed: `feed` — chuvadi-pdf's nuget.config needs Chuvadi.Fonts in its git-ignored `.fonts-feed` (their `build/fetch-fonts.ps1`). It passed locally only because the package cache already had it. Fixed: `setup-feed.sh` now does the same fetch with the SHA-256 check. Re-verified with an empty package cache: 45 packages, build 0 warnings.

### 8. Signal for the UI chats
- Coordinator (6 Nov): Mobile app UI and Web portal UI chats join; they need a platform + form-factor signal.
- Added `ShellLayout` (Desktop / Tablet / Phone / Portal) + `ShellLayouts.From/Key/Shell()` to the contracts (7 new tests, 18 in the kit). Group A now also writes `<html data-shell>` and cascades `PlatformState.Shell`. Guide: `FOR-UI-CHATS.md`.
- Patch regenerated (23 files, +290/−66), re-verified: Platforms solution and WPF host compile with `-warnaserror`, 179/179 tests.

### 9. Decisions applied (coordinator, 6 Nov)
- Group A applied (aeeac43): 0 warnings incl. WPF host compile, 179/179 tests.
- Feed: `ci/packages` is the source (byte-identical to Windows); `setup-feed.sh --verify-pack` re-packs v3.31.1 only to check the package set (34/34 match). CI restores from `ci/packages`; the `pack-check` job replaces `feed`.
- Contract: F-5 (Web follows the browser: `ShellLayouts.From` gives Phone/Tablet for small browsers, `IsPortal()` for portal chrome); `SavesAsDownload`; `IAppCommands` reshaped and filled from `ShellService` by `ShellAppCommands` (kit default, 3 tests) — no Ui hook for the Mac menu bar; kit defaults for `IAppPaths`, `IAccount`, `IAppLifecycle`. `HANDOFF.md` written.

### 10. Groups B and C (proposal 2)
- `proposal/foundation-edits-2.patch` (27 files, +451/−86): write-back at every save, host folders, account name, individual Pdf packages (F4), Mac chat F1/F2/F3, About version bug fix, capability gating.
- Verified on scratch: Platforms solution + WPF host compile `-warnaserror`, 188/188 tests; save-site guard mutation-checked.
- Sheets 2.0.1 probe (throwaway): Core's only compile break is `using Chuvadi.Sheets.Excel` → `Chuvadi.Sheets.Model` in XlsxViewService.cs; then 0 errors.

### 11. chuvadi-pdf 3.31.2 (D-54)
- Tag `v3.31.2` present (commit aff17e6). Packed unchanged → 34 packages, 0 warnings; tree clean.
- `ci/packages`: 3.31.1 chuvadi-pdf set replaced by 3.31.2 (hashes in `PACKAGES-3.31.2.md`); `ChuvadiPdfVersion` 3.31.2; `tests/ChuvadiReader.Tests` umbrella 3.31.2; `setup-feed.sh` and the pack-check job point at v3.31.2.
- Empty package cache → only 3.31.2 restored. Platforms solution + WPF host: 0 warnings; 188/188 tests.
- Chuvadi.Fonts kept at 1.0.0 (the v3.31.2 tag pins it).

### 12. Combined hooks (D-26) and ReadyToRun (D-50)
- One patch for Web H0–H9, Web UI and Mobile UI hooks, D-35/37/39/42/46/48/52 and the Mac Catalyst checks (`3358b00`, `COMBINED-HOOKS.md`, `proposal/combined-hooks.patch`). H0 not added: every hook reads the existing contract.
- Pixel proof (Chromium, 6 routes × 1/1.25/1.5×): a first draft differed on Home — a conditional had merged the shelf caption's two text nodes, moving glyphs by sub-pixels. Desktop branches now keep the original markup verbatim: 18/18 identical. Library page folder path gated the same way (H9), re-proved.
- D-39 on WebKit 26.6: dividers painted at 5–9 of 10 sub-pixel positions before, 10/10 after (1–1.5×). Kept off Windows (Chromium differs at 1.25×/1.75×).
- Phone/tablet smoke: 5 routes each, no errors.
- Mobile UI, Web UI tests and the preview host added to the Platforms solution. D-48 regression test added (fails without the fix).
- Platforms solution + WPF host `-warnaserror`: 0 warnings; 206/206 tests.
- Startup re-check: ReadyToRun +1 method / +1.2 ms (noise); JIT +12 methods, time within noise.
- D-50 (`a1a4660`): Windows csproj publishes ReadyToRun (win-x64 default) only when publishing. Cross-compiled here from Linux: every ChuvadiReader DLL in the publish has native code; plain build stays IL. Publish 43 → 52 MB.

### 13. Second combined patch (7 Nov)
- Coordinator round: U-11 Web wording, Android wording, F-2, F-3, L-8, kit save-folder fallback, Chuvadi.Print packages. `aadd992` + `7c67b89`; `COMBINED-HOOKS-2.md`, `proposal/combined-hooks-2.patch`.
- L-8 via the open document (`PdfDocumentVm.AllowsPrinting`, read from the encryption dictionary at open — no second file read) + the Print card + `WpfPrintService`. Test fixture: `TestSigning.ProtectRestricted` with a no-print mask; the test fails without the fix.
- F-2 moved into Core (`GreetingText`) so it is testable; the name is never changed.
- F-3: `PendingOpen` records in a locked queue; `TryTake` hands each out once. Reader loops over them.
- Kit fallback mirrors `LinuxAppPaths`; Windows result unchanged (Documents always exists).
- Print packages copied from `platform/linux`; SHA-256s match its §18; dependency closure complete.
- Pixel proof: 9 screens × 3 scales, 27/27 identical once `~/Documents` exists. The first run differed only in the save-folder path (`/tmp/…` before, `~/Documents/…` after) — item 6 doing its job in this Documents-less sandbox.
- Preview gained `CHVD_FF=web`; checking the portal found that the Web host must register `PortalLinks` (by design; noted in HANDOFF).
- 229/229 tests; 0 warnings. Startup (ReadyToRun) 196.0 → 196.3 ms, 358 → 358 methods.

### 14. Licence, single-instance design, third patch (7 Nov)
- Licence files (drafts for legal review), shared metadata, Windows ships them; notices generated from each package's licence field with verbatim licence texts (Debian's Apache-2.0, .NET's MIT, the font package's OFL).
- Single instance: design with options (`SINGLE-INSTANCE.md`). Tested here: .NET named pipes on Linux leave the socket file after a crash (every later launch waits 500 ms) — rejected; lock file + Unix socket recovers from a crash; a frozen primary is detected in 1.5 s. Election 4.3 ms in a cold test process; socket set-up 12 ms, so it runs after the first paint.
- Third patch: Web UI Portal files (both rounds), CI on Node 24, Licences page (About row + `/licence`). Drop zone options in `DROP-ZONE-OPTIONS.md`.
- PR push to arunshivab/chuvadi-reader: still 403 (app not installed); branch kept current.

### 15. Legal documents (Arun's answers, 7 Nov)
- DPDP checked against published summaries of the Rules (notified 13 Nov 2025; core obligations from 13 May 2027; 90-day grievance limit; 72-hour Board breach report; one-year log retention for specified purposes — flagged to counsel against the portal's "nothing kept").
- Found: the portal keeps no appearance settings in browser storage (settings are per session in server memory; the Ui uses no browser storage), so the cookie notice says so and leaves a section ready.
- Found: the four display fonts in `wwwroot/fonts/google` shipped without their OFL texts; now in the notices (verbatim from google/fonts). Android declares no INTERNET permission, so Play's answer is "no data collected".
- `scripts/make-notices.py` rebuilds the notices from `ci/packages` and `legal/licenses/`.

### 16. Single instance built; host notices; fourth patch (7 Nov)
- Kit: `IAppInstance`, `IncomingFiles`, `DesktopSingleInstance` + `InstanceChannel`; Windows host wired. 13 tests (real OS paths per runner); 48/48 kit tests here, stable over 5 runs. Windows path compiled only (CI stopped).
- First-launch claim on Linux was 7.7 ms cold: culture formatting/casing (ICU), `string.Create<uint>` (JIT) and `getpwuid` removed → 1–3.5 ms, the rest is the first native open + lock; `ClaimAsync` overlaps it with host start-up.
- Wayland: the hand-over carries `XDG_ACTIVATION_TOKEN`.
- `IHostNotices` + Licences tab (preview shows it on phone/tablet only).
- P-06 and the Linux toast patch applied as given; W-1–W-4 as the Web portal UI chat worded them. 265/265; pixel-identical (one known harness artefact).

### 17. PR opened; D-90 proposal; D-89 built; Windows mutex fix (7 Nov)
- PR #11 opened on arunshivab/chuvadi-reader (4 commits, Arun sole author, no attribution). Its CI (Windows, Ubuntu, macOS) started automatically; cancelled after ~35 s to keep to "no Mac/Windows runs until confirmed".
- My 04:55 push to platform/foundation ran the old full matrix (repo now public). The Windows job failed 4 single-instance tests: an owned mutex is re-entrant on one thread. Fixed by never acquiring it.
- D-90 workflow patch on local branch `proposal/d90-workflows` (not pushed): Linux x64 per push, the rest manual, docs-only pushes run nothing, pack check only when the feed changes, cancel-in-progress, job timeouts. actionlint clean.
- D-89: `IDroppedFiles`, `PortalDropCatcher`, `portal-drop.js`, B wording; 7 browser checks; 27/27 desktop identical.

### 18. CI rules (D-93)
- Kept only D-90 (a) docs-only pushes run nothing and (d) cancel-in-progress per branch, in `platforms.yml` and `ci.yml`; the Linux-only matrix, manual Mac/Windows jobs, the pack-check split and job timeouts were dropped (repo public, minutes free). Local branch `proposal/d90-workflows` deleted.

### 19. Sixth combined patch (8 Nov)
- Mobile UI H-5 to H-12 (+ their current Phone files, the PhoneShell slot), Web UI H5 (merged by hand with H-5) and H6, D-96 touch tip. H-12 widened to every non-Windows platform (one rule, `PhaseZeroSamples`). `COMBINED-HOOKS-6.md`.
- Proof: desktop identical (Library's "1m"/"2m" relative time and the one-pixel Reader artefact aside); phone, tablet and portal smoke clean; 273/273; 0 warnings.

### 20. D-98 legal wording (8 Nov)
- Terms of Use, Privacy Notice B6 and the App Store portal text: "encrypted working area" → "kept in memory only, never written to disk" (D-28: tmpfs). Still DRAFT. Kept as accurate: the encrypted sign-in cookie (ASP.NET Data Protection) and HTTPS.
- Not changed (approved wording, D-20): PlatformText.ServerPrivacy still says "encrypted" on the portal (Settings footer, Library index hint, About › Network) — raised with the coordinator.

### 21. Seventh combined patch, part 1 (8 Nov)
- H-13 (Mobile UI), Web H7 + Portal files (PortalLegalLinks), Linux window-button layout (shared diff unchanged). 27/27 desktop identical; portal/phone smoke clean; 273/273. Part 2 waits for D-101 (live chrome event) and D-104 (drop overlay). `COMBINED-HOOKS-7.md`.

### 22. Seventh combined patch, part 2a (8 Nov)
- Portal privacy line "kept in memory only" (D-106), Android P-07, Web UI D-104 drop overlay and D-103 pitch. 27/27 desktop identical; drop checks pass; 274/274. D-101 still awaited.

### 23. Seventh combined patch, part 2b (8 Nov)
- D-101 written here (coordinator 20:22): `IPlatformInfo.ChromeChanged` (default does nothing), MainLayout redraws the top bar on non-Windows platforms. Contract test, plus the preview's Linux mode with a live-layout endpoint and `tools/PixelProof/chrome-changed.js` (6/6). 27/27 desktop identical; 276/276.
- Lesson: start the preview with `ASPNETCORE_ENVIRONMENT=Development`. Otherwise static web assets are off, `blazor.web.js` is served empty and the page never becomes interactive. Only prerender runs, so a live event finds no listener.

### 24. Eighth combined patch, part 1 (8 Nov)
- H-14 (Mobile UI: the phone opens at Fit width; desktop margin 60 px), Mobile UI's own phone and tablet files (9bb7f76), Web UI U-17 (b) Home drop zone during a drag (D-109). 27/27 desktop identical; phone fit 12 | 12 px; portal drop 7/7 plus U-17 state check; 276/276. Part 2 waits for D-112 and D-114. `COMBINED-HOOKS-8.md`.

### 25. Eighth combined patch, part 2 (9 Nov)
- The Linux chat's shared diff (B-02, B-03, B-07, B-08, B-09/B-10, B-11, B-19, B-23) without its D-101 hunks (already here), Linux host files and uitest.py. D-120 fixes WT-02, WT-09, WT-10/B-11b, B-08b, B-16 in the kit, B-22 (D-123). Mobile UI D-112 and D-114 (H-16). 295/295; Windows-layout screens identical apart from the intended Print-card text. `COMBINED-HOOKS-8.md`.

### 26. Ninth combined patch (9 Nov)
- B-07 through the library's `PasswordSupplied`; unlock card in the Password card's language; B-11a repair already shared via `RecoveryService` → `PdfRepairer.Repair`; Sheets 1.1.2 has no `XlsxReaderOptions.CancellationToken` (waits for the Sheets move). 297/297; 27/27 identical. `COMBINED-HOOKS-9.md`.
- Open questions to the coordinator: (a) `RequiresPassword` measured at the cost of a plain open, so not on the open path; (b) `PdfOpenOptions.Password` would mean the stream overload, which buffers the file, while the path overload already takes the password.

### 27. Tenth combined patch (9 Nov, D-130)
- B-24: the PDF open (existence check included) moved off the Reader's thread with a per-tab cancel; closing frees the slot at once; late results disposed; queued opens load side by side. Tests with a thread-blocking reader and a real FIFO (the old path hangs on them). 299/299; 26/27 + Home artefact. `COMBINED-HOOKS-10.md`.

### 28. Tenth combined patch, part 2 (9 Nov)
- Chuvadi.Zip 1.0.0 (release asset of `zip-v1.0.0`, commit 170bc40) into `ci/packages`, SHA-256 recorded (`PACKAGES-ZIP-1.0.0.md`), notices regenerated. Web UI Upload wording and Q-3 "this session" wording, with tests. 305/305; 27/27.

### 29. Eleventh combined patch (9 Nov, D-136)
- Chuvadi.Docs 2.0.0 and Chuvadi.Sheets 2.0.2 (release assets of docs-v2.0.0 / sheets-v2.0.2, SHA-256 in `PACKAGES-DOCS-SHEETS-2.md`), namespaces to `.Model`, table cells from `TableCell.Blocks`, whitespace shown as in the file. Library limits (Docs 20 MB / 64 MB; Sheets ForUntrustedInput) and cancellation (token, 60 s) replace Core's guard; viewers stop waiting at once. Corpus with independent expected values: old 18 wrong, new 0. 313/313; 24/27 + About (versions). `COMBINED-HOOKS-11.md`.

### 30. Eleventh combined patch, part 2 (9 Nov, D-137)
- Word limits keyed on `ServerSide`: portal 20/64 MB, desktop and mobile 100/400 MB (a 30 MB picture opens locally, is refused on the server). List markers from `Document.GetListLabels()`; Symbol/Wingdings bullets drawn as Unicode shapes. 324/324; 26/27 + Home artefact.

### 31. Twelfth combined patch, part 1 (9 Nov, D-139 item 1 + AT-03)
- chuvadi-pdf 3.32.0 from the release, 34/34 against its SHA256SUMS.txt. Stale commit stamp (aff17e6) checked: packed the signed tag, so 33/33 DLLs are identical in metadata, IL sizes and resources (`PACKAGES-3.32.0.md`). Printing follows `EncryptionInfo.EffectiveFlags` (the owner password prints; D-102 ends). LIB-1 pypdf `/P 4294965243` fixture opens (3.31.2 failed).
- AT-03: typed registrations with `DynamicallyAccessedMembers`; trim and AOT analysers on Hosting; settings JSON source-generated (byte-identical). `tools/TrimProbe` reproduced 6/53 failures before, 0 after; `trim-check` CI job. `TRIMMING.md`.

### 32. Twelfth combined patch, part 2 (9 Nov, D-142, D-143)
- W-01: the top bar's empty middle moves the window (press-stopping moved from the containers to display:contents wrappers; pixel-identical). Already in main. Test before FAIL / after PASS.
- W-02: dropped files reach the Windows host through WebView2's postMessageWithAdditionalObjects (host-drop.js and MainWindow.OnWebMessageReceived); WebView2's drop stays on. Already in main. Page-side test 5/5; host side for Arun's retest.
- D-143: the 93 Android packages MAUI 10.0.110 bundles (scripts/resolve-android-packages.py → legal/android/maui-android-packages.json) in the notices with licence and holder; Glide's BSD text; Play data-safety basis corrected.

### 33. Twelfth combined patch, part 3 (9 Nov, D-145)
- Mobile UI H-17 (desk header's small buttons in the ··· menu on touch), H-18 (Arrange keeps Document ▾ on touch), H-19 (Find scrolls the match itself into view; all platforms) from `mobile-ui-hooks-6.patch` (3e80750). 327/327; 27/27 identical; W-01/W-02 tests still pass.

### 34. AT-03 made structural (9 Nov, D-146)
- Registrations are typed `Core<TService, TImpl>` / `Seam<TService, TImpl>` calls (annotated `TImpl`, `TImpl : TService`); no `Type` tables. A new seam default such as Android P-08's `PlainPathLabels` is one `Seam` line.
- Found while proving it: the trim analyser doesn't flag a `(Type, Type)` array, so the first fix's annotated record was safe only while every entry used it. Guard test added; matrix in TRIMMING.md.
- 331/331 tests (Hosting 64); 0 warnings in both solutions; trimmed probe 53/53; StartupProbe no cost (JIT 1448 vs 1450). Windows: registrations pinned identical in order and type, and no Ui file changed.

### 35. Twelfth combined patch, part 4: Android P-08 path labels (9 Nov, D-146)
- From platform/android 4260ca4 (`docs/android/proposal/P-08-path-labels.patch`): `IPathLabels` seam with `PlainPathLabels` default (the path itself) in Platform; every path shown in the Ui (titles, chips, folder rows) goes through it; drift-guard test reads the Ui's .razor files.
- The patch's registration hunk targeted the old `(Type, Type)` table; registered instead as one typed line, `Seam<IPathLabels, PlainPathLabels>`, per §34. `TypedRegistrationTests` gains the new row.
- 333/333 tests (Hosting 66); 0 warnings in both solutions; trimmed probe 54/54; desktop 27/27 identical (Windows shows the same paths as before); drift guard proved by putting one raw `title="@entry.Path"` back: it fails naming Merge.razor:41.

### 36. Thirteenth combined patch, part 1 (9 Nov evening)
**Linux (platform/linux 2cb9c35, the f84c9ed diff rebased on c91a581):**
- B-24 reopened: the Reader no longer opens each new tab's file on the page's thread (`RefreshDocKind` / `_isXfaDoc` removed; nothing read them; the XFA notice uses the session).
- B-25: viewer errors follow WT-10. A plain sentence, the system's text under a collapsed **Details**, and one shared access sentence (`TabsService.CantRead`).
- S3 (Linux step 31b, found after D-139): the Print card gets the tab's password, so a file opened with its owner password prints. **Windows has the same fix:** before, it opened the file to print with no password and failed.

**Mac (platform/mac `docs/mac/foundation/*.patch`):**
- M-01 / M-11: the shell's own commands (Open a file…, Settings, Go to, modes, themes, tabs) reach `IAppCommands`, so the macOS menu bar has File › Open… and Settings.
  - Foundation change: published only when `Chrome.NativeMenuBar` (macOS). Other hosts have no menu bar to fill, so there's no extra render at startup and nothing changes there.
- M-08 to M-10: ⌃Tab cycles tabs, labelled ⌃Tab on a Mac (on Windows, Ctrl+Tab does exactly what it did); F11 is labelled ⌃⌘F on a Mac; the green light is Zoom in both states.
- D-132: privacy notice A2a for the macOS error log.

**Android Test (D-149):**
- AT-10 (Find drops typed characters): every typed field used `value="@x"` + `@oninput`, so each answer from the app wrote the field back to what it had seen so far. Now `@bind` / `@bind:get`/`@bind:set` with `@bind:event="oninput"` (Reader Find, viewer Find, bookmark rename, Library search and tags, Redact find, Bench search/range/bands, band editor, preferred name, Ctrl K).
  - `tools/PixelProof/find-typing.js`, with the round trip slowed: before "burstdt", "butwtt", "burwdt"; after "burstwordtest" in all three fields, 3 runs.
  - Guard: `TypingFieldsTests` fails if the old pairing comes back (proved: it names Library.razor:29 and :133 on the old file).
- AT-13 (a heavy image page sometimes never paints, CPU idle): not reproduced here, since it needs the device. Code review found a way a page stays a placeholder for good: in-flight renders were remembered by page number and never forgotten, so after the tab's document was reopened in place (Save in place, unlock) with the same page count, or a render landed in a replaced session, that page was never asked for again.
  - Now keyed by (session, page), forgotten when the render ends; a new session for the same tab re-arms the observers.
  - Still open for the device: the page render runs on the Ui thread (a 6000 × 6000 image takes 4 s and holds up the next open). Options are with the coordinator.

**Checks:**
- 342/342 tests (Hosting 75);
- 0 warnings in both solutions;
- trimmed probe 54/54;
- desktop 27/27 identical;
- StartupProbe the same (JIT 1453 both; times within noise).

### 37. Fourteenth combined patch: pages render off the Ui thread (9 Nov, D-152, AT-13)
- `ChuvadiPdfSession.RenderPageSvgAsync` draws the page on a worker thread, one page at a time per document (a per-session gate, requests in the order asked). A heavy page no longer holds up the Ui or the next open.
- **Why it's safe:** chuvadi-pdf says a `PdfDocument` is not for concurrent use. So after the open, every use of the session's document holds the same gate:
  - the render;
  - a measurement (`TryUseDocument`, which never waits: during a render it skips, and the next pointer move measures);
  - closing (`Dispose` never waits; a render in progress closes the document when it ends, and nothing new starts).
  - Layers are read once, at the open.
  - The page cache is locked.
  - A page drawn for layers that changed meanwhile isn't kept.
- Redact's lazy render keyed by (session, page) and forgotten when done, as the Reader's (§36).
- **Measured** with a 26 MB file holding one 6000 × 6000 JPEG, as AT-13: the call took 6549 ms to return before (the Ui thread blocked for the whole render); now 1.9 ms, with the page ready at 7.1 s. Its SVG is 4.0 MB after the raster fallback, from a ~35 MB vector SVG first, so the library ask (C, decode at display size) stands.
- **Guard:** `RenderOffUiThreadTests` (6 stall tests).
  - Rendering on the caller's thread fails 3 of them (caller held, two documents can't render together, measure held).
  - The other 3 pin one-at-a-time and order, closing mid-render, and dropping a stale-layer page.
- 348/348 tests (Reader 218); 0 warnings in both solutions; trimmed probe 54/54; desktop 27/27 identical; find-typing 3/3.

### 38. The osx-arm64 trimmed-launch "hang" on 556090b (9 Nov, coordinator 22:28)
- **What it was:** run 42654252893, first attempt. The osx-arm64 job has no runner name and no steps: it never got a macOS runner and sat in the queue until it was cancelled. On the re-run it also queued for 5 min before starting. Nothing ran, so nothing could hang.
  - That job runs only `tools/TrimProbe`, which builds the services and never renders a page, so D-152's code isn't on its path at all.
- **Could the render queue and a Ui dispatcher wait on each other? No.** A cycle needs the Ui thread to block on something the render side holds. Nothing on the Ui thread blocks:
  - `Dispose` and `TryUseDocument` take the gate with `Wait(0)` and never wait;
  - the render runs in `Task.Run` and never posts to the Ui;
  - its continuations use `ConfigureAwait(false)` inside the session;
  - callers only `await`; nothing uses `.Result` or `.Wait()` on a render.
- **Guard added anyway:** two tests on a one-thread dispatcher (posted work runs in order, as the WPF, Mac Catalyst, GTK and Android Ui threads do):
  - the Ui thread keeps answering (measure, cache, layer toggle, drop cache) while pages render, and each render comes back to it;
  - closing a tab on the Ui thread mid-render never blocks it.
  - Proved by making the gate block (`Wait(Timeout.Infinite)`): the suite goes red on the 5 s guard.
- 350/350 tests. Tests only; no behaviour change.

### 39. Fifteenth combined patch: fast paging (Linux on the fourteenth, platform/linux ba94dfa)
- **The finding:** every page entering the view queued a render and nothing withdrew it, and renders ran in order, so after 150 fast PageDowns the landed page waited behind the backlog (about 2 s on the CAD PDF, with memory growing).
- **Session (`ChuvadiPdfSession`):** renders wait in the session's own queue, not on the gate.
  - A render that hasn't started is dropped when its token is cancelled; nothing is drawn for it, and its task reports cancelled.
  - The worker takes the waiting page nearest the focus page (`IPdfSession.SetFocusPage`, a default no-op for other sessions), oldest first among equals. With no focus set (Bench, Redact) it's the order asked, as before.
  - Still one worker and the same gate, so D-152's guarantees stand: the document is never used by two threads, and nothing on the Ui waits.
- **Reader:**
  - one token per (session, page) render;
  - `PageLeft` (from the page observer, sent only for pages it asked for, so a long document doesn't send one message per far page) cancels it;
  - `OnPageVisible` sets the focus page;
  - a changed tab or reopened document cancels everything waiting.
- **Guard tests:**
  - 150 fast PageDowns at 40 ms a page: the landed page is drawn within the first 3, and at most 4 pages are drawn in all;
  - waiting pages drawn nearest the page in view first (exact order pinned);
  - a cancelled render never draws.
  - With the old queue (in order, not cancellable) all 3 fail.
- 353/353 tests; 0 warnings in both solutions; trimmed probe 54/54; desktop 27/27 identical (Home's relative-time artefact cleared on re-run).
- **Behaviour on every platform, Linux included:** pages render nearest the one in view first, and pages scrolled past aren't drawn.

### 40. Sixteenth combined patch: memory after closing a heavy document (Linux 36c, platform/linux-test ba94dfa note)
Measured with Linux's own CAD stand-in (`tools/linux/make-heavy-pdfs.py`, run here test-only), with a GC heap dump (dotnet-gcdump / dotnet-dump, testing tools, never shipped) after closing:

| After closing (one or two A1 sheets) | Live managed | Resident | Who held it |
|---|---|---|---|
| Before this patch | 205 MB | 1666 MB | a 134 MB `char[]` in the shared array pool, plus memory the runtime kept |
| Pool fix only | 77 MB* | 1404–1745 MB | memory the runtime kept for later use (committed, nothing alive in it) |
| Pool fix + relief | 77 MB* | 127–129 MB | — |

\* The 77 MB left was the probe's own stack slot holding its last page (gcroot: the probe's `Render` frame), not the app.

- **Our code, fixed:**
  1. The raster page's SVG was built three times over (base64, data URI, then an interpolated string that grew through `ArrayPool<char>.Shared`). The pool kept a 134 MB buffer for the life of the app, and three 77 MB copies existed at once while drawing.
     - Now `RasterSvg` writes the base64 straight into the final string, in one allocation, with the same characters as before (pinned against the old formula for 7 lengths).
  2. The runtime keeps the memory a big drawing used, even with nothing alive in it, so the app stayed near 1.7 GB after the tab closed.
     - `MemoryRelief`: when a closed document's pages held ≥ 64 MB, one aggressive collection runs on a worker 0.5 s after the close, at most once per 30 s (so a shared Web server never runs them back to back).
     - Measured: 1745 → 129 MB resident, a 74 ms pause.
- **Closing releases everything of ours:**
  - the session's page cache is cleared at close;
  - queued renders end empty;
  - an in-flight render closes the document when it ends;
  - session and document were collected (weak references dead).
- **Test (`MemoryAfterCloseTests`, run alone):** a large-format page of noise (≈ 96 MB of page SVG, the raster path the CAD sheets take). After close, live memory is within 16 MB of where it was, and memory kept by the runtime within 64 MB. It fails without the relief (563 → 655 MB kept) and with the old string building (1 → 129 MB live).
- **Left for the library (with B-27):**
  - drawing one A1 sheet of 120,000 strokes peaks at about 1.8 GB resident inside the rasteriser, and takes about 37 s here;
  - a page holding one big image first comes out as a vector SVG of about 35 MB before the raster fallback.
- 356/356 tests; 0 warnings in both solutions; trimmed probe 54/54.
- **Every platform, Linux and Web included:** that one collection after a heavy close.

### 41. Sixteenth combined patch, part 2: what allocated after the close (Linux step 47, platform/linux 64c9c16)
Reproduced in the desktop preview, which now opens real files (`CHVD_OPEN`, test-only). A cold app with the two sample PDFs and Linux's `cad.pdf` active: two sheets drawn, then Ctrl+W. Sampled with `tools/PixelProof/close-memory.js`, dotnet-counters and heap dumps (testing tools only).

1. **A sheet kept drawing for the closed tab.** The page observer had already asked for sheet 3 (1000 px ahead), and the gate drew it to the end: about 55 MB/s allocated at one core's CPU for 33 s after the close, then a burst at its PNG encode. On Linux, where a sheet takes about 0.3 s, that is the "back up within 2 s".
   - **Fixed:** the page being drawn stops when its page leaves the view and nobody else waits for it, or when the document closes. The library's rasteriser takes the token (`PageRasterizer.Rasterize(page, token)`).
   - Whoever asks for that page after the stop waits for a fresh draw.
   - Vector pages drawn by `SvgRenderer` can't be stopped inside the library: it takes no token. **Library item.**
   - After the fix, allocation and CPU drop to 0 within 1 s of the close.
2. **The relief didn't run for vector sheets** (their kept SVG is small).
   - **Fixed:** a document also counts as heavy when its renders allocated ≥ 256 MB (library included; counted on the render thread). A blank A1 sheet costs 277 MB to draw and keeps 0.1 M characters.
3. **Pooled buffers held by the Ui's transport.** After the close, about 260 MB stayed alive in .NET's shared byte pool: arrays of 8, 16, 32, 64 and 128 MB, grown while sending the pages' text to the Ui. No gcroot from our code. The pool lets go of a buffer only at a full collection after it has gone unused for about a minute.
   - **Fixed:** the relief does a second pass 65 s after the first.
   - Measured: resident memory 1198 MB before close → 623 MB at 0.5 s → **167 MB after the second pass** (cold start 114 MB, with the two sample PDFs still open).
   - The cause is page images travelling as huge strings; options are with the coordinator.

- **Tests (`MemoryAfterCloseTests`, run alone, 6):**
  - closing mid-sheet stops the drawing (well under half a sheet's time);
  - a sheet that leaves the view mid-draw stops, and the next sheet is ready within 1.4 sheet-times;
  - a costly but small-keeping document gets the relief.
  - The first two fail when the rasteriser is given no token; the third fails without the drawing-cost rule.
- 360/360 tests (3 runs of the Reader suite); 0 warnings in both solutions; trimmed probe 54/54.
- **Every platform, Linux included:** the drawing stops, and the second pass runs.

### 42. Sixteenth combined patch, part 3: the drawing line at 64 MB (Linux proposal, platform/linux 2f6a803, step 48)
- `MemoryRelief.HeavyDrawingBytes` 256 → 64 MB, Linux's one-line diff. Linux, cold 36c: memory back in 6 of 6 runs (5 of 7 before; the misses had allocated just under 256 MB). Warm full pass 121/121.
- **Paging never runs a collection** (new test `Paging_never_runs_a_collection_only_closing_a_heavy_document_does`): drawing, pages cancelled as they leave the view, the focus page, dropping a background tab's cache and a layer reset leave the relief alone, however heavy the drawing. Closing is what asks.
  - Proved by making a cache drop ask: the test fails.
  - The only caller is `CloseDocument`.
- **Ordinary documents don't qualify:** a text page costs about 0.04 MB to draw, so about 1,600 such pages would reach 64 MB.
- **No startup cost:** nothing on the startup path changed; the relief code runs only when a document closes.
- **Two drawings closed in a row** (Linux's note): the second close is skipped while the first relief runs, but that relief's second pass, 65 s later, collects for both.
- 361/361 tests (2 runs); 0 warnings in both solutions; trimmed probe 54/54.

### 43. Eighteenth combined patch (10 Nov)
1. **D-156: a second heavy close inside the gap waits for the relief instead of being dropped.** Linux's exact diff (`PROPOSAL-memory-relief-defer-10nov.md`, platform/linux ec108e7, on a203a62), applied unchanged, with its test.
   - Tests now set `MinGap` to zero in the memory class's constructor, so one test's relief never makes the next wait 30 s.
2. **B-30 (Linux Test, on 64c9c16): nothing in Core holds a closed document.** New retention test `Nothing_holds_a_closed_document_its_pages_or_its_queued_renders`:
   - a tab opened through `TabsService`, a page drawn and cached, one sheet mid-draw and one queued, then the tab closed;
   - the session, the `PdfDocument` and the drawn page's string are all collected;
   - live memory is within 16 MB of where it started.
   - It passes on add7b05 (before 4f67a4c) too, so the 680 MB that B-30 saw after a full collection on 64c9c16 wasn't a stuck reference. It was the sheets still drawing for the closed tab (4f67a4c stops them; on 64c9c16 a real-CAD sheet takes 10–13 s, so at 60 s the background sheets could still be drawing), and the shared byte pool's buffers from the Ui's transport, which go at the 65 s second pass (§41; the preview showed the heap's large arrays fall from ~423 MB to ~2 MB).
   - **For Linux Test:** measure 70 s or more after the close, past the second pass. At 60 s the pool's buffers are still there by design.
3. **B-29: the closed tab's raster pass is cancelled mid-sheet.** New test `Closing_mid_sheet_never_holds_up_the_next_file`: an A1 stroked sheet (a real `PageRasterizer` pass) closed 0.3 s in, then the next file opened.
   - The next file's page is ready within a second of its time alone.
   - The closed sheet has stopped by then (< 1 s).
   - On add7b05 it fails: the closed sheet was still drawing 5.2 s after the next file was ready.
4. **B-27 for the library list** (Linux Test): draw time is in the library rasteriser (`PaintStrokeOp` under `RasterizeInternal`). Real CAD sheet 1 takes 6–8 s and sheets 2–3 take 10–13 s, against about 1 s in Poppler; the stand-in takes 53 s. Nothing to change in the app.

- 363/363 tests (2 runs); 0 warnings in both solutions; trimmed probe 54/54.

### 44. Nineteenth combined patch: Linux's render findings (`PROPOSAL-render-findings-10nov.md`, platform/linux 8dcf4d8)
1. **A page back in view after its render was cancelled is drawn.** Linux's one-line diff, unchanged: a cancelled entry in `_rendering` counts as gone, so the page is asked for again. Pinned by a source guard. The Core side already gives a page asked for after a stop a fresh draw (§41).
2. **Moving to a page while the heights change lands on that page** (`reader.js`).
   - The view keeps an anchor: the page at the top and how far into it the view starts, or the page asked for after a jump.
   - A `ResizeObserver` on the pages puts it back whenever page heights change: the fit zoom landing, or a drawing replacing its placeholder.
   - The page box is reported once the jump's quiet time is over (it kept showing 1).
   - Gesture zooms (pinch, double-tap, marquee) still place the view themselves, and the anchor is re-read after them.
   - `tools/PixelProof/page-anchor.js` (continuous mode, with Chromium's own scroll anchoring off, as in WebKit, which has none): **before 0/3, after 3/3** (twice). Cases: jump then the fit shrinks the pages; the fit grows them mid-jump; scrolled by hand then the heights change.
   - On Windows, WebView2 already anchors natively, so its jumps behave as before. Desktop screens identical (every screen identical in at least one of 3 runs; the 1× relative-time and scrollbar artefacts move between runs).
3. **Memory after closing: the relief ran before the drawing was garbage.**
   - **Found:** Blazor keeps the render before last for diffing. One render after the document on show changes still holds the old document's page text (≈ 77 MB per CAD sheet); a second render lets it go. `PageReleaseTests` (Web Ui tests, real `PageSvg`, a minimal renderer) pins both halves: after one render the page is still alive, after a second it isn't.
   - **Fixed:** the Reader renders once more after the document on show changes, and keys every `PageSvg` by its document, so the old document's page components are disposed instead of reused. The page text goes at the close, before the 0.5 s relief.
   - **Outside the .NET heap** (Linux's ~150 MB after a full collection): none of it is ours or chuvadi-pdf's (both managed only). Here, without WebKit, resident memory comes back to within ~50 MB of a cold start (§41).
     - On Linux the rest is WebKitGTK and GLib in the app's process, and freed C memory that glibc keeps until `malloc_trim(0)`.
     - New `MemoryReliefNotice.Relieved` (Core, public static event) is raised after each relief pass, so the Linux host can call `malloc_trim(0)` there. That's the Linux chat's one line.
- 366/366 tests (2 runs); 0 warnings in both solutions; trimmed probe 54/54; find-typing 3/3.

### 45. Twentieth combined patch: the relief pass waits for the finalizer thread (D-158 part 1)
- **Linux 36c on the nineteenth patch:** 80 s after closing, the heap still held 681–807 MB that one more aggressive collection cleared.
- **Cause:** the shared byte pool lets go of idle buffers in a callback that runs on the finalizer thread *after* a full collection. The second pass collected again at once, so the buffers it trimmed became garbage just afterwards, and an idle app ran no further collection.
  - Reproduced with a test-only console program: buffers grown 8 → 128 MB on four pool threads (744 MB pooled), idle 62 s.
  - Today's pass left 248 MB live, which a later collection took down to 128 MB. With the wait, 128 MB right after the pass.
  - The pool keeps ~128 MB per long-lived thread whatever we do. D-158 part 2 (page images by address) removes the huge strings that grow these buffers in the first place.
- **Fix:** `MemoryRelief.Pass()`, used by both passes: two full collections, each followed by `GC.WaitForPendingFinalizers()`, then the aggressive one, then `MemoryReliefNotice`.
- **Test:** `A_relief_pass_also_frees_what_finalizers_release_during_it`. A stand-in drops a 96 MB buffer from its finalizer; after one pass, the buffer is gone. Without the waits it fails or passes depending on the race (1 of 2 runs failed). With them, 3 of 3 pass.
- 367/367 tests; 0 warnings in both solutions.

### 46. Twenty-first combined patch: page images by address (D-158 part 2)
See `docs/foundation/PAGE-IMAGES.md` for the design, the hooks per host and the proof.
- **Core:**
  - `PageImages` store;
  - `ChuvadiPdfReader(PageImages, IPageImageOwner)`: raster pages are kept in the store and drawn from `_chvd/pages/<token>.png`. The parameterless reader still draws inline, for tools and the existing tests.
- **Platform:** `IPageImageOwner` with `LocalPageImageOwner` (`"local"`).
- **Hosting:**
  - `PageImageFileProvider`;
  - registrations `Seam<IPageImageOwner, LocalPageImageOwner>` and `Shared<PageImages>` (one per process, whatever the lifetime);
  - a reference to `Microsoft.Extensions.FileProviders.Abstractions`, already in every WebView host.
- **Windows:** `ChuvadiWebView`.
- **Preview:** the endpoint, with the same rules as the portal's except sign-in.
- Pixel-identical raster page (1× and 2×, twice); preview memory 2354 → 438 MB with the same pages shown.
- 385/385 tests (2 runs); 0 warnings in both solutions; trimmed probe 56/56.

### 47. Twenty-second combined patch: H-20 (Mobile UI, platform/mobile-ui c5c0b7e; D-134, D-151)
- Mobile UI's `H20-tools-rail-heading.patch`, unchanged: `SideRail` gains `Label` (its `aria-label` is `Label ?? Heading`). The five tools rails show **TOOLS** and keep their mode name as the label (READ, MARK, PROTECT twice, ARRANGE), so a screen reader hears what it heard before. The view-only document's rail already said TOOLS.
- Guard: `ToolsRailHeadingTests` (all six tools rails say TOOLS; the five mode names stay as labels; the rail's aria-label reads `Label ?? Heading`).
- **Windows behaviour change (D-134):** the tools rail heading reads TOOLS instead of READ/MARK/PROTECT/ARRANGE. Desktop before/after: only the rail heading differs (≈ 40 × 6 px at 1×, at the rail's top), plus Home's clock artefact. Every other screen identical.
- 386/386 tests; 0 warnings in both solutions.

### 48. PR #13's first Windows run: a test measured its own stack (10 Nov)
- **The failure:** `Closing_a_heavy_document_brings_memory_back_near_where_it_was` failed once on windows-latest with 72 MB still live after the close (limit 16 MB). The re-run passed. Not H-20's (rail markup only).
- **Found by reproducing the pattern and taking a heap dump:** `gcroot` on the 72 MB page string led to a stack slot of the test helper's own async frame (`MoveNext` of the helper that drew the page). When the drawn page's task completes, the test method can carry on inline, on top of that frame, and measures memory while the frame still holds the page. Whether it runs inline depends on timing, which is why the failure was intermittent. Nothing in the app held it.
- **Fixed in the test:** `await Task.Yield()` after the helper and after the relief, so the measurement runs on a fresh stack. No production change.
- 386/386 tests; 0 warnings.


### 49. Twenty-third combined patch: Android Test round 5 — Mark with a finger (AT-15), the page box after opening a document, Find (AT-09) (10 Nov)
- **AT-15, found:** Android's host says `Pen` always (`AndroidHostServices.Input`: Touch | Pen | Keyboard), so `<html data-input>` has "pen" on every phone and tablet. The pen split (design 12e, D-42: the pen marks, a finger pans) then took every finger that started on the markup layer: it stopped the pointerdown and captured the pointer to the desk. That is exactly Android Test's log (pointerdown on `.mk-layer`, pointerup on `.rdesk`). **Fixed (reader.js):** the split starts only after a pen has touched the desk in this run; until then a finger marks. A host says "pen" when the system supports one, not when one is there.
- **AT-15, second point (the awaited rectangle):** every markup press (`MkDown`, the move/resize starts for text, shapes, lines and images, and Redact's `MkDown`) is now a tracked task; `MkUp` waits for a press still in flight and applies the lift point as the last move. Not reproducible in the preview (Blazor Server sends one event at a time); kept because Android's WebView may not, and it does nothing when the press is done.
- **Page box drift, found:** the desk is shared by every tab, so a document shown for the first time kept the last document's scroll offset; and since the nineteenth patch (f88f9ef) the last document's anchor page was carried into the next `init`, pulling the new one to that page as its heights changed — "page 1, then 4 of 4 two seconds later". **Fixed:** `init` takes `newDocument` (set when the active tab changes) and starts with no anchor; a document shown for the first time is put at its top before the fit.
- **AT-09 on tablet, found:** with the page taller than the view, the Find scroll was undone by a page drawn during it (the anchor still pointed at the page's top), and a match at a page's foot placed a third of the way down left the next page covering most of the view, so the page box named that page. **Fixed (scrollToMatch):** the anchor is the match's place, and the view stays on the match's page where the match allows (it ends at the page's foot instead of showing the next page). Snapping pages are left to the snap.
- **Proof:** `tools/PixelProof/touch-markup.js` (tablet preview; finger tap → text box, finger drag → rectangle, slow rectangle read, after a pen a finger pans): before 0/5, after 5/5, 4 runs. `tools/PixelProof/page-box.js` (tablet preview, Android Test's `doc-12p.pdf` and a 4-page PDF, one fresh preview per case): before 1/5 (opening after page 9, 3, 12: box 4, 3, 4; Find at fit width: match off screen), after 5/5.
- **Guards:** `TouchMarkupTests` (the split waits for a pen; every press is tracked and `MkUp` waits), `PageBoxTests` (`init` drops the anchor for another document; the first show starts at the top; Find anchors the match and stays on its page). Both fail on 846706e.
- **Windows behaviour changes:** a document shown for the first time opens at its top (it kept the last tab's scroll offset); Find on a zoomed or tall page keeps the view on the match's page and the match stays put while pages draw. The pen split never runs on Windows (no "pen"). Desktop before/after: every screen identical apart from Home's clock artefact.
- **Linux:** the same three changes apply (shared Ui); Linux declares no pen, so only the first-show and Find changes are seen there.
- AT-13 (renders off the UI thread, re-render after reopen): the render and memory tests 25 runs in a row, see the status.

### 50. Twenty-fourth combined patch: show what the file holds — Excel number formats (D-161), the CropBox everywhere (D-160 revised), the audit, and the Linux fresh-open stall (10 Nov)
- **Excel (D-161):** every cell shows Chuvadi.Sheets' display text (`Cell.GetDisplayText`) in the system culture (`NumberFormatOptions.Culture = CultureInfo.CurrentCulture`; en-IN gives 12,34,567 and ₹ 12,34,567.50), colour sections colour the text, a conditional format's number format, fill and font apply, and a `*` fill keeps an accounting symbol at the left edge. The stored value stays underneath, unchanged (`SheetCell.Value`: numbers in their shortest exact form, dates as the stored day count, TRUE/FALSE, text as written): copy all and Find use it, hovering a cell shows it, and clicking or tapping a cell shows it in a value bar above the grid. Numbers and dates align right, booleans centre. No formatting is done in the app. **The Web host must set the request's culture to the person's** (the server's own culture is not theirs).
- **What the library can't show yet** is listed as SH-L1 to SH-L10 in `FILE-FIDELITY-AUDIT.md`; the two that matter most: time-only cells show `########` and lose their value (SH-L1), and built-in date 14 shows 03-15-23 on en-IN where Excel shows 15-03-2023 (SH-L2).
- **CropBox (D-160 revised):** chuvadi-pdf 3.32.0 already draws the CropBox and nothing passes another box. But everything the Reader placed on a page still used the MediaBox, so on a cropped page it landed elsewhere. New `ShownBox` (CropBox within MediaBox, inherited ones too); redaction boxes, text, shape and image markups, page numbers, the bench's crop, margins, resize and N-up, measuring, redaction search boxes, the raster page's SVG size (it was stretched) and Properties' page size all use it. Files whose boxes are equal are unchanged, byte for byte paths included (`AtOrigin`). Library: headers/footers and watermarks still sit on the MediaBox (PDF-L1, PDF-L2).
- **Audit:** `docs/foundation/FILE-FIDELITY-AUDIT.md` — 17 Excel, 8 PDF, 5 Word and 4 Markdown/text/HTML items, each with a fix or a library item, and three questions for Arun.
- **Linux fresh-open stall (bf60da9), found by reproducing it** (`tools/PixelProof/fresh-open.js`, preview `POST /__preview/open`): when a document finished loading between a render and its `OnAfterRender`, the observers were armed with the previous render's desk element, which the loading view had already removed. `init` failed silently, so no page was ever asked for and the fit measured nothing — the portrait placeholder at the old zoom, for good. Before: 10 of 13 fresh opens never drew sheet 1. **Fixed:** armed only after a render that drew the desk for this generation, retried if arming fails; and a first page that takes longer than the fit's 2 s wait is fitted when it lands (unless the person zoomed meanwhile). After: 17 of 17 drawn and fitted.
- **Found on the way (anchor, nineteenth patch):** the anchor is taken 200 ms after scrolling stops, so a page that changed size just after a wheel or finger scroll pulled the view back to where it had been. Now a resize after the person's own scrolling takes the anchor afresh. `page-anchor.js` case 4: before FAIL (page 10, wanted 12), after PASS; cases 1–3 unchanged.
- **Tests:** `XlsxDisplayTests` (34, hand-written Excel en-IN results, and every stored value against the sheet's own XML), `OfficeViewTests` moved onto the stored value, `CropBoxTests` (11; 6 fail without the fix), `FreshOpenTests`. Three skipped tests name their library item (SH-L1, PDF-L1, PDF-L2). 438 pass, 3 skipped; 0 warnings in both solutions; trimmed probe 56 of 56; no startup cost (nothing runs before a workbook or cropped page is opened).
- **Windows behaviour changes:** Excel cells show their formats and the value bar appears on click; on PDFs with a CropBox, redaction, markups, numbering, bench tools, measuring and Properties follow the page as shown; a document opened while another loads is always drawn and fitted; scrolling is never pulled back by a page landing. Desktop screens before/after: identical (Home's known 1× artefact apart).
- **Linux:** all of the above apply. The fresh-open fix and the anchor change are the ones Linux Test should re-check (36c first run).

### 51. Twenty-fifth combined patch, parts 1–3 (D-173): AT-17, AT-16, the Android cold open (D-164), H-22, the Mac tab lock, RemoveOwner (WT-21), relief for Excel and Word (WT-19) (10–11 Nov)
- **AT-17 (Mark › Text by finger), reproduced** (`touch-markup.js`, before: the caret stayed on the page, "chuvadimark" ran one-key commands and left Mark). **Fixed:** the tap itself puts the caret in an unseen field (so a touch keyboard opens inside the gesture), and the new box takes it over when it is drawn, with anything typed in between (`focusNewText`); with a text box selected, a plain key goes into the box instead of to a one-key command (shell.js), and the Reader's bare keys stand down while the caret is in a field. After: caret in `TEXTAREA.mk-ta`, "chuvadimark 123" typed into the box.
- **AT-16 (tablet page box after Find): not reproduced here** (Chromium, `page-box.js find`, `find-width` and the new `find-keyboard` all show 3 before and after). Two ways the box could be wrong are removed: the page in view is now measured from where the pages are (halving to the first page in view), not from the observer's table, which is filled a moment late so a sliver of the page before could win; and every jump reports the page on screen when its quiet time ends (a jump that ended early sent nothing). Find's second scroll gets its own quiet time. Android Test to confirm on the tablet.
- **D-164 (A): the incoming file is not parked.** `IncomingFiles` asks for the open as the file arrives (the request queue keeps the order); only `Delivered` (bring the window forward) waits for the first render, and a launch without a file still brings nothing forward. `MainLayout` starts on the Reader when a request is waiting, so Home isn't drawn first and thrown away. The shell's own loads (folders, profile, theme, shell.js) run as before, alongside the open; their order is unchanged, so a normal start is unchanged.
- **D-164 (C), the tablet's 2 s before the first render request:** the timeline was measured before patch 24. Its length is the fit's 2 s wait for a drawn page, which only shows when the observers weren't armed — the fresh-open fault patch 24 fixed (armed with a desk the loading view had removed). On 741930b a small PDF opened into a running tablet preview was never drawn in 1 of 6 runs; now 6 of 6 draw in 34–121 ms after the page boxes. No 2 s gap seen here; Android Test to re-measure.
- **H-22** (Mobile UI `mobile-ui-hooks-9.patch`, D-163: page-tile actions in the desk menu; Bench.razor only) applied as sent. **Mac** `TabsService-overlapping-opens.patch` (a lock around tab-list changes, two tests) applied as sent.
- **WT-21:** `PageImages.RemoveOwner(owner)` frees every image a session drew; other owners' stay. Web calls it at session end.
- **WT-19:** reading a workbook or a Word file reports what it allocated (`LoadedBytes`); the viewer hands that to `ViewMemory.Released` when the view is let go (tab closed, another file, reload), and a stopped or refused open does the same; 64 MB or more asks for the relief PDFs get. Measured: a 120,000 × 6 workbook, live and committed memory back near where they were (3 runs).
- **Tests:** router (3), tab concurrency (Mac's 2), RemoveOwner, view memory (3). 446 pass, 3 skipped (library items); 0 warnings in both solutions; trimmed probe 56 of 56. Startup: nothing added to a normal start; a start with a file skips Home.
- **Windows behaviour changes:** started with a file (double-click, Open with), the app opens on the Reader instead of Home first; a new text box has the caret at once and typing with a text box selected goes into it; the page box after a jump always names the page on screen; closing a heavy Excel or Word file gives its memory back. Desktop screens before/after: identical (Home's known 1× artefact apart).
- **Linux:** the same. **Web:** call `RemoveOwner` at session end. **Waiting:** part 4 (Docs 2.1.0) for the package and hash in `docs/coordination/library/`.

### 52. Twenty-fifth combined patch, part 4: Docs 2.1.0 (D-168), and two faults the Windows runner showed (11 Nov)
- **Docs 2.1.0** (hash and tag checked, `PACKAGES-DOCS-2.1.0.md`): Word files open with the library's limits for files somebody was sent (`ForUntrustedInput`: ratio 200:1, depth 128, 1,000,000 elements a read), with the D-136 server sizes and D-137 local sizes on top (locally the file itself may be 400 MB; the library's 50 MB applies on the server). The Reader reads in one `Document.Load` call with the open's token, so the reader-lifetime token needs no change. Optional parts the library had to skip are named above the page (`WordView.LeftOut`, from `Document.SkippedParts`). A damaged protected file is shown as damaged, not as a wrong password.
- **Memory test on Windows (first item, 10 Nov), found:** not the render worker (a hand-over fix still failed 3 of 6 and 8 of 10 on the runner) and not background collection (fails with it off). The tests set `MemoryRelief.Settle` to zero, so the measuring collections ran back to back and one of them stopped a thread that still had the closed page on its stack; the page was gone 3–13 ms later. The app waits 500 ms before it collects, so it never kept a closed heavy page. **Fixed:** the three measuring tests run with the shipped settle (`MemoryRelief.ShippedSettle`); thresholds unchanged. Windows runner: 16 full runs, memory tests clean.
- **Tab saves, found in the same 16 runs** (`Overlapping_opens_from_several_threads…` failed once): each overlapping open saved the tab list from its own thread, so saves ran side by side. In the app an older list could be written after a newer one, leaving a tab out of the next start; in the tests the in-memory store is a plain dictionary. **Fixed:** `TabsService` saves one at a time, each with the list as it is when its turn comes. New test (a store that counts overlapping saves): 15 overlaps before, 0 after, and the last save holds every tab.
- **Tests:** 453 pass, 3 skipped (library items); 0 warnings in both solutions (built from an empty package cache); trimmed probe 56 of 56. Startup: nothing added.
- **Windows behaviour changes:** a Word file with a missing or damaged header, footer or picture says so above the page; a damaged protected Word file says it couldn't be read; Word files nested deeper than 128 levels, packed tighter than 200:1 or with more than a million elements are refused as too large. Desktop screens before/after: identical apart from the Docs version in About (and Home's known 1× artefact).
- **Linux, Mac, Android, Web:** the same.

### 53. Twenty-sixth combined patch (part 5, one PR, D-176 revised): /Rotate on screen (B-32), the Excel grid as the file holds it (audit E-05 to E-16, D-171, D-172), the value bar and tab fixes, text encoding (11 Nov)
- **Base:** Reader main dd7138a (Windows PR 15 brought in). One PR, a commit per step.
- **Excel value bar (H-23 and the Web portal UI diff, one fault):** the tap listener was asked for once, in a render that could run before the grid existed. Web portal UI's diff applied as sent (the script says whether it is on; asked again until it is). It replaces Mobile UI's H-23.
- **Word left-out note margins:** Web portal UI's one-rule diff, as sent.
- **WT-22, the shared Core part:** `TabsService` is disposable and closes every document it holds when its owner goes; the saved tab list stays. Keeping tabs across a page reload (D-174) is Web's: one tab service per session.
- **B-32 (`/Rotate`), found:** shared Core, not the library. The renderers have `ApplyPageRotation`, off by default; the Reader never asked for it, while redaction, shapes and measuring already allowed for the turn (so on a turned page they were also misplaced). **Fixed:** screen (SVG and the large-sheet image) and image/SVG export turn the page; page numbers, text and pictures added in Mark are drawn on the page as shown and then placed on the page's own space (`ShownBox.FromShown`), so they stay upright in their box; Properties gives the size as shown; the bench's crop, mask, margins, resize, N-up and booklet rebuild a turned page as shown (`ShownBox.ToShown`). On screen (`tools/PixelProof/page-rotate.js`): before 1 of 4, after 4 of 4. **Library items:** PDF-L6 (the rasteriser draws nothing for a picture placed with a quarter or half turn; print and image export miss it, the file and the screen are right; checked with Poppler, testing only), PDF-L7 (header/footer ignores `/Rotate`), PDF-L8 (watermark ignores `/Rotate`).
- **Excel grid, rebuilt as a window onto the sheet:** the old table drew every column and capped the sheet at 5,000 × 256. Now only what is on screen exists as elements, each cell placed from the file's own column widths (Excel's width-to-pixel rule) and row heights; headers and frozen rows and columns stay put. With that: any row and column (E-05), hidden rows and columns (E-06), merged cells (E-07), the file's sizes (E-08), text running over empty neighbours with no ellipsis (E-09), no forced bold (E-10), borders, pattern and gradient fills, strike, super/subscript, double underline, indent, rotation, on empty cells too (E-11), rich text (E-12), data bars and icons (E-13), comments and links (E-15), gridlines off, right-to-left and the sheet's zoom (E-16). **D-171:** Find matches the stored value or the shown text. **D-172:** hidden sheets are shown, dimmed and marked. A sheet's pictures and charts are counted under the grid ("2 pictures or charts aren't shown").
- **Not done, said plainly** (each in `FILE-FIDELITY-AUDIT.md`): shrink-to-fit, auto-fitted row heights and room for turned text (need text measuring), diagonal borders, exact dash patterns, Excel's own icon pictures (ours are a likeness), pictures and charts (Layout), a merged range or running text that crosses the frozen line is cut there, a number too wide for its cell is cut where Excel shows ##### (SH-L7), a link is shown, not followed. A sheet used to its very last row (1,048,576) is 21 million pixels tall at 100 %, near the largest a browser lays out; above about 150 % zoom such a sheet may not scroll to its end.
- **B-31 / SH-L1:** purely the library, and in loading, not formatting: a stored 0.75 loads as 1899-12-30 18:00, which `ToSerial` gives back as -0.25. The exact ask is `LIBRARY-ASK-SHEETS-SH-L1.md`.
- **Text encoding (T-01):** a file that isn't Unicode is read as Windows-1252 (27 characters mapped in Core; no code-page provider, nothing added to a start).
- **Proof:** `tools/PixelProof/excel-grid.js` 19 of 19 (sizes, hidden, merged, overflow, rich text, bars and icons, borders, comment, link, a cell at row 6,000 column 300 drawn when scrolled to, frozen parts stay, 278 elements drawn on that sheet, Find both ways and scrolling to a far hit, right-to-left, gridlines off, zoom). New tests: `PageRotateTests` (49, 3 skipped for PDF-L6 to L8), `XlsxLayoutTests` (16), tab disposal and encoding.
- **For other chats:** Mobile UI's `mobile.js` and `checks.py` select `td.xv-cell`; cells are now `div.xv-cell` (same `data-a`, `data-v`, `.xv-valbar`), so the selector should be `.xv-cell`. Web: `TabsService` now closes its documents on dispose.

### 54. Pinch-to-zoom on every host (Arun's Linux laptop, 11 Nov)
- **What the shared Ui did:** the PDF view zoomed on a wheel event with Ctrl held (how WebView2 and Chromium deliver a trackpad pinch) and on two fingers on a touchscreen. It had no handling of WebKit's gesture events, and the Word, Excel, Markdown and text viewers had no pinch at all.
- **Now, in the shared Ui (`reader.js`):** one piece takes a trackpad pinch three ways: Ctrl + wheel; WebKit's `gesturestart` / `gesturechange` / `gestureend` (the Mac host, Safari), stopping the browser's own whole-page magnify; and a host passing its own gesture on, `window.chuvadiPinch(phase, scale, x, y)` with phase `begin`, `change` or `end`, the scale since `begin`, and the pointer in the page's CSS pixels (left out: the middle of the view). The PDF view and the other viewers both use it; the place under the pointer stays under it. The viewers' pinch (trackpad, Ctrl + wheel, and two fingers on a touchscreen) changes their text size, 60 to 200 %, which is their zoom.
- **For the Linux host:** WebKitGTK does not give the page a trackpad pinch, so the host has to catch GTK's zoom gesture on the web view and call `window.chuvadiPinch`. (That WebKitGTK sends nothing is the Foundation's understanding, not something it could try on a device.)
- **Not covered:** an HTML file shown rendered sits in its own frame, so a pinch over it doesn't reach the Reader; its source view does zoom.
- **Proof:** `tools/PixelProof/pinch-zoom.js`: Chromium 10 of 10, WebKit 7 of 7 (Ctrl + wheel is not played there). Before: only Ctrl + wheel on a PDF did anything. A two-finger pinch on the Word viewer on the tablet preview: 100 % to 167 %. Guards: `PinchZoomTests` (3).
- **Windows behaviour changes:** Ctrl + wheel and trackpad pinch now zoom Word, Excel, Markdown and text files (they did nothing there). PDFs are unchanged.
- **Linux, Mac, Android, Web:** the same; Mac and Safari gain trackpad pinch on PDFs.

### 55. Patch 27, added to PR 18: a mark lands under the pointer on every engine (Mac Test M-27, S1); repo `global.json` (D-178, D-186) (15 Nov)
- **M-27, read:** patch 26 changed nothing in pointer-to-page mapping (in the shared Ui it touched only the Excel view, the Word note's style and the viewers' script; its `/Rotate` work is in Core, which only receives fractions of the page). A pointer becomes a place on the page one way: (pointer − the mark layer's left) ÷ the layer's width, the rectangle read by the script (code from June, the same on every host). The page is zoomed with CSS zoom, and older WebKit reports a rectangle under it in the element's own unzoomed pixels: the mark then lands at pointer times zoom, which is M-27.
- **Fixed:** the script asks the engine once, with a hidden probe, which it does, and puts the rectangle right (`screenRect`); Reader marks and Redact boxes. No change on Chromium or current WebKit.
- **Proof:** `tools/PixelProof/mark-pointer.js`, a rectangle dragged between two screen points at three zooms, checked by asking the page what is under the pointer: Chromium and WebKit pass before and after; with rectangles reported as older WebKit does, before fails, after passes. That an older WebKit is what the Mac runners have is inferred from the symptom; Mac Test proves it on them. Guards: `MarkPointerTests` (3).
- **Still to check on such an engine:** other places that measure zoomed pages (the page box, Find scrolling to a match, fit) were not changed.
- **`global.json` (D-186, strict):** SDK 10.0.403, workload set 10.0.403, roll-forward disabled. **Found while adding it:** with the workload set pinned, no project builds, not even Core, until that set's manifests are installed (`MSB4242`, "10.0.403 (not installed)"). One command per machine, once, in the repo folder: `dotnet workload update` (6 seconds here). Every workflow reads the file (`global-json-file`) and runs that command after setting up the SDK.
- **Mac:** with PR 18 in, the Mac host's own pinch handler (`cf17eff`, which forwards the pinch as Ctrl + wheel) and the shared gesture handling may both zoom; one of them should go.

### 56. Patch 28: turned pages (Mobile UI F-1, F-2), hidden Find matches named (D-188), and four patches from Mobile UI, Web portal UI and Mac (16 Nov)
- **Base:** Reader main 04d97d7. One PR, a commit per step.
- **As sent, each its own commit:** Mobile UI H-24 (AT-22: a selected mark's card has a close control on tablet and phone; Mobile UI's own fix in 5a49864 keeps Save reachable, this hook is the part in the shared Reader page); Web portal UI's D-187 (Sign with certificate stays on the portal, greyed out with its reason) and its Find-box focus; Mac's M-28 (keys bound with no PDF opened yet; Ctrl or Cmd F opens the viewers' Find). M-28 also answers Web portal UI's finding that Ctrl F did nothing on Word, Excel, Markdown and text files, so no separate task remains. Mac's own M-27 patch (`docs/mac/foundation/M-27-webkit-css-zoom-rects.patch`) is not taken: patch 27 already has that fix.
- **F-1, found:** a Find match's boxes were turned into fractions of the unturned page, while the page is shown turned since patch 26. **Fixed in Core** (`ShownBox.Fractions`): the box is where the word is shown, on its side for a quarter turn; the highlight's extra depth goes across the text. Search-and-redact uses the same boxes, so the word found on a turned page is the word covered (it was misplaced there too).
- **F-2, found:** one zoom for the whole document, measured from the first drawn page. In a file whose pages differ in shape, a turned or wider page then ran off the side (549 px in a 412 px phone). **Fixed:** while Fit width or Fit page is on, a drawn page that needs another zoom to fit gets its own (`fitEachPage`, a `--rpage-zoom` on that page); pages of the fitted shape are untouched; a zoom of the person's own puts every page back on one zoom. The fit is measured from the page on screen.
- **D-188 (U-19 a):** Find matches of the sheet on screen that are in a hidden row or column are named above the grid: "2 matches are in hidden cells: A5 (hidden row), C6 (hidden column)"; the first five, then "and N more".
- **Proof:** `tools/PixelProof/patch28.js`, before 3 of 13, after 13 of 13 (F-1 on each turn, F-2 on a phone at 412 × 915, D-188, Ctrl F and the cursor on Excel and Word). Re-run after: pinch-zoom 11 of 11, mark-pointer 9 of 9, touch-markup 9 of 9, page-anchor 4 of 4, page-box 3 of 4 (the fourth, "open after page N", reads the page box as "N" before this patch too: the proof's own reading, the page and scroll are right). New tests: `PageRotateTests` Find and search-and-redact on each turn (8), `TurnedPageUiTests` (3).
- **Windows behaviour changes:** Find's highlight on a turned page is on the word; search-and-redact on a turned page covers the word; in a file with pages of different shapes, Fit width and Fit page fit each page; Ctrl F opens Find on Word, Excel, Markdown and text files, with the cursor in the box; hidden Find matches in Excel are named. Files whose pages are all one shape, and the mark cards on desktop, are unchanged.
- **Encoding picker, measured for the Windows Reader chat's design:** `System.Text.Encoding.CodePages` is part of .NET (no package). In a trimmed build it adds one file of 0.7 MB (linux-x64 726 KB, osx-arm64 726 KB, android-arm64 715 KB; the trimmed folder grows 23.5 to 24.2 MB), no trim warnings, 109 encodings. Registering it takes about 2 ms and the first decode about 15 ms, so it is registered on first need, never at start.


### 57. Encoding picker, Core side (16 Nov)
- **What:** `TextEncodings` (Core): the list for a picker (`All`, 38 encodings in 8 groups, each a .NET code page number and a name), `Decode(bytes, codePage, out bytesNotFitting)`, and `TextViewService.Load(path, codePage)`. `TextDocument` gains `CodePage`, `EncodingChosen` and `BytesNotFitting`. No Ui change; the automatic read gives the same text as before.
- **Rules kept:** a chosen encoding is never second-guessed and never refused as binary; bytes that do not fit are still shown (U+FFFD, or for a one-byte code page the stand-in Windows gives an undefined byte) and counted once each; a byte-order mark is consumed only by its own encoding; Windows-1252 by hand uses the same table as the automatic read.
- **Count on the automatic read:** zero, except when a file falls back to Windows-1252 and holds one of the five bytes that page leaves undefined (81, 8D, 8F, 90, 9D); a picker can use that to suggest another encoding.
- **Startup:** the code-pages provider is switched on the first time a code page that needs it is asked for. `tools/TrimProbe` now fails if it is on after every service is built, or if any listed encoding is missing from a trimmed build: "38 encodings, 0 missing, code pages on at startup: False".
- **Tests:** `TextEncodingTests` (40): every listed encoding exists; 25 round trips against .NET's own encoders (Tamil ISCII, Cyrillic, Greek, Japanese, Chinese, Korean and others); counts for invalid UTF-8, a lone Shift-JIS byte, an odd UTF-16 byte and undefined one-byte values.
- **Probe note:** an incremental `dotnet publish` of TrimProbe can leave a stale deps file that hides a newly used framework file; publish it from clean (CI does).

### 58. Patch 29: marks kept with the tab, the question before closing, Excel resize, and fixes (16 Nov)
- **Base:** Reader main 814f755 (patch 28). One PR, a commit per step.
- **Confirmed by Arun on real hardware (his Windows PC, Reader main on patch 27 then patch 28):** a rectangle drawn on a PDF page at a zoom lands exactly under the pointer (the mark placement fix, M-27), and pinch zoom works well. Mac Test has verified M-27 at four zooms on all its runners.
- **The dead control Arun found (B-33):** on an Excel file the title bar's sheet arrows and number box moved the number and not the sheet. They call into the Excel view from outside it and nothing redrew the grid; the outline's sheet entries took the same path. **Fixed** (the view redraws when the sheet is changed from outside). Proof `tools/PixelProof/sheet-pager.js`.
- **Marks kept with the tab (D-186 Q3 B):** the Reader page held one set of marks and cleared it whenever the file on screen changed, so switching tabs lost unsaved marks. Marks and their undo history now belong to the tab (`ReaderTab.Marks`, an `ITabMarks`); they are still there after another tab, Home or Arrange. Memory only; they go when the tab closes.
- **The question before closing (D-190, option C part A):** closing a tab, the other tabs, all tabs or the app with unsaved marks shows each such file and asks Save / Save as / Discard / Cancel. Save writes into the file as Ctrl S does; a file that cannot be written offers only Save as. A Discard takes effect only when every file has an answer, so Cancel loses nothing. After "Save as new PDF" the marks count as saved until they change. Every close path goes through `TabsService` (`CloseGuard`), so none can miss the question.
- **Contract for hosts:** `IUnsavedWork` (Platform): `HasUnsavedWork` (cheap, any thread) and `ConfirmCloseAsync()` (any thread; true = close now). The kit registers it (`TabsUnsavedWork`). Before its window closes a host reads `HasUnsavedWork`; when true it holds the close, brings the window forward, awaits `ConfirmCloseAsync()`, and closes only on true. With nothing unsaved the close goes on at once, as before.
- **Windows host:** `MainWindow.OnClosing` (close button, Alt F4, the in-app close) and `App.OnSessionEnding` (sign-out, shutdown). **Not run on Windows here** (this machine cannot run WPF): it builds, and the same contract is exercised through the preview's stand-in; it needs Arun's check on his PC.
- **Excel resize (D-190, option A):** drag the far edge of a column letter or the bottom edge of a row number; press the edge twice to fit it (a column to its widest text, measured with the cells' own styles; a row to its tallest). Mouse, pen and finger (a finger gets a wider edge and does not scroll the sheet instead). Sizes are kept with the tab in memory (`ReaderTab.SheetSizes`), per file and sheet, and are never written to the file. A changed header carries a mark, and "File's own sizes" puts the sheet back. A hidden row or column stays hidden; a column cannot be dragged below 8 px; row heights do not re-flow when a column changes. A fit looks at the 60 cells of the column likeliest to be widest, merged cells left out as Excel does.
- **As sent, each its own commit:** Android's P-09 (AT-21: Bind all, Scatter, Export and Split name their folder through `IPathLabels`; hosts with the plain labels show the path as before) and Mac's M-24 (files opened together become tabs in the order they arrive: one run of the open loop at a time). Both reviewed and taken unchanged.
- **WT-18:** a workbook the library refuses for its size was called damaged. The library refuses an oversized part with a plain `InvalidDataException` (library item SH-L2, `docs/foundation/LIBRARY-ASK-SHEETS-SH-L2.md`; the same in Sheets 2.0.5). After such a refusal the Core reads the file's own zip table of contents with .NET's zip reader and words it "too large" when a declared size is beyond the library's limits. It never refuses a file itself.
- **WT-19, measured again (no code):** the relief for Excel and Word landed in patch 25; Web Test's figures (690 → 692 MB) are from a build before it. A 1,048,576-row workbook in the preview: 3,266 MB while open, 197 MB one second after the tab closed. A close that comes within 30 seconds of another relief on the same process waits for that gap, by design.
- **Found on the way:** `TabsService.OpenAsync` walked the tab list without its lock while another open could be adding to it; the overlapping-opens test failed once in about 23 runs. The failure's text was not captured, so the cause is read from the code; the lookup now uses the locked copy, and the test passed 25 runs in a row after.
- **Encoding picker, Core side:** see section 57 (it travels in this PR).
- **Proof:** `close-guard.js` 23 of 23; `excel-resize.js` 19 of 19 (a right-to-left sheet at 150 % and a finger included); `sheet-pager.js`; re-run: `excel-grid.js` 19 of 19, `mark-pointer.js` 9 of 9 (Chromium, WebKit, older-WebKit behaviour), `pinch-zoom.js` 11 of 11 and 8 of 8, `patch28.js` 13 of 13, `touch-markup.js` 9 of 9, `page-anchor.js` 4 of 4. `mark-pointer.js` now clears marks first, since marks outlive a page load in the preview.
- **Desktop screens against main:** 25 of 27 identical. Home at 1× is the known artefact. Library at 1× differs in 14 pixels by one colour level on the search box's corner; both renderings appear in earlier patches' runs on either side, so it is the renderer, not this patch.
- **Startup:** the first render's markup is the same length (20,462); one more method is compiled (1,461 → 1,462); times within noise over six alternating runs.
- **Windows behaviour changes:** unsaved marks survive a tab switch and a visit to Home or Arrange; closing with unsaved marks asks first; Excel's title-bar sheet arrows work; Excel columns and rows can be resized on screen; a workbook too large to open says so; the status line says "unsaved" only while marks are unsaved (not after Save as). With no marks and no resizing, screens are as before.

### 59. Patch 30: a blank page at the wrong instant, Find at a page's foot, the encoding choice remembered (16 Nov)
- **Base:** Reader main cc8134b (patch 29). One PR, a commit per step.
- **The test Mac saw fail once** (`MemoryAfterCloseTests.A_sheet_that_left_the_view_while_drawing_stops_and_the_next_page_starts`, an empty string instead of a page): a real fault in the shared Core, not the test. The render worker, on its way out, answered everything still waiting as for a closed document, and did so after releasing the lock that ends it. A page asked for in that instant (which had just started the next worker) got the empty answer: a blank page, rarely, on any host. Those left at close are now taken under that same lock. `A_page_asked_for_as_the_worker_goes_idle_is_drawn_not_answered_empty` makes the instant certain: an empty string before, the page after.
- **Encoding picker, Core part (D-191):** `TextEncodings.FirstChoices` (UTF-8, Windows-1252, UTF-16 LE, UTF-16 BE; Automatic is no code page) and `NameOf`; `RecentFilesService.TextEncodingFor` / `SetTextEncodingAsync` remember the choice with the recent entry (this machine only, never in the file, Automatic forgets it, gone when the file leaves Recents; opening the file again keeps it; Recents saved before this still load). Text, Markdown and HTML all read through `TextViewService.Load(path, codePage)`. The Windows chat builds the Ui on top.
- **Web portal UI, as sent:** the greyed "Sign with certificate" says "soon" (the follow-up patch 28 missed); the hidden-matches line in Excel sits under the sheet (D-192, U-20 a; placed by hand, the Excel resize had moved the lines around it).
- **Mobile UI hooks 12:** H-25 as sent (a two-finger pinch in Mark leaves no mark; before, a pinch with Text left a text box). H-26 as sent plus one addition (on a snapping page taller than the view, a match below the first screenful keeps its own place; that case did not arise in the runs here, where zooming turns snapping off). H-26 is not only touch: on the desktop, page at a time, a match 91 % down its page was off screen in 4 of 6 steps before, none after (`find-at-page-foot.js`, fixture `tools/PixelProof/fixtures/foot.pdf`).
- **Records:** `SKIPPED-TESTS.md` (the six, each with its library item), `CODEPAGES-MEASUREMENT.md`.
- **Read from the code, nothing changed:** other unsaved work lost without a question (Protect's redaction areas, bookmark edits, Arrange's desks); M-26 (Mac's own print path; its latest fix is newer than Mac Test's last run); F-3 (options sent).
- **Proof:** both solutions 0 warnings; 597 tests pass, 6 skipped; trimmed probe 57 of 57 and 38 encodings; desktop screens against main 26 of 27 identical (Home at 1× the known artefact); close-guard 23 of 23, excel-resize 19 of 19, excel-grid 19 of 19, patch28 13 of 13, mark-pointer 9 of 9, pinch 11 and 8, touch-markup 11 of 11 (two new), page-anchor 4 of 4.
- **Windows behaviour changes:** Find lands on the page of a match at a page's foot; the hidden-matches line in Excel is under the sheet; no blank page at the worker's idle instant. Nothing else visible until the encoding Ui lands.
- **Not in this patch:** `global.json` to workload set 10.0.401 (D-191), which waits for Android's green build and run on that set.

### 60. Patch 31: the sheet pager's second cause, the first key in a new text box, the close card's keyboard (16 Nov)
- **Base:** Reader main fc54ece (patch 30). One PR, a commit per step.
- **B-33, second cause (Linux Test, sign-off b0e5608):** the sheet arrows and number box were still dead on Linux after patch 29. Reproduced in the preview with two Excel files open: going from one to the other kept the same viewer component and only changed its file; the Reader had just cleared its reference to the viewer for the new tab, and that reference is only set when a viewer is created, so it stayed empty. The pager, the number box and the outline did nothing from then on (Word and text tabs the same). **Fix:** each viewer is keyed by its tab, and the reference is dropped only for a tab that has no viewer. Patch 29's fix was a real first cause (the grid was not redrawn) but its proof had one Excel tab open, so it could not see this. Proof `sheet-pager-two.js`. Whether this is all of what Linux saw needs Linux Test's re-run (it had four tabs open; a single file opened from the command line could not be made to fail here, with slow storage or without).
- **Seen on the way, not changed:** coming back to an Excel tab shows its first sheet again, not the sheet that was showing.
- **AT-10 reopened (Android Test round 12, tablet):** the first key typed into a new Mark text box was lost. After a finger lifts, the browser sends the tap's mouse events, and the mouse-down's own action moved the focus from the unseen field that holds the caret to the page area, until the box arrived some tens of milliseconds later; keys in that gap went nowhere. **Fix:** that focus move is stopped while the caret is held, and the field takes the focus back if it leaves another way. Reproduced and proved in Chromium's touch emulation, tablet and phone layouts, keys 0, 15 and 40 ms after the tap: 4 of 30 boxes whole before, 30 of 30 after (`first-key.js`). The gap existed before patch 29 on both layouts here; why it began to show on the tablet at patch 29 is not known. Not run on an Android device: Android Test's check decides.
- **Android's question:** an empty text box is not unsaved work, by intent: a save writes nothing for it, so there is nothing to lose.
- **Web portal UI, as sent:** the ask-before-closing card keeps the keyboard inside it (Tab and Shift Tab go round its buttons; before, they reached the page behind, where Esc no longer cancelled). A typed catch replaces the bare one.
- **Touch sizes:** the close card's buttons are 44 px on the tablet (AT-26) and "File's own sizes" is 44 px on phone and tablet (AT-25). Set in the rules, not looked at on those layouts here. AT-28 (Export card, Merge page) is Mobile UI's touch-size rule.
- **Workflows:** `platform-build.yml` takes the runner as an input, so a Mac Catalyst build passes `xcode-27`; the description and the note in `platforms.yml` now say so. Nothing else needs changing: the jobs here build no Mac Catalyst code. `global.json` stays at workload set 10.0.403 (Android answered no to 10.0.401).
- **Proof:** both solutions 0 warnings; 597 tests pass, 6 skipped; trimmed probe 57 of 57; desktop screens against main 26 of 27 identical (Home differs by the clock's minute); close-guard 24 of 24 (one new), excel-resize 19 of 19, excel-grid 19 of 19, patch28 13 of 13, mark-pointer 9 of 9, pinch 11 and 8, touch-markup 11 of 11, page-anchor 4 of 4, find-at-page-foot 0 of 6 off screen.
- **Windows behaviour changes:** with two Excel (or Word, or text) files open, the title bar pager and the outline keep working after switching between them; Tab stays inside the close question.

### 61. Patch 32: the rest of the unsaved work asks before it is lost, and the Find bar no longer covers a match (16 Nov)
- **Base:** Reader main 24b2bac (patch 31 and the encoding picker's Ui). One PR, a commit per step.
- **Arrange at app close (D-194):** closing the app (and signing out, on the portal) with desks that changed since the last save, import, bind or combine asks once: Save session / Discard / Cancel. `BenchService` keeps a signature of each desk as last kept and compares; an untouched Arrange never asks. Arrange is asked before the files, so cancelling there loses nothing already answered.
- **The portal's card (D-194):** where a host cannot write into the file it was given (`PlatformCapabilities.SaveInPlace` false, which Web already sets) the marks question offers Save as / Discard / Cancel only. `IUnsavedWork.ConfirmCloseAsync()` is the one call for a window close and for Sign out; `IUnsavedWork.Changed` tells a host when unsaved work appears or goes (Linux's hold on sign-out and shutdown).
- **Bookmarks and Protect kept with the tab and asked on close (D-195, B and A):** bookmark edits and Protect's redaction areas (drawn areas, found matches and their ticks, the search word) now stay with the file while it is open: going to another tab, Home or Arrange and coming back keeps them (before, both were dropped without a word). Closing the tab or the app asks: bookmarks, Save bookmarks / Discard / Cancel; redaction areas, Discard / Cancel only (the redacted copy is made in Protect, so Cancel takes the user back there), with Cancel the button in focus. A file opened in Protect that has no tab is asked about at app close the same way. One tab with several kinds is asked about each in turn; a Cancel on a later one discards nothing answered earlier.
- **F-3 (D-195):** the current Find match no longer comes to rest under the Find bar. Where the view scrolls freely it stops higher by the overlap; page at a time (and at the very top of a file) the bar moves to the bottom of the page area for that match and returns for the next. `find-under-bar.js`, fixture `top.pdf`: before, 8 of 16 steps covered on the desktop page at a time, 4 of 16 zoomed, 12 of 16 on the phone; after, none on desktop, tablet or phone.
- **Found on the way, in Protect on main too:** drawing one redaction area with the mouse selected the page text under it, and the next press then began the browser's own drag of that selection: no area was added. Ten areas drawn one after another kept 1 or 9 on main; 10 of 10 now (`protect-draw.js`). The drawing layer no longer selects (a style rule, Protect only).
- **Mac minimum macOS 14 / Mac Catalyst 17.0 (D-195):** no shared project file, props file or workflow states a Mac minimum (only the Windows host states a minimum, its own), so nothing here changed; it is the Mac host's project to state.
- **Proof:** both solutions 0 warnings; 606 tests pass, 6 skipped; trimmed probe 57 of 57 and 38 encodings, code pages off at startup, the same 17 trim notes; desktop screens against main 25 of 27 identical (Home differs by the clock, Library at 1× is the known 14-pixel renderer artefact); close-guard 24 of 24, close-guard-arrange 16 of 16 desktop and 4 of 4 web, close-guard-work 18 of 18, excel-resize 19 of 19, excel-grid 19 of 19, patch28 13 of 13, mark-pointer 9 of 9, pinch 11 and 8, touch-markup and page-anchor 15 of 15, first-key whole at every gap, find-at-page-foot 0 of 6 off screen, sheet-pager-two, encoding-picker 17 of 17.
- **Windows behaviour changes:** closing the window with changed Arrange desks, unsaved bookmark edits or redaction areas asks first; bookmark edits and redaction areas survive a tab switch and a visit to Home or Arrange; a Find match under the Find bar is scrolled clear, or the bar steps to the bottom; several redaction areas can be drawn in a row with the mouse. With none of those in play, screens are as before.
- **Not run here:** the Windows window-close wiring on Windows itself (Arun's check); the Find bar's bottom place on a real phone.

### 62. Patch 33: library holds until the patched libraries (D-198), Mobile UI H-27 to H-29, M-29 and AT-29 (18 Nov)
- **Base:** Reader main 6c33eab (patch 32). One PR, a commit per step. Everything held is listed in one place, `Core/Documents/LibraryHolds.cs` (and `PageOperationCheck`), so Phase 2 can undo it with one search.
- **Redaction is hidden (D-198).** The library chat found that on Pdf 3.32.0 a redacted copy can keep the text it should remove while the library's own check says it is clean (RED-16/17/18). In Protect, the rail shows one greyed "Redaction" whose reason reads "Redaction comes back with the next update." on hover and on a tap. "Redact box" and "Find & redact" stay in Ctrl K (and the Mac menu, the phone strip) greyed with the same reason, and the R key does nothing. Arrange's "Send to Redact" is gone. The redaction page itself, reached only by an old link, says when it comes back and opens nothing. Stamp, watermark, flatten, password and signing are in Protect as before.
- **Signature check: no verdict (D-198).** On 3.32.0 a signature can come back valid while the page shows what an attacker added (SIG-12). Until 3.35.1 every signature shows what it states (signer, time, reason, place, issuer, what it covers) and "Not checked", in the neutral colour: "Signed by A. Shiva · 18 Nov 2026 · not checked in this version". No green "Valid", no "changed after signing", no "not valid". No trust store is built. `SignatureCheckOptions.CheckTrust` turns the verdict back on; the tests use it so that code stays proven.
- **Page operations ask first, on every file (D-198).** Every operation that makes a new file from pages: Arrange's Bind desk, Bind all, Combine, Lift, Scatter, Impose and Export, Open in Reader from a desk, Merge, Split and Extract. They drop bookmarks, form fields, links to places in the file, page labels, language and tags (OPS-03), and can show a hidden layer (OPS-02). A card names, per file, which of these that file holds ("The new file will not have the bookmarks, page labels and the document's language of rich.pdf."). It adds a line when a file has layers. Continue goes on; Cancel and Esc stop with nothing written; Cancel is where the keyboard starts. A file agreed to is not asked about again while the app runs.
- **Excel conditional formats are not evaluated (D-200), for every file,** until the Sheets release that fixes SEC-08 and SEC-10 is taken: no `EvaluateConditionalFormats` call. Cell values and their own formatting are as before; data bars, icons and rule colours are not drawn. A sheet that has rules says "Conditional colours return with the next update" in the bar under it, beside the sheet tabs, with a tooltip saying the values are shown. **One difference in shown text:** a rule can also switch a cell's number format; such a cell now shows its own format (the test file's "CF: 7.000" shows "7.00", the cell's own format with the same value). Rare, and reported to Arun. `LibraryHolds.ConditionalFormatsHeld` is the switch. Tests and `excel-grid.js` check the held behaviour; the bar-and-icon checks (E-13) and "CF: 7.000" return with the update.
- **Mobile UI H-27 to H-29** (`mobile-ui-hooks-14.patch`, applied as sent): a pinch from an Excel edge zooms and resizes nothing (H-27); by finger an edge is taken after a 350 ms rest, a finger that moves at once scrolls (H-28, D-193); the Export and Press cards get class names, with desktop unchanged (H-29). `excel-resize.js` now rests the finger before its finger drag.
- **M-29 (Mac, macOS 15):** not an app fault as far as can be seen here. In WebKit 18.0, the engine of macOS 15, fetched for testing only, `excel-resize.js` passes all 19 checks: a column drag moves 70 px at 100 %, and on the right-to-left sheet at 150 % a 60 px drag widens the column 60 px on screen. Its one first failure was the proof's own: on that engine, positions under CSS zoom must be read the way the app reads them. The proof now does so. Mac Test's harness guesses the edge as "halfway between the two letters' frames, plus 4 px". That lands on the edge only when the two columns are the same width and the letter frames are the same on both macOS versions; the mouse's reach is 5 px. Asked of Mac Test: take the edge from the header cell's frame, and check for the resize cursor before dragging.
- **AT-29 (Android, phone):** not reproduced. Two swipes, then turning the phone after 0, 100, 300 and 700 ms, warm and right after opening: the page is kept every time (3 to 3; mid-move 2 then 3). Android Test's own R14 did not reproduce it either. Nothing changed; on watch.
- **Proof:** both solutions 0 warnings; 608 tests pass, 6 skipped. Trimmed probe: 57 of 57 services, 38 encodings, code pages off at startup, same 17 trim notes. Desktop screens against main: only Protect differs (the redaction page, and the Protect rail, at every scale), the rest identical. `library-holds.js` 13 of 13 (new); close-guard 24, close-guard-arrange 16 and 4, close-guard-work 7 (its redaction half skipped while redaction is hidden), excel-resize 19 (Chromium; WebKit 18 19), excel-grid 19, patch28 13, mark-pointer 9, pinch 11 and 8, touch-markup and page-anchor 15, first-key whole at every gap, find-at-page-foot 0 of 6 off screen, find-under-bar 0 of 16 covered, sheet-pager-two, encoding-picker 17.
- **Windows behaviour changes:** redaction cannot be used; Protect shows a greyed "Redaction" with "Redaction comes back with the next update."; the Signatures card and the strip over the page say "Not checked" and give no verdict; every page operation asks first and names what each file will lose. With none of those in play, screens are as before.
- **Not run here:** WebKit on macOS 15 itself (M-29 needs Mac Test's run); a real phone turn for AT-29.

### 63. Patch 34: Arrange kept for the next start (D-203), AT-24, B-36/AT-30, M-29, Web portal UI (18 Nov)
- **Base:** Reader main dab96c1 (patch 33 and PR 26, the Windows chat's picker follow-up). One PR, a commit per step.
- **Arrange at app close (D-203, amends D-195 B and D-194).** The close card asks only when Arrange was used in this run and its desks differ from what is kept: "Keep what you arranged? 1 desk in Arrange has changed. Save keeps your desks for the next time the app starts. Discard clears them." **Save** opens no file dialog: the desks go into the app's own data folder (`arrange-kept.json`, written to a temporary name and then put in place). **Discard** clears what is kept; **Cancel** keeps the app open. The desks come back the first time Arrange is opened (or sent a file) in the next run, never at startup, so starting costs nothing; files that moved are reported as opening a session reports them. An Arrange emptied by hand is quietly kept empty. Binding or exporting a desk, or saving or opening a session file, no longer counts as "kept": the question is about the next start. Reset clears the kept desks too, and its card now says so ("…here and the next time the app starts"). Save session stays inside Arrange as the deliberate export. Portal: the same, kept in the person's session folder, so desks survive a reload and end with the session.
- **AT-24 (phone Find on the wrong page).** Not a page-numbering fault: a match's page always came from the document's page number. In page-at-a-time each page box has `min-height: 100%`, which replaces the flex "at least its content" minimum, so where a page fitted to the width is taller than the view (the phone with the keyboard up, any phone on its side) every box shrank to the view's height and each page ran over the next. Find's highlight at the foot of page 3 was drawn over page 4, and the desk snapped there. Fixed with `flex-shrink: 0` on the page box (Reader.razor.css); the jump also stays inside the match's page when the page is taller than the view, waits for the page to be drawn, and keeps the match's place (not its page's top) while pages around it are drawn. `find-short-screen.js` with Android Test's own file (`fixtures/find-page3-foot.pdf`): 5 of 5 on the phone at 520 and 915 px high and the tablet at 700 and 1280; before, on a 520 px phone, 2 of 5 on page 3 with the match off screen and 3 of 5 on page 4.
- **B-36, AT-30 (redaction held).** Protect's "Save redacted copy" is greyed with "Redaction comes back with the next update." (it was shown and enabled). The greyed "Redaction" and "Save redacted copy" answer a tap or a click with that line. On touch shells every greyed rail tool now answers a tap with its reason, and so do the phone strip's greyed tools ("Box: Redaction comes back with the next update."); desktop is unchanged, its tooltips as before.
- **M-29 (Excel column edge on the Mac).** Mac Test's log found it: on macOS 15 Intel (and once on 26) Mac Catalyst hands WebKit the mouse drag as a `touch` pointer, so since D-193 it had to rest 350 ms first. The Excel resize now treats every pointer as a mouse where the host has no touch screen (`IPlatformInfo.Input` without `Touch`: the Mac, Linux, the portal on a desktop browser). Touch hosts keep the finger rule. `excel-resize.js` checks it: 19 of 19 (Chromium), 18 of 18 plus one skipped in WebKit 18 (it has no touch input). For the Mac chat, optional: `UIApplicationSupportsIndirectInputEvents` in Info.plist would make Catalyst report the trackpad and mouse as such everywhere.
- **Web portal UI:** `web-ui-close-card-phone-labels.patch` (the phone close card's buttons sized to their labels, one line) and `web-ui-encoding-rows-finger-size.patch` (44 px encoding rows on touch), applied as sent.
- **Proof:** both solutions 0 warnings; 609 tests pass, 6 skipped. Trimmed probe: 57 of 57 services, 38 encodings, code pages off at startup, the same 17 trim notes. Desktop screens against main: Protect's rail differs (Save redacted copy greyed), Home by the clock; the rest identical. close-guard-arrange 17 of 17 desktop and 4 of 4 web, arrange-comes-back 8 of 8 (new: kept desks come back on the next start, not asked about; after Reset nothing comes back), find-short-screen 4 of 4 (new), library-holds 13, close-guard 24, close-guard-work 7, excel-resize 19, excel-grid 19, patch28 13, mark-pointer 9, pinch 11 and 8, touch-markup and page-anchor 15, first-key whole at every gap, find-at-page-foot 0 of 6 off screen, find-under-bar 0 of 16 covered, sheet-pager-two, encoding-picker 22.
- **Windows behaviour changes:** closing with changed desks asks "Keep what you arranged?" with Save / Discard / Cancel, and Save opens no dialog; the desks come back when Arrange is next opened; binding or saving a session no longer stops the question; Reset clears the kept desks; "Save redacted copy" is greyed with its reason; a Find match at a page's foot on a page taller than the view lands on its own page.

### 64. Patch 35: save safety, unlocked files in every tool, the Windows test package's findings (25 Nov)
- **Base:** Reader main 0d3c639 (PR 28, Windows host fixes and the tab strip in the shared Ui). One PR, a commit per group, in the coordinator's order: data safety first, then BUG-19, then the rest.
- **Save safety (D-207, D-210).** Same lock in, same lock out. `SourceLock.Plan` decides what a save into a locked file needs. Opened with the owner password, the Reader locks the file again as it came (RC4 files are written as AES-128). Opened with the open password alone, a save into the file is refused with a card that asks for the owner password, or offers Save as. Before any save onto the source, `SourceGuard` checks the file is still the one that was opened (size, time and SHA-256 of the bytes taken at open). A moved, deleted or changed file is never written over silently (BUG-23, BUG-24, BUG-26, MK30). A save onto the source goes to a temporary "~name.xxxx.chuvadi-tmp" first and is then moved into place (SF12, SF13). Watermark, header/footer and flatten work from memory, with no scratch files. Tests: SaveSafetyTests (10), with an RC4 restricted fixture made with qpdf.
- **Unlocked files in every tool (BUG-19, PR15).** The password given at open is kept in memory for the run (`PdfPasswords`), and every service opens through `PdfFiles`. A tool that makes a new file from a locked source says so rather than failing; that stays until Arun answers the locked-pages question. Press keeps the lock and uses the library's `CompressToTarget`, and it says when nothing shrank (PS09, PS10). Protect refuses to re-lock without the change permission. Tests: UnlockedToolsTests (5).
- **Group 3:**
  - Files open with shared read, so one open in another app still opens (FileShare). The tools' PDF opens (`PdfFiles`, with Library, Protect and Windows printing) go through the same shared stream: the library's own path open lets only other readers in, which Windows CI caught on the first PR run.
  - A failed open's message has a Close.
  - Markdown stops at a nesting depth of 64 and says so.
  - Tabs that were opening when the app crashed are held at the next start and offered, not reopened (HO33).
  - Links out of a file ask first (BUG-01).
  - Repair works in memory.
  - Merge lists only PDFs (MG02).
  - Merged Word cells show as merged (VW01).
  - Excel hidden rows, columns and sheets stay hidden, with one switch in the View card and on the View rail to show them greyed (D-214).
  - mobile-ui-hooks-17 applied.
- **M-29 and ST15.** The Excel column drag follows either pointer of Catalyst's pair on macOS 15 Intel. Below 760 px the side bar floats and closes after a pick (D-214).
- **Text the file can't hold is refused, not garbled (D-210 Q2, BUG-11, BUG-18).** Stamps, watermarks, headers, footers, flatten and page numbers name the letters the standard fonts can't hold. D-215 wording ("language", "Read this file as…"). Saving keeps the document info (BUG-03). UTF-16 text without a byte-order mark is read as UTF-16 (VW25).
- **Group 6.** Desk images are checked before they're taken (CR70, CR71). No scratch PDFs are left behind (SC14). Properties draws its answer (RD51).
- **HO16.** A tab opens at its page on first show and keeps its place after Home.
- **PR21.** A file signed by another tool is countersigned and keeps its signature. The library names the new field "Signature{n}" whatever is asked, so the check takes the newest signature. Proved with the Windows test's F08 and S01 (CountersignOtherToolTests).
- **RD47, RD35.** To desk > New desk keeps its page when the kept desks come back. Save bookmarks on a desk's working copy asks where to save.
- **BUG-02, BUG-10.** Keys typed while a field opens land in it: Ctrl K, Ctrl B and the bookmark rename. Text already selected is typed over.
- **BUG-05, ST19.** The Shortcuts page matches the wired keys.
- **UI30, UI31.** Errors and action messages fade after 15 s and successes after 6 s; at most three show at once. Esc or a tab switch closes Press.
- **BUG-20 (memory on a long scroll).** A PDF tab kept every page it had ever drawn. It now keeps the 24 nearest the view (and at most 48 M characters of text) and frees the far pages' images. On F02 (500 pages), scrolled end to end: 24 drawings kept, app memory 171 → 249 MB and flat, 184 MB after close. Before: 264 drawings and 305 MB. Proof: memory-long-scroll.js; test: DrawnPagesKeptTests.
- **Proof:**
  - Both solutions build with 0 warnings. 641 tests pass, 6 skipped.
  - Trimmed probe: 57 of 57 services, the same 17 trim notes.
  - Desktop screens against main: 27 of 27 identical. A first run showed Home at 1x and 1.5x differing by the clock; a rerun was identical.
  - Browser proofs: close-guard 24, close-guard-work 7 plus 1 skipped, library-holds 13, excel-grid 20, arrange 25, encoding-picker 22, find-short-screen 4, type-ahead 5 (new), narrow-sidebar 7 (new), shelf-lazy (new), memory-long-scroll (new), first-key whole at every gap.
- **Windows behaviour changes:**
  - A save into a file opened with only its open password asks for the owner password or offers Save as.
  - A file changed or moved since it was opened is not saved over without asking.
  - Locked files work in every tool once unlocked.
  - Letters a stamp can't hold are named and refused.
  - Hidden Excel parts stay hidden, with a switch to show them.
  - Links out ask first.
  - Tabs held after a crash are offered at the next start.
  - Messages fade, and at most three show.
  - Typed keys land in a field as it opens.
- **Linux behaviour changes:** the same, plus the narrow-window side bar (below 760 px).
- **Open, with Arun:**
  - New files made from locked pages: the tools stop for now. Recommended: the same open password and permissions as the source.
  - Tamil page numbers: 3.32.0's DrawTextBlock refuses LiPi, so they are refused for now (D-217).
  - AR18: no change.
- **With the Windows chat:** BUG-25 (repeat on this build and record WebView2's memory) and AR51 (the Save session dialog).
- **Not reproduced:** UI32. Home's 520 MB at idle on Windows is WebView2's own, not the app's.
- **For the library:** PdfCounterSigner ignores SignatureFieldName; document dates can't be written back (BUG-03); DrawTextBlock has no LiPi.
