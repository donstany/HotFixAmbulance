# Demo Flow Uses Qwen (end-to-end, visible in the UI) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Supersedes** `2026-06-20-ollama-docker-infra.md` and `2026-06-20-ollama-demo-flow.md`. Same end-to-end goal, but the **user-facing identity is Qwen**, not Ollama. Ollama remains the local runtime under the hood (the `ollama/ollama` image serving `qwen2.5:3b`); everything a developer sees — the UI badge, the infra folder, the `Llm:Provider` value, the demo messages — says **Qwen**.

**Goal:** When a developer runs `scripts/demo.ps1`, the demo brings up the Dockerized Qwen runtime (model pre-pulled), starts the API + frontend wired to the LLM strategy, produces a triage run through the API, asserts the run was analyzed by the LLM, and opens the UI — where a "🤖 Qwen" badge is visible on the AI columns of the analyzed groups.

**Architecture:** Four layers change. (1) **Frontend rebrand:** the already-committed `OllamaBadge` becomes `QwenBadge` ("🤖 Qwen"). (2) **Infra:** a CPU-only Qwen runtime (`infra/qwen`, container `hfa-qwen`, `ollama/ollama` image) + idempotent bootstrap that guarantees `qwen2.5:3b` is pulled. (3) **Backend:** fix the CLI's broken DI (it never registered `IGroupEnricher`, so it throws today) so both CLI and API honor `Analysis:Strategy=Llm`; set `Llm:Provider=Qwen` + model. (4) **Orchestration:** `demo.ps1` always starts the Qwen runtime, sets the LLM `HFA_*` env vars for every child process, starts the API (`:5283`) + frontend (`:5173`), creates the run via `POST /api/triage/demo-api`, hard-asserts `analyzedBy ∈ {Llm, Mixed}`, and opens the UI.

**Tech Stack:** Docker Compose, Ollama runtime (`ollama/ollama`) serving Qwen, PowerShell 5.1, .NET 9 (`HFA_*` env config, double-underscore convention), React + Vite (`:5173` proxies `/api` → `:5283`), Vitest.

## Global Constraints

- **Identity is Qwen; runtime is Ollama.** Do not rename the C# `OllamaLlmClient` class or change the HTTP protocol — it already speaks the Ollama `/api/chat` API the runtime serves. Only user-facing names/labels/config become "Qwen".
- The internal analysis-strategy tag stays `AnalysisStrategyNames.Llm == "Llm"` (it means "the LLM strategy"). The badge **triggers** on `analyzedBy === 'Llm'` and **displays** "Qwen". Do not change the backend strategy name or its persisted value.
- Model is `qwen2.5:3b`; CPU-only (no GPU reservation).
- Fixed ports: Qwen runtime `11434`, API `5283`, frontend `5173` (set by `vite.config.ts` proxy + API `launchSettings.json`).
- The badge renders only for groups whose `analyzedBy === 'Llm'`, produced only by `LlmGroupEnricher` on a *successful* model call (it falls back to `Heuristic` on any failure). The demo's guarantee = assert the API-produced run's `analyzedBy` is `Llm` or `Mixed`.
- Re-runnable / idempotent: compose `up -d` no-ops if running; model pull skipped if present; processes on busy ports reclaimed before relaunch.
- The checked-in `appsettings.json` keeps `Analysis:Strategy=Heuristic` (safe, runtime-free for unit tests / manual non-demo runs). The demo flips to `Llm` via env vars only.

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Rename+Modify | `frontend/src/components/OllamaBadge.tsx` → `QwenBadge.tsx` | "🤖 Qwen" badge, `data-testid="qwen-badge"` |
| Modify | `frontend/src/components/TriageTable.tsx` | Import + render `QwenBadge` |
| Modify | `frontend/src/components/TriageTable.test.tsx` | `qwen-badge` testid + test names |
| Create | `infra/qwen/docker-compose.yml` | CPU-only Qwen runtime on `:11434`, model volume, healthcheck |
| Create | `infra/qwen/bootstrap.ps1` | Wait for runtime, idempotently pull `qwen2.5:3b`, `/api/chat` JSON probe |
| Create | `infra/qwen/README.md` | Usage docs |
| Modify | `backend/src/HotFixAmbulance.Cli/Program.cs` | Register `AddHotFixGroupEnrichment` (fixes the `IGroupEnricher` DI break; enables `Strategy=Llm`) |
| Modify | `backend/src/HotFixAmbulance.Api/appsettings.json` | `Llm:Provider` → `Qwen`, `Llm:Model` → `qwen2.5:3b` |
| Modify | `scripts/demo.ps1` | Always start Qwen runtime; start API + frontend with LLM env; produce run via API; assert `analyzedBy`; open UI |
| Modify | `scripts/README.md` | Document the Qwen demo flow |

