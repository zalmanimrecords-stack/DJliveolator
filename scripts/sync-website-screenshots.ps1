<#
.SYNOPSIS
    Refresh the marketing site's screenshots from the app's UI-shot captures.

.DESCRIPTION
    The website shows a curated subset of the app's UI shots. This script maps the
    canonical captures in artifacts/ui-shots/*.png to the website's filenames and
    copies them into website/public/screenshots.

    With -Capture it first regenerates the captures by running the UiShots test
    harness (dotnet test ... --filter UiShots). That step renders the real app, so
    it's heavier and can fail on a headless/locked box - it's therefore opt-in, and
    a failure is non-fatal (we fall back to the existing captures).

    publish-website-release.ps1 calls this (copy step) on every release so the site
    never drifts from the latest captures.

.PARAMETER Capture
    Re-run the UiShots harness before copying (best-effort).

.PARAMETER Deploy
    Also upload the refreshed screenshots to the VPS and rebuild the site.
#>
[CmdletBinding()]
param(
    [switch]$Capture,
    [switch]$Deploy,
    [string]$VpsHost = 'root@<VPS_HOST>',
    [string]$VpsKey  = "$env:USERPROFILE\.ssh\<SSH_KEY>",
    [string]$RemoteDir = '/docker/liveolator'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcDir   = Join-Path $repoRoot 'artifacts/ui-shots'
$destDir  = Join-Path $repoRoot 'website/public/screenshots'

# Canonical capture name -> website filename. Edit if the tab set changes.
$map = [ordered]@{
    '00-LIVE.png'      = 'live.png'
    '01-DJ.png'        = 'dj.png'
    '02-STUDIO.png'    = 'studio.png'
    '03-VJ.png'        = 'vj.png'
    '04-LIBRARIES.png' = 'libraries.png'
}

# --- Optional re-capture --------------------------------------------------------
if ($Capture) {
    Write-Host 'Re-capturing UI shots (dotnet test --filter UiShots)...' -ForegroundColor Cyan
    try {
        Push-Location $repoRoot
        & dotnet test 'tests/Liveolator.App.Tests' --filter UiShots
        if ($LASTEXITCODE -ne 0) { throw "UiShots test exited $LASTEXITCODE." }
    }
    catch {
        Write-Warning "UI-shot capture failed: $($_.Exception.Message)"
        Write-Warning 'Falling back to the existing captures in artifacts/ui-shots.'
    }
    finally { Pop-Location }
}

# --- Copy mapped shots into the website ----------------------------------------
if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
$copied = 0
foreach ($name in $map.Keys) {
    $from = Join-Path $srcDir $name
    $to   = Join-Path $destDir $map[$name]
    if (Test-Path $from) {
        Copy-Item -LiteralPath $from -Destination $to -Force
        $copied++
    } else {
        Write-Warning "Missing capture: $name (kept existing $($map[$name]))."
    }
}
Write-Host "Screenshots refreshed: $copied/$($map.Count) copied into website/public/screenshots." -ForegroundColor Green

# --- Optional deploy ------------------------------------------------------------
if ($Deploy) {
    try {
        $sshOpts = @('-i', $VpsKey, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'BatchMode=yes')
        Push-Location (Join-Path $repoRoot 'website/public')
        & scp @sshOpts -r 'screenshots' "${VpsHost}:$RemoteDir/public/"
        Pop-Location
        if ($LASTEXITCODE -ne 0) { throw "scp screenshots failed ($LASTEXITCODE)." }
        & ssh @sshOpts $VpsHost "cd $RemoteDir && docker compose up -d --build"
        if ($LASTEXITCODE -ne 0) { throw "remote rebuild failed ($LASTEXITCODE)." }
        Write-Host 'Screenshots deployed.' -ForegroundColor Green
    }
    catch {
        Write-Warning "Screenshot deploy failed: $($_.Exception.Message)"
    }
}
