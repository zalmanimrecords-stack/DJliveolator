#!/usr/bin/env bash
#
# fetch-bass.sh — fetch the un4seen BASS native library for the current (or a specified)
# platform into runtimes/<rid>/native/, where the App build step picks it up.
#
# BASS ships as a per-platform zip from un4seen.com. This script downloads the right
# archive, extracts only the native library we need, and places it under
# runtimes/<rid>/native/ using the canonical name ManagedBass probes for:
#   win-x64    -> bass.dll
#   osx-x64    -> libbass.dylib   (the macOS dylib is universal: arm64 + x64)
#   osx-arm64  -> libbass.dylib
#   linux-x64  -> libbass.so
#
# The binaries are intentionally git-ignored (.gitignore: /runtimes/). They are NOT
# redistributed in source control because BASS requires a commercial license for
# distribution (see docs/01-audio-source-layer.md, "BASS licensing").
#
# Usage:
#   ./scripts/fetch-bass.sh                 # auto-detect current platform
#   ./scripts/fetch-bass.sh osx-arm64       # force a RID
#   BASS_VERSION=24 ./scripts/fetch-bass.sh # override archive version tag
#
set -euo pipefail

VERSION="${BASS_VERSION:-24}"

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os" in
    Darwin)
      if [ "$arch" = "arm64" ]; then echo "osx-arm64"; else echo "osx-x64"; fi ;;
    Linux)  echo "linux-x64" ;;
    MINGW*|MSYS*|CYGWIN*) echo "win-x64" ;;
    *) echo "" ;;
  esac
}

RID="${1:-$(detect_rid)}"
if [ -z "$RID" ]; then
  echo "Unsupported platform; pass a RID explicitly (win-x64 | osx-x64 | osx-arm64 | linux-x64)." >&2
  exit 1
fi

# Per-RID: archive name, inner lib name to search for, output name, preferred arch subdir.
case "$RID" in
  win-x64)   ARCHIVE="bass${VERSION}.zip";       INNER="bass.dll";     OUT="bass.dll";     PREFER="x64" ;;
  osx-x64)   ARCHIVE="bass${VERSION}-osx.zip";   INNER="libbass.dylib"; OUT="libbass.dylib"; PREFER="" ;;
  osx-arm64) ARCHIVE="bass${VERSION}-osx.zip";   INNER="libbass.dylib"; OUT="libbass.dylib"; PREFER="" ;;
  linux-x64) ARCHIVE="bass${VERSION}-linux.zip"; INNER="libbass.so";   OUT="libbass.so";   PREFER="x86_64" ;;
  *) echo "Unknown RID: $RID" >&2; exit 1 ;;
esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST_DIR="$REPO_ROOT/runtimes/$RID/native"
DEST_FILE="$DEST_DIR/$OUT"
URL="https://www.un4seen.com/files/$ARCHIVE"

echo "Fetching BASS for $RID"
echo "  source : $URL"
echo "  target : $DEST_FILE"

if [ -f "$DEST_FILE" ]; then
  echo "  already present — skipping download. Delete it to re-fetch."
  exit 0
fi

mkdir -p "$DEST_DIR"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
ZIP="$TMP/$ARCHIVE"

if command -v curl >/dev/null 2>&1; then
  curl -fSL "$URL" -o "$ZIP"
elif command -v wget >/dev/null 2>&1; then
  wget -q "$URL" -O "$ZIP"
else
  echo "Neither curl nor wget is available." >&2
  exit 1
fi

unzip -qo "$ZIP" -d "$TMP"

# un4seen layouts vary (root, x64/, libs/x86_64/, ...). Find the lib by name, preferring an
# architecture subfolder when one exists, then the shortest path as a tiebreak. Built with a
# plain while-read loop (not mapfile) so it works on the bash 3.2 that ships with macOS.
CHOSEN=""
FALLBACK=""
while IFS= read -r m; do
  [ -z "$m" ] && continue
  [ -z "$FALLBACK" ] && FALLBACK="$m"
  if [ -n "$PREFER" ] && printf '%s' "$m" | grep -q "$PREFER"; then CHOSEN="$m"; break; fi
done < <(find "$TMP" -type f -name "$INNER" | sort)

[ -z "$CHOSEN" ] && CHOSEN="$FALLBACK"
if [ -z "$CHOSEN" ]; then
  echo "Could not find $INNER inside $ARCHIVE. The archive layout may have changed; inspect $TMP." >&2
  exit 1
fi

cp -f "$CHOSEN" "$DEST_FILE"
echo "  done   : extracted $(basename "$CHOSEN") -> $DEST_FILE"
