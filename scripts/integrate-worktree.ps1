<#
.SYNOPSIS
  Merge gate for a finished agent branch: fast-forward into the integration branch, then
  build + test to confirm green (multi-agent orchestration).

.DESCRIPTION
  Only the orchestrator runs this, one branch at a time. Preconditions (the agent already
  rebased its worktree onto Base and validated, per orchestration/README.md step 5/7):
    - the integration tree has no uncommitted changes,
    - Liveolator.App is NOT running (it locks bin/Debug DLLs and would fail the build),
    - the branch is a fast-forward of Base (the agent rebased it).

  On a failed build/test it does NOT auto-rollback (destructive ops are the human's call);
  it prints the exact rollback command. ASCII-only (PowerShell 5.1).

.PARAMETER Branch
  The agent branch to merge, e.g. feat/keylock-native.

.PARAMETER Base
  The integration branch. Default: feat/studio-tab.

.PARAMETER SkipTests
  Build only (faster gate); use only when the branch is docs/scripts-only.

.EXAMPLE
  scripts/integrate-worktree.ps1 -Branch feat/keylock-native
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Branch,
    [string]$Base = "feat/studio-tab",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

function Fail($message) { Write-Host "BLOCKED: $message" -ForegroundColor Red; exit 1 }

# Native tools (git, dotnet) write normal progress to stderr; under EAP=Stop PowerShell 5.1
# turns those into throwing errors even on exit 0. Run them with EAP=Continue and judge by
# the exit code only.
function Git-Run {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    try { $out = & git @GitArgs; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "git $($GitArgs -join ' ') failed (exit $code)." }
    return $out
}

function Git-Code {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    try { & git @GitArgs | Out-Null; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    return $code
}

function Dotnet-Code {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$DotnetArgs)
    $previous = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    try { & dotnet @DotnetArgs; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    return $code
}

$repoRoot = "$(Git-Run rev-parse --show-toplevel)".Trim()
Set-Location $repoRoot

# 1. Integration tree must be clean (else a merge would mix in stray edits).
$dirty = (Git-Run status --porcelain) -join "`n"
if (-not [string]::IsNullOrWhiteSpace($dirty)) {
    Fail "Integration tree has uncommitted changes. Commit/stash them first:`n$dirty"
}

# 2. The app must not be running (build-lock on bin/Debug/*.dll).
$app = Get-Process -Name "Liveolator.App" -ErrorAction SilentlyContinue
if ($app) { Fail "Liveolator.App is running (PID $($app.Id)). Close it before integrating." }

# 3. Branch must exist.
if ((Git-Code rev-parse --verify --quiet "refs/heads/$Branch") -ne 0) { Fail "Branch not found: $Branch" }

# 4. Move to Base and record the rollback point.
Git-Run checkout $Base | Out-Null
$prevSha = "$(Git-Run rev-parse HEAD)".Trim()

# 5. Fast-forward only. If the branch is not ahead-only of Base, the agent must rebase it.
if ((Git-Code merge --ff-only $Branch) -ne 0) {
    Fail "Not a fast-forward. In the worktree run: git rebase $Base   then re-run this gate."
}
$mergedSha = "$(Git-Run rev-parse HEAD)".Trim()
Write-Host "Fast-forwarded $Base to $mergedSha." -ForegroundColor Green

# 6. Build the solution (errors only).
Write-Host "Building..."
if ((Dotnet-Code build Liveolator.sln -clp:ErrorsOnly --nologo) -ne 0) {
    Write-Host "Build FAILED after merge. Rollback with:" -ForegroundColor Red
    Write-Host "  git reset --hard $prevSha"
    Fail "Build red post-merge ($Branch). Base left at $mergedSha for inspection."
}

# 7. Tests (unless skipped).
if (-not $SkipTests) {
    Write-Host "Testing..."
    if ((Dotnet-Code test Liveolator.sln --nologo -clp:ErrorsOnly) -ne 0) {
        Write-Host "Tests FAILED after merge. Rollback with:" -ForegroundColor Red
        Write-Host "  git reset --hard $prevSha"
        Fail "Tests red post-merge ($Branch). Base left at $mergedSha for inspection."
    }
}

# 8. Green: remove the worktree + delete the branch.
$wtRoot = Join-Path (Split-Path $repoRoot -Parent) "Liveolator-wt"
$slug = $Branch -replace '^feat/', ''
$wtPath = Join-Path $wtRoot $slug
if (Test-Path $wtPath) {
    if ((Git-Code worktree remove $wtPath) -ne 0) {
        Write-Host "Note: could not auto-remove worktree $wtPath (remove manually: git worktree remove --force $wtPath)." -ForegroundColor Yellow
    }
}
Git-Code branch -d $Branch | Out-Null

Write-Host ""
Write-Host "Merged and verified: $Branch -> $Base ($mergedSha). Build + tests green." -ForegroundColor Green
Write-Host "Remind still-open lanes to: git rebase $Base"
