# Install Liveolator's version-controlled git hooks into this clone.
# Run once after cloning:   pwsh scripts/install-hooks.ps1
$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Push-Location $root
try {
    $common = (git rev-parse --git-common-dir).Trim()
    if (-not [System.IO.Path]::IsPathRooted($common)) { $common = Join-Path $root $common }
    $dest = Join-Path $common 'hooks'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($h in 'pre-commit', 'pre-push') {
        Copy-Item (Join-Path $root ".githooks/$h") (Join-Path $dest $h) -Force
        Write-Host "installed $h"
    }
    Write-Host "Hooks installed to $dest"
}
finally { Pop-Location }
