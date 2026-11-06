# Third combined patch — in progress on `platform/foundation`

Approved 7 Nov 01:00. Each item lands when its input is ready; the same pixel proof runs each time.

| Item | State |
|---|---|
| (b) Web UI's Portal files (decided versions: D-31 promises, D-34 no password — `PortalCredentials` removed, two-argument `PortalLinks` + `PlaceholderDesktopAppUrl`, U-9 `ShowSangamId`, `PortalPhoneAccount` for H5) and their tests | **Applied.** The portal status bar has no nested `sb-item` (checked in the preview: 0; the user item reads "root · Sangam") |
| (1) Web UI's latest sign-in page and `PortalLinks` (`f4e20d9`: optional `TermsUrl`, two-argument construction still works) and its tests; the preview and tests updated | **Applied** (part 2) |
| (2) **Licences page:** About › Help › Licences › Open shows the licence agreement, the licence and the third-party notices (one segmented view, "‹ About" back). The same page lives at `/licence` (`?part=notices`) for links from outside the shell, e.g. the portal's "Terms of use" (`PortalLinks.TermsUrl = "/licence"`). Texts are embedded from `LICENSE` and `legal/` (one source; read only when the page opens); a test checks they match the shipped files | **Applied** (part 2). Every host gets it with no host code |
| (5) **Legal documents (Arun's answers, 7 Nov):** Sthraas as copyright holder everywhere (`Copyright © 2026 Sthraas Intelligence Pvt Ltd. All rights reserved.`, read by About from the assembly), Ahmedabad courts, "not a medical device" kept. Drafts in `legal/` (index `legal/README.md`): EULA (desktop and mobile, with Apple's required terms), portal Terms of Use, Privacy Notice (apps + portal, DPDP Act 2023 / Rules 2025), cookie and browser-storage notice, third-party notices regenerated (now with the display fonts Fraunces, IBM Plex Mono, Instrument Serif, Tiro Tamil — each OFL text verbatim from google/fonts), Play data-safety answers, App Store privacy answers and text. Ui: About gets a Licence row with the copyright line; the Licences view shows the apps' or the portal's documents; `/terms` and `/privacy` pages for the Web host to serve before sign-in | **Applied** (part 3) |
| (3) Home drop zone on the portal and mobile | Options in `DROP-ZONE-OPTIONS.md`; waiting for a decision |
| (d) `ci.yml` and `platform-build.yml`: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7` (Node 24), as on `platform/mac` | **Applied** |
| (a) Web UI H5 phone account block (`docs/web-ui/proposal/web-ui-hooks-3-phone.patch`) | Waiting: needs Mobile UI's PhoneShell slot first (`docs/web-ui/proposal/mobile-ui-request-phoneshell-slot.patch`, not yet on `platform/mobile-ui`) |
| (c) Mobile UI Redact fit-width on phone | Waiting for the Mobile UI chat's diff |

## Proof so far
- Windows pixel-identical: 9 screens × 1/1.25/1.5×, before `2fafb60` vs after — 27/27 identical.
- Web portal preview renders with no errors.
- Build 0 warnings; tests 231/231 (Web UI tests 14 → 16).

## Proof, part 2
- Windows: 9 screens × 3 scales — 24/27 identical; the 3 differences are Settings › About, all in the Licences row (hint "Licence agreement and third-party notices", Open now enabled) — the approved change. One Reader 1× difference (17 px, top bar) was a timing flake: three reruns identical.
- Licences page checked on desktop, phone and the portal (`/licence`); no errors.
- My two drafts (`LICENSE`, `legal/EULA.txt`) are now stored one line per paragraph so they wrap to any width; the words are unchanged (checked). The third-party licence texts stay verbatim.
- Build 0 warnings; tests 234/234.

## Proof, part 3
- Windows: 22/27 identical at first; Home 1.5× and Reader 1× were timing flakes (identical on two reruns each); Settings › About differs at every scale because of the new Licence row (copyright line), which moves the rows below it — the approved change.
- `/terms`, `/privacy` and About › Licences checked on the portal; About › Licences on desktop. No errors.
- Tests 246/246 (Web UI 31: every shown text equals its shipped file, carries the DRAFT line and the copyright line; Ahmedabad in the four contractual texts).

## For the Web host
`PortalLinks` takes `(desktopAppUrl, accountSettingsUrl, termsUrl = null)` (no "Forgot?" link — D-34). Set `termsUrl` to `"/terms"` and serve `/terms` and `/privacy` before sign-in (the Ui pages; nothing else needed).
