# Foundation — build and run

## Prerequisites
- .NET SDK 10.0 (10.0.4xx), git, curl, sha256sum (Linux/macOS; on Windows use Git Bash or WSL for the feed script).

## Package feed
Packages come from chuvadi-reader's committed `ci/packages` (byte-identical to what the Windows app ships).
```bash
scripts/setup-feed.sh                 # copy ci/packages → ./localpackages (what nuget.config points to)
scripts/setup-feed.sh --verify-pack   # also check: chuvadi-pdf v3.31.2 re-packs to the same package set
```
CI restores directly with `--configfile ci/nuget.config`.

## Build and test (any OS)
```bash
dotnet build ChuvadiReader.Platforms.slnx -c Release
dotnet test  ChuvadiReader.Platforms.slnx -c Release --no-build
```
Expected: 0 warnings, 0 errors, all tests pass.

## Windows app (Windows only)
```powershell
dotnet build ChuvadiReader.slnx -c Release
dotnet run --project src/ChuvadiReader.Windows -c Release
dotnet publish src/ChuvadiReader.Windows -c Release              # ReadyToRun, win-x64 (add -r win-arm64 for Arm)
```

## Testing-only previews and pixel checks
`tests/ChuvadiReader.MobileUi.Preview` serves the real Ui in a browser (`CHVD_FF=phone|tablet|desktop|web`; desktop reports Windows, web the server portal). `tools/PixelProof` holds the Playwright scripts used to prove Windows renders unchanged (`COMBINED-HOOKS.md`). Neither ships.

## Writing a host
See `PLATFORM-INTERFACE.md` §1-§2 and `HANDOFF.md`.
