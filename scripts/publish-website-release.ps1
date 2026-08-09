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
    # VPS deploy target. Never hardcode the host/creds in source; set via env:
    #   $env:LIVEOLATOR_VPS_HOST = 'root@<your-vps-ip>'
    #   $env:LIVEOLATOR_VPS_KEY  = '<path-to-ssh-key>'   (optional)
    #   $env:LIVEOLATOR_VPS_DIR  = '/docker/liveolator'  (optional)
    [string]$VpsHost = $env:LIVEOLATOR_VPS_HOST,
    [string]$VpsKey  = $(if ($env:LIVEOLATOR_VPS_KEY) { $env:LIVEOLATOR_VPS_KEY } else { "$env:USERPROFILE\.ssh\liveolator_deploy" }),
    [string]$RemoteDir = $(if ($env:LIVEOLATOR_VPS_DIR) { $env:LIVEOLATOR_VPS_DIR } else { '/docker/liveolator' }),
    # How many recent installers to keep downloadable on the VPS (current build +
    # rollback buffer). Older ones are pruned. Must match DOWNLOADABLE in
    # website/src/pages/changelog.astro, which links the same count of versions.
    [int]$KeepDownloads = 6
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
$Notes = @($Notes | ForEach-Object { ($_ -replace '^[-*]\s*', '').Trim() } | Where-Object { $_ -ne '' })

# --- Read existing changelog ---------------------------------------------------
$existing = @()
if (Test-Path $changelog) {
    $raw = Read-Utf8Text $changelog
    if ($raw.Trim()) { $existing = @((ConvertFrom-Json $raw)) }
}
$prior = $existing | Where-Object { [string]$_.version -eq $Version } | Select-Object -First 1

# No fresh notes? Don't clobber a good existing entry with a placeholder - reuse
# its notes (and keep its date). Only invent a placeholder for a brand-new version.
$entryDate = (Get-Date).ToString('yyyy-MM-dd')
if (@($Notes).Count -eq 0) {
    if ($prior -and @($prior.notes).Count -gt 0) {
        $Notes = @($prior.notes)
        $entryDate = [string]$prior.date
        Write-Host "  No new notes - keeping the existing v$Version entry." -ForegroundColor Yellow
    } else {
        $Notes = @('Maintenance build: fixes and small improvements.')
        Write-Warning "No release notes provided; using a placeholder. Edit website/RELEASE_NOTES_NEXT.md next time."
    }
}

# --- Update changelog.json (prepend; replace if version already present) -------
$existing = @($existing | Where-Object { [string]$_.version -ne $Version })
$newEntry = [pscustomobject]@{ version = $Version; date = $entryDate; notes = $Notes }
$entries = @($newEntry) + $existing
Write-Utf8NoBom $changelog (ConvertTo-ChangelogJson $entries)
Write-Host "  changelog.json updated ($(@($Notes).Count) note(s))." -ForegroundColor Green

