# Chuvadi Reader — platform interface (DRAFT)

Status: **decided** (F-1…F-7, coordinator on Arun's delegation, 6 Nov 2026 — see §8). Group A is applied on `platform/foundation`; Groups B/C are proposed in `EDITS-FOR-APPROVAL.md`.
Code: `src/ChuvadiReader.Platform` (contracts) and `src/ChuvadiReader.Hosting` (shared host kit), branch `platform/foundation`. Both build with warnings as errors; tests in `tests/ChuvadiReader.Hosting.Tests`.
Inventory of what this replaces: `docs/foundation/INVENTORY.md`. Visual reference: `docs/design-handoff/` per `docs/coordination/DESIGN-NOTE.md`.

## 1. Shape

```
ChuvadiReader.Platform   contracts only, no dependencies (net10.0, trim/AOT clean)
ChuvadiReader.Core       existing; keeps its existing host interfaces (see §2)
ChuvadiReader.Ui         existing; reads IPlatformInfo to switch chrome and form factor
ChuvadiReader.Hosting    NEW shared host kit: AddChuvadiReader(lifetime) + reusable defaults
ChuvadiReader.Windows    existing WPF host (unchanged until Arun approves its edits)
ChuvadiReader.Mac / .Linux / .Android / .Web   new hosts, one per platform chat
```

Every host does the same three things:

```csharp
services.AddChuvadiReader(AppStateLifetime.Process);          // Web: AppStateLifetime.PerConnection
services.AddSingleton<IPlatformInfo>(new FixedPlatformInfo(…)); // or the host's own, if form factor changes
// then its own implementations of the seams in §2 — a later registration wins
```

## 2. The seams a host implements

Existing interfaces (in Core today, unchanged — implement them as Windows does):

| Interface | Purpose | Default if not registered |
|---|---|---|
| `IAppStorage` (Core.Storage) | key/value settings | **required** — no default |
| `IFilePicker` (Core.Reader) | open/save/folder/image/session/certificate pickers; **returns local paths** (see §3) | **required** |
| `IPrintService` (Core.Reader) | native print of a PDF path | `NoopPrintService` (Print card greyed) |
| `IImageClipboard` (Core.Reader) | copy PNG | **required** (Reader injects it) |
| `IWindowControls` (Core.Window) | min / max / close / drag | `NoopWindowControls` |
| `IUiScale` (Core.Window) | whole-window zoom 100/125/150 | `NoopUiScale` (CSS zoom fallback) |
| `IExternalLinks` (Core.Window) | open http/https/mailto outside the app | `NoExternalLinks` |
| `IAppReadySignal` (Core.Lifecycle) | first-render → dismiss splash | registered by the kit |

New contracts (`ChuvadiReader.Platform`):

| Interface | Purpose | Desktop default in the kit |
|---|---|---|
| `IPlatformInfo` | `Kind`, `FormFactor` (+`FormFactorChanged`), `Input`, `Chrome`, `Capabilities`, `Keys` | `FixedPlatformInfo` |
| `IFileWriteBack` | after a write to a picked/opened path, send it to its real destination | `NoFileWriteBack` (registered) |
| `IAppPaths` | app-data, working (temp) and default save folders | `DesktopAppPaths` |
| `IAccount` | user chip, Sangam sign-in/out | `LocalAccount` |
| `IClipboard` | text + image copy (replaces the JS `navigator.clipboard` call) | — host |
| `IFileManager` | "Show in folder" | — host (desktop only) |
| `IShare` | share sheet | — host (Android, macOS) |
| `IAppLifecycle` | suspend / resume / Android back | `NoAppLifecycle` |
| `IAppCommands` | the Ui's commands (Ctrl K list + Document ▾ menu) for the macOS menu bar; filled host-side from `ShellService`, no Ui hook | `ShellAppCommands` (registered) |

### What each value means for the Ui (form factor and chrome switch)

| | Windows | macOS | Linux | Android tablet | Android phone | Web |
|---|---|---|---|---|---|---|
| `FormFactor` | Desktop | Desktop | Desktop | Tablet | Phone | follows the browser: Desktop / Tablet / Phone (F-5) |
| `ShellLayout` | Desktop | Desktop | Desktop | Tablet | Phone | Portal at desktop width; Tablet / Phone in small browsers, portal chrome kept (`IsPortal()`) |
| `Chrome.Buttons` | Right | Left | Right or Left (desktop's layout) | None | None | None (account menu) |
| `Chrome.NativeMenuBar` | no | **yes** | no | no | no | no |
| `Chrome.AccountMenu` | no | no | no | no | no | **yes** |
| `Chrome.TitleBarDrag` | yes | yes | yes | no | no | no |
| `Chrome.CornerRadiusPx` | 0 | 0 (native) | 12 | 0 | 0 | 0 |
| `Keys.Primary` | Ctrl | ⌘ | Ctrl | Ctrl | Ctrl | ⌘ on a Mac browser, else Ctrl (F-5) |
| `Capabilities.PickFolders` | ✓ | ✓ | ✓ | ✓ (SAF tree) | ✓ (SAF tree) | ✗ |
| `SaveInPlace` | ✓ | ✓ | ✓ | ✓ (URI write-back) | ✓ | ✗ |
| `SavesAsDownload` | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ (Save and Save as deliver a browser download) |
| `WatchFolders` | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ |
| `RevealInFolder` | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ |
| `DropFiles` | ✓ | ✓ | ✓ | ✗ (share-into instead) | ✗ | ✓ (upload) |
| `Share` | ✗ | ✓ | ✗ | ✓ | ✓ | ✗ |
| `ServerSide` | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ |

**`ShellLayout`** (Desktop / Tablet / Phone / Portal) is the single value the Ui switches chrome on, derived by `ShellLayouts.From(kind, formFactor)`: Web → Portal; otherwise Tablet/Phone/Desktop from the form factor. It is cascaded as `PlatformState.Shell` and written as `<html data-shell>` (see `FOR-UI-CHATS.md`).

The Ui turns these into attributes on `<html>` (`data-shell`, `data-platform`, `data-form-factor`, `data-input`) so CSS for the new components can follow the design (e.g. 72/72/60 px rails on tablet), and picks new components for the chrome that differs (§5). Existing desktop markup stays as is.

## 3. The file model — decision F-1

Core is path-based end to end (tabs, recents, bench, sessions, save-in-place, print). Android gives content URIs and Web gives uploaded bytes.

**Option C — DECIDED: working path + write-back.**
Hosts always hand Core a readable local path. Desktop: the real file. Android: a copy in app-private storage made from the URI (permission persisted so recents reopen). Web: a **session-only** working copy, encrypted, deleted on sign-out, session end or expiry — **no per-user server storage this release** (Arun). After any write to such a path, Ui calls `IFileWriteBack.CommitAsync(path)`; desktop does nothing, Android writes the bytes back to the URI, Web delivers the file as a **browser download** (Save and Save as alike).
- For: Core logic untouched; ~25 one-line `CommitAsync` calls in Ui; zero behaviour change on Windows (`NoFileWriteBack`); every library call stays path/stream as today.
- Against: Android and Web keep a working copy of each open document (disk use, cleanup needed); "Save" on Android is two steps (local write, then URI write) so a failed write-back must be shown; a future save path that forgets `CommitAsync` silently doesn't reach Android/Web (mitigated by a test that greps every save site).

**Option B: stream-first.** Replace paths with a document handle (`IPlatformFile`: name, OpenRead/OpenWrite, token) across Core and Ui.
- For: the clean long-term model; no working copies; natural for Web and Android.
- Against: rewrites most of Core's services and every page (hundreds of edits), high risk to the Windows app, delays every platform. Library APIs that take paths would need stream overloads anyway.

**Option A: paths only, no write-back.** Hosts copy in; saving produces a new file the person must export.
- Against: "Save" doesn't save on Android/Web — a visible regression from the design. Not recommended.

## 4. Host → app events (no new interface)

Files that arrive from outside — window drop, "Open with", Android share/VIEW intent, macOS open-file event, Web upload — go through the existing services, exactly as Windows does today (`MainWindow.xaml.cs:70-86`):
- Bench open (`BenchDropService.IsBenchActive`) → `BenchDropService.Drop(paths)`
- otherwise → `OpenDocumentService.Request(path)`
Paths are working paths per §3.

## 5. Chrome that isn't in the Ui yet

Per DESIGN-NOTE, these are built as **new components** (new files under `src/ChuvadiReader.Ui/Components/Platform/`) selected by `IPlatformInfo`; the only existing-file edits are the hooks that place them (listed in EDITS-FOR-APPROVAL.md). Who builds each component is Arun's pending decision; the interface supports all of them now.

| Component | Shown when | Design frame |
|---|---|---|
| `PlatformRoot` — sets `data-platform`/`data-form-factor`/`data-input` on `<html>`, listens to `FormFactorChanged` | always | — |
| `WindowButtons` — traffic lights left or right, or nothing | `Chrome.Buttons` | Revamp v3 frames |
| `AccountMenu` — avatar + menu: Get the desktop app, Account settings, Sign out (**no** My files / Shared with me: no storage or sharing this release) | `Chrome.AccountMenu` | 12b |
| `PortalSignIn` — Sangam sign-in page | Web, not signed in | 12a |
| `SangamGate` — "Signing needs a Sangam identity" card | any platform, gated action | 12c |
| `StatusLocation` — "offline · on this device" / "processed on the server · encrypted at rest · only you can see it" | `Capabilities.ServerSide` | 12b |
| `PhoneBottomBar`, `PhoneToolStrip`, `ViewSheet`, overflow ⋮ menu | `FormFactor.Phone` | 12g-12j |
| Tablet sizing (CSS only, keyed on `data-form-factor="tablet"`) | `FormFactor.Tablet` | 12d-12f |
| macOS menu bar | native, built by the Mac host from `IAppCommands` | README "Platforms" |
| Linux window chrome (12 px corners, GNOME/KDE button order) | Linux host + `Chrome` | README "Platforms" |

## 6. Printing per platform — DECIDED (F-4)

**Rule (Arun): printing is vector — never pages rendered as images.**

| Host | How |
|---|---|
| Windows | Keep `WpfPrintService` **for now**. Note: today it rasterises pages (chuvadi-pdf raster → XPS images), which breaks the vector rule; moving Windows to vector (Chuvadi.Print XPS in rc.3, or the PDF through the spooler) is a later item for Arun. |
| macOS | OS print panel fed the **PDF itself** (`NSPrintOperation` / `UIPrintInteractionController` with the PDF data) — vector. |
| Linux | Send the **PDF as-is to CUPS** (CUPS filters are an OS component); raster only as a fallback when the queue can't take PDF. The app's PrintCard (range, copies, fit) stays the dialog. |
| Android | `PrintManager` with the PDF document. |
| Web | Browser print of the PDF. Server/hub printing later. |

## 7. Web-specific host settings (no Ui edits)

- Register with `AppStateLifetime.PerConnection`.
- No per-user server storage this release: working copies are session-only, encrypted at rest, deleted on sign-out, session end or expiry; `IAppPaths.WorkingFolder` is per session. Save / Save as → browser download (`SavesAsDownload`).
- `IAppStorage` (settings): per signed-in person; where it lives is the Web chat's design within "no file storage".
- Raise SignalR `MaximumReceiveMessageSize` (or stream) for image paste/drop and snapshots (INVENTORY B4).
- Library blockers before any multi-user load: chuvadi-pdf **Pdf A** (name table never shrinks) and **Pdf B** (shared LiPi FontRenderer race).

## 8. Decisions (coordinator, on Arun's delegation, 6 Nov 2026)

- **F-1** C — working path + write-back. Web: session-only working copies; Save and Save as = browser download.
- **F-2** (a) — new `ChuvadiReader.Platform` project, referenced by Core (applied in Group A).
- **F-3** Windows uses `AddChuvadiReader(...)`; the set-equality test stays (applied in Group A).
- **F-4** Vector printing per §6.
- **F-5** Web follows the browser: phone/tablet layouts on small screens, ⌘ labels on Mac browsers.
- **F-6** Keep both `IImageClipboard` and `IClipboard`.
- **F-7** Dev-only preview host (never shipped).
- Packages come from chuvadi-reader's committed `ci/packages` (byte-identical to the Windows app); re-packing from the tag is only a check.
