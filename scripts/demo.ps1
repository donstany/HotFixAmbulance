#requires -Version 5.1
<#
.SYNOPSIS
  End-to-end HotFixAmbulance demo on Qwen. Starts the Dockerized Qwen runtime (model
  pre-pulled), demo-api, the API (:5283) and frontend (:5173) wired to Analysis:Strategy=Llm,
  produces a triage run through the API, asserts it was analyzed by the LLM, and opens the UI
  where the Qwen badge is visible. Pass -SkipLlm for the old heuristic-only flow.

.PARAMETER LlmModel
  Qwen model tag pulled and used. Defaults to qwen2.5:3b.

.PARAMETER SkipLlm
  Escape hatch: skip Qwen and run the heuristic flow (no badge, no Docker LLM dependency).

.DESCRIPTION
  Phase 8.2 of plan.md. By default the demo works without Elasticsearch -- the CLI just
  reports zero logs. Pass -WithElastic to spin up the local single-node ES container
  defined in infra/elasticsearch/docker-compose.yml, wire demo-api to it, and reshape
  the freshly-emitted ECS logs through the ingest pipeline before the CLI runs.
    The demo now uses a real MSSQL database in Docker by default for realistic EF Core
    failures (unique-constraint violations and database timeouts).

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

.PARAMETER WithMssql
    When true (default), starts infra/mssql/docker-compose.yml and points demo-api to
    SQL Server on localhost:14333.

.PARAMETER MssqlSaPassword
    SA password passed to the MSSQL container and connection string.

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
    [string]$ElasticUri = 'http://localhost:9200',
    [string]$LlmModel = 'qwen2.5:3b',
    [switch]$SkipLlm,
    [bool]$WithMssql = $true,
    [string]$MssqlSaPassword = 'Your_strong_Password123!'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Write-Step($msg) { Write-Host "[demo] $msg" -ForegroundColor Cyan }
function Test-PortFree([int]$port) {
    try { (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop) | Out-Null; return $false }
    catch { return $true }
}
function Wait-Http([string]$url, [int]$timeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    do {
        try { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2 | Out-Null; return $true }
        catch { Start-Sleep -Milliseconds 500 }
    } while ((Get-Date) -lt $deadline)
    return $false
}

if (-not $SkipLlm) {
    Write-Step 'Ensuring Qwen runtime container is up (docker compose up -d)'
    $qwenDir = Join-Path $repoRoot 'infra/qwen'
    $qwenCompose = Join-Path $qwenDir 'docker-compose.yml'
    $qwenCorpCompose = Join-Path $qwenDir 'docker-compose.corp.yml'
    if (-not (Test-Path $qwenCompose)) { throw "compose file not found: $qwenCompose" }

    # Corporate networks MITM-terminate TLS; export the host CA bundle so the container
    # can verify the proxy and the model pull works. The bundle is git-ignored.
    Write-Step 'Exporting host CA bundle for the container (corporate TLS interception)'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $qwenDir 'export-host-ca.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'export-host-ca.ps1 failed' }

    & docker compose -f $qwenCompose -f $qwenCorpCompose up -d
    if ($LASTEXITCODE -ne 0) { throw 'qwen docker compose up failed (is Docker Desktop running?)' }

    $qwenBootstrap = Join-Path $qwenDir 'bootstrap.ps1'
    if (-not (Test-Path $qwenBootstrap)) { throw "bootstrap script missing: $qwenBootstrap" }
    Write-Step "Bootstrapping Qwen (pull $LlmModel if absent - first run downloads ~2GB)"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $qwenBootstrap -Model $LlmModel
    if ($LASTEXITCODE -ne 0) { throw "qwen bootstrap exited with $LASTEXITCODE" }

    # Every child process (demo-api, API, CLI) inherits these - they honour HFA_*.
    $env:HFA_Analysis__Strategy = 'Llm'
    $env:HFA_Llm__Provider = 'Qwen'
    $env:HFA_Llm__Endpoint = 'http://localhost:11434'
    $env:HFA_Llm__Model = $LlmModel
    Write-Step "LLM strategy enabled: provider=Qwen model=$LlmModel endpoint=http://localhost:11434"
} else {
    Write-Step 'LLM disabled (-SkipLlm): running the heuristic flow'
}

