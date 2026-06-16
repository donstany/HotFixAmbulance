#requires -Version 5.1
<#
.SYNOPSIS
  End-to-end HotFixAmbulance demo. Starts demo-api, hammers its endpoints to produce errors,
  then runs the CLI to triage them and prints the analysis id and React UI URL.

.DESCRIPTION
  Phase 8.2 of plan.md. Designed to work without Elasticsearch — the CLI will simply report
  zero logs in that case. To wire it to a real cluster set $env:HFA_Elastic__Uri.

.PARAMETER ApiName
  Logical API name passed to the CLI. Defaults to `demo-api`.

.PARAMETER LookbackHours
  Lookback window forwarded to the CLI. Defaults to 1.

.PARAMETER SkipBackend
  When set, just starts demo-api and produces logs (handy for manual experimentation).

.PARAMETER KeepRunning
  When set, leaves demo-api running after the script finishes. Otherwise it is stopped.

.EXAMPLE
  pwsh scripts/demo.ps1
  pwsh scripts/demo.ps1 -LookbackHours 2 -KeepRunning
#>

[CmdletBinding()]
param(
    [string]$ApiName = 'demo-api',
    [int]$LookbackHours = 1,
    [switch]$SkipBackend,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Write-Step($msg) { Write-Host "[demo] $msg" -ForegroundColor Cyan }
function Test-PortFree([int]$port) {
    try { (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop) | Out-Null; return $false }
    catch { return $true }
}

Write-Step 'Building demo-api'
dotnet build demo-api/demo-api.csproj --nologo --verbosity minimal | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'demo-api build failed' }

if (-not (Test-PortFree 5333)) {
    throw 'Port 5333 is already in use. Stop the listener or use a different port.'
}

Write-Step 'Starting demo-api on http://localhost:5333'
$demoProcess = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', 'demo-api/demo-api.csproj', '--no-build', '--urls', 'http://localhost:5333') `
    -PassThru -WindowStyle Hidden

try {
    Write-Step 'Waiting for /health to respond'
    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $health = Invoke-RestMethod -Uri 'http://localhost:5333/health' -TimeoutSec 1
            if ($health.status -eq 'ok') { break }
        } catch { }
    } while ((Get-Date) -lt $deadline)
    if (-not $health -or $health.status -ne 'ok') { throw 'demo-api did not become healthy in time' }

    Write-Step 'Producing error traffic'
    foreach ($i in 1..3) {
        try { Invoke-WebRequest -Uri 'http://localhost:5333/orders' -Method POST -ContentType 'application/json' -Body '{}' -UseBasicParsing | Out-Null } catch { }
        try { Invoke-WebRequest -Uri "http://localhost:5333/users/-$i" -UseBasicParsing | Out-Null } catch { }
        try { Invoke-WebRequest -Uri 'http://localhost:5333/payments/ab' -UseBasicParsing | Out-Null } catch { }
    }
    Write-Step '9 erroneous requests sent'

    if (-not $SkipBackend) {
        Write-Step "Running CLI: hot-fix-ambulance $ApiName --lookback ${LookbackHours}h"
        & dotnet run --project backend/src/HotFixAmbulance.Cli -- $ApiName --lookback "${LookbackHours}h" --format json --no-open
        $exit = $LASTEXITCODE
        Write-Step "CLI exited with $exit"
    }
}
finally {
    if (-not $KeepRunning -and $demoProcess -and -not $demoProcess.HasExited) {
        Write-Step 'Stopping demo-api'
        Stop-Process -Id $demoProcess.Id -Force -ErrorAction SilentlyContinue
    }
    elseif ($KeepRunning) {
        Write-Step "demo-api still running (PID $($demoProcess.Id)). Stop it with: Stop-Process -Id $($demoProcess.Id)"
    }
}
