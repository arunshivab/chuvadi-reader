# Combined platform hooks (D-26) — applied on `platform/foundation`

One patch carries every existing-file hook the platform chats asked for. Commit `3358b00`; the full diff is `proposal/combined-hooks.patch` (47 files: 16 existing files edited, +185/−21; the rest are new files from the UI chats and the test projects).

**Windows renders pixel-identical** (proof below). Startup cost: none with ReadyToRun (below).

## What went in

| Item | From | Where | Desktop |
|---|---|---|---|
| Phone shell: `PhoneShell` replaces top bar + left bar + status bar when the shell is Phone | Mobile UI hooks | MainLayout | not rendered |
| Touch support (pinch, swipe, long-press) on tablet and phone | Mobile UI hooks | MainLayout → `TouchSupport` | not rendered |
| Account menu in the top bar (portal) | Web UI hooks, Web H1 | MainLayout → `Portal/AccountMenu` | not rendered (`Chrome.AccountMenu` false) |
| "Upload" label and icon for Open on the portal | Web H3 | MainLayout | "Open" as before |
| Status bar: server wording, signed-in user | Web H2, Web UI hooks | MainLayout | "offline · on this device", profile name as before |
| Server status dot colour (`.status-dot.server`) | D-37 | MainLayout, shell.css | class not added |
| Clock re-renders only its own span (`StatusClock`) | D-52 | MainLayout, new `Shell/StatusClock.razor` | same text, same markup; the shell no longer re-renders every second |
| Privacy line, Settings footer, About › Network, Library index hint | Web H4, H5 | Dashboard (new), Settings, About, LibrarySection (already from Group B/C) | same wording |
| Server paths hidden: Storage-map folder, Library shelf folder, Library page folder, Watched folder card, About › Data folder | Web H5, H9 | Dashboard, Library, AboutSection | shown as before (`RevealInFolder`/`WatchFolders` true) |
| Phase 0 sample recents/pins only off the server | Web H8, option (b) | RecentFilesService, PinnedService | samples as before |
| User name from the account, then OS user (H6); default save folder from the host (H7); folder choosers hidden (H9) | Web H6, H7, H9 | already in Group B/C | unchanged |
| `string? Unit` on `IAccount` (default null), shown after the Sangam ID | D-35 | Platform/Services.cs, AccountMenu | — |
| 0.5px dividers drawn as borders | D-39 | shell.css, scoped to `html[data-platform]` | **not applied** (Windows has no `data-platform`) |
| Pen draws, finger pans on the markup layer when a pen is present | D-42 | reader.js | only acts when `data-input` contains `pen` (never on Windows) |
| Tablet desk counter scrolls to a desk | D-46 | Bench.Shell.cs, bench.js | only on Tablet |
| **Bug fix:** restoring tabs could show the same file twice | D-48 | TabsService.RestoreAsync, + test | fixed on every platform |
| Mac Catalyst treated like macOS for key storage and the temp-path guard | Mac chat | CertificateSignService, PasswordService | unchanged |
| Mobile UI and Web UI tests (+ preview host) in `ChuvadiReader.Platforms.slnx` | coordinator | slnx | CI now runs them |

### Not taken as proposed, with reasons

| Proposal | Done instead | Pros | Cons |
|---|---|---|---|
| Web H0: new `IHostChrome` interface every host registers | Each hook reads the existing contract (`IPlatformInfo.Chrome/Capabilities/Kind`, `IAccount`, `IAppPaths`) | No new seam, nothing new for Windows/Mac/Linux/Android to register, one source of truth for "what this platform can do" | Web wording lives in the Ui (`PlatformText`) rather than in the Web host |
| Web H8 option (a): remove the Phase 0 samples everywhere | Option (b): samples off on the server only | Desktop first run unchanged (pixel proof holds) | Placeholder data stays in desktop builds. **Recommendation: (a) when the Reader chat removes Phase 0 data** — one-line change in each service |
| Web H9: Print card Copies / Fit to page on the portal | Not changed | — | Open: whether the Web print service honours copies/fit decides it. **Options:** (a) hide both on Web (browser dialog decides; honest); (b) keep and pass them to the generated print PDF (works only if the Web host implements it). Recommendation: (a) unless the Web chat implements (b) |

