# Chuvadi.Docs 2.1.0 in ci/packages (twenty-fifth combined patch, part 4, D-168, 11 Nov 2026)

Replaces 2.0.0. Downloaded unchanged from Arun's release asset:
`github.com/arunshivab/chuvadi/releases/download/docs-v2.1.0/Chuvadi.Docs.2.1.0.nupkg`. The copy the coordinator
attached is the same file, byte for byte.

| Package | Tag → commit | Size | SHA-256 |
|---|---|---|---|
| Chuvadi.Docs.2.1.0.nupkg | `docs-v2.1.0` → f87ea0a | 391,512 B | `687703A2605F3CF2EA5EAB4AC12D3EFDE40D85C6873A2848F72CE0D556FFF67F` |

- **Hash:** equals the one in the coordinator's package list (`DOCS-2.1.0-PACKAGE.md`).
- **Ties to the tag:** the nuspec names the monorepo at commit `f87ea0a224e86d97e14ebc86a6c645d28f356a90`, the tag's commit.
- **Same terms:** no package dependencies, net10.0, MIT. Notices: the version line only.
- **Chuvadi.Docs.Conversion is not taken:** the Reader doesn't convert Markdown or text to Word.

## What the Reader does with 2.1.0
- **Limits (`DocxViewService.ReadOptions`):** starts from `DocxReaderOptions.ForUntrustedInput()` on every platform, so
  each open now also has: compression ratio 200:1 (was 1,000:1), XML depth 128 (was 256), and 1,000,000 XML elements a
  read (new). The agreed sizes stay on top:

  | | Per part | Unpacked total | The file itself |
  |---|---|---|---|
  | Shared server (D-136) | 20 MB | 64 MB | 50 MB (the library's) |
  | A person's own device (D-137) | 100 MB | 400 MB | 400 MB |

  The library's own figures (64 MB a part, 256 MB in total, 50 MB file) are looser than D-136 and tighter than D-137,
  so neither decision changes. **For the coordinator:** see the question in the status (local sizes).
- **Cancellation:** the Reader reads a Word file in one call, `Document.Load(path, password, options, token)`, with the
  open's own token (Stop opening, a closed tab, the time limit). It never keeps a `DocxReader` open, so there is no
  later read for a token to miss. Nothing to change.
- **`Document.SkippedParts`:** a header, footer, picture, styles, numbering, notes, comments or another optional part
  the file refers to but that is missing or unreadable is named above the page: "Part of this document is damaged and
  is left out: a header, 2 pictures." Before, the document was shown without it and without a word.
- **Damaged protected files:** with a password given, a damaged container is now "This file couldn't be read as a Word
  document", not "Couldn't open it with that password". Without a password the file still asks for one first (nothing
  can be known before that).

## Checks
- `OfficeViewTests`: 6 new (limits, a 60 MB scan opens locally and is refused on the server, nothing left out of a
  sound file, a damaged header and a missing picture are named, a damaged protected file is damaged). All pass.
- A sound Word file before and after: the same pixels (desktop preview). About shows 2.1.0.
- Built from an empty package cache; 0 warnings in both solutions; trimmed probe 56 of 56.