---

## Task 1: Rebrand the committed badge Ollama → Qwen

**Files:**
- Rename + Modify: `frontend/src/components/OllamaBadge.tsx` → `frontend/src/components/QwenBadge.tsx`
- Modify: `frontend/src/components/TriageTable.tsx`
- Modify: `frontend/src/components/TriageTable.test.tsx`

**Background:** an earlier commit added `OllamaBadge` (text "Ollama", `data-testid="ollama-badge"`) rendered in the Suggestion / How-to-fix cells when `analyzedBy === 'Llm'`. This task renames it to Qwen. The trigger condition is unchanged.

**Interfaces:**
- Produces: `QwenBadge` component, `data-testid="qwen-badge"`. Consumed by `TriageTable.tsx`.

---

- [ ] **Step 1: Update the tests to the new name (red)**

In `frontend/src/components/TriageTable.test.tsx`, replace the three badge tests at the end of the suite.

Before:
```tsx
  it('renders the Ollama badge on both AI columns when analyzedBy is Llm', () => {
    renderTable({ groups: [groupWith({ analyzedBy: 'Llm' })] });
    expect(screen.getAllByTestId('ollama-badge')).toHaveLength(2);
  });

  it('does not render the Ollama badge when analyzedBy is Heuristic', () => {
    renderTable({ groups: [groupWith({ analyzedBy: 'Heuristic' })] });
    expect(screen.queryByTestId('ollama-badge')).not.toBeInTheDocument();
  });

  it('does not render the Ollama badge when analyzedBy is null', () => {
    renderTable({ groups: [groupWith({ analyzedBy: null })] });
    expect(screen.queryByTestId('ollama-badge')).not.toBeInTheDocument();
  });
```

After:
```tsx
  it('renders the Qwen badge on both AI columns when analyzedBy is Llm', () => {
    renderTable({ groups: [groupWith({ analyzedBy: 'Llm' })] });
    expect(screen.getAllByTestId('qwen-badge')).toHaveLength(2);
  });

  it('does not render the Qwen badge when analyzedBy is Heuristic', () => {
    renderTable({ groups: [groupWith({ analyzedBy: 'Heuristic' })] });
    expect(screen.queryByTestId('qwen-badge')).not.toBeInTheDocument();
  });

  it('does not render the Qwen badge when analyzedBy is null', () => {
    renderTable({ groups: [groupWith({ analyzedBy: null })] });
    expect(screen.queryByTestId('qwen-badge')).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx vitest run src/components/TriageTable.test.tsx`
Expected: the "renders the Qwen badge" test FAILS (`qwen-badge` not found — the component still emits `ollama-badge`).

- [ ] **Step 3: Create `QwenBadge.tsx` and delete `OllamaBadge.tsx`**

Create `frontend/src/components/QwenBadge.tsx`:

```tsx
import { Bot } from 'lucide-react';

/**
 * Small badge shown on AI columns when the group was analyzed by the LLM strategy
 * (analyzedBy === 'Llm'). The model is Qwen (served locally by the Ollama runtime).
 */
export function QwenBadge() {
  return (
    <span
      data-testid="qwen-badge"
      className="mb-1 inline-flex items-center gap-1 rounded-full bg-violet-100 px-1.5 py-0.5 text-[10px] font-medium text-violet-700 ring-1 ring-violet-300"
      title="Analysis generated by the Qwen model (qwen2.5:3b)"
    >
      <Bot size={10} aria-hidden="true" />
      Qwen
    </span>
  );
}
```

