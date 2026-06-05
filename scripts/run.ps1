<#
.SYNOPSIS
    Builds and runs the Liveolator Avalonia application.

.DESCRIPTION
    Ensures the BASS native library is present (fetches it when missing), builds
    src/Liveolator.App, then launches the UI. Live Mode stays disabled when BASS
    cannot be fetched; the shell still starts.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Debug.

.PARAMETER SkipFetch
    Do not attempt to download the BASS native library when it is missing.

.PARAMETER BuildOnly
    Build the app and exit without launching it.

.EXAMPLE
    powershell -File ./scripts/run.ps1
    ./scripts/run.ps1 -Configuration Release
    ./scripts/run.ps1 -BuildOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipFetch,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'

function Resolve-CurrentRid {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') { return 'win-x64' }
    if ($IsMacOS) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { return 'osx-arm64' }
        return 'osx-x64'
    }
    if ($IsLinux) { return 'linux-x64' }
    throw "Unsupported platform for automatic BASS RID detection."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src/Liveolator.App/Liveolator.App.csproj'
$rid = Resolve-CurrentRid
$bassNativeDir = Join-Path $repoRoot "runtimes/$rid/native"

if (-not $SkipFetch) {
    $hasBass = $false
    if (Test-Path $bassNativeDir) {
        $hasBass = @(Get-ChildItem -Path $bassNativeDir -File -ErrorAction SilentlyContinue).Count -gt 0
    }

    if (-not $hasBass) {
        Write-Host "BASS native lib missing for $rid - running scripts/fetch-bass.ps1"
        & (Join-Path $repoRoot 'scripts/fetch-bass.ps1') -Rid $rid
    }
}

Write-Host "Building Liveolator.App ($Configuration)..."
dotnet build $appProject -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($BuildOnly) {
    Write-Host "Build complete."
    exit 0
}

Write-Host "Starting Liveolator..."
dotnet run --project $appProject -c $Configuration --no-build
exit $LASTEXITCODE
