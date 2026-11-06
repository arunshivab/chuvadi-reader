# chuvadi-pdf 3.31.2 packages in ci/packages (D-54, 6 Nov 2026)

Packed by the Foundation chat from the **unchanged** tag `v3.31.2` of `arunshivab/chuvadi-pdf`
(tag object b152b9b, commit aff17e6), because chuvadi-pdf has no release assets. They stand in
until Arun's own 3.31.2 copies land in chuvadi-reader's `ci/packages`; then those replace these
(the DLLs will differ byte-for-byte — embedded build paths — but not in content or version).

- Command (chuvadi-pdf's own `build/pack.ps1`): `dotnet pack Chuvadi.slnx -c Release -p:Version=3.31.2`
  with Chuvadi.Fonts 1.0.0 in `.fonts-feed` (its `build/fetch-fonts.ps1`), SDK 10.0.403, Linux x64.
- Result: 34 packages, 0 warnings, 0 errors; the tag's working tree unchanged afterwards.
- Same 34 package names as the 3.31.1 set, which they replace (3.31.1 removed from `ci/packages`).
- Core references 17 of them through `ChuvadiPdfVersion` = 3.31.2; `tests/ChuvadiReader.Tests` the umbrella 3.31.2.
- Chuvadi.Fonts stays 1.0.0: the v3.31.2 tag itself pins Chuvadi.Fonts 1.0.0 (`Directory.Packages.props`).

## SHA-256
| Package | SHA-256 |
|---|---|
| Chuvadi.Pdf.3.31.2.nupkg | `c84f0980a4b8595cf0589083b48261416645d7321c3948c16dcf7834446154cc` |
| Chuvadi.Pdf.Annotations.3.31.2.nupkg | `57253d6b193fca96d907686d59700bfd5bf1eeff2412afe1cf5617c6c994665b` |
| Chuvadi.Pdf.Authoring.3.31.2.nupkg | `5df5b2f95b21a11584c60c1505d14f01327df45e00d01050529d8f2352650dc2` |
| Chuvadi.Pdf.Color.3.31.2.nupkg | `0230888241e280e1b1999e06b181e8bfa1a63afd8421cabfc19dcf2931cd9f20` |
| Chuvadi.Pdf.Content.3.31.2.nupkg | `a10cc1f81e20a41c94d154e53e5c31c925e26e7b0acc7c1a43d870414d60fd5a` |
| Chuvadi.Pdf.Documents.3.31.2.nupkg | `0d143ab5553d718eedd8638626fa6aa42136fa05ec29af2545fab80a1e65a8a0` |
| Chuvadi.Pdf.Encryption.3.31.2.nupkg | `fab29f6a3d3d170b1e13df9aa5c92db94814ba322ffd947eba4cde6d874c885a` |
| Chuvadi.Pdf.Filters.3.31.2.nupkg | `acc9c5ada10c2b5b0e2be3984fe89e861e83f6abeb47378800cc6bb5addefde9` |
| Chuvadi.Pdf.Fonts.3.31.2.nupkg | `48a36e783101aae5d79ad449d0aca74585e48cf373fc76aecbb2587dfda30d60` |
| Chuvadi.Pdf.Fonts.Bundled.3.31.2.nupkg | `6d5204e15c75b234d29dc9dbd1ac20c1f3314edae8c41c19c4c5a9a4333443e7` |
| Chuvadi.Pdf.Fonts.Rendering.3.31.2.nupkg | `f920958110ee9c0584cac78de3e4a15addaca6e73939099fe2f79469fe136594` |
| Chuvadi.Pdf.Fonts.Woff2.3.31.2.nupkg | `8a96e1ee6e5cfa381ce6d8cbbeb3470b7f774c358446dd640a651f471fbfe29d` |
| Chuvadi.Pdf.Forms.3.31.2.nupkg | `a7e4969740794568ed1b2cf343fa2af24763b9f3127ac89dba7fe52e005eedce` |
| Chuvadi.Pdf.Graphics.3.31.2.nupkg | `572e4cfc321e4600be948f1343e02982fb4178bbcc749ad60d6c4e1f6b75e519` |
| Chuvadi.Pdf.IO.3.31.2.nupkg | `e3079b4ce0e318a85939f38ce2685a022f31454eae06401405accae7ba6b30c6` |
| Chuvadi.Pdf.Images.3.31.2.nupkg | `47d130bbedc7d79801a349e3d8d75758c591f55de6db9ce5836ff35b3ca62e24` |
| Chuvadi.Pdf.Objects.3.31.2.nupkg | `3e1e052b2cbc307a78c74b12e4447b0d9241946710acb039fd60eb730084c2a4` |
| Chuvadi.Pdf.Operations.3.31.2.nupkg | `456a0ab1eb5dc6942ddba51c6491b58d397b9067f7f4bcb096b401e1a33651fc` |
| Chuvadi.Pdf.PdfA.3.31.2.nupkg | `7d2d9478e53c52f697ea63d247fd8837b53d6745fbf7a20c9b2c9533b8eda123` |
| Chuvadi.Pdf.Primitives.3.31.2.nupkg | `4651bf3080b904720c971a4ae7fd23e1ec95febe24b6f739d393885338159f33` |
| Chuvadi.Pdf.Reader.3.31.2.nupkg | `23997505d3794be0096304c9765d7f98411a93b0fd441414b3655d89fd2fb28f` |
| Chuvadi.Pdf.Redaction.3.31.2.nupkg | `e2e2c58aeeecb3affefe823e7af1a48abe09914f2e6ca15a5501e864254b2b41` |
| Chuvadi.Pdf.Rendering.3.31.2.nupkg | `55e78be15fd5f1f651e327efe1eb0ef8eebfe0a7a4aef3e206ce8ce808fabaa8` |
| Chuvadi.Pdf.Rendering.DisplayList.3.31.2.nupkg | `abb5f749322f2dee754158df7b14021ac59775dcd9b2c6055efb4da3c6e45b7a` |
| Chuvadi.Pdf.Rendering.Raster.3.31.2.nupkg | `182957b8f37a0d7be81eff9016e00eec309e441e5be9a4691368510482a8596b` |
| Chuvadi.Pdf.Rendering.Walking.3.31.2.nupkg | `c6217c8267b6065bbe588ba5721c601530910bb96ed22f9bb845a8afef0e4c7c` |
| Chuvadi.Pdf.Rendering.Wpf.3.31.2.nupkg | `fc050d8c42473384e30ce831b8c6367609e9687c38038c14f845836cf0681c64` |
| Chuvadi.Pdf.Signatures.3.31.2.nupkg | `4908e1ded34a1b4facbad68d19c19219b6c53bd502b7dc2f9a4088515e4d4cf9` |
| Chuvadi.Pdf.Svg.3.31.2.nupkg | `8efbdf70a64c256f4086aa471d93f31001c5a9d6693985ee3c33a4fc2845b6cd` |
| Chuvadi.Pdf.Text.3.31.2.nupkg | `2515b0aa0555fda74efecce24becd7f27a1d06d5b57dccc9cdaa35a19ecdc1ca` |
| Chuvadi.Pdf.Text.Shaping.3.31.2.nupkg | `6316731c0a3024970e7ba7a70b86f599f5513a53a8a75a3986c48ff501ed02ec` |
| Chuvadi.Pdf.Watermark.3.31.2.nupkg | `4c9693a7daa3585fdb1d82fda715efeda9389705cb8375dfb18fd37ea5c0411f` |
| Chuvadi.Pdf.Xfa.3.31.2.nupkg | `14c98d1e243097865a50d8783a39aa26251b8721c9e2f3b0d4e4fa2f03eca4d7` |
| Chuvadi.Cryptography.3.31.2.nupkg | `f2de68810fa20e27ed0a2ba59cd0d3d0c1d49090c3a8f6bdc4cc6b1fed2ca2f8` |

## Chuvadi.Print and Chuvadi.Print.Ipp 1.0.0-rc.2 (adopted centrally, 7 Nov 2026)
Taken byte-for-byte from `platform/linux`'s `ci/packages` (the Linux chat downloaded them from the
`print-v1.0.0-rc.2` release of `arunshivab/chuvadi`; no checksum file is published there). Checked here
against the Linux chat's BUILD-LOG §18 — both match:

| Package | SHA-256 |
|---|---|
| `Chuvadi.Print.1.0.0-rc.2.nupkg` | `252b6d10204d37069a35040ea89d58e1662a452c4e05fd90a0d8b90cd1049cbf` |
| `Chuvadi.Print.Ipp.1.0.0-rc.2.nupkg` | `82304b782ce23ac5dcb3895e1fee31898abf3c22222f9f9c65c423fba68aa562` |

Both target net10.0, MIT, source commit `0ffaa4b` of `arunshivab/chuvadi`; `Chuvadi.Print.Ipp` depends only
on `Chuvadi.Print` 1.0.0-rc.2, so the feed is complete. Only feed packages: no shared project references
them; the Linux host does (and any other host that prints over IPP).