Then delete the old file:
```
git rm frontend/src/components/OllamaBadge.tsx
```

- [ ] **Step 4: Update `TriageTable.tsx` import + usages**

In `frontend/src/components/TriageTable.tsx`:

Replace the import:
```tsx
import { OllamaBadge } from './OllamaBadge';
```
with:
```tsx
import { QwenBadge } from './QwenBadge';
```

Replace **both** cell occurrences:
```tsx
                  {i.row.original.analyzedBy === 'Llm' && <OllamaBadge />}
```
with:
```tsx
                  {i.row.original.analyzedBy === 'Llm' && <QwenBadge />}
```
(There are two — in the `suggestion` cell and the `howToFix` cell. Use replace-all.)

- [ ] **Step 5: Run the full frontend suite (green)**

Run: `cd frontend && npx vitest run`
Expected: all tests pass, including the three renamed Qwen-badge tests.

- [ ] **Step 6: Commit**

```
git add frontend/src/components/QwenBadge.tsx frontend/src/components/OllamaBadge.tsx frontend/src/components/TriageTable.tsx frontend/src/components/TriageTable.test.tsx
git commit -m "refactor(ui): rebrand the LLM badge from Ollama to Qwen"
```

---

## Task 2: Qwen runtime docker-compose service + README

**Files:**
- Create: `infra/qwen/docker-compose.yml`
- Create: `infra/qwen/README.md`

**Interfaces:**
- Produces: Compose project `hfa-qwen`, container `hfa-qwen` on `localhost:11434`, model data in named volume `hfa-qwen-data`. Consumed by Tasks 3 and 6.

---

- [ ] **Step 1: Write the compose file**

Create `infra/qwen/docker-compose.yml`:

```yaml
# Single-node CPU-only Qwen runtime for local HotFixAmbulance LLM-strategy demos.
#
# Qwen (qwen2.5:3b) is served by the Ollama runtime image, which exposes the
# /api/chat endpoint HotFixAmbulance's OllamaLlmClient already speaks. Local
# development only (http://localhost:11434). Do NOT expose to a network.
#
# Usage:
#   docker compose -f infra/qwen/docker-compose.yml up -d
#   docker compose -f infra/qwen/docker-compose.yml down            # keeps the model volume
#   docker compose -f infra/qwen/docker-compose.yml down -v         # wipes the model volume
#
# The container starts with NO models. infra/qwen/bootstrap.ps1 pulls qwen2.5:3b
# into the hfa-qwen-data volume.

name: hfa-qwen

services:
  qwen:
    image: ollama/ollama:latest
    container_name: hfa-qwen
    ports:
      - "11434:11434"
    volumes:
      - hfa-qwen-data:/root/.ollama
    healthcheck:
      # `ollama list` exits non-zero until the runtime is accepting requests.
      test: ["CMD", "ollama", "list"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 10s

volumes:
  hfa-qwen-data:
    name: hfa-qwen-data
```

- [ ] **Step 2: Validate the compose file parses**

Run: `docker compose -f infra/qwen/docker-compose.yml config`
Expected: prints resolved config (service `qwen`, volume `hfa-qwen-data`), exit 0.

- [ ] **Step 3: Bring it up and confirm healthy**

Run: `docker compose -f infra/qwen/docker-compose.yml up -d`
Poll: `docker inspect --format='{{.State.Health.Status}}' hfa-qwen`
Expected: within ~30s prints `healthy`.

- [ ] **Step 4: Confirm the runtime answers on the host port**

Run (PowerShell): `(Invoke-WebRequest -Uri http://localhost:11434/api/tags -UseBasicParsing).StatusCode`
Expected: `200`.

- [ ] **Step 5: Write the README**

Create `infra/qwen/README.md`:

```markdown
# Local Qwen runtime for HotFixAmbulance

Runs **Qwen** (`qwen2.5:3b`) locally, CPU-only, so the demo can exercise
HotFixAmbulance's LLM analysis strategy (`Analysis:Strategy=Llm`) and show the
"🤖 Qwen" badge in the UI. Qwen is served by the [Ollama](https://ollama.com)
runtime image, which exposes the `/api/chat` endpoint `OllamaLlmClient` speaks.
Not suitable for anything else.

## Files

| File | Purpose |
|---|---|
| `docker-compose.yml` | CPU-only Qwen runtime on `http://localhost:11434`, models in the `hfa-qwen-data` named volume. |
| `bootstrap.ps1` | Idempotent: waits for the runtime, pulls `qwen2.5:3b` if absent, runs a `/api/chat` JSON sanity probe. |

