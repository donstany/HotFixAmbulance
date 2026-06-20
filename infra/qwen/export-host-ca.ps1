#requires -Version 5.1
<#
.SYNOPSIS
  Exports the host's trusted root + intermediate CA certificates into a single PEM
  bundle (infra/qwen/corp-ca-bundle.crt) so the Qwen runtime container can verify TLS
  through a corporate intercepting proxy.

.DESCRIPTION
  In corporate networks a proxy MITM-terminates TLS and presents certificates signed
  by a corporate root CA. The host trusts that CA (it is in the Windows cert store),
  but a fresh Linux container does not — so `ollama pull` fails with
  "x509: certificate signed by unknown authority". This script writes every cert in
  the LocalMachine Root and CA stores to a PEM bundle that docker-compose.corp.yml
  mounts into the container and points SSL_CERT_FILE at.

  The bundle is machine-specific and git-ignored. Re-runnable (overwrites).

.PARAMETER OutFile
  Destination PEM path. Defaults to corp-ca-bundle.crt beside this script.
#>

[CmdletBinding()]
param(
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

function Write-Step($msg) { Write-Host "[qwen-ca] $msg" -ForegroundColor Cyan }

if (-not $OutFile) {
    $dir = $PSScriptRoot
    if (-not $dir) { $dir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    if (-not $dir) { $dir = (Get-Location).Path }
    $OutFile = Join-Path $dir 'corp-ca-bundle.crt'
}

$stores = @('Cert:\LocalMachine\Root', 'Cert:\LocalMachine\CA')
$sb = New-Object System.Text.StringBuilder
$count = 0
foreach ($store in $stores) {
    foreach ($cert in (Get-ChildItem $store -ErrorAction SilentlyContinue)) {
        $b64 = [Convert]::ToBase64String($cert.RawData, [Base64FormattingOptions]::InsertLineBreaks)
        [void]$sb.AppendLine("# $($cert.Subject)")
        [void]$sb.AppendLine('-----BEGIN CERTIFICATE-----')
        [void]$sb.AppendLine($b64)
        [void]$sb.AppendLine('-----END CERTIFICATE-----')
        $count++
    }
}

# ASCII, LF line endings — the bundle is read by OpenSSL/Go inside a Linux container.
$bytes = [System.Text.Encoding]::ASCII.GetBytes(($sb.ToString() -replace "`r`n", "`n"))
[System.IO.File]::WriteAllBytes($OutFile, $bytes)
Write-Step "Wrote $count certificate(s) to $OutFile"
