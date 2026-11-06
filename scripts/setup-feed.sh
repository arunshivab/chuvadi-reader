#!/usr/bin/env bash
# Local NuGet feed for every platform build.
#
# SOURCE OF PACKAGES (coordinator decision, 6 Nov 2026): the committed ci/packages folder from
# chuvadi-reader — byte-identical to what the Windows app ships. This script copies it into
# ./localpackages, the folder the repo's nuget.config already points to.
# (CI restores straight from ci/packages with --configfile ci/nuget.config; same packages.)
#
#   scripts/setup-feed.sh                 copy ci/packages → ./localpackages
#   scripts/setup-feed.sh --verify-pack   also CHECK: pack chuvadi-pdf from the unchanged tag v3.31.2
#                                         and confirm it produces exactly the chuvadi-pdf package
#                                         names and versions in ci/packages. The re-packed files
#                                         are only compared, never used.
#
# Never modifies library code. Safe to re-run. Needs bash, coreutils; --verify-pack also git, curl,
# sha256sum and the .NET 10 SDK.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/ci/packages"
FEED="$ROOT/localpackages"
PDF_TAG="v3.31.2"
PDF_VERSION="3.31.2"
FONTS_SHA="c15a82d1caa3d70f3e2abe16dd94201ea1d3dae91764735a19ffa7398c37f5fe"

mkdir -p "$FEED"
cp -f "$SRC"/*.nupkg "$FEED"/
echo "Feed ready: $(ls "$FEED"/*.nupkg | wc -l) packages from ci/packages in $FEED"

if [ "${1:-}" != "--verify-pack" ]; then
  exit 0
fi

# ---- check only: re-pack chuvadi-pdf from the tag and compare package identities -------------
WORK="${CHUVADI_FEED_WORK:-${TMPDIR:-/tmp}/chuvadi-feed-work}"
rm -rf "$WORK" && mkdir -p "$WORK/out"
git clone -q --depth 1 --branch "$PDF_TAG" https://github.com/arunshivab/chuvadi-pdf "$WORK/chuvadi-pdf"
# chuvadi-pdf's build/fetch-fonts.ps1: Chuvadi.Fonts into its git-ignored .fonts-feed, SHA-256 checked.
mkdir -p "$WORK/chuvadi-pdf/.fonts-feed"
cp "$SRC/Chuvadi.Fonts.1.0.0.nupkg" "$WORK/chuvadi-pdf/.fonts-feed/"
echo "$FONTS_SHA  $WORK/chuvadi-pdf/.fonts-feed/Chuvadi.Fonts.1.0.0.nupkg" | sha256sum -c --quiet -
# chuvadi-pdf's build/pack.ps1 command.
dotnet pack "$WORK/chuvadi-pdf/Chuvadi.slnx" -c Release -p:Version="$PDF_VERSION" -o "$WORK/out" --nologo -v quiet
if [ -n "$(git -C "$WORK/chuvadi-pdf" status --porcelain)" ]; then
  echo "chuvadi-pdf working tree changed during pack — refusing to continue" >&2
  exit 1
fi

expected=$(cd "$SRC" && ls Chuvadi.Pdf*.nupkg Chuvadi.Cryptography*.nupkg | sort)
packed=$(cd "$WORK/out" && ls *.nupkg | sort)
if [ "$expected" != "$packed" ]; then
  echo "Package set differs between ci/packages and the $PDF_TAG pack:" >&2
  diff <(echo "$expected") <(echo "$packed") >&2 || true
  exit 1
fi
echo "Verified: $PDF_TAG packs to the same $(echo "$packed" | wc -l) chuvadi-pdf package names and versions as ci/packages."
