#requires -Version 5.1
<#
.SYNOPSIS
  Appends a one-line entry to docs/dev-log.md after a tool call, as exam evidence that work is AI-driven.

.DESCRIPTION
  Designed to be invoked by Claude Code's PostToolUse hook. Reads a JSON object on stdin with the keys
  `tool`, `phase`, `step`, `note`. Falls back to env vars HFA_LOG_TOOL / HFA_LOG_PHASE / HFA_LOG_STEP / HFA_LOG_NOTE
  for ad-hoc CLI use:

    "tool=git phase=0.1 step=scaffold note=created repo skeleton" | ./.claude/hooks/post-tool-use.ps1

  Behaviour is idempotent: duplicate consecutive lines are collapsed.

.NOTES
  Phase 0.6 of plan.md.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$logPath  = Join-Path $repoRoot 'docs\dev-log.md'

# Read payload: stdin JSON first, env-var fallback otherwise.
$payload = $null
if (-not [Console]::IsInputRedirected) {
    $payload = @{}
} else {
    $raw = [Console]::In.ReadToEnd()
    if ($raw -and $raw.Trim()) {
        try   { $payload = $raw | ConvertFrom-Json -ErrorAction Stop }
        catch { $payload = @{} }
    } else { $payload = @{} }
}

function Get-Field([string]$name, [string]$envName) {
    if ($payload.PSObject.Properties.Name -contains $name -and $payload.$name) { return [string]$payload.$name }
    $v = [Environment]::GetEnvironmentVariable($envName)
    if ($v) { return $v }
    return ''
}

$tool  = Get-Field 'tool'  'HFA_LOG_TOOL'
$phase = Get-Field 'phase' 'HFA_LOG_PHASE'
$step  = Get-Field 'step'  'HFA_LOG_STEP'
$note  = Get-Field 'note'  'HFA_LOG_NOTE'

if (-not ($tool -or $phase -or $step -or $note)) {
    # Nothing actionable; exit quietly so we don't pollute logs.
    exit 0
}

$when = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')
$toolPrefix = if ($tool) { "$tool — " } else { '' }
$line = "| $when | $($phase -as [string]) | $($step -as [string]) | $toolPrefix$note |"

if (-not (Test-Path $logPath)) {
    @(
        '# HotFixAmbulance — development log',
        '',
        '| When (UTC) | Phase | Cycle step | Note |',
        '| --- | --- | --- | --- |'
    ) | Set-Content -Path $logPath -Encoding utf8
}

$existing = Get-Content $logPath -ErrorAction SilentlyContinue
if ($existing -and ($existing[-1] -eq $line)) { exit 0 }   # de-dupe consecutive
Add-Content -Path $logPath -Value $line -Encoding utf8