## Why a bootstrap step exists

The runtime container starts with **no models**. It answers on `:11434`
immediately, but `OllamaLlmClient` gets a 404 until `qwen2.5:3b` is pulled.
`bootstrap.ps1` pulls it into the volume so it survives `down`/`up`.

## Typical usage

```powershell
# Started automatically by scripts/demo.ps1. To run it standalone:
docker compose -f infra/qwen/docker-compose.yml up -d
powershell -File infra/qwen/bootstrap.ps1

# Tear down (keeps the model volume):
docker compose -f infra/qwen/docker-compose.yml down
# Tear down (wipes the model volume — next run re-downloads ~2GB):
docker compose -f infra/qwen/docker-compose.yml down -v
```

## Running `bootstrap.ps1` directly

```powershell
powershell -File infra/qwen/bootstrap.ps1                       # default model qwen2.5:3b
powershell -File infra/qwen/bootstrap.ps1 -Model qwen2.5:7b     # a larger Qwen
powershell -File infra/qwen/bootstrap.ps1 -OllamaUri http://localhost:11434
```

The script prints `chat probe: ok` on success. On a pull failure check
`docker logs hfa-qwen` and that Docker Desktop has enough disk.
```

- [ ] **Step 6: Commit**

```
git add infra/qwen/docker-compose.yml infra/qwen/README.md
git commit -m "feat(infra): add CPU-only Qwen runtime docker-compose service + README"
```

---

## Task 3: Qwen bootstrap script (model pull + sanity probe)

**Files:**
- Create: `infra/qwen/bootstrap.ps1`

**Interfaces:**
- Consumes: the running `hfa-qwen` container (Task 2).
- Produces: `infra/qwen/bootstrap.ps1` with params `-OllamaUri` (default `http://localhost:11434`), `-Model` (default `qwen2.5:3b`), `-WaitSeconds` (default `60`). Invoked by `demo.ps1` (Task 6).

---

- [ ] **Step 1: Write the bootstrap script**

Create `infra/qwen/bootstrap.ps1`:

```powershell
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
```