# --- Update site.ts ------------------------------------------------------------
$ts = Read-Utf8Text $siteTs
$ts = [regex]::Replace($ts, 'version:\s*"[^"]*"', "version: `"$Version`"")
$ts = [regex]::Replace($ts, 'downloadUrl:\s*"[^"]*"', "downloadUrl: `"/downloads/LiveolatorSetup-$Version.exe`"")
if ($sizeText) {
    $ts = [regex]::Replace($ts, 'downloadSize:\s*"[^"]*"', "downloadSize: `"$sizeText`"")
}
Write-Utf8NoBom $siteTs $ts
Write-Host "  site.ts updated (version $Version$(if($sizeText){", $sizeText"}))." -ForegroundColor Green
# Downloads are email-gated: WordPress signs the link from its OWN per-product
# setting, so it must be pointed at this new filename or emailed links will 404.
Write-Host "  REMINDER: update WordPress -> Newsletter -> Settings -> Gated downloads (Liveolator path/version = $Version). See website/DEPLOY.md." -ForegroundColor Yellow

# --- Update version.json (the app's machine-readable update manifest) ----------
# The in-app startup update check (Liveolator.App.Features.Update) GETs this from the
# site root and compares its "version" to the running build.
# The URL points at the DOWNLOAD PAGE, not the direct /downloads/*.exe path: downloads are
# email-gated and nginx returns 403 for any unsigned direct link, so the app must send the
# user through the same gate the site's Download button uses. site.ts keeps the direct path
# (that is the one WordPress signs) - only this manifest differs.
$siteOrigin     = 'https://liveolator.zalmanim.com'
$versionJsonPath = Join-Path $repoRoot 'website/public/version.json'
$downloadAbs    = "$siteOrigin/#download"
$vjLines = New-Object System.Collections.Generic.List[string]
$vjLines.Add('{')
$vjLines.Add('  "version": ' + (ConvertTo-JsonString ([string]$Version)) + ',')
$vjLines.Add('  "downloadUrl": ' + (ConvertTo-JsonString $downloadAbs) + ',')
$vjLines.Add('  "notes": [')
$vjNotes = @($Notes)
for ($k = 0; $k -lt $vjNotes.Count; $k++) {
    $vjComma = if ($k -lt ($vjNotes.Count - 1)) { ',' } else { '' }
    $vjLines.Add('    ' + (ConvertTo-JsonString ([string]$vjNotes[$k])) + $vjComma)
}
$vjLines.Add('  ]')
$vjLines.Add('}')
Write-Utf8NoBom $versionJsonPath (($vjLines -join "`n") + "`n")
Write-Host "  version.json updated (update manifest for v$Version)." -ForegroundColor Green

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
if (-not $VpsHost) {
    Write-Warning "No VPS host set (`$env:LIVEOLATOR_VPS_HOST). Local files updated; skipping deploy."
    return
}
try {
    $sshOpts = @('-i', $VpsKey, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'BatchMode=yes')
    if ($haveInstaller) {
        Push-Location (Split-Path -Parent $SetupExe)
        & scp @sshOpts (Split-Path -Leaf $SetupExe) "${VpsHost}:$RemoteDir/downloads/"
        Pop-Location
        if ($LASTEXITCODE -ne 0) { throw "scp installer failed ($LASTEXITCODE)." }

        # Retention: keep only the newest $KeepDownloads installers for rollback;
        # prune the rest. sort -V orders by version (oldest first); drop all but the
        # last N. Non-fatal - a prune failure must not fail an otherwise-good deploy.
        $prune = "cd $RemoteDir/downloads && ls -1 LiveolatorSetup-*.exe 2>/dev/null " +
                 "| sort -V | head -n -$KeepDownloads | xargs -r rm -f"
        & ssh @sshOpts $VpsHost $prune
        if ($LASTEXITCODE -ne 0) { Write-Warning "Pruning old installers failed ($LASTEXITCODE) - leaving them in place." }
    }
    Push-Location $webData
    & scp @sshOpts 'site.ts'        "${VpsHost}:$RemoteDir/src/data/site.ts"
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "scp site.ts failed ($LASTEXITCODE)." }
    & scp @sshOpts 'changelog.json' "${VpsHost}:$RemoteDir/src/data/changelog.json"
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "scp changelog.json failed ($LASTEXITCODE)." }
    Pop-Location

    Push-Location (Join-Path $repoRoot 'website/public')
    & scp @sshOpts -r 'screenshots' "${VpsHost}:$RemoteDir/public/"
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "scp screenshots failed ($LASTEXITCODE)." }
    # The app's update manifest — must reach the live site so the startup check sees the new version.
    & scp @sshOpts 'version.json' "${VpsHost}:$RemoteDir/public/version.json"
    Pop-Location
    if ($LASTEXITCODE -ne 0) { throw "scp version.json failed ($LASTEXITCODE)." }

    & ssh @sshOpts $VpsHost "cd $RemoteDir && docker compose up -d --build"
    if ($LASTEXITCODE -ne 0) { throw "remote rebuild failed ($LASTEXITCODE)." }

    Write-Host "Deployed: https://liveolator.zalmanim.com (v$Version)" -ForegroundColor Green
}
catch {
    Write-Warning "Deploy step failed: $($_.Exception.Message)"
    Write-Warning "Local files were updated; deploy manually (see website/DEPLOY.md)."
}
