#!/bin/sh
# Install Liveolator's version-controlled git hooks into this clone.
# Run once after cloning:   sh scripts/install-hooks.sh
set -eu
root=$(git rev-parse --show-toplevel)
common=$(cd "$root" && cd "$(git rev-parse --git-common-dir)" && pwd)
dest="$common/hooks"
mkdir -p "$dest"
for h in pre-commit pre-push; do
  cp "$root/.githooks/$h" "$dest/$h"
  chmod +x "$dest/$h"
  echo "installed $h"
done
echo "Hooks installed to $dest"