## Proof: Windows unchanged

Preview host (`tests/ChuvadiReader.MobileUi.Preview`, desktop profile = Windows kind), Release builds of `be4498a` (before) and `3358b00` (after), headless Chromium, 1440×900, clock and caret masked, animations off. Script: `tools/PixelProof/desktop-identical.js`.

| Route | 1× | 1.25× | 1.5× | `<html>` attributes |
|---|---|---|---|---|
| Home | identical | identical | identical | same (`lang,data-theme,data-ui-scale,style`) |
| Reader | identical | identical | identical | same |
| Bench | identical | identical | identical | same |
| Redact | identical | identical | identical | same |
| Library | identical | identical | identical | same |
| Settings | identical | identical | identical | same |

Notes: a first draft differed on Home (glyph positions of the shelf caption, because a conditional merged two text nodes into one). Each desktop branch now keeps the original markup verbatim inside the `@if`, and Home went back to identical. The first shots after a cold server start can differ while fonts load; both runs above are from warm servers.

## Proof: D-39 dividers on WebKit (Mac/Linux engines)

Real app, desktop shell with `data-platform="macos"` (as the Mac host sets it), Playwright WebKit 26.6, each divider moved through 10 sub-pixel positions. "Before" forces today's 0.5px box. Script: `tools/PixelProof/webkit-dividers.js`.

| Scale | Status-bar divider before → after | Left-bar separator before → after |
|---|---|---|
| 1× | 5/10 painted → **10/10** | 5/10 → **10/10** |
| 1.25× | 8/10 → **10/10** | 7/10 → **10/10** |
| 1.5× | 9/10 → **10/10** | 9/10 → **10/10** |
| 2× | 10/10 → 10/10 | 10/10 → 10/10 |

Why scoped to non-Windows: in Chromium (WebView2) the border version differs from today's box at 1.25× and 1.75×, so Windows keeps the box.

## Phone and tablet smoke check
Preview host, phone (390×844) and tablet (1180×820), touch: Home, Reader, Bench, Library, Settings — the right `data-shell/data-platform/data-input`, no console errors, no Blazor error bar. Script: `tools/PixelProof/touch-smoke.js`.

## Build, tests, startup
- `ChuvadiReader.Platforms.slnx` and the WPF host (`EnableWindowsTargeting`) with `-warnaserror`: 0 warnings, 0 errors.
- Tests: **206/206** — 161 Reader, 28 Hosting (+1: D-48), 3 Mobile UI, 14 Web UI. The D-48 test fails without the fix.
- Startup (`tools/StartupProbe`, published Release, 25 interleaved runs, medians, this sandbox is slower than on 6 Nov so compare rows, not with STARTUP.md's table):

| Build | Total | Methods JIT-compiled |
|---|---|---|
| before, ReadyToRun | 209.9 ms | 357 |
| after, ReadyToRun | 211.1 ms | 358 |
| before, JIT | 317.8 ms | 1,415 |
| after, JIT | 305.1 ms | 1,427 |

ReadyToRun: +1 method, +1.2 ms (noise floor ≈ 7 ms). JIT: +12 small methods (`StatusClock`, the new conditionals), time within noise. Windows now publishes ReadyToRun (D-50), so the first row is what ships.

## What the other chats must update

- **Mobile UI:** `TouchSupport` now injects `IPlatformInfo` (the `PlatformState` cascade no longer exists); MainLayout renders it only for Tablet/Phone. Rebase `platform/mobile-ui` on `platform/foundation` and drop the duplicated Phone files.
- **Web UI:** `StatusLocation` and `PlatformRoot` are not used; the status text is inline in MainLayout. `AccountMenu` shows `IAccount.Unit`. Rebase and drop the duplicated Portal files.
- **Web:** no `IHostChrome` to register. Set `Capabilities.ServerSide = true`, `RevealInFolder = WatchFolders = PickFolders = false`, `Chrome.AccountMenu = true`; implement `IAccount.Unit` if the unit is known. H9 print card: see options above.
- **Mac:** nothing to change; the Catalyst checks are in. Dividers now paint on WebKit.
