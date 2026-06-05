<#
.SYNOPSIS
    Fetches the un4seen BASS native library for the current (or a specified) platform
    into runtimes/<rid>/native/, where the App build step picks it up.

.DESCRIPTION
    BASS ships as a per-platform zip from un4seen.com. This script downloads the right
    archive, extracts only the native library we need, and places it under
    runtimes/<rid>/native/ using the canonical name ManagedBass probes for:
      win-x64    -> bass.dll
      osx-x64    -> libbass.dylib   (the macOS dylib is universal: arm64 + x64)
      osx-arm64  -> libbass.dylib
      linux-x64  -> libbass.so

    The binaries are intentionally git-ignored (see .gitignore: /runtimes/). They are NOT
    redistributed in source control because BASS requires a commercial license for
    distribution (see docs/01-audio-source-layer.md, "BASS licensing").

.PARAMETER Rid
    Target runtime identifier. Defaults to the current OS/architecture.
    One of: win-x64, osx-x64, osx-arm64, linux-x64.

.PARAMETER Version
    BASS archive version tag in the un4seen filename (e.g. "24" for bass24.zip).
    Defaults to 24. Override if un4seen bumps the archive name.

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

# Per-RID: source archive, the lib name inside the archive (a hint we search for), and
# the canonical output name ManagedBass loads.
$plan = switch ($Rid) {
    'win-x64'   { @{ Archive = "bass$Version.zip";       InnerName = 'bass.dll';     OutName = 'bass.dll';     PreferDir = 'x64' } }
    'osx-x64'   { @{ Archive = "bass$Version-osx.zip";   InnerName = 'libbass.dylib'; OutName = 'libbass.dylib'; PreferDir = '' } }
    'osx-arm64' { @{ Archive = "bass$Version-osx.zip";   InnerName = 'libbass.dylib'; OutName = 'libbass.dylib'; PreferDir = '' } }
    'linux-x64' { @{ Archive = "bass$Version-linux.zip"; InnerName = 'libbass.so';   OutName = 'libbass.so';   PreferDir = 'x86_64' } }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$destDir = Join-Path $repoRoot "runtimes/$Rid/native"
$destFile = Join-Path $destDir $plan.OutName
$url = "https://www.un4seen.com/files/$($plan.Archive)"

Write-Host "Fetching BASS for $Rid"
Write-Host "  source : $url"
Write-Host "  target : $destFile"

if (Test-Path $destFile) {
    Write-Host "  already present — skipping download. Delete it to re-fetch."
    exit 0
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("bass-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$zipPath = Join-Path $tmp $plan.Archive

try {
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force

    # un4seen layouts vary by platform/version (root, x64/, libs/x86_64/, ...). Find the
    # native lib by name, preferring an architecture subfolder when one exists.
    $candidates = Get-ChildItem -Path $tmp -Recurse -File -Filter $plan.InnerName
    if (-not $candidates) {
        throw "Could not find $($plan.InnerName) inside $($plan.Archive). The archive layout may have changed; inspect $tmp."
    }

    $chosen = $candidates |
        Sort-Object -Property @{ Expression = { if ($plan.PreferDir -and $_.FullName -match [regex]::Escape($plan.PreferDir)) { 0 } else { 1 } } },
                              @{ Expression = { $_.FullName.Length } } |
        Select-Object -First 1

    Copy-Item -Path $chosen.FullName -Destination $destFile -Force
    Write-Host "  done   : extracted $($chosen.Name) -> $destFile"
}
catch {
    Write-Error "fetch-bass failed for $($Rid): $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
