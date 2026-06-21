<#
.SYNOPSIS
    Update the marketing website for a new Liveolator build and (optionally) deploy it.

.DESCRIPTION
    This is the "installer -> website" hook. build-installer.ps1 calls it after a
    successful build, but you can also run it by hand. It:
      1. Reads release notes (from -Notes, else website/RELEASE_NOTES_NEXT.md, else a
         placeholder) and prepends a dated entry to website/src/data/changelog.json.
      2. Updates version / downloadUrl / downloadSize in website/src/data/site.ts.
      3. Resets RELEASE_NOTES_NEXT.md to its template for the next build.
      4. Unless -NoDeploy: copies the installer + the two updated data files to the
         VPS and rebuilds the site container (Traefik serves it; see website/DEPLOY.md).

    File edits always happen locally even if deploy is skipped or fails, so the repo
    stays in sync and you can commit + deploy later.

.PARAMETER Version
    Build version. Defaults to <Version> in Liveolator.App.csproj.

.PARAMETER SetupExe
    Path to the installer. Defaults to artifacts/dist/win-x64/LiveolatorSetup-<version>.exe.

.PARAMETER Notes
    Release-note lines. Overrides the notes file when given.

.PARAMETER NoDeploy
    Update local files only; do not touch the VPS.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/publish-website-release.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$SetupExe,
    [string[]]$Notes,
    [string]$NotesFile,
    [switch]$CaptureShots,
    [switch]$NoDeploy,
    [string]$VpsHost = 'root@<VPS_HOST>',
    [string]$VpsKey  = "$env:USERPROFILE\.ssh\<SSH_KEY>",
    [string]$RemoteDir = '/docker/liveolator'
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$webData    = Join-Path $repoRoot 'website/src/data'
$changelog  = Join-Path $webData 'changelog.json'
$siteTs     = Join-Path $webData 'site.ts'
if (-not $NotesFile) { $NotesFile = Join-Path $repoRoot 'website/RELEASE_NOTES_NEXT.md' }

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}
# Read as UTF-8 explicitly. PowerShell 5.1's Get-Content defaults to the ANSI
# code page, which mangles UTF-8 (e.g. em-dashes -> mojibake) on round-trip.
function Read-Utf8Text([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, (New-Object System.Text.UTF8Encoding($false)))
}
function Read-Utf8Lines([string]$Path) {
    return [System.IO.File]::ReadAllLines($Path, (New-Object System.Text.UTF8Encoding($false)))
}

# JSON string escaper + deterministic array serializer. Avoids PS 5.1
# ConvertTo-Json quirks (single-element arrays collapsing to objects).
function ConvertTo-JsonString([string]$s) {
    if ($null -eq $s) { return '""' }
    $s = $s -replace '\\', '\\'
    $s = $s -replace '"', '\"'
    $s = $s -replace "`r", ''
    $s = $s -replace "`n", '\n'
    $s = $s -replace "`t", '\t'
    return '"' + $s + '"'
}
function ConvertTo-ChangelogJson($entries) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('[')
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $e = $entries[$i]
        $lines.Add('  {')
        $lines.Add('    "version": ' + (ConvertTo-JsonString ([string]$e.version)) + ',')
        $lines.Add('    "date": ' + (ConvertTo-JsonString ([string]$e.date)) + ',')
        $lines.Add('    "notes": [')
        $notesArr = @($e.notes)
        for ($j = 0; $j -lt $notesArr.Count; $j++) {
            $comma = if ($j -lt ($notesArr.Count - 1)) { ',' } else { '' }
            $lines.Add('      ' + (ConvertTo-JsonString ([string]$notesArr[$j])) + $comma)
        }
        $lines.Add('    ]')
        $close = if ($i -lt ($entries.Count - 1)) { '  },' } else { '  }' }
        $lines.Add($close)
    }
    $lines.Add(']')
    return ($lines -join "`n") + "`n"
}

# --- Version -------------------------------------------------------------------
if (-not $Version) {
    $csproj = Join-Path $repoRoot 'src/Liveolator.App/Liveolator.App.csproj'
    $m = Select-String -LiteralPath $csproj -Pattern '<Version>([^<]+)</Version>'
    if (-not $m) { throw "No <Version> in $csproj and no -Version given." }
    $Version = $m.Matches[0].Groups[1].Value.Trim()
}
Write-Host "Publishing website for v$Version" -ForegroundColor Cyan

# --- Installer + size ----------------------------------------------------------
if (-not $SetupExe) {
    $SetupExe = Join-Path $repoRoot "artifacts/dist/win-x64/LiveolatorSetup-$Version.exe"
}
$haveInstaller = Test-Path $SetupExe
$sizeText = $null
if ($haveInstaller) {
    $sizeMb = [Math]::Round((Get-Item $SetupExe).Length / 1MB, 0)
    $sizeText = "$sizeMb MB"
} else {
    Write-Warning "Installer not found at $SetupExe - keeping existing size, skipping upload."
}