- [ ] **Step 2: Run the bootstrap against the running container**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File infra/qwen/bootstrap.ps1`
Expected: `Runtime is up.`, pulls `qwen2.5:3b` (progress), `chat probe: ok`, `done.` Exit 0. (First run is minutes for the ~2GB download.)

- [ ] **Step 3: Verify the model is listed**

Run (PowerShell): `(Invoke-RestMethod http://localhost:11434/api/tags).models.name`
Expected: contains `qwen2.5:3b`.

- [ ] **Step 4: Verify idempotency**

Run the bootstrap again.
Expected: `Model 'qwen2.5:3b' already present -- skipping pull.` then `chat probe: ok`. Exit 0.

- [ ] **Step 5: Commit**

```
git add infra/qwen/bootstrap.ps1
git commit -m "feat(infra): add idempotent Qwen bootstrap (pull qwen2.5:3b + chat probe)"
```

---

## Task 4: Fix the CLI's broken enrichment DI (and enable Strategy=Llm)

**Files:**
- Modify: `backend/src/HotFixAmbulance.Cli/Program.cs`

**Background (verified):** running the CLI today fails with
`Unable to resolve service for type 'HotFixAmbulance.Api.IGroupEnricher' while attempting to activate 'HotFixAmbulance.Api.TriageService'.`
The Phase L.5 enrichment seam was never wired into the CLI. `AddHotFixGroupEnrichment` (in the `HotFixAmbulance.Api` assembly the CLI already references) registers both the `IGroupEnricher` and, internally, `AddHotFixLlm`, and selects `LlmGroupEnricher` when `Analysis:Strategy=Llm`.

**Interfaces:**
- Consumes: `HotFixAmbulance.Api.GroupEnrichmentServiceCollectionExtensions.AddHotFixGroupEnrichment(IServiceCollection, IConfiguration)`.
- Produces: a CLI that resolves `TriageService` and honors `Analysis:Strategy`.

---

- [ ] **Step 1: Verify the failing behavior (baseline)**

Run (PowerShell, repo root):
```
$env:HFA_Apis__ConfigPath = (Resolve-Path config/apis.config.json).Path
dotnet run --project backend/src/HotFixAmbulance.Cli -- demo-api --lookback 1h --format json --no-open
```
Expected: exit 1 and `Unable to resolve service for type 'HotFixAmbulance.Api.IGroupEnricher'`.

- [ ] **Step 2: Register group enrichment in the CLI**

In `backend/src/HotFixAmbulance.Cli/Program.cs`:

Before:
```csharp
builder.Services.AddSingleton<IAnalysisStrategy, HeuristicAnalyzer>();

var apisConfigPath = configuration["Apis:ConfigPath"];
```

After:
```csharp
builder.Services.AddSingleton<IAnalysisStrategy, HeuristicAnalyzer>();

// Group enrichment fills the two AI columns; the active strategy is chosen from
// Analysis:Strategy (Llm -> LlmGroupEnricher via the Qwen runtime, else the git heuristic).
// Registers IGroupEnricher (required by TriageService) and, internally, AddHotFixLlm.
builder.Services.AddHotFixGroupEnrichment(configuration);

var apisConfigPath = configuration["Apis:ConfigPath"];
```

- [ ] **Step 3: Build the CLI**

Run: `dotnet build backend/src/HotFixAmbulance.Cli/HotFixAmbulance.Cli.csproj --nologo --verbosity minimal`
Expected: build succeeds, exit 0.

- [ ] **Step 4: Verify the CLI now runs (heuristic path, no runtime needed)**

Run (PowerShell, repo root):
```
$env:HFA_Apis__ConfigPath = (Resolve-Path config/apis.config.json).Path
Remove-Item Env:HFA_Analysis__Strategy -ErrorAction SilentlyContinue
dotnet run --project backend/src/HotFixAmbulance.Cli --no-build -- demo-api --lookback 1h --format json --no-open
```
Expected: exit 0; JSON prints with an `"analyzedBy"` field; no DI exception.

- [ ] **Step 5: Commit**

```
git add backend/src/HotFixAmbulance.Cli/Program.cs
git commit -m "fix(cli): register group enrichment so TriageService resolves (and honor Strategy=Llm)"
```

---

## Task 5: Point the API at Qwen (provider + model)

**Files:**
- Modify: `backend/src/HotFixAmbulance.Api/appsettings.json`

**Interfaces:**
- Produces: the API's default `Llm` section names Qwen as the provider and `qwen2.5:3b` as the model (used when the env override is absent — e.g. manual `dotnet run` of the API outside the demo).

---

- [ ] **Step 1: Change provider + model**

In `backend/src/HotFixAmbulance.Api/appsettings.json`:

Before:
```json
  "Llm": {
    "Provider": "Ollama",
    "Endpoint": "http://localhost:11434",
    "Model": "llama3.1",
    "TimeoutSeconds": 30
  },
```

After:
```json
  "Llm": {
    "Provider": "Qwen",
    "Endpoint": "http://localhost:11434",
    "Model": "qwen2.5:3b",
    "TimeoutSeconds": 30
  },
```

- [ ] **Step 2: Verify the JSON still parses**

Run (PowerShell): `Get-Content backend/src/HotFixAmbulance.Api/appsettings.json -Raw | ConvertFrom-Json | Out-Null; 'json ok'`
Expected: `json ok`, exit 0.

- [ ] **Step 3: Verify backend unit tests still pass**

Run: `dotnet test backend/tests/HotFixAmbulance.UnitTests --nologo`
Expected: all pass. (`LlmOptions` defaults live in code; this is config only — no test should assert the `appsettings.json` literal. If one does, update it to `Qwen`/`qwen2.5:3b` in this step.)

- [ ] **Step 4: Commit**

```
git add backend/src/HotFixAmbulance.Api/appsettings.json
git commit -m "chore(api): default Llm provider to Qwen (qwen2.5:3b)"
```

---

## Task 6: Orchestrate the full Qwen demo in `demo.ps1` (+ README)

**Files:**
- Modify: `scripts/demo.ps1`
- Modify: `scripts/README.md`

**Interfaces:**
- Consumes: `infra/qwen/docker-compose.yml`, `infra/qwen/bootstrap.ps1`, API on `:5283`, frontend on `:5173`, `POST /api/triage/{apiName}` returning a `TriageRunHeader` with `id` and `analyzedBy`.
- Produces: a `demo.ps1` that, by default, runs the entire flow on Qwen and opens the UI on a run whose `analyzedBy` is asserted to be `Llm`/`Mixed`. New params: `-LlmModel` (default `qwen2.5:3b`), `-SkipLlm` (escape hatch to the heuristic flow).

**Note on placement:** line numbers refer to the current `scripts/demo.ps1`. Apply edits in order; later anchors are text-based and survive earlier insertions.

---

- [ ] **Step 1: Add new parameters**

In `scripts/demo.ps1` `param(...)`, after the `$ElasticUri` line:

Before:
```powershell
    [switch]$WithElastic,
    [string]$ElasticUri = 'http://localhost:9200',
    [bool]$WithMssql = $true,
```

After:
```powershell
    [switch]$WithElastic,
    [string]$ElasticUri = 'http://localhost:9200',
    [string]$LlmModel = 'qwen2.5:3b',
    [switch]$SkipLlm,
    [bool]$WithMssql = $true,
```

- [ ] **Step 2: Add a `Wait-Http` helper**

In `scripts/demo.ps1`, after the `Test-PortFree` function:

Before:
```powershell
function Test-PortFree([int]$port) {
    try { (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop) | Out-Null; return $false }
    catch { return $true }
}
```

After:
```powershell
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
```

- [ ] **Step 3: Start the Qwen runtime + enable the LLM strategy (before the MSSQL block)**

In `scripts/demo.ps1`, after the helper/`Set-Location $repoRoot` setup and **before** `if ($WithMssql) {`, insert:

```powershell
if (-not $SkipLlm) {
    Write-Step 'Ensuring Qwen runtime container is up (docker compose up -d)'
    $qwenCompose = Join-Path $repoRoot 'infra/qwen/docker-compose.yml'
    if (-not (Test-Path $qwenCompose)) { throw "compose file not found: $qwenCompose" }
    & docker compose -f $qwenCompose up -d
    if ($LASTEXITCODE -ne 0) { throw 'qwen docker compose up failed (is Docker Desktop running?)' }

    $qwenBootstrap = Join-Path $repoRoot 'infra/qwen/bootstrap.ps1'
    if (-not (Test-Path $qwenBootstrap)) { throw "bootstrap script missing: $qwenBootstrap" }
    Write-Step "Bootstrapping Qwen (pull $LlmModel if absent — first run downloads ~2GB)"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $qwenBootstrap -Model $LlmModel
    if ($LASTEXITCODE -ne 0) { throw "qwen bootstrap exited with $LASTEXITCODE" }

    # Every child process (demo-api, API, CLI) inherits these — they honour HFA_*.
    $env:HFA_Analysis__Strategy = 'Llm'
    $env:HFA_Llm__Provider = 'Qwen'
    $env:HFA_Llm__Endpoint = 'http://localhost:11434'
    $env:HFA_Llm__Model = $LlmModel
    Write-Step "LLM strategy enabled: provider=Qwen model=$LlmModel endpoint=http://localhost:11434"
} else {
    Write-Step 'LLM disabled (-SkipLlm): running the heuristic flow'
}
```

- [ ] **Step 4: After the CLI step, start the API + frontend and produce/verify a run via the API**

In `scripts/demo.ps1`, find the CLI block at the end of the `try { ... }` body.

Before:
```powershell
    if (-not $SkipBackend) {
        Write-Step "Running CLI: hot-fix-ambulance $ApiName --lookback ${LookbackHours}h"
        & dotnet run --project backend/src/HotFixAmbulance.Cli -- $ApiName --lookback "${LookbackHours}h" --format json --no-open
        $exit = $LASTEXITCODE
        Write-Step "CLI exited with $exit"
    }
}
```

After:
```powershell
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
    $header = Invoke-RestMethod -Method POST -Uri "http://localhost:5283/api/triage/$ApiName`?lookbackHours=$LookbackHours" -TimeoutSec 180
    Write-Step ("API run id={0} analyzedBy={1} groups={2}" -f $header.id, $header.analyzedBy, $header.totalGroups)

    if (-not $SkipLlm) {
        if ($header.analyzedBy -notin @('Llm', 'Mixed')) {
            throw "Demo guarantee failed: expected analyzedBy 'Llm'/'Mixed' but got '$($header.analyzedBy)'. Qwen was not used — check 'docker logs hfa-qwen' and that '$LlmModel' is pulled."
        }
        Write-Step "Verified: this run was analyzed by Qwen (analyzedBy=$($header.analyzedBy)). The UI shows the 🤖 Qwen badge."
    }

    $uiUrl = "http://localhost:5173/?analysisId=$($header.id)&api=$ApiName"
    Write-Step "Opening UI: $uiUrl"
    Start-Process $uiUrl
}
```

- [ ] **Step 5: Keep the API + frontend alive in `finally`**

In `scripts/demo.ps1` `finally { ... }`:

Before:
```powershell
finally {
    if (-not $KeepRunning -and $demoProcess -and -not $demoProcess.HasExited) {
        Write-Step 'Stopping demo-api'
        Stop-Process -Id $demoProcess.Id -Force -ErrorAction SilentlyContinue
    }
    elseif ($KeepRunning) {
        Write-Step "demo-api still running (PID $($demoProcess.Id)). Stop it with: Stop-Process -Id $($demoProcess.Id)"
    }
}
```

After:
```powershell
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
```

- [ ] **Step 6: Update the comment header (.SYNOPSIS / params / examples)**

In `scripts/demo.ps1`:

Before:
```powershell
.SYNOPSIS
  End-to-end HotFixAmbulance demo. Starts demo-api, hammers its endpoints to produce errors,
  then runs the CLI to triage them and prints the analysis id and React UI URL.
