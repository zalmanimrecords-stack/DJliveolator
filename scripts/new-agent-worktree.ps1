<#
.SYNOPSIS
  Create an isolated git worktree + branch for one agent (multi-agent orchestration).

.DESCRIPTION
  Gives an agent its own working tree, index, and bin/obj so its commits and builds never
  collide with other agents on the shared tree. See orchestration/README.md.

  ASCII-only on purpose (PowerShell 5.1 smart-quote trap). Run from anywhere inside the repo.

.PARAMETER Task
  Short task name; slugged into the branch (feat/<slug>) and worktree path
  (../Liveolator-wt/<slug>).

.PARAMETER Base
  Branch to fork from. Default: feat/studio-tab (the current integration line).

.EXAMPLE
  scripts/new-agent-worktree.ps1 -Task keylock-native
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Task,
    [string]$Base = "feat/studio-tab"
)

$ErrorActionPreference = "Stop"

# Native git writes normal progress (e.g. "Preparing worktree", "Switched to branch") to
# stderr. Under EAP=Stop, PowerShell 5.1 turns those lines into throwing errors even on exit 0.
# So run git with EAP=Continue and decide success by the exit code only.
function Git-Run {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { $out = & git @GitArgs; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "git $($GitArgs -join ' ') failed (exit $code)." }
    return $out
}

function Git-Code {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArgs)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & git @GitArgs | Out-Null; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    return $code
}

$repoRoot = "$(Git-Run rev-parse --show-toplevel)".Trim()

$slug = ($Task.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($slug)) { throw "Task name produced an empty slug." }

$branch = "feat/$slug"
$wtRoot = Join-Path (Split-Path $repoRoot -Parent) "Liveolator-wt"
$wtPath = Join-Path $wtRoot $slug

if (Test-Path $wtPath) { throw "Worktree path already exists: $wtPath" }
if ((Git-Code rev-parse --verify --quiet "refs/heads/$branch") -eq 0) {
    throw "Branch already exists: $branch (pick another task name or clean it up)."
}

if (-not (Test-Path $wtRoot)) { New-Item -ItemType Directory -Path $wtRoot | Out-Null }

Git-Run worktree add -b $branch $wtPath $Base | Out-Null

Write-Host ""
Write-Host "Worktree ready." -ForegroundColor Green
Write-Host "  Path:   $wtPath"
Write-Host "  Branch: $branch  (from $Base)"
Write-Host ""
Write-Host "Next:"
Write-Host "  1. Open a NEW agent with its working directory set to the path above."
Write-Host "  2. Build and test ONLY inside that worktree."
Write-Host "  3. Do NOT launch Liveolator.App from a build lane (it locks bin/Debug DLLs)."
Write-Host "  4. Add a row to orchestration/TASKS.md (task, owner, branch, files, status)."
Write-Host "  5. When green, the orchestrator merges via scripts/integrate-worktree.ps1 -Branch $branch"
