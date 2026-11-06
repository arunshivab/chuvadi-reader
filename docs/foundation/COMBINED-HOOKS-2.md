# Second combined patch — applied on `platform/foundation`

Commits `aadd992` (code and tests) and `7c67b89` (packages). The full diff is `proposal/combined-hooks-2.patch`: 18 files; the 11 existing `src` files account for +147/−36.

**Windows renders pixel-identical** (proof below). Startup cost: none (below).

## What went in

| # | Item | Where | Windows |
|---|---|---|---|
| 1 | **Web wording (U-11):** Home summary ends "… · processed on the Chuvadi server · removed when you sign out."; drop zone "Drop a document, or click to upload" | Dashboard.razor, PlatformText.HomeServerLine | unchanged text |
| 1+ | Web first-run box: "Upload a file…" and no "Choose a library folder" (see note below) | Dashboard.razor | unchanged |
| 2 | **Android wording:** "Follow the phone's / device's light / dark setting"; Preferred-name hint "Leave blank to show no name. Stored on this phone / device, never sent anywhere." | PlatformText, AppearanceSection, LanguageSection | same strings as before, character for character |
| 3 | **Bug fix F-2:** "Good evening, Arun S." — no second full stop when the name already ends with one. The name itself is shown exactly as typed (spaces, case, everything) | new Core `GreetingText`, Dashboard.razor | names without a final "." unchanged |
| 4 | **Bug fix F-3:** `OpenDocumentService` queues requests; the Reader takes each one once, in order, so two requests made before it runs open two tabs, each with its own file and name | OpenDocumentService (Core), Reader.razor | Windows only ever sends one request, so nothing changes there |
| 5 | **Bug fix L-8:** a PDF whose own permissions forbid printing doesn't print on **any** platform. The open document knows (`AllowsPrinting`, read at open — no extra file read); the Print card is locked with "This file's permissions don't allow printing."; the Windows print service refuses it as well | new Core `PrintPermission`, PdfDocumentVm, ChuvadiPdfReader, PrintCard, WpfPrintService | **behaviour change on Windows** (approved bug fix) |
| 6 | **Kit:** `DesktopAppPaths` save folder falls back to the XDG documents folder, else `~/Documents` (created on first save), when Documents doesn't exist yet — no more saving under `/tmp` | DesktopAppPaths | unchanged (Documents always exists) |
| 7 | **Chuvadi.Print / Chuvadi.Print.Ipp 1.0.0-rc.2** in `ci/packages`; SHA-256s match the Linux chat's BUILD-LOG §18 (`PACKAGES-3.31.2.md`) | ci/packages | feed only; no shared project references them |

Tests added: 10 Reader (L-8 ×2, F-2 ×6, F-3 ×2), 5 kit (save folder), 8 wording. The L-8 test fails without the fix.

## Choices to confirm (each easy to reverse)

| Choice | Done | Alternative | Pros of what's done | Cons |
|---|---|---|---|---|
| Android Preferred-name hint | "Leave blank to show no name. Stored on this phone…" | Literal swap: "Leave blank to use your phone's name" | True: Android shows no name until one is set (D-59) | Not the literal wording asked for |
| Web first-run box (item 1+) | "Upload a file…", library-folder button hidden | Leave it as desktop until approved | Follows H3 ("Upload") and the HIDE rule (no folders on the Web); a new Web user sees it first | One step beyond U-11's two lines |
| L-8 Print card | Locked, reason as tooltip | Hidden | The person learns why they can't print | — |
| L-8 owner password | Printing stays refused even when the file was opened with its owner (permissions) password | Allow printing after an owner-password open | Safe default | The owner can't print here until the library says which password opened the file — the same library gap as removing a password. **Library ask via the coordinator** (one property, e.g. `PdfDocument.OpenedWithOwnerPassword`) |
| F-3 jump-to-page | When several requests queue, the page of the last one applies (its tab is the one shown) | Per-tab pending page | No new state | Earlier tabs open at their remembered page, not a requested one (only reachable on Web multi-upload) |

## Proof: Windows unchanged

Same method as the first patch (`tools/PixelProof/desktop-identical.js`): preview host, desktop profile (Windows), Release builds of `63af90f` (before) and `aadd992` (after), headless Chromium 1440×900, clock and caret masked. Now 9 screens, including the Print card (`/reader` + Ctrl P) and Settings › Language and › About.

| Screen | 1× | 1.25× | 1.5× |
|---|---|---|---|
| Home, Reader, Bench, Redact, Library, Settings, Settings › Language, Settings › About, Print card | identical | identical | identical |

27/27 identical; `<html>` attributes identical. Note: the first run of this check showed a difference on Home and Library — the save-folder path. This sandbox has no `~/Documents`, so before showed `/tmp/Chuvadi Documents` and after `~/Documents/Chuvadi Documents`: item 6 working. With a Documents folder present (as on every Windows PC) the two are identical.

## Other platforms checked in the preview
- **Web portal** (new `CHVD_FF=web`: Web kind, server-side, no folders, account menu): Home shows "… · processed on the Chuvadi server · removed when you sign out." and "Drop a document, or click to upload"; no errors.
- **Android phone / tablet:** "Follow the phone's / device's light / dark setting"; the hint as above.
- Found while previewing the portal: `AccountMenu` needs `PortalLinks` registered, and the kit doesn't register it (by design — the Web host does, from its configuration). The preview registers placeholders. **Web chat: register `PortalLinks` in your host.**

## Build, tests, startup
- Platforms solution and WPF host with `-warnaserror`: 0 warnings, 0 errors. Tests **229/229** (171 Reader, 33 Hosting, 11 Mobile UI, 14 Web UI).
- Startup probe, 25 interleaved runs, medians: ReadyToRun 196.0 → 196.3 ms, 358 → 358 methods compiled at startup; JIT 288.8 → 278.5 ms (noise). No startup cost.

## For the other chats
- **Linux:** the kit now does what `LinuxAppPaths` does; you can delete it and use `DesktopAppPaths` (same result). Your print service should also refuse with `PrintPermission.Allows(doc)` / `PrintPermission.Refused` (the card already blocks it). Print packages are now in the shared feed.
- **Mac, Android, Web print services:** call `PrintPermission.Allows` on the opened document before the dialog, as Windows does.
- **Web:** register `PortalLinks`. A multi-file upload may now call `OpenDocumentService.Request` once per file.
- **Android:** the wording follows the form factor; nothing to register.
