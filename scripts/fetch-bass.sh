#!/usr/bin/env bash
#
# fetch-bass.sh — fetch the un4seen BASS + BASSmix native libraries for the current (or a
# specified) platform into runtimes/<rid>/native/, where the App build step picks them up.
#
# BASS ships as per-platform zips from un4seen.com. This script downloads the right archives,
# extracts only the native libraries we need, and places them under runtimes/<rid>/native/
# using the canonical names ManagedBass probes for:
#   win-x64    -> bass.dll      + bassmix.dll
#   osx-x64    -> libbass.dylib + libbassmix.dylib   (the macOS dylib is universal: arm64 + x64)
#   osx-arm64  -> libbass.dylib + libbassmix.dylib
#   linux-x64  -> libbass.so    + libbassmix.so
#
# BASSmix is required by the two-deck engine (TwoDeckBassEngine): the two decks feed one BASSmix
# master channel. Without it, realtime audio (and "Add to Deck") is disabled.
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

# Per-RID: archive suffix, native-lib extension, lib-name prefix, preferred arch subdir.
case "$RID" in
  win-x64)   SUFFIX="";       EXT="dll";   PREFIX="";    PREFER="x64" ;;
  osx-x64)   SUFFIX="-osx";   EXT="dylib"; PREFIX="lib"; PREFER="" ;;
  osx-arm64) SUFFIX="-osx";   EXT="dylib"; PREFIX="lib"; PREFER="" ;;
  linux-x64) SUFFIX="-linux"; EXT="so";    PREFIX="lib"; PREFER="x86_64" ;;
  *) echo "Unknown RID: $RID" >&2; exit 1 ;;
esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST_DIR="$REPO_ROOT/runtimes/$RID/native"
mkdir -p "$DEST_DIR"

fetch_lib() {
  local base="$1"
  local archive="${base}${VERSION}${SUFFIX}.zip"
  local lib="${PREFIX}${base}.${EXT}"
  local dest_file="$DEST_DIR/$lib"
  local url="https://www.un4seen.com/files/$archive"

  echo "Fetching $base for $RID"
  echo "  source : $url"
  echo "  target : $dest_file"

  if [ -f "$dest_file" ]; then
    echo "  already present — skipping. Delete it to re-fetch."
    return 0
  fi

  local tmp zip
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' RETURN
  zip="$tmp/$archive"

  if command -v curl >/dev/null 2>&1; then
    curl -fSL "$url" -o "$zip"
  elif command -v wget >/dev/null 2>&1; then
    wget -q "$url" -O "$zip"
  else
    echo "Neither curl nor wget is available." >&2
    return 1
  fi

  unzip -qo "$zip" -d "$tmp"

  # un4seen layouts vary (root, x64/, libs/x86_64/, ...). Find the lib by name, preferring an
  # architecture subfolder when one exists. Plain while-read loop for bash 3.2 (macOS).
  local chosen="" fallback=""
  while IFS= read -r m; do
    [ -z "$m" ] && continue
    [ -z "$fallback" ] && fallback="$m"
    if [ -n "$PREFER" ] && printf '%s' "$m" | grep -q "$PREFER"; then chosen="$m"; break; fi
  done < <(find "$tmp" -type f -name "$lib" | sort)

  [ -z "$chosen" ] && chosen="$fallback"
  if [ -z "$chosen" ]; then
    echo "Could not find $lib inside $archive. The archive layout may have changed; inspect $tmp." >&2
    return 1
  fi

  cp -f "$chosen" "$dest_file"
  echo "  done   : extracted $(basename "$chosen") -> $dest_file"
}

# Core BASS + the BASSmix add-on (the two-deck master mixer).
fetch_lib bass
fetch_lib bassmix