```

After:
```powershell
.SYNOPSIS
  End-to-end HotFixAmbulance demo on Qwen. Starts the Dockerized Qwen runtime (model
  pre-pulled), demo-api, the API (:5283) and frontend (:5173) wired to Analysis:Strategy=Llm,
  produces a triage run through the API, asserts it was analyzed by the LLM, and opens the UI
  where the "🤖 Qwen" badge is visible. Pass -SkipLlm for the old heuristic-only flow.

.PARAMETER LlmModel
  Qwen model tag pulled and used. Defaults to qwen2.5:3b.

.PARAMETER SkipLlm
  Escape hatch: skip Qwen and run the heuristic flow (no badge, no Docker LLM dependency).
```

And add an example after the existing `-WithElastic -KeepRunning` example:
```powershell
  powershell -File scripts/demo.ps1 -WithElastic -KeepRunning      # full flow on Qwen
```

- [ ] **Step 7: Validate `demo.ps1` parses**

Run (PowerShell):
```
powershell -NoProfile -Command "$e=$null; $null=[System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path scripts/demo.ps1),[ref]$null,[ref]$e); if($e){$e;exit 1}else{'parse ok'}"
```
Expected: `parse ok`, exit 0.

- [ ] **Step 8: Update `scripts/README.md`**

Before:
```markdown
- `demo.ps1` — end-to-end runner: starts Dockerized MSSQL (`infra/mssql`), starts `demo-api`, hammers endpoints to produce Elastic logs, launches `HotFixAmbulance.Api` + `frontend`, invokes the CLI, captures screenshots.
```

After:
```markdown
- `demo.ps1` — end-to-end runner **on Qwen by default**: starts the Dockerized Qwen runtime (`infra/qwen`, model pre-pulled) + MSSQL (`infra/mssql`), starts `demo-api`, hammers endpoints to produce Elastic logs, launches `HotFixAmbulance.Api` (:5283) + `frontend` (:5173) wired to `Analysis:Strategy=Llm`, creates a triage run through the API, asserts it was analyzed by the LLM, and opens the UI showing the "🤖 Qwen" badge. Pass `-SkipLlm` for the old heuristic-only flow.
```

- [ ] **Step 9: End-to-end verification**

Run: `powershell -File scripts/demo.ps1 -WithElastic -KeepRunning`
Expected: the script brings up the Qwen runtime (pull/skip), MSSQL, Elasticsearch, demo-api; starts the API + frontend; prints `Verified: this run was analyzed by Qwen (analyzedBy=Llm)` (or `Mixed`); opens the browser at `http://localhost:5173/?analysisId=...`. In the UI the "🤖 Qwen" badge appears on the Suggestion / How-to-fix columns of the analyzed groups. If `analyzedBy` came back `Heuristic`, the script throws "Demo guarantee failed" — diagnose the Qwen runtime first.

