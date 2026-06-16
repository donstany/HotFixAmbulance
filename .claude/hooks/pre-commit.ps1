#requires -Version 5.1
<#
.SYNOPSIS
  HotFixAmbulance pre-commit guard. Blocks `git commit` if .NET tests, frontend tests, or formatting fail.

.DESCRIPTION
  Wired by `scripts/bootstrap.ps1` as `.git/hooks/pre-commit`. Each gate is opt-out via env var so partially scaffolded
  phases of the plan don't immediately wedge commits:
    HFA_SKIP_DOTNET_TESTS=1
    HFA_SKIP_FRONTEND_TESTS=1
    HFA_SKIP_FORMAT=1

.NOTES
  Phase 0.6 of plan.md. Do NOT use `git commit --no-verify` to bypass this hook.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
Set-Location $repoRoot

$results = New-Object System.Collections.Generic.List[object]
function Invoke-Gate {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Test,
        [Parameter(Mandatory)][scriptblock]$Command,
        [string]$SkipEnv
    )
    if ($SkipEnv -and [Environment]::GetEnvironmentVariable($SkipEnv) -eq '1') {
        Write-Host "[skip] $Name (env $SkipEnv=1)" -ForegroundColor DarkYellow
        $results.Add([pscustomobject]@{ Gate = $Name; Status = 'skipped' })
        return
    }
    if (-not (& $Test)) {
        Write-Host "[skip] $Name (not yet scaffolded)" -ForegroundColor DarkGray
        $results.Add([pscustomobject]@{ Gate = $Name; Status = 'absent' })
        return
    }
    Write-Host "[run]  $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        $results.Add([pscustomobject]@{ Gate = $Name; Status = 'FAIL' })
        Write-Host "[FAIL] $Name (exit $LASTEXITCODE)" -ForegroundColor Red
        $script:HasFailure = $true
    } else {
        $results.Add([pscustomobject]@{ Gate = $Name; Status = 'ok' })
        Write-Host "[ok]   $Name" -ForegroundColor Green
    }
}

$script:HasFailure = $false

Invoke-Gate -Name 'dotnet test'   -SkipEnv 'HFA_SKIP_DOTNET_TESTS' `
    -Test    { Test-Path 'backend/HotFixAmbulance.sln' } `
    -Command { dotnet test 'backend/HotFixAmbulance.sln' --nologo --verbosity minimal }

Invoke-Gate -Name 'dotnet format' -SkipEnv 'HFA_SKIP_FORMAT' `
    -Test    { Test-Path 'backend/HotFixAmbulance.sln' } `
    -Command { dotnet format 'backend/HotFixAmbulance.sln' --verify-no-changes --no-restore }

Invoke-Gate -Name 'frontend test' -SkipEnv 'HFA_SKIP_FRONTEND_TESTS' `
    -Test    { Test-Path 'frontend/package.json' } `
    -Command { npm --prefix frontend test -- --run }

Invoke-Gate -Name 'frontend lint' -SkipEnv 'HFA_SKIP_FORMAT' `
    -Test    { Test-Path 'frontend/package.json' } `
    -Command { npm --prefix frontend run lint --if-present }

Write-Host ''
$results | Format-Table -AutoSize | Out-String | Write-Host

if ($script:HasFailure) {
    Write-Host 'Commit blocked. Fix the failing gate(s) and try again.' -ForegroundColor Red
    exit 1
}
exit 0
