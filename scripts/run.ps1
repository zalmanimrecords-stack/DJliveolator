<#
.SYNOPSIS
    Builds and runs the Liveolator Avalonia application - the reliable dev launcher.

.DESCRIPTION
    Does exactly what a clean manual launch does, with no room for "I'm looking at a stale window"
    confusion:
      1. Ensure the BASS native libraries are present (fetch when missing).
      2. Stop EVERY running Liveolator instance (the dev build AND any installed copy - they share the
         exe name) and WAIT until each process has exited and released its file locks. A fixed sleep is
         not enough: the build copies the engine DLLs into bin, and a still-locked DLL makes the build
         fail with MSB3021, which would otherwise leave you on the old build.
      3. Build src/Liveolator.App. On failure, say so loudly and DO NOT launch - so you are never
         silently left on a previous/stale build.
      4. Re-kill any instance that respawned during the build, then launch the freshly built exe and
         bring its window to the FOREGROUND, so you always look at the build this script just produced.
      5. Print the exact exe path, version, and log path so it is unambiguous which build is running.

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
$IsWin = $IsWindows -or $env:OS -eq 'Windows_NT'

function Resolve-CurrentRid {
    if ($IsWin) { return 'win-x64' }
    if ($IsMacOS) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { return 'osx-arm64' }
        return 'osx-x64'
    }
    if ($IsLinux) { return 'linux-x64' }
    throw "Unsupported platform for automatic BASS RID detection."
}

function Resolve-AppExecutable {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Configuration
    )

    $outDir = Join-Path $RepoRoot "src/Liveolator.App/bin/$Configuration/net8.0"
    if ($IsWin) { return Join-Path $outDir 'Liveolator.App.exe' }
    return Join-Path $outDir 'Liveolator.App'
}

# Kill EVERY Liveolator instance (the dev build and any installed copy share the process name). Optionally
# wait until they are truly gone and their file locks are released - the surest "all locks freed" signal
# is that the output exe becomes writable. Without this the rebuild can fail on a locked DLL and the
# launcher would leave you on a stale build (the whole class of "it looks old" bugs).
function Stop-LiveolatorApp {
    param(
        [string]$UnlockPath,
        [switch]$Quick
    )

    $running = @(Get-Process -Name 'Liveolator.App' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        if (-not $Quick) { Write-Host "Stopping $($running.Count) running Liveolator instance(s)..." }
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    $seconds = if ($Quick) { 4 } else { 20 }
    $deadline = (Get-Date).AddSeconds($seconds)
    while (((Get-Date) -lt $deadline) -and
           (@(Get-Process -Name 'Liveolator.App' -ErrorAction SilentlyContinue).Count -gt 0)) {
        Start-Sleep -Milliseconds 200
    }

    if ($UnlockPath -and (Test-Path -LiteralPath $UnlockPath)) {
        while ((Get-Date) -lt $deadline) {
            try {
                $stream = [System.IO.File]::Open($UnlockPath, 'Open', 'ReadWrite', 'None')
                $stream.Close()
                break
            }
            catch { Start-Sleep -Milliseconds 200 }
        }
    }
}

# Bring the live Liveolator window to the foreground so you always look at THIS build, never a stale
# window behind it. Foregrounds whichever Liveolator.App window is up. Windows-only; a no-op elsewhere.
function Show-AppWindow {
    if (-not $IsWin) { return }

    if (-not ([System.Management.Automation.PSTypeName]'LiveolatorWin').Type) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class LiveolatorWin {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
"@
    }

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $proc = Get-Process -Name 'Liveolator.App' -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($proc) {
            [LiveolatorWin]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null   # 9 = SW_RESTORE
            [LiveolatorWin]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
            return
        }
        Start-Sleep -Milliseconds 300
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src/Liveolator.App/Liveolator.App.csproj'
$rid = Resolve-CurrentRid
$bassNativeDir = Join-Path $repoRoot "runtimes/$rid/native"
$appExe = Resolve-AppExecutable -RepoRoot $repoRoot -Configuration $Configuration

# --- 1. BASS natives (Live Mode needs them; the shell still starts without) ------------------------
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

# --- 2. Stop every instance + wait for locks to release --------------------------------------------
Stop-LiveolatorApp -UnlockPath $appExe

# --- 3. Build (loud failure, no silent stale launch) -----------------------------------------------
Write-Host "Building Liveolator.App ($Configuration)..."
dotnet build $appProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host "BUILD FAILED - the app was NOT launched, so nothing stale was started." -ForegroundColor Red
    Write-Host "If the errors above say 'file locked by Liveolator.App', close every Liveolator window and re-run." -ForegroundColor Yellow
    exit $LASTEXITCODE
}

if ($BuildOnly) {
    Write-Host "Build complete." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $appExe)) {
    Write-Error "Built app not found at $appExe"
    exit 1
}

# --- 4. Launch the fresh build + bring its window to the front -------------------------------------
# Final quick re-kill: anything that respawned during the build would make the new process defer to it
# via the single-instance guard, so the fresh build must be the only one launching.
Stop-LiveolatorApp -Quick

$logDir = if ($IsWin) {
    Join-Path $env:APPDATA 'Liveolator/logs/liveolator.log'
} elseif ($IsMacOS) {
    Join-Path $env:HOME 'Library/Application Support/Liveolator/logs/liveolator.log'
} else {
    Join-Path $env:HOME '.local/share/Liveolator/logs/liveolator.log'
}

$version = (Get-Item $appExe).VersionInfo.ProductVersion
Write-Host ''
Write-Host "Starting Liveolator..." -ForegroundColor Green
Write-Host "  exe:     $appExe"
Write-Host "  version: $version"
Write-Host "  log:     $logDir"

# Start non-blocking so the window can be pulled to the foreground, then wait so this terminal stays
# attached to the app's lifetime (closing the app returns the prompt).
$app = Start-Process -FilePath $appExe -PassThru
Show-AppWindow

Start-Sleep -Milliseconds 1500
if ($app.HasExited -and $app.ExitCode -ne 0) {
    # The single-instance guard sent this launch to an already-running instance (same build, since the
    # rest were cleared above). Not an error - the running window was brought to the front.
    Write-Host "A Liveolator instance was already running; brought it to the front." -ForegroundColor Yellow
    exit 0
}

$app.WaitForExit()
$exitCode = $app.ExitCode
if ($exitCode -ne 0) {
    Write-Host "Liveolator exited with code $exitCode. If no window appeared, check the log path above." -ForegroundColor Yellow
}
exit $exitCode