- [ ] **Step 10: Commit**

```
git add scripts/demo.ps1 scripts/README.md
git commit -m "feat(demo): run the whole demo on Qwen and prove it with the UI badge"
```

---

## Self-Review

**Spec coverage ("use Qwen instead of Ollama; whole demo flow uses it; guarantee it shows; all affected places"):**
- ✅ Identity is Qwen everywhere visible — badge (Task 1), infra folder/container (Task 2/3), `Llm:Provider` (Task 5), demo messages (Task 6).
- ✅ Ollama kept only as the hidden runtime — `ollama/ollama` image + unchanged `OllamaLlmClient`.
- ✅ Whole flow on Qwen by default — `demo.ps1` sets `Strategy=Llm` for every child process; `-SkipLlm` is opt-out.
- ✅ Model guaranteed present before app start — Task 3 + `demo.ps1` runs bootstrap before launching anything (Task 6 Step 3).
- ✅ CLI honors the strategy (and no longer crashes) — Task 4 fixes the `IGroupEnricher` DI break.
- ✅ API produces the run the UI shows, with `analyzedBy=Llm` → badge — Task 6 Step 4 hard-asserts `analyzedBy ∈ {Llm, Mixed}` and opens the UI.
- ✅ Model `qwen2.5:3b`, CPU-only — Tasks 2, 3, 5.

