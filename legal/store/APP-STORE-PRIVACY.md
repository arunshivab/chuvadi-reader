> **DRAFT, to be reviewed by counsel.** App privacy answers and store privacy text for Apple (Mac), Microsoft Store (Windows) and Linux stores, if Chuvadi is distributed there. Re-check before every release that adds an online feature.

# App stores — privacy answers and text

## Basis (checked 7 Nov 2026)
- The desktop apps have no accounts, analytics, advertising or crash reporting, and send nothing to Sthraas (Privacy Notice, Part A).
- Mac: today distributed outside the Mac App Store (Developer ID, not sandboxed; `platform/mac`). These answers apply if App Store distribution is chosen (an open decision; it would also require sandboxing).

## Apple — App Privacy ("nutrition label")
| Question | Answer |
|---|---|
| Do you or your third-party partners collect data from this app? | **No — "Data Not Collected"** |
| Tracking (App Tracking Transparency) | No tracking |
| Privacy policy URL | `https://[portal domain - to be confirmed]/privacy` |
| Licence agreement | Custom EULA: `legal/EULA.txt` (section 10 carries Apple's required terms) |

## Microsoft Store (Windows), if used
- Privacy policy URL: as above (required for apps that declare network capabilities; recommended anyway).
- Declared capabilities: none beyond file access chosen by the user. [To be confirmed with the packaging chosen.]

## Store privacy text (for listings and websites)

**Short (≤ 170 characters):**
Chuvadi Reader works on your device. Your documents never leave it: no account, no analytics, no ads, nothing sent to us.

**Long:**
Chuvadi Reader opens, edits, signs and protects your documents on your own device. Your files are not uploaded anywhere, and the app has no accounts, analytics, advertising or crash reporting — it does not send us any personal data. Settings and recent files stay in the app's folder on your device. Chuvadi is free to use. See the Chuvadi Privacy Notice for details.

**Web portal (if listed anywhere):**
On the Chuvadi web portal you sign in with your Sangam ID. Your documents are processed in an encrypted area only your session can use and are deleted when you sign out or the session ends.
