# Teacher Review Workflow

Step-by-step guide for the reviewer to run HotFixAmbulance end-to-end and verify each module.
Designed for a single ~15 min session on Windows + PowerShell 5.1 + Docker Desktop.

> Every step lists **what to do**, **what to observe**, and **what it proves**.

---

## 0 . Prerequisites (one-time, ~30 s to verify)

**Do**
```powershell
dotnet --version    # expect 10.0.300
node   --version    # expect 22.x
npm    --version    # expect 11.x
docker --version    # expect 29.x  (Docker Desktop must be running)
```

**Observe**
- All four print versions, no "command not found".
- Docker Desktop tray icon is green.

**Proves**
- Toolchain matches what the project was built against.

If any line fails: see [scripts/bootstrap.ps1](scripts/bootstrap.ps1) and the [README.md](README.md) "Prerequisites" section.

---

## 1 . Read the pitch (~2 min)

**Do** — open these in order:
1. [README.md](README.md) — what the project does.
2. [Project-assignment.md](Project-assignment.md) — the original brief.
3. [docs/exam.md](docs/exam.md) — Softuni rubric writeup (idea + 9 modules + AI prompts + evidence).
4. [docs/dev-log.md](docs/dev-log.md) — chronological build log.
5. [plan.md](plan.md) — the phased plan.

**Observe**
- The README explains *why* (triage real Serilog/ECS errors, surface AI-generated purpose + Git-based fix hints) in one screen.
- `docs/exam.md` has one row per project module with: approach, workflow, tests, AI tool, two prompts.
- `docs/dev-log.md` rows are tagged with phase (`8.2`, `9.1`, …) and colour (`green`/`yellow`/`red`).

**Proves**
- The project is documented per the rubric, not assembled the night before.

---

## 2 . Verify the test suite (~1 min)

**Do**
```powershell
dotnet test backend/HotFixAmbulance.sln --nologo --verbosity minimal
npm --prefix frontend run test -- --run
npm --prefix frontend run lint
```

**Observe**
- `Test summary: total: 134; failed: 0; succeeded: 134; skipped: 0` (123 unit + 11 integration).
- `Test Files 5 passed (5)   Tests 19 passed (19)`.
- ESLint runs with `--max-warnings=0` and exits cleanly.

**Proves**
- 153 real tests (134 backend + 19 frontend), all green, lint policy enforced.

---

## 3 . Walk the layered architecture (~3 min)

**Do** — open these files in dependency order and skim:

| Layer | Project | Key file |
|---|---|---|
| Domain | Core | [backend/src/HotFixAmbulance.Core/LogEntry.cs](backend/src/HotFixAmbulance.Core/LogEntry.cs) |
| Ingestion | Elastic | [backend/src/HotFixAmbulance.Elastic/SerilogDocumentMapper.cs](backend/src/HotFixAmbulance.Elastic/SerilogDocumentMapper.cs) |
| Analysis | Analysis | [backend/src/HotFixAmbulance.Analysis/HeuristicAnalyzer.cs](backend/src/HotFixAmbulance.Analysis/HeuristicAnalyzer.cs) |
| Git insights | GitInsights | [backend/src/HotFixAmbulance.GitInsights/FixHintBuilder.cs](backend/src/HotFixAmbulance.GitInsights/FixHintBuilder.cs) |
| Persistence | Persistence | [backend/src/HotFixAmbulance.Persistence](backend/src/HotFixAmbulance.Persistence) (EF Core + SQLite) |
| HTTP | Api | [backend/src/HotFixAmbulance.Api/Program.cs](backend/src/HotFixAmbulance.Api/Program.cs) |
| CLI | Cli | [backend/src/HotFixAmbulance.Cli/Program.cs](backend/src/HotFixAmbulance.Cli/Program.cs) |
| UI | frontend | [frontend/src/App.tsx](frontend/src/App.tsx), [frontend/src/components/TriageTable.tsx](frontend/src/components/TriageTable.tsx) |
| Demo | demo-api | [demo-api/Program.cs](demo-api/Program.cs) |
| Infra | infra | [infra/elasticsearch/README.md](infra/elasticsearch/README.md) |

**Observe**
- Each project has a single role; dependencies go one direction only (Core ← everything).
- Tests sit next to the production code under [backend/tests/](backend/tests/).

**Proves**
- Layered, testable architecture; not a monolith.

---

## 4 . Run the end-to-end demo (~3 min)

**Do** — one command:
```powershell
powershell -File scripts/demo.ps1 -WithElastic -KeepRunning
```