**All affected places enumerated:** frontend badge (rename + table + tests), Qwen infra (compose/bootstrap/README), CLI DI (Program.cs), API config (appsettings.json), orchestration (demo.ps1), docs (scripts/README.md). The model/provider literal appears in one checked-in JSON (`appsettings.json`, Task 5); everywhere else they are supplied via `HFA_*` env in `demo.ps1`, read by both the CLI (`Program.cs:26`) and API (`Program.cs:14`).

**Placeholder scan:** none — all YAML/PowerShell/TSX/JSON blocks are complete.

**Name/type consistency:** container `hfa-qwen`, volume `hfa-qwen-data`, model `qwen2.5:3b`, ports `11434`/`5283`/`5173`, env vars `HFA_Analysis__Strategy` / `HFA_Llm__Provider` / `HFA_Llm__Endpoint` / `HFA_Llm__Model`, badge `data-testid="qwen-badge"` triggering on `analyzedBy === 'Llm'`, and `bootstrap.ps1 -Model $LlmModel` are used identically across tasks. `TriageRunHeader.analyzedBy` (the asserted field) matches the backend record and the `ToHeader` projection in `Api/Program.cs:215`.

## Risks / Notes

- **Why the strategy tag stays "Llm":** `AnalysisStrategyNames.Llm` is the internal *strategy* name and is persisted in old runs and asserted by existing tests. Renaming it to "Qwen" would be a data + test migration for no user-visible gain — the badge already displays "Qwen" while keying off "Llm". If you want the persisted tag itself to read "Qwen", that's a separate, larger change (backend record + tests + serialized history) — call it out and it can be planned.
- **First run is slow:** `qwen2.5:3b` is ~2GB (one-time); CPU inference over N groups can take a while; the API POST uses a 180s timeout.
- **SQLite isolation:** the CLI keeps its own `hotfix.db`; the run shown in the UI is produced by the **API** (which owns its DB), so there is no cross-process SQLite locking.
- **`analyzedBy=Mixed`** counts as success: groups Qwen answered show the badge even if others fell back — still proves Qwen is in use.
