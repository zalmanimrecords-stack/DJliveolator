#!/usr/bin/env bash
#
# run.sh — build and run the Liveolator Avalonia application.
#
# Ensures the BASS native library is present (fetches it when missing), builds
# src/Liveolator.App, then launches the UI. Live Mode stays disabled when BASS
# cannot be fetched; the shell still starts.
#
# Usage:
#   ./scripts/run.sh
#   CONFIGURATION=Release ./scripts/run.sh
#   ./scripts/run.sh --build-only
#   ./scripts/run.sh --skip-fetch
#
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Debug}"
SKIP_FETCH=0
BUILD_ONLY=0

for arg in "$@"; do
  case "$arg" in
    --skip-fetch) SKIP_FETCH=1 ;;
    --build-only) BUILD_ONLY=1 ;;
    -h|--help)
      sed -n '2,14p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      echo "Usage: $0 [--skip-fetch] [--build-only]" >&2
      exit 1
      ;;
  esac
done

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os" in
    Darwin)
      if [[ "$arch" == "arm64" ]]; then echo "osx-arm64"; else echo "osx-x64"; fi
      ;;
    Linux) echo "linux-x64" ;;
    MINGW*|MSYS*|CYGWIN*) echo "win-x64" ;;
    *) echo "Unsupported platform for automatic BASS RID detection." >&2; return 1 ;;
  esac
}

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
app_project="$repo_root/src/Liveolator.App/Liveolator.App.csproj"
rid="$(detect_rid)"
bass_native_dir="$repo_root/runtimes/$rid/native"

if [[ "$SKIP_FETCH" -eq 0 ]]; then
  has_bass=0
  if [[ -d "$bass_native_dir" ]] && compgen -G "$bass_native_dir/*" > /dev/null; then
    has_bass=1
  fi

  if [[ "$has_bass" -eq 0 ]]; then
    echo "BASS native lib missing for $rid - running scripts/fetch-bass.sh"
    "$repo_root/scripts/fetch-bass.sh" "$rid"
  fi
fi

if pgrep -x Liveolator.App >/dev/null 2>&1; then
  count="$(pgrep -x Liveolator.App | wc -l | tr -d ' ')"
  echo "Stopping ${count} running Liveolator instance(s)..."
  pkill -x Liveolator.App || true
  sleep 0.5
fi

# dotnet run hosts also lock output assemblies.
if pgrep -f 'dotnet.*Liveolator\.App' >/dev/null 2>&1; then
  echo "Stopping dotnet host(s) for Liveolator.App..."
  pkill -f 'dotnet.*Liveolator\.App' || true
  sleep 0.5
fi

echo "Building Liveolator.App ($CONFIGURATION)..."
dotnet build "$app_project" -c "$CONFIGURATION" --no-incremental

if [[ "$BUILD_ONLY" -eq 1 ]]; then
  echo "Build complete."
  exit 0
fi

app_exe="$repo_root/src/Liveolator.App/bin/$CONFIGURATION/net8.0/Liveolator.App"
if [[ ! -x "$app_exe" ]]; then
  echo "Built app not found at $app_exe" >&2
  exit 1
fi

case "$(uname -s)" in
  Darwin) log_file="$HOME/Library/Application Support/Liveolator/logs/liveolator.log" ;;
  *) log_file="$HOME/.local/share/Liveolator/logs/liveolator.log" ;;
esac

built_at="$(stat -f '%Sm' -t '%Y-%m-%d %H:%M:%S' "$app_exe" 2>/dev/null || stat -c '%y' "$app_exe" 2>/dev/null || echo '?')"

echo "Starting Liveolator..."
echo "  exe:   $app_exe"
echo "  built: $built_at"
echo "  log:   $log_file"
exec "$app_exe"