**Observe the log lines in order:**
```
[demo] Ensuring Elasticsearch container is up (docker compose up -d)
[+] up 1/1  ✔ Container hfa-es Running
[demo] Waiting for Elasticsearch at http://localhost:9200
[demo] demo-api will write to http://localhost:9200
[demo] Starting demo-api on http://localhost:5333
[demo] Producing error traffic
[demo] 9 erroneous requests sent
[demo] Letting Serilog flush its Elasticsearch sink (8s)
[es-bootstrap] PUT _ingest/pipeline/ecs_to_fields
[es-bootstrap] Refresh logs-yyyy.MM.dd ...
[es-bootstrap] Reindex logs-... -> hfa-mapped via pipeline ecs_to_fields
[es-bootstrap] Reindex: total=N created=N updated=0 failures=0
[es-bootstrap] Bind alias logs-mapped-yyyy.MM.dd -> hfa-mapped
[es-bootstrap] Sanity probe: ... hits(...)=N
[demo] Running CLI: hot-fix-ambulance demo-api --lookback 1h
{
  "Id": "<guid>",
  "ApiName": "demo-api",
  "TotalLogs": <N>,
  "Groups": [ { "Severity": 2, ... "Purpose": "Operation timeout — ..." }, ... ]
}
```

**Proves**
- The whole pipeline runs from one script: ES container, ECS docs → mapper shape via ingest pipeline, CLI triage, grouped output with AI-generated `Purpose` for the timeout group.
- Self-healing: if a stale `hfa-es` container or a leftover demo-api on port 5333 was around, the script removes/kills it and continues.

While it runs, open these in another tab and skim:
- [infra/elasticsearch/docker-compose.yml](infra/elasticsearch/docker-compose.yml)
- [infra/elasticsearch/ecs_to_fields.pipeline.json](infra/elasticsearch/ecs_to_fields.pipeline.json) — the ECS-to-mapper bridge
- [infra/elasticsearch/bootstrap.ps1](infra/elasticsearch/bootstrap.ps1)
- [scripts/demo.ps1](scripts/demo.ps1) — note the `-WithElastic` switch

---

## 5 . Triage via the API and open the UI (~3 min)

The CLI in step 4 wrote to its own SQLite next to the CLI binary. The API uses a different SQLite. So we trigger a fresh analysis through the API so the UI can render it.

### 5a . Start the backend API (terminal A)

```powershell
$env:HFA_Elastic__Uri              = 'http://localhost:9200'
$env:HFA_Apis__ConfigPath          = "$PWD\config\apis.config.json"
$env:HFA_Persistence__ConnectionString = "Data Source=$PWD\hotfix.db"
$env:ASPNETCORE_URLS               = 'http://localhost:5283'
dotnet run --project backend/src/HotFixAmbulance.Api --no-launch-profile
```

**Observe**
- Logs end with `Now listening on: http://localhost:5283`.

### 5b . Start Vite (terminal B)

```powershell
npm --prefix frontend run dev
```

**Observe**
- `VITE v6.4.3  ready in <ms>` and `Local: http://localhost:5173/`.
- [frontend/vite.config.ts](frontend/vite.config.ts) proxies `/api` → `http://localhost:5283`.

### 5c . Trigger a triage + open the UI (terminal C)

```powershell
$r = Invoke-RestMethod -Method POST -Uri 'http://localhost:5283/api/triage/demo-api?lookbackHours=1'
"id=$($r.id)  totalLogs=$($r.totalLogs)  groups=$($r.groups.Count)  fromUtc=$($r.fromUtc)  toUtc=$($r.toUtc)  truncated=$($r.isTruncated)"
Start-Process "http://localhost:5173/?analysisId=$($r.id)&api=demo-api"
```

**Observe in the browser** (top-down)
1. **Run-analysis bar** at the top — `API` dropdown populated from the new `GET /api/apis` endpoint, plus the **TimeRangePicker** (7 presets: 15m / 1h / 6h / 24h / 7d / 30d / Custom; Custom reveals two `datetime-local` inputs in browser local time, converted to UTC on submit; 30-day cap enforced client- and server-side). Click `Run analysis` to POST `/api/triage/demo-api?lookbackHours=…` (or `?fromUtc=…&toUtc=…`) and watch the URL switch to the new `analysisId`.
2. **Header pill** below the title shows `Logs from <fromUtc> → <toUtc> (<duration>)` for the requested window (persisted on `TriageRun`).
3. **Rerun this window** button next to the picker pre-fills the picker with the persisted From/To of the currently loaded run and POSTs the same absolute window — proves Phase 12.E round-trips through Phase 12.C persistence.
4. **Amber `results truncated` badge** appears when Elastic hits `MaxDocuments` (default 10000). With the demo's ~960 docs this stays hidden — that's expected.
5. A sortable, filterable **TanStack Table v8** of error groups. The **Severity** column renders the enum as `Error` / `Warning` / `Fatal` (proves the `JsonStringEnumConverter` wiring).
6. The `/invoices/duplicate` group shows **How-to-fix** populated by `FixHintBuilder` with `Where to fix: demo-api/DemoDatabase.cs:277 - DatabaseFailureSimulator.TriggerDuplicateInvoiceAsync` followed by the offending source snippet (`>>` marker on the offending line) and `blame: <sha> by <author>` — proves `LibGit2SharpHistoryReader.GetLineContextAsync` is mining `origin/main`. Click the three-dot expand on any cell to open the full text in a styled modal (Phase 11.2 ExpandableCell + CellDetailModal).

