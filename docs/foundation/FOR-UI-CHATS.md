# For the Mobile app UI and Web portal UI chats

How to switch platform chrome without touching existing Ui files. Groups A, B and C are applied on `platform/foundation`.

## The signal
One value, four layouts — `ChuvadiReader.Platform.ShellLayout`:

| `ShellLayout` | When | Design frames |
|---|---|---|
| `Desktop` | Windows, macOS, Linux | Reader Side Rails |
| `Tablet` | Android, `FormFactor.Tablet` | Portal & Android 12d-12f |
| `Phone` | Android, `FormFactor.Phone` | Portal & Android 12g-12j |
| `Portal` | Web in a desktop-sized browser | Portal & Android 12a-12c |

It is derived by `ShellLayouts.From(kind, formFactor)` — hosts report kind and form factor, nobody sets the layout by hand. **Web follows the browser (F-5):** a phone- or tablet-sized browser gets `Phone` / `Tablet`, and the portal chrome (account menu, sign-in, server status text) still shows — test it with `Info.IsPortal()` or `Chrome.AccountMenu`, never with `Shell == Portal`. The value changes at run time when the host raises `FormFactorChanged` (Android fold/unfold, browser resize).

Web this release: no server storage — Save and Save as are browser downloads (`Capabilities.SavesAsDownload`); the account menu has Get the desktop app, Account settings, Sign out only.

## How to read it
1. **Razor:** `@inject IPlatformInfo Info`, then `@switch (Info.Shell()) { … }` and `Info.IsPortal()`. A component whose look depends on the form factor subscribes to `Info.FormFactorChanged` in `OnInitialized` and calls `InvokeAsync(StateHasChanged)` (unsubscribe in `Dispose`). MainLayout already re-renders the shell on every change.
2. **CSS:** `<html data-shell="desktop|tablet|phone|portal">`, plus `data-platform`, `data-form-factor`, `data-input` (`touch`, `pen` …), set by MainLayout on non-Windows hosts (and by the host page for the first paint). Absent = Windows desktop. Example: `html[data-shell="tablet"] .leftbar { width: 72px; }`.

There is no cascaded platform object on purpose: it cost ~25 extra JIT-compiled methods at Windows startup for nothing (startup rule, 6 Nov).

Other facts on the same object: `Chrome` (window buttons left/right/none, native menu bar, account menu, title-bar drag, corner radius), `Capabilities` (pick folders, save in place, watch folders, reveal, drop files, share, server-side), `Keys.Format("Ctrl K")` (⌘ on macOS).

## Where your components go
- New files only: `src/ChuvadiReader.Ui/Components/Platform/Phone/…`, `…/Tablet/…`, `…/Portal/…` (and CSS beside them).
- Each needs a hook in an existing file to appear (MainLayout, Reader, Bench, Redact). Don't edit those: list each hook (file, line, exact change, why, pros/cons, risk to Windows) and send it to the coordinator — the "Later hooks" list in `EDITS-FOR-APPROVAL.md` shows the ones already expected.
- Keep desktop markup untouched: your component renders only for its `ShellLayout`.

## Previewing without an Android device or a web host
Not provided yet — see the Foundation status for the proposed preview host (decision F-7).
