<#
.SYNOPSIS
    Fetches the un4seen BASS native libraries — core BASS, the BASSmix add-on, and the BASSFLAC add-on
    — for the current (or a specified) platform into runtimes/<rid>/native/, where the App build step
    picks them up. BASSFLAC is optional (FLAC decode for playback + waveform); core + BASSmix are required.

.DESCRIPTION
    BASS ships as per-platform zips from un4seen.com. This script downloads the right
    archives, extracts only the native libraries we need, and places them under
    runtimes/<rid>/native/ using the canonical names ManagedBass probes for:
      win-x64    -> bass.dll      + bassmix.dll
      osx-x64    -> libbass.dylib + libbassmix.dylib   (universal: arm64 + x64)
      osx-arm64  -> libbass.dylib + libbassmix.dylib
      linux-x64  -> libbass.so    + libbassmix.so

    BASSmix is required by the two-deck engine (TwoDeckBassEngine): the two decks feed one
    BASSmix master channel. Without it, realtime audio (and "Add to Deck") is disabled.

    The binaries are intentionally git-ignored (see .gitignore: /runtimes/). They are NOT
    redistributed in source control because BASS requires a commercial license for
    distribution (see docs/01-audio-source-layer.md, "BASS licensing").

.PARAMETER Rid
    Target runtime identifier. Defaults to the current OS/architecture.
    One of: win-x64, osx-x64, osx-arm64, linux-x64.

.PARAMETER Version
    Archive version tag in the un4seen filenames (e.g. "24" for bass24.zip / bassmix24.zip).
    Defaults to 24. Override if un4seen bumps the archive names.

.EXAMPLE
    pwsh ./scripts/fetch-bass.ps1
    pwsh ./scripts/fetch-bass.ps1 -Rid osx-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'osx-x64', 'osx-arm64', 'linux-x64')]
    [string]$Rid,
    [string]$Version = '24'
)

$ErrorActionPreference = 'Stop'

function Resolve-CurrentRid {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { return 'win-x64' }
    if ($IsMacOS) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { return 'osx-arm64' }
        return 'osx-x64'
    }
    if ($IsLinux) { return 'linux-x64' }
    throw "Unsupported platform; pass -Rid explicitly (win-x64 | osx-x64 | osx-arm64 | linux-x64)."
}

if (-not $Rid) { $Rid = Resolve-CurrentRid }

# Per-RID archive suffix, native-lib extension, and the architecture subfolder to prefer
# inside the (sometimes multi-arch) archive.
$ridPlan = switch ($Rid) {
    'win-x64'   { @{ Suffix = '';       Ext = 'dll';   Prefix = '';   PreferDir = 'x64' } }
    'osx-x64'   { @{ Suffix = '-osx';   Ext = 'dylib'; Prefix = 'lib'; PreferDir = '' } }
    'osx-arm64' { @{ Suffix = '-osx';   Ext = 'dylib'; Prefix = 'lib'; PreferDir = '' } }
    'linux-x64' { @{ Suffix = '-linux'; Ext = 'so';    Prefix = 'lib'; PreferDir = 'x86_64' } }
}

# The libraries to fetch: core BASS + the BASSmix add-on (the two-deck master mixer) + the BASSFLAC
# add-on (FLAC decode for both realtime playback and the offline waveform/analysis; without it FLAC
# tracks neither play nor draw a waveform). Add-ons are optional — a fetch failure for one is logged
# and does not abort the others.
$libs = @(
    @{ Base = 'bass';     Required = $true },
    @{ Base = 'bassmix';  Required = $true },
    @{ Base = 'bassflac'; Required = $false }
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$destDir = Join-Path $repoRoot "runtimes/$Rid/native"
New-Item -ItemType Directory -Force -Path $destDir | Out-Null

function Get-NativeLib($base) {
    $archive = "$base$Version$($ridPlan.Suffix).zip"
    $libName = "$($ridPlan.Prefix)$base.$($ridPlan.Ext)"
    $destFile = Join-Path $destDir $libName
    $url = "https://www.un4seen.com/files/$archive"

    Write-Host "Fetching $base for $Rid"
    Write-Host "  source : $url"
    Write-Host "  target : $destFile"

    if (Test-Path $destFile) {
        Write-Host "  already present - skipping. Delete it to re-fetch."
        return
    }

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("$base-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zipPath = Join-Path $tmp $archive

    try {
        Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
        Expand-Archive -Path $zipPath -DestinationPath $tmp -Force

        # un4seen layouts vary (root, x64/, libs/x86_64/, ...). Find the lib by name, preferring
        # an architecture subfolder when one exists.
        $candidates = Get-ChildItem -Path $tmp -Recurse -File -Filter $libName
        if (-not $candidates) {
            throw "Could not find $libName inside $archive. The archive layout may have changed; inspect $tmp."
        }

        $chosen = $candidates |
            Sort-Object -Property @{ Expression = { if ($ridPlan.PreferDir -and $_.FullName -match [regex]::Escape($ridPlan.PreferDir)) { 0 } else { 1 } } },
                                  @{ Expression = { $_.FullName.Length } } |
            Select-Object -First 1

        Copy-Item -Path $chosen.FullName -Destination $destFile -Force
        Write-Host "  done   : extracted $($chosen.Name) -> $destFile"
    }
    finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
}

try {
    foreach ($lib in $libs) {
        if ($lib.Required) {
            Get-NativeLib $lib.Base
        }
        else {
            # Optional add-on: a download/layout failure must not block the core libraries.
            try { Get-NativeLib $lib.Base }
            catch { Write-Warning "Optional add-on '$($lib.Base)' could not be fetched: $($_.Exception.Message)" }
        }
    }
}
catch {
    Write-Error "fetch-bass failed for $($Rid): $($_.Exception.Message)"
    exit 1
}