# --- Notes ---------------------------------------------------------------------
if (-not $Notes -or $Notes.Count -eq 0) {
    if (Test-Path $NotesFile) {
        $Notes = Read-Utf8Lines $NotesFile |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -ne '' -and -not $_.StartsWith('#') -and -not $_.StartsWith('<!--') }
    }
}
if (-not $Notes -or @($Notes).Count -eq 0) {
    $Notes = @('Maintenance build: fixes and small improvements.')
    Write-Warning "No release notes provided; using a placeholder. Edit website/src/data/changelog.json to refine."
}
$Notes = @($Notes | ForEach-Object { ($_ -replace '^[-*]\s*', '').Trim() } | Where-Object { $_ -ne '' })

# --- Update changelog.json (prepend; replace if version already present) -------
$existing = @()
if (Test-Path $changelog) {
    $raw = Read-Utf8Text $changelog
    if ($raw.Trim()) { $existing = @((ConvertFrom-Json $raw)) }
}
$existing = @($existing | Where-Object { [string]$_.version -ne $Version })
$today = (Get-Date).ToString('yyyy-MM-dd')
$newEntry = [pscustomobject]@{ version = $Version; date = $today; notes = $Notes }
$entries = @($newEntry) + $existing
Write-Utf8NoBom $changelog (ConvertTo-ChangelogJson $entries)
Write-Host "  changelog.json updated ($($Notes.Count) note(s))." -ForegroundColor Green

# --- Update site.ts ------------------------------------------------------------
$ts = Read-Utf8Text $siteTs
$ts = [regex]::Replace($ts, 'version:\s*"[^"]*"', "version: `"$Version`"")
$ts = [regex]::Replace($ts, 'downloadUrl:\s*"[^"]*"', "downloadUrl: `"/downloads/LiveolatorSetup-$Version.exe`"")
if ($sizeText) {
    $ts = [regex]::Replace($ts, 'downloadSize:\s*"[^"]*"', "downloadSize: `"$sizeText`"")
}
Write-Utf8NoBom $siteTs $ts
Write-Host "  site.ts updated (version $Version$(if($sizeText){", $sizeText"}))." -ForegroundColor Green

# --- Refresh website screenshots from the latest UI-shot captures --------------
# Keeps the site's screenshots in step with the app. -CaptureShots re-renders them
# first (heavier); otherwise we just copy the latest captures. Non-fatal.
try {
    $shotArgs = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'sync-website-screenshots.ps1'))
    if ($CaptureShots) { $shotArgs += '-Capture' }
    & powershell @shotArgs
}
catch {
    Write-Warning "Screenshot refresh failed: $($_.Exception.Message)"
}

# --- Reset the notes staging file ----------------------------------------------
$template = @(
    '# Release notes for the NEXT build.',
    '# One bullet per line. Lines starting with # are ignored.',
    '# These become the changelog entry when the installer is built.',
    ''
) -join "`n"
Write-Utf8NoBom $NotesFile $template

# --- Deploy --------------------------------------------------------------------
if ($NoDeploy) {
    Write-Host "Local files updated. Skipping deploy (-NoDeploy)." -ForegroundColor Yellow
    return
}
try {
    $sshOpts = @('-i', $VpsKey, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'BatchMode=yes')
    if ($haveInstaller) {
        Push-Location (Split-Path -Parent $SetupExe)
        & scp @sshOpts (Split-Path -Leaf $SetupExe) "${VpsHost}:$RemoteDir/downloads/"
        Pop-Location
        if ($LASTEXITCODE -ne 0) { throw "scp installer failed ($LASTEXITCODE)." }
    }
    Push-Location $webData
    & scp @sshOpts 'site.ts'        "${VpsHost}:$RemoteDir/src/data/site.ts"
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "scp site.ts failed ($LASTEXITCODE)." }
    & scp @sshOpts 'changelog.json' "${VpsHost}:$RemoteDir/src/data/changelog.json"
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "scp changelog.json failed ($LASTEXITCODE)." }
    Pop-Location

    Push-Location (Join-Path $repoRoot 'website/public')
    & scp @sshOpts -r 'screenshots' "${VpsHost}:$RemoteDir/public/"
    Pop-Location
    if ($LASTEXITCODE -ne 0) { throw "scp screenshots failed ($LASTEXITCODE)." }

    & ssh @sshOpts $VpsHost "cd $RemoteDir && docker compose up -d --build"
    if ($LASTEXITCODE -ne 0) { throw "remote rebuild failed ($LASTEXITCODE)." }

    Write-Host "Deployed: https://liveolator.zalmanim.com (v$Version)" -ForegroundColor Green
}
catch {
    Write-Warning "Deploy step failed: $($_.Exception.Message)"
    Write-Warning "Local files were updated; deploy manually (see website/DEPLOY.md)."
}
