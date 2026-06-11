<#
.SYNOPSIS
    Builds the Windows installer: self-contained publish of Liveolator.App + Inno Setup compile.

.DESCRIPTION
    Pipeline:
      1. Ensure the BASS native libraries are present (runs fetch-bass.ps1 if not) -
         an installer without them would silently ship with Live Mode disabled.
      2. dotnet publish Liveolator.App, Release, self-contained for the target RID,
         into artifacts/dist/<rid>/publish.
      3. Verify the publish output actually contains the exe and the BASS natives.
      4. Compile installer/windows/Liveolator.iss with Inno Setup (ISCC), producing
         artifacts/dist/<rid>/LiveolatorSetup-<version>.exe.

    The version comes from <Version> in Liveolator.App.csproj - single source of truth
    for exe metadata, setup filename, and the Add/Remove Programs entry.

.PARAMETER Rid
    Target runtime identifier. Only win-x64 is supported (Inno Setup is Windows-only;
    macOS packaging is a separate, future .dmg pipeline).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Rid = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# --- 1. Version from the csproj (single source of truth) -------------------------------
$csprojPath = Join-Path $repoRoot 'src/Liveolator.App/Liveolator.App.csproj'
$versionMatch = Select-String -LiteralPath $csprojPath -Pattern '<Version>([^<]+)</Version>'
if (-not $versionMatch) {
    throw "No <Version> found in $csprojPath - the installer needs one."
}
$version = $versionMatch.Matches[0].Groups[1].Value.Trim()
Write-Host "Building Liveolator $version installer for $Rid" -ForegroundColor Cyan

# --- 2. BASS natives (required: core + mix; flac optional) -----------------------------
$nativeDir = Join-Path $repoRoot "runtimes/$Rid/native"
$requiredNatives = 'bass.dll', 'bassmix.dll'
$missing = $requiredNatives | Where-Object { -not (Test-Path (Join-Path $nativeDir $_)) }
if ($missing) {
    Write-Host "BASS natives missing ($($missing -join ', ')) - fetching..." -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'fetch-bass.ps1') -Rid $Rid
    if ($LASTEXITCODE -ne 0) { throw 'fetch-bass.ps1 failed; cannot build an installer without realtime audio.' }
}

# --- 3. Publish (self-contained: users must not need a .NET install) -------------------
$distDir = Join-Path $repoRoot "artifacts/dist/$Rid"
$publishDir = Join-Path $distDir 'publish'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

& dotnet publish $csprojPath -c Release -r $Rid --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

# --- 4. Verify the payload before packaging --------------------------------------------
$mustExist = @('Liveolator.App.exe') + $requiredNatives
$absent = $mustExist | Where-Object { -not (Test-Path (Join-Path $publishDir $_)) }
if ($absent) {
    throw "Publish output is incomplete - missing: $($absent -join ', '). Refusing to package."
}

# --- 5. Compile the installer with Inno Setup ------------------------------------------
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($iscc) { $isccPath = $iscc.Source }
else {
    $isccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $isccPath)) {
        throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) and retry.'
    }
}

$issPath = Join-Path $repoRoot 'installer/windows/Liveolator.iss'
& $isccPath "/DAppVersion=$version" "/DPublishDir=$publishDir" "/O$distDir" $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)." }

$setupExe = Join-Path $distDir "LiveolatorSetup-$version.exe"
if (-not (Test-Path $setupExe)) { throw "ISCC reported success but $setupExe was not produced." }

$sizeMb = [Math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host ''
Write-Host "Installer ready: $setupExe ($sizeMb MB)" -ForegroundColor Green
