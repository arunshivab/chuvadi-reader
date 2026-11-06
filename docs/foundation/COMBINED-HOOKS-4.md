# Fourth combined patch — applied on `platform/foundation` (7 Nov)

| Item | From | Where | Desktop |
|---|---|---|---|
| **Single instance, tabbed** (W-A, L-A, F-1): kit contract `IAppInstance`, router `IncomingFiles`, `DesktopSingleInstance`; Windows host wired; the file Windows is started with now opens; every dropped file opens in its own tab | Coordinator (decided 7 Nov) | Platform, Hosting, Windows `App`/`MainWindow`; `IAppReadySignal.IsReady` added | Behaviour change (approved) — `SINGLE-INSTANCE.md` |
| **Host notices** `IHostNotices` (Title + ReadText, read only when its tab opens); kit default none; the Licences view adds the host's tab | Coordinator, Android chat | Platform `Services.cs`, `LicencesSection` | No tab (Windows uses the installed .NET) |
| **P-06** Back closes the open card (and measure), else the host's default | Android chat | `Reader.Shell.cs`, `Reader.razor` (`IAppLifecycle.BackRequested`) | Never raised on desktop |
| **Print card toast style** from the print outcome (Failed / Unavailable → error toast), not from the wording | Linux chat | `PrintCard.razor` | The no-printing refusal (L-8) and host failures now show as errors; success unchanged |
| **W-1** Library empty state on the portal: "No PDFs here yet. Use Upload to open documents. Files are removed when you sign out." | Web portal UI (O-1) | `Library.razor`, `PlatformText.LibraryEmptyServer` | Unchanged |
| **W-2** "Follow the browser's light / dark setting" on the portal | Web portal UI (O-2) | `PlatformText.FollowSystemTheme` | Unchanged |
| **W-3** "Leave blank to use your Sangam name. Kept until you sign out." on the portal | Web portal UI (O-2) | `PlatformText.PreferredNameHint` | Unchanged |
| **W-4** Home first-run tip on the phone layout: "Tip: tap “Find or do anything” to reach any file or action. Try typing “merge”." | Web portal UI (O-3) | `Dashboard.razor`, `PlatformText.PhoneFindTip` | Unchanged |

## Proof
- Windows pixel-identical: 9 screens × 3 scales, before `89cffb5` vs after — 26/27 on the first pass; the one difference is the known first-shot Reader 1× artefact (17 px, top right; seen before these changes too), identical on reruns.
- Portal preview: W-1, W-2, W-3 shown; phone/tablet preview: the host-notices tab appears and reads on demand; desktop has no such tab. No page errors.
- Build: Platforms solution and WPF host with `-warnaserror`, 0 warnings. Tests **265/265** (Reader 171, Hosting 48, Mobile UI 15, Web UI 31).
- CI is stopped (GitHub billing); everything above is local. The Windows single-instance path runs in CI's Windows jobs once billing is back.
