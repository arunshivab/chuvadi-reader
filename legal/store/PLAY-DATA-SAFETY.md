> **DRAFT, to be reviewed by counsel.** Answers for Google Play Console › App content › Data safety, for `in.chuvadi.reader`. Re-check before every release that adds a permission, an SDK or an online feature.

# Google Play — Data safety answers (Chuvadi Reader for Android)

## Basis (checked on `platform/android`, 7 Nov 2026)
- The manifest declares **no `INTERNET` permission** and no storage permission. Files arrive only through the Storage Access Framework, Open with or Share, which grant access per file.
- No analytics, advertising, crash-reporting or other third-party SDKs. The only libraries are Chuvadi's own and Microsoft's .NET/MAUI.
- Backups are off (`allowBackup="false"`), so app data is not copied to Google Drive backups.
- Documents, settings, recent files and drafts stay in the app's private storage on the device.

Under Play's definitions, data processed only on the device and never sent off it is not "collected".

## Answers

| Question | Answer |
|---|---|
| Does your app collect or share any of the required user data types? | **No** |
| Is all of the user data collected by your app encrypted in transit? | Not applicable: no data is collected |
| Do you provide a way for users to request that their data be deleted? | Not applicable: no data is collected. (Users delete local data by clearing app storage or uninstalling.) |
| Data types collected | None |
| Data types shared | None |
| Independent security review (MASA) | No |
| Committed to the Play Families Policy | No (the app is not designed for children) |

## Store listing — privacy policy
- Privacy policy URL (required by Play even when nothing is collected): `https://[portal domain - to be confirmed]/privacy` (the Chuvadi Privacy Notice; Part A covers the apps).
- Target audience and content: 18 and over; general productivity. [To be confirmed.]

## What would change these answers
| Change | Effect |
|---|---|
| `INTERNET` for network printing (decision A-04 pending) | Still "No data collected" if the app talks only to printers on the local network and nothing goes to Sthraas or third parties. Re-check. |
| Sangam sign-in on Android | Collected: Name, Email address, User IDs (Personal info), for App functionality and Account management; encrypted in transit: Yes; deletion request: Yes (through Sangam). Re-answer the form. |
| Any analytics or crash-reporting SDK | Must be declared. Not planned. |
