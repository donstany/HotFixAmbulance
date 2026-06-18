# Teacher Workflow — CLI Cheat Sheet

Minimal copy-paste runbook. Each block: one-line description, then exact commands.
Windows + PowerShell 5.1 + Docker Desktop.

---

## 1. Verify toolchain
```powershell
dotnet --version; node --version; npm --version; docker --version
```

## 2. Run the full test suite (153 tests: 134 backend + 19 frontend)
```powershell
dotnet test backend/HotFixAmbulance.sln --nologo --verbosity minimal
npm --prefix frontend test -- --run
npm --prefix frontend run lint
```

## 3. End-to-end demo (Elasticsearch + demo-api + CLI triage)
```powershell
powershell -File scripts/demo.ps1 -WithElastic -KeepRunning
```

## 4. Start the backend API (terminal A)
```powershell
$env:HFA_Elastic__Uri                  = 'http://localhost:9200'
$env:HFA_Apis__ConfigPath              = "$PWD\config\apis.config.json"
$env:HFA_Persistence__ConnectionString = "Data Source=$PWD\hotfix.db"
$env:ASPNETCORE_URLS                   = 'http://localhost:5283'
dotnet run --project backend/src/HotFixAmbulance.Api --no-launch-profile
```

## 5. Start the React UI (terminal B)
```powershell
npm --prefix frontend run dev
```

## 6. Trigger a triage + open the UI (terminal C)
```powershell
# Relative window (matches a TimeRangePicker preset like 1h):
$r = Invoke-RestMethod -Method POST -Uri 'http://localhost:5283/api/triage/demo-api?lookbackHours=1'
"id=$($r.id)  totalLogs=$($r.totalLogs)  groups=$($r.groups.Count)  fromUtc=$($r.fromUtc)  toUtc=$($r.toUtc)  truncated=$($r.isTruncated)"
Start-Process "http://localhost:5173/?analysisId=$($r.id)&api=demo-api"

# Absolute window (matches TimeRangePicker -> Custom, also drives the 'Rerun this window' button):
# Note: PowerShell strips '+' from interpolated query strings; URL-encode it as %2B.
$from = '2026-06-18T12:00:00Z'; $to = '2026-06-18T13:00:00Z'
$rAbs = Invoke-RestMethod -Method POST -Uri "http://localhost:5283/api/triage/demo-api?fromUtc=$from&toUtc=$to"
"abs id=$($rAbs.id)  groups=$($rAbs.groups.Count)"

# List configured APIs (powers the dropdown in the Run-analysis bar):
Invoke-RestMethod 'http://localhost:5283/api/apis'

# Verify the MaxRangeDays=30 cap (expect HTTP 400):
try { Invoke-WebRequest -Method POST 'http://localhost:5283/api/triage/demo-api?fromUtc=2025-01-01T00:00:00Z&toUtc=2026-06-01T00:00:00Z' -UseBasicParsing | Out-Null }
catch { "got $($_.Exception.Response.StatusCode.value__) (expected 400)" }
```

## 7. Prove the pre-commit hook blocks bad commits
```powershell
# 1) Edit backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs:
#    flip BeGreaterThan(0) -> BeLessThan(0) on Compare_Fatal_IsHigherThan_Error, save.
git add backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
git commit -m "should fail"   # hook prints "Commit blocked. Fix the failing gate(s)."

# Revert:
git restore --staged backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
git checkout --     backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
```

## 8. Tear down
```powershell
# Ctrl+C the API and Vite terminals.
docker compose -f infra/elasticsearch/docker-compose.yml down       # keep data
docker compose -f infra/elasticsearch/docker-compose.yml down -v    # wipe data
```

---

## Quick recovery (only if something is stuck)

```powershell
# Stale ES container blocking compose:
docker rm -f hfa-es

# Port 5333 (demo-api) busy:
Get-NetTCPConnection -State Listen -LocalPort 5333 | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }

# Port 5283 (API) locked by previous run:
Get-Process | Where-Object { $_.Path -like '*HotFixAmbulance.Api*' } | Stop-Process -Force

# Sanity check ports:
foreach ($p in 5283,5173,5333,9200) {
  $b = Get-NetTCPConnection -State Listen -LocalPort $p -ErrorAction SilentlyContinue
  if ($b) { "$p BUSY pid $($b.OwningProcess -join ',')" } else { "$p free" }
}
```
