# Legal documents — Chuvadi (all DRAFTS, to be reviewed by counsel)

Arun's decisions (7 Nov 2026): proprietary, free for everyone; copyright holder for all products **Sthraas Intelligence Pvt Ltd** (rights to be transferred from Arun by a separate legal document); governing law India, courts at **Ahmedabad, Gujarat**; the "not a medical device" clause stays. Nothing here goes to a customer until counsel has reviewed it and the DRAFT line is removed.

| File | For | Shown in the app | Placeholders left |
|---|---|---|---|
| `../LICENSE` | Repository and every package | Settings › About › Licences › Licence | registered address, legal e-mail |
| `EULA.txt` | Desktop and mobile apps; installers and app stores (section 10: Apple's required terms) | About › Licences › Licence agreement; beside the Windows exe | registered address, legal e-mail |
| `TERMS-OF-USE.txt` | Web portal | `/terms` (before sign-in) and About › Licences on the portal | grievance officer, address, e-mail, acknowledgement time, date |
| `PRIVACY-NOTICE.txt` | Apps (Part A) and portal (Parts B–D), DPDP Act 2023 and DPDP Rules 2025 | `/privacy` (before sign-in) and About › Licences › Privacy | log retention, hosting provider, legal basis wording, parental consent process, grievance officer, languages, date |
| `COOKIE-NOTICE.txt` | Web portal: cookies and browser storage | About › Licences › Cookies (portal) | browser-storage list if the Web team adds any; date |
| `THIRD-PARTY-NOTICES.txt` | Everything shipped: chuvadi-pdf (Apache-2.0), Chuvadi libraries (MIT), LiPi Sans and the display fonts Fraunces, IBM Plex Mono, Instrument Serif, Tiro Tamil (OFL-1.1, each with its own copyright line), Microsoft/.NET (MIT) | About › Licences › Third-party notices; beside the Windows exe | — (regenerate with `python3 scripts/make-notices.py` after any package change) |
| `licenses/` | Verbatim licence texts the notices are built from (Apache-2.0, .NET MIT, each font's OFL.txt from google/fonts and the LiPi Sans package) | — | — |
| `store/PLAY-DATA-SAFETY.md` | Google Play data-safety form (Android: no data collected) | — | privacy URL domain, audience |
| `store/APP-STORE-PRIVACY.md` | Apple App Privacy, Microsoft Store, store privacy text | — | privacy URL domain |

## Points for counsel
1. **DPDP timing.** The DPDP Rules 2025 were notified on 13 Nov 2025; the core Data Fiduciary obligations apply from 13 May 2027. The notice is written to that standard now.
2. **Log retention vs "nothing kept".** The portal deletes documents at session end, and the deployment runbook keeps server logs in memory only. The DPDP Rules may require certain logs to be kept for at least one year. Decide the retention, and whether file names may appear in logs.
3. **Grievance officer and contact person.** Required by the DPDP Act (section 8(9)). Name, address and e-mail are placeholders.
4. **Sangam's role.** Sangam is a separate identity service. Confirm whether it is a separate Data Fiduciary (as written) or a processor for Sthraas.
5. **Children.** The portal is written as 18+ unless a parent or guardian gives verifiable consent. Confirm the approach.
6. **Copyright in packages.** The chuvadi-pdf packages say "© 2025 Chuvadi Contributors" (Apache-2.0); LiPicons names imagiQa and states no licence. Both are changes in their own repositories after the rights transfer.
7. **Hosting provider and region** for the portal (India) are still to be confirmed.
