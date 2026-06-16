#requires -Version 5.1
<#
.SYNOPSIS
  End-to-end HotFixAmbulance demo. Starts demo-api, hammers its endpoints to produce errors,
  then runs the CLI to triage them and prints the analysis id and React UI URL.

.DESCRIPTION
  Phase 8.2 of plan.md. By default the demo works without Elasticsearch -- the CLI just
  reports zero logs. Pass -WithElastic to spin up the local single-node ES container
  defined in infra/elasticsearch/docker-compose.yml, wire demo-api to it, and reshape
  the freshly-emitted ECS logs through the ingest pipeline before the CLI runs.

.PARAMETER ApiName
  Logical API name passed to the CLI. Defaults to `demo-api`.

.PARAMETER LookbackHours
  Lookback window forwarded to the CLI. Defaults to 1.

.PARAMETER SkipBackend
  When set, just starts demo-api and produces logs (handy for manual experimentation).

.PARAMETER KeepRunning
  When set, leaves demo-api running after the script finishes. Otherwise it is stopped.

.PARAMETER WithElastic
  When set, starts the infra/elasticsearch/docker-compose.yml stack (if not already
  running), points demo-api at it via HFA_Elastic__Uri, points the CLI at
  config/apis.config.example.json via HFA_Apis__ConfigPath, and runs the ECS-to-fields
  bootstrap after error traffic has been generated.

.PARAMETER ElasticUri
  Override the Elasticsearch URI used when -WithElastic is set. Defaults to
  http://localhost:9200.

.EXAMPLE
  powershell -File scripts/demo.ps1
  powershell -File scripts/demo.ps1 -WithElastic -KeepRunning
  powershell -File scripts/demo.ps1 -LookbackHours 2 -KeepRunning
#>

[CmdletBinding()]
param(
    [string]$ApiName = 'demo-api',
    [int]$LookbackHours = 1,
    [switch]$SkipBackend,
    [switch]$KeepRunning,
    [switch]$WithElastic,
    [string]$ElasticUri = 'http://localhost:9200'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Write-Step($msg) { Write-Host "[demo] $msg" -ForegroundColor Cyan }
function Test-PortFree([int]$port) {
    try { (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop) | Out-Null; return $false }
    catch { return $true }
}

if ($WithElastic) {
    Write-Step 'Ensuring Elasticsearch container is up (docker compose up -d)'
    $composeFile = Join-Path $repoRoot 'infra/elasticsearch/docker-compose.yml'
    if (-not (Test-Path $composeFile)) { throw "compose file not found: $composeFile" }
    & docker compose -f $composeFile up -d
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed (is Docker Desktop running?)' }

    Write-Step "Waiting for Elasticsearch at $ElasticUri"
    $deadline = (Get-Date).AddSeconds(60)
    $esReady = $false
    do {
        try {
            $h = Invoke-RestMethod -Method GET -Uri "$($ElasticUri.TrimEnd('/'))/_cluster/health?wait_for_status=yellow&timeout=2s" -ErrorAction Stop
            if ($h.status -in @('yellow', 'green')) { $esReady = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    } while ((Get-Date) -lt $deadline)
    if (-not $esReady) { throw "Elasticsearch did not become ready at $ElasticUri" }

    # Wire demo-api to ES via env var (Program.cs honours HFA_*).
    $env:HFA_Elastic__Uri = $ElasticUri
    Write-Step "demo-api will write to $ElasticUri"

    # CLI defaults to apis.config.json beside its binary, which doesn't exist.
    # Point it at the example config so it can resolve demo-api.
    $apisConfig = Join-Path $repoRoot 'config/apis.config.example.json'
    if (Test-Path $apisConfig) {
        $env:HFA_Apis__ConfigPath = $apisConfig
        Write-Step "CLI will use apis config: $apisConfig"
    } else {
        Write-Warning "apis config not found at $apisConfig -- CLI may fail to start"
    }
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

    if ($WithElastic) {
        Write-Step 'Letting Serilog flush, then reshaping ECS logs -> mapper shape'
        Start-Sleep -Seconds 4
        $bootstrap = Join-Path $repoRoot 'infra/elasticsearch/bootstrap.ps1'
        if (-not (Test-Path $bootstrap)) { throw "bootstrap script missing: $bootstrap" }
        & powershell -NoProfile -ExecutionPolicy Bypass -File $bootstrap -ElasticUri $ElasticUri
        if ($LASTEXITCODE -ne 0) { throw "elastic bootstrap exited with $LASTEXITCODE" }
    }

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
