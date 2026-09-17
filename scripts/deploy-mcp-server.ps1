<#
.SYNOPSIS
  Publish the Liveolator MCP server for linux-x64 and deploy it to the music host as a container.

.DESCRIPTION
  The scan is bound by reading audio. Over SMB a single track reads at roughly 1 MB/s, which makes a
  full library pass a multi-hour job; on the host the same files are local. So the server runs where
  the music is, and only the results cross the network.

  The app is published SELF-CONTAINED because the host has no .NET installed, and the image is built
  ON the host from that output - no registry, no local Docker needed.

  Deploys ALONGSIDE anything already running (its own port, its own data directory). Promotion is a
  deliberate, separate step; this script never stops an existing server.

  ASCII-only on purpose (PowerShell 5.1 smart-quote trap).

.PARAMETER RemoteHost
  SSH host alias. Default: simonsrv.

.PARAMETER MusicDir
  Absolute path to the music on the HOST. Must match the paths already stored in the catalog, or
  every existing row is orphaned.

.PARAMETER DataDir
  Host directory bind-mounted at /data. Gets the catalog. Default keeps it away from the live one.

.PARAMETER Port
  Host port, published on 127.0.0.1 only. Default 5175 (the existing server uses 5174).

.PARAMETER SeedCatalogFrom
  Optional path to an existing catalog.db to COPY into DataDir before first start, so the new server
  begins with the work already done instead of rescanning from nothing.

.EXAMPLE
  scripts/deploy-mcp-server.ps1
  scripts/deploy-mcp-server.ps1 -SeedCatalogFrom /home/simon/liveolator/data/catalog.db
#>
[CmdletBinding()]
param(
    [string]$RemoteHost = "simonsrv",
    [string]$MusicDir   = "/media/simon/external_4tb/Navidrome/music",
    [string]$DataDir    = "/home/simon/liveolator/next-data",
    [int]$Port          = 5175,
    [string]$RemoteRoot = "/home/simon/liveolator/next",
    [string]$SeedCatalogFrom = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repo "artifacts/dist/linux-x64"

Write-Host "Publishing Liveolator.Mcp self-contained for linux-x64..."
& dotnet publish (Join-Path $repo "src/Liveolator.Mcp/Liveolator.Mcp.csproj") `
    -c Release -r linux-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$entry = Join-Path $publishDir "liveolator-mcp"
if (-not (Test-Path $entry)) { throw "Publish produced no linux entry point at $entry." }
$sizeMb = [math]::Round(((Get-ChildItem $publishDir | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Published $sizeMb MB."

# Ship the publish output plus the image definition as ONE stream: many small files over SSH are
# dominated by per-file round trips.
Write-Host "Shipping to ${RemoteHost}:${RemoteRoot} ..."
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("liveolator-mcp-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    Copy-Item (Join-Path $repo "docker/mcp/Dockerfile") $stage
    Copy-Item (Join-Path $repo "docker/mcp/docker-compose.yml") $stage
    Copy-Item $publishDir (Join-Path $stage "publish") -Recurse

    & tar -czf - -C $stage . | & ssh $RemoteHost "rm -rf '$RemoteRoot' && mkdir -p '$RemoteRoot' && tar -xzf - -C '$RemoteRoot'"
    if ($LASTEXITCODE -ne 0) { throw "Shipping the payload failed (exit $LASTEXITCODE)." }
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

# 10001 is the image's non-root uid; the bind mount has to be writable by it.
$seed = ""
if ($SeedCatalogFrom) {
    $seed = "if [ ! -f '$DataDir/catalog.db' ] && [ -f '$SeedCatalogFrom' ]; then cp -n '$SeedCatalogFrom' '$DataDir/catalog.db'; echo 'seeded catalog'; fi;"
}

$remote = @"
set -e
mkdir -p '$DataDir'
$seed
sudo chown -R 10001:10001 '$DataDir' 2>/dev/null || chown -R 10001:10001 '$DataDir' 2>/dev/null || true
cd '$RemoteRoot'
export LIVEOLATOR_MUSIC_DIR='$MusicDir'
export LIVEOLATOR_DATA_DIR='$DataDir'
export LIVEOLATOR_MCP_PORT='$Port'
docker compose up -d --build
docker compose ps
"@

Write-Host "Building the image and starting the container on $RemoteHost ..."
$remote | & ssh $RemoteHost "bash -s"
if ($LASTEXITCODE -ne 0) { throw "Remote build/up failed (exit $LASTEXITCODE)." }

Write-Host ""
Write-Host "Deployed. It listens on 127.0.0.1:$Port on $RemoteHost only."
Write-Host "Reach it from here with:  ssh -N -L ${Port}:127.0.0.1:$Port $RemoteHost"
