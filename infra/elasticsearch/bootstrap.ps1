#requires -Version 5.1
<#
.SYNOPSIS
  Bootstraps Elasticsearch for the HotFixAmbulance demo: installs the ECS->fields
  ingest pipeline and reshapes the daily 'logs-yyyy.MM.dd' index emitted by demo-api
  into 'hfa-mapped', then exposes it through a 'logs-mapped-<date>' alias so the
  CLI's 'logs-*' wildcard picks it up.

.DESCRIPTION
  The Elastic.CommonSchema.Serilog formatter used by demo-api writes ECS-shaped
  documents (labels.Application, log.level, url.path, ...) but
  HotFixAmbulance.Elastic.SerilogDocumentMapper expects the legacy Serilog shape
  (fields.Application, top-level level, ...). Instead of changing either side,
  this script installs a server-side ingest pipeline that rewrites incoming
  documents to the mapper shape, then reindexes the existing daily index through
  the pipeline. Re-runnable: pipeline is overwritten, alias is recreated.

.PARAMETER ElasticUri
  Base URI of the Elasticsearch cluster. Defaults to http://localhost:9200.

.PARAMETER SourceDate
  UTC date for the daily ECS index to reshape, formatted yyyy.MM.dd.
  Defaults to today (UTC).

.PARAMETER WaitSeconds
  How long to wait for the cluster to report yellow/green before giving up.

.EXAMPLE
  powershell -File infra/elasticsearch/bootstrap.ps1
  powershell -File infra/elasticsearch/bootstrap.ps1 -SourceDate 2026.06.16
#>

[CmdletBinding()]
param(
    [string]$ElasticUri = 'http://localhost:9200',
    [string]$SourceDate = ([DateTime]::UtcNow.ToString('yyyy.MM.dd')),
    [int]$WaitSeconds = 60
)

$ErrorActionPreference = 'Stop'
$ElasticUri = $ElasticUri.TrimEnd('/')

$pipelineId   = 'ecs_to_fields'
$destIndex    = 'hfa-mapped'
$aliasName    = "logs-mapped-$SourceDate"
$sourceIndex  = "logs-$SourceDate"
$pipelineFile = Join-Path $PSScriptRoot 'ecs_to_fields.pipeline.json'

function Write-Step($msg) { Write-Host "[es-bootstrap] $msg" -ForegroundColor Cyan }
function Write-Skip($msg) { Write-Host "[es-bootstrap] $msg" -ForegroundColor DarkGray }

function Invoke-Es {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [string]$Path,
        [object]$Body
    )
    $uri = "$ElasticUri$Path"
    $params = @{
        Method      = $Method
        Uri         = $uri
        ContentType = 'application/json'
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        if ($Body -is [string]) { $params.Body = $Body }
        else { $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    }
    return Invoke-RestMethod @params
}

# --- 1. wait for cluster ------------------------------------------------------
Write-Step "Waiting for $ElasticUri (max ${WaitSeconds}s)"
$deadline = (Get-Date).AddSeconds($WaitSeconds)
$ready = $false
do {
    try {
        $health = Invoke-RestMethod -Method GET -Uri "$ElasticUri/_cluster/health?wait_for_status=yellow&timeout=2s" -ErrorAction Stop
        if ($health.status -in @('yellow', 'green')) { $ready = $true; break }
    } catch { Start-Sleep -Milliseconds 500 }
} while ((Get-Date) -lt $deadline)
if (-not $ready) { throw "Elasticsearch at $ElasticUri did not become ready within ${WaitSeconds}s" }
Write-Step "Cluster status: $($health.status)"

# --- 2. install / refresh ingest pipeline ------------------------------------
if (-not (Test-Path $pipelineFile)) { throw "Pipeline definition not found: $pipelineFile" }
Write-Step "PUT _ingest/pipeline/$pipelineId  (from $([System.IO.Path]::GetFileName($pipelineFile)))"
$pipelineBody = Get-Content -Raw -LiteralPath $pipelineFile
Invoke-Es -Method PUT -Path "/_ingest/pipeline/$pipelineId" -Body $pipelineBody | Out-Null

# --- 3. reshape the daily index ----------------------------------------------
$sourceExists = $true
try { Invoke-Es -Method HEAD -Path "/$sourceIndex" | Out-Null } catch { $sourceExists = $false }

if (-not $sourceExists) {
    Write-Skip "Source index '$sourceIndex' does not exist yet -- pipeline installed, nothing to reshape."
    Write-Skip "Hint: run the demo (HFA_Elastic__Uri=$ElasticUri) so demo-api writes logs, then re-run this script."
    return
}

# Use _reindex with the pipeline. Dynamic mapping on the destination is what the
# CLI relies on (it queries fields.Application.keyword + level.keyword subfields).
Write-Step "Reindex $sourceIndex -> $destIndex via pipeline $pipelineId"
$reindexBody = @{
    source = @{ index = $sourceIndex }
    dest   = @{ index = $destIndex; pipeline = $pipelineId }
}
$reindex = Invoke-Es -Method POST -Path '/_reindex?refresh=true&wait_for_completion=true' -Body $reindexBody
Write-Step ("Reindex: total={0} created={1} updated={2} failures={3}" -f $reindex.total, $reindex.created, $reindex.updated, @($reindex.failures).Count)
if (@($reindex.failures).Count -gt 0) {
    $reindex.failures | ConvertTo-Json -Depth 8 | Write-Host
    throw 'Reindex reported document failures (see above).'
}

# --- 4. (re)bind the alias ----------------------------------------------------
$existingAliases = @{}
try { $existingAliases = Invoke-Es -Method GET -Path "/_alias/$aliasName" } catch { }

$aliasActions = @()
foreach ($idx in $existingAliases.PSObject.Properties.Name) {
    if ($idx -ne $destIndex) {
        $aliasActions += @{ remove = @{ index = $idx; alias = $aliasName } }
    }
}
$aliasActions += @{ add = @{ index = $destIndex; alias = $aliasName } }

Write-Step "Bind alias $aliasName -> $destIndex"
Invoke-Es -Method POST -Path '/_aliases' -Body @{ actions = $aliasActions } | Out-Null

# --- 5. quick sanity probe ----------------------------------------------------
$probe = Invoke-Es -Method POST -Path "/$aliasName/_search?size=0" -Body @{
    query = @{
        bool = @{
            filter = @(
                @{ term  = @{ 'fields.Application.keyword' = 'demo-api' } },
                @{ terms = @{ 'level.keyword' = @('Fatal', 'Error', 'Warning') } }
            )
        }
    }
}
$hits = $probe.hits.total.value
Write-Step "Sanity probe: alias=$aliasName  hits(level in Fatal/Error/Warning, app=demo-api)=$hits"
Write-Host "[es-bootstrap] done." -ForegroundColor Green