if ($WithMssql) {
    Write-Step 'Ensuring MSSQL container is up (docker compose up -d)'
    $mssqlCompose = Join-Path $repoRoot 'infra/mssql/docker-compose.yml'
    if (-not (Test-Path $mssqlCompose)) { throw "compose file not found: $mssqlCompose" }

    $env:MSSQL_SA_PASSWORD = $MssqlSaPassword
    & docker compose -f $mssqlCompose up -d
    if ($LASTEXITCODE -ne 0) { throw 'mssql docker compose up failed (is Docker Desktop running?)' }

    Write-Step 'Waiting for MSSQL health checks to pass'
    $deadline = (Get-Date).AddMinutes(2)
    $dbReady = $false
    do {
        try {
            $status = (& docker inspect --format='{{.State.Health.Status}}' hfa-mssql 2>$null)
            if ($status -eq 'healthy') { $dbReady = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    if (-not $dbReady) {
        throw 'MSSQL did not become healthy in time. Check: docker logs hfa-mssql'
    }

    # Wire demo-api DB to SQL Server.
    $env:HFA_Database__Provider = 'SqlServer'
    $env:HFA_ConnectionStrings__DemoDb = "Server=localhost,14333;Database=HotFixDemo;User Id=sa;Password=$MssqlSaPassword;Encrypt=True;TrustServerCertificate=True;"
    Write-Step 'demo-api will use SQL Server at localhost:14333 (database: HotFixDemo)'
}

if ($WithElastic) {
    Write-Step 'Ensuring Elasticsearch container is up (docker compose up -d)'
    $composeFile = Join-Path $repoRoot 'infra/elasticsearch/docker-compose.yml'
    if (-not (Test-Path $composeFile)) { throw "compose file not found: $composeFile" }

    # If a stale 'hfa-es' exists that was NOT started by this compose project,
    # compose will fail with a name-conflict ("Container name /hfa-es is already
    # in use"). Detect by comparing all 'hfa-es' containers against the subset
    # that carries the compose project label.
    $allIds     = @(& docker ps -a -q --filter 'name=^hfa-es$' 2>$null) | Where-Object { $_ }
    $managedIds = @(& docker ps -a -q --filter 'name=^hfa-es$' --filter 'label=com.docker.compose.project=hfa' 2>$null) | Where-Object { $_ }
    $stale      = $allIds | Where-Object { $managedIds -notcontains $_ }
    foreach ($cid in $stale) {
        Write-Step "Removing stale hfa-es container $cid (not owned by compose project 'hfa')"
        & docker rm -f $cid | Out-Null
    }

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
    # Prefer the real apis.config.json (which maps demo-api to the local repo so
    # FixHintBuilder can mine origin/main for blame + code snippet); fall back to
    # the example config when the local one is missing.
    $apisConfigLocal   = Join-Path $repoRoot 'config/apis.config.json'
    $apisConfigExample = Join-Path $repoRoot 'config/apis.config.example.json'
    $apisConfig = if (Test-Path $apisConfigLocal) { $apisConfigLocal } else { $apisConfigExample }
    if (Test-Path $apisConfig) {
        $env:HFA_Apis__ConfigPath = $apisConfig
        Write-Step "CLI will use apis config: $apisConfig"
    } else {
        Write-Warning "apis config not found at $apisConfig -- CLI may fail to start"
    }
}

if (-not (Test-PortFree 5333)) {
    $owners = @(Get-NetTCPConnection -State Listen -LocalPort 5333 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
    Write-Step "Port 5333 is held by pid(s) $($owners -join ', ') -- stopping (likely leftover demo-api)"
    foreach ($pidToKill in $owners) { Stop-Process -Id $pidToKill -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
    if (-not (Test-PortFree 5333)) { throw 'Port 5333 is still in use after attempted cleanup.' }
}

# Also stop any leftover demo-api process that isn't listening on 5333 yet
# (e.g. startup crashed before bind) -- it can still hold demo-api.exe and
# break `dotnet build` with MSB3027.
$lingering = @(Get-Process -Name 'demo-api' -ErrorAction SilentlyContinue)
foreach ($p in $lingering) {
    Write-Step "Stopping lingering demo-api process (PID $($p.Id)) that holds the build output"
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
if ($lingering) { Start-Sleep -Milliseconds 500 }

Write-Step 'Building demo-api'
dotnet build demo-api/demo-api.csproj --nologo --verbosity minimal | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'demo-api build failed' }

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
        try { Invoke-WebRequest -Uri 'http://localhost:5333/payments/ab/settlement' -UseBasicParsing | Out-Null } catch { }
        try { Invoke-WebRequest -Uri 'http://localhost:5333/invoices/duplicate' -Method POST -UseBasicParsing | Out-Null } catch { }
        try { Invoke-WebRequest -Uri 'http://localhost:5333/invoices/reprice' -UseBasicParsing | Out-Null } catch { }
        try { Invoke-WebRequest -Uri 'http://localhost:5333/transfers/on-hold' -Method POST -UseBasicParsing | Out-Null } catch { }
        try { Invoke-WebRequest -Uri 'http://localhost:5333/pricing/preview' -UseBasicParsing | Out-Null } catch { }
    }
    Write-Step '24 erroneous requests sent across null-reference, upstream timeout, upstream 503, DB constraint, DB timeout, SQL limit reached on-hold transfers, and code-state failures'

    if ($WithElastic) {
        Write-Step 'Letting Serilog flush its Elasticsearch sink (8s)'
        Start-Sleep -Seconds 8
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

    # ---- Start the API (:5283) + frontend (:5173) and create the run the UI shows ----
    if (-not (Test-PortFree 5283)) {
        $owners = @(Get-NetTCPConnection -State Listen -LocalPort 5283 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
        Write-Step "Port 5283 is held by pid(s) $($owners -join ', ') -- stopping (likely leftover API)"
        foreach ($pidToKill in $owners) { Stop-Process -Id $pidToKill -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 800
    }

    Write-Step 'Building HotFixAmbulance.Api'
    dotnet build backend/src/HotFixAmbulance.Api/HotFixAmbulance.Api.csproj --nologo --verbosity minimal | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'HotFixAmbulance.Api build failed' }

    Write-Step 'Starting HotFixAmbulance.Api on http://localhost:5283'
    $apiProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', 'backend/src/HotFixAmbulance.Api', '--no-build', '--urls', 'http://localhost:5283') `
        -PassThru -WindowStyle Hidden
    if (-not (Wait-Http 'http://localhost:5283/health' 30)) { throw 'HotFixAmbulance.Api did not become healthy on :5283' }

    if (Test-PortFree 5173) {
        Write-Step 'Starting frontend dev server on http://localhost:5173 (npm run dev)'
        $frontendProcess = Start-Process -FilePath 'npm' `
            -ArgumentList @('--prefix', 'frontend', 'run', 'dev') `
            -PassThru -WindowStyle Hidden
        if (-not (Wait-Http 'http://localhost:5173' 40)) { Write-Warning 'frontend did not answer on :5173 yet — it may still be starting' }
    } else {
        Write-Step 'frontend already running on :5173'
    }

    Write-Step "Creating the run via API: POST /api/triage/$ApiName?lookbackHours=$LookbackHours"
    # CPU inference is sequential, one call per error group, so allow generous headroom.
    $header = Invoke-RestMethod -Method POST -Uri "http://localhost:5283/api/triage/$ApiName`?lookbackHours=$LookbackHours" -TimeoutSec 900
    Write-Step ("API run id={0} analyzedBy={1} groups={2}" -f $header.id, $header.analyzedBy, $header.totalGroups)

    if (-not $SkipLlm) {
        if ($header.analyzedBy -notin @('Llm', 'Mixed')) {
            throw "Demo guarantee failed: expected analyzedBy 'Llm'/'Mixed' but got '$($header.analyzedBy)'. Qwen was not used - check 'docker logs hfa-qwen' and that '$LlmModel' is pulled."
        }
        Write-Step "Verified: this run was analyzed by Qwen (analyzedBy=$($header.analyzedBy)). The UI shows the Qwen badge."
    }

    $uiUrl = "http://localhost:5173/?analysisId=$($header.id)&api=$ApiName"
    Write-Step "Opening UI: $uiUrl"
    Start-Process $uiUrl
}
finally {
    if (-not $KeepRunning -and $demoProcess -and -not $demoProcess.HasExited) {
        Write-Step 'Stopping demo-api'
        Stop-Process -Id $demoProcess.Id -Force -ErrorAction SilentlyContinue
    }
    elseif ($KeepRunning) {
        Write-Step "demo-api still running (PID $($demoProcess.Id)). Stop it with: Stop-Process -Id $($demoProcess.Id)"
    }

    # The API + frontend must stay up for the UI link to resolve — leave them running.
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Write-Step "HotFixAmbulance.Api still running (PID $($apiProcess.Id)) on :5283. Stop it with: Stop-Process -Id $($apiProcess.Id)"
    }
    if ($frontendProcess -and -not $frontendProcess.HasExited) {
        Write-Step "frontend dev server still running (PID $($frontendProcess.Id)) on :5173. Stop it with: Stop-Process -Id $($frontendProcess.Id)"
    }
}