**Proves**
- The picker → API → Elastic → analysis → DB → React → user flow works end-to-end.
- Configurable time range (Phase 12) is wired everywhere: UI picker, API query params, CLI flags (`--from`/`--to`), SQLite persistence, and the `MaxRangeDays=30` cap from `appsettings.json` (Phase 12.F).
- AI tooling (Suggestion via `SuggestionBuilder`, How-to-fix via `FixHintBuilder` with Git blame) is wired and degrades gracefully when an API is unmapped.

---

## 6 . Show the pre-commit hook blocks bad commits (~1 min)

**Do**
```powershell
# Open the file:
code backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
# Flip ONE assertion: change BeGreaterThan(0) -> BeLessThan(0)
#   on the test 'Compare_Fatal_IsHigherThan_Error'.
# Save.
```

```powershell
git add backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
git commit -m "should fail"
```

**Observe** (from [.claude/hooks/pre-commit.ps1](.claude/hooks/pre-commit.ps1)):
```
Gate          Status
----          ------
dotnet test   FAIL
dotnet format ok
frontend test ok
frontend lint ok
Commit blocked. Fix the failing gate(s) and try again.
```

**Revert cleanly**
```powershell
git restore --staged backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
git checkout --     backend/tests/HotFixAmbulance.UnitTests/Core/SeverityTests.cs
```

**Proves**
- Quality gates (4 of them: `dotnet test`, `dotnet format`, frontend `test`, frontend `lint`) are wired into Git and actually block regressions.

---

## 7 . Inspect the AI-tooling artefacts (~2 min)

**Do** — open:
- [.claude/skills/elastic-query/SKILL.md](.claude/skills/elastic-query/SKILL.md) — canonical Elasticsearch query template.
- [.claude/skills/serilog-mapping/SKILL.md](.claude/skills/serilog-mapping/SKILL.md) — field-name mapping between Serilog property names and `LogEntry`.
- [.claude/skills/tdd-cycle/SKILL.md](.claude/skills/tdd-cycle/SKILL.md) — the 6-step TDD loop the agent is required to follow.
- [.claude/hooks/pre-commit.ps1](.claude/hooks/pre-commit.ps1) — the 4-gate guard.
- [docs/exam.md](docs/exam.md) module sections — list the AI tool used + the two key prompts per module.

**Proves**
- AI was used as a tool, not a crutch: skills package domain knowledge, hooks enforce quality, prompts and tools are logged per module in the exam writeup.

---

## 8 . Tear down (~30 s)

```powershell
# Ctrl+C the API (terminal A) and Vite (terminal B). demo-api (5333) is just a child process.

# Stop the Elasticsearch container (keeps the data volume):
docker compose -f infra/elasticsearch/docker-compose.yml down

# Or wipe everything including indexed logs:
docker compose -f infra/elasticsearch/docker-compose.yml down -v
```

**Observe**
- `Container hfa-es Stopped` and `Removed`.

---

## What the teacher should walk away convinced of

| # | Claim | Evidence step |
|---|---|---|
| 1 | Runs end-to-end with one command | 4 |
| 2 | 153 real tests (134 backend + 19 frontend), enforced on every commit | 2, 6 |
| 3 | Layered, testable architecture (9 projects, single-responsibility) | 3 |
| 4 | Real Elasticsearch + ECS-to-mapper bridge, reproducible from `infra/` | 4 |
| 5 | Backend → SQLite → API → React UI flow works, with configurable time range (picker, Run, Rerun, isTruncated badge, 30-day cap) | 5 |
| 6 | AI augments the workflow (skills, prompts, generated Suggestion + Git-blame How-to-fix) without replacing engineering | 5, 7 |
| 7 | Documented per the Softuni rubric | 1 |

---

## Troubleshooting (only if something breaks)

| Symptom | Fix |
|---|---|
| `docker compose up failed` — name conflict on `hfa-es` | The script auto-removes stale containers since commit `73f507c`. If you skipped that, run `docker rm -f hfa-es` and re-run. |
| `Port 5333 is already in use` | The script auto-stops the listener. If you skipped that, find pid via `Get-NetTCPConnection -State Listen -LocalPort 5333` and `Stop-Process`. |
| API on 5283 won't start with `dotnet run` | A previous instance is locking `HotFixAmbulance.Api.exe`. `Get-Process | Where-Object Path -like '*HotFixAmbulance.Api*' | Stop-Process -Force`. |
| UI shows `/api/... ECONNREFUSED` in Vite logs | The backend API on 5283 is not running. Start it (step 5a). |
| CLI throws `apis config not found` | `$env:HFA_Apis__ConfigPath = "$PWD\config\apis.config.example.json"` before running the CLI. |

---

Repo: <https://github.com/myPOStech/mps-banking-hot-fix-ambulance>
HEAD when this guide was written: `2a98807` (Phase 12.G — configurable analysis time range, shared SQLite schema shim)
