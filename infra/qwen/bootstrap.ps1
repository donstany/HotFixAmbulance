#requires -Version 5.1
<#
.SYNOPSIS
  Bootstraps the local Qwen runtime: waits for it, pulls the model into the
  hfa-qwen-data volume if absent, runs a JSON /api/chat sanity probe. Re-runnable.

.PARAMETER OllamaUri
  Base URI of the runtime. Defaults to http://localhost:11434.

.PARAMETER Model
  Model tag to ensure is present. Defaults to qwen2.5:3b.

.PARAMETER WaitSeconds
  How long to wait for the runtime to answer /api/tags before giving up.

.EXAMPLE
  powershell -File infra/qwen/bootstrap.ps1
  powershell -File infra/qwen/bootstrap.ps1 -Model qwen2.5:7b
#>

[CmdletBinding()]
param(
    [string]$OllamaUri = 'http://localhost:11434',
    [string]$Model = 'qwen2.5:3b',
    [int]$WaitSeconds = 60
)

$ErrorActionPreference = 'Stop'
$OllamaUri = $OllamaUri.TrimEnd('/')

function Write-Step($msg) { Write-Host "[qwen-bootstrap] $msg" -ForegroundColor Cyan }
function Write-Skip($msg) { Write-Host "[qwen-bootstrap] $msg" -ForegroundColor DarkGray }

# --- 1. wait for the runtime --------------------------------------------------
Write-Step "Waiting for $OllamaUri (max ${WaitSeconds}s)"
$deadline = (Get-Date).AddSeconds($WaitSeconds)
$tags = $null
do {
    try {
        $tags = Invoke-RestMethod -Method GET -Uri "$OllamaUri/api/tags" -TimeoutSec 2 -ErrorAction Stop
        break
    } catch { Start-Sleep -Milliseconds 500 }
} while ((Get-Date) -lt $deadline)
if ($null -eq $tags) { throw "Qwen runtime at $OllamaUri did not become ready within ${WaitSeconds}s" }
Write-Step 'Runtime is up.'

# --- 2. pull the model if absent (idempotent) --------------------------------
$names = @()
if ($tags.models) { $names = @($tags.models | ForEach-Object { $_.name }) + @($tags.models | ForEach-Object { $_.model }) }
if ($names -contains $Model) {
    Write-Skip "Model '$Model' already present -- skipping pull."
} else {
    Write-Step "Pulling model '$Model' (first run downloads a few GB)"
    & docker exec hfa-qwen ollama pull $Model
    if ($LASTEXITCODE -ne 0) { throw "ollama pull '$Model' failed (see docker logs hfa-qwen)" }
}

# --- 3. sanity probe: a tiny JSON chat round-trip ----------------------------
Write-Step "Sanity probe: POST /api/chat (model=$Model, format=json)"
$probeBody = @{
    model    = $Model
    stream   = $false
    format   = 'json'
    messages = @(
        @{ role = 'system'; content = 'Reply with a JSON object having a single key "ok" set to true.' },
        @{ role = 'user';   content = 'ping' }
    )
} | ConvertTo-Json -Depth 6
$probe = Invoke-RestMethod -Method POST -Uri "$OllamaUri/api/chat" -ContentType 'application/json' -Body $probeBody -TimeoutSec 120
if ([string]::IsNullOrWhiteSpace($probe.message.content)) {
    throw 'Sanity probe returned an empty message — the model may not have loaded.'
}
Write-Step 'chat probe: ok'
Write-Host '[qwen-bootstrap] done.' -ForegroundColor Green
