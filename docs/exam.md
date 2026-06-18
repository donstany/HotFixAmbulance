# HotFixAmbulance — AI-Assisted Development Exam

**Author:** Stanislav Stanev
**Course:** Softuni — AI-Assisted Development
**Repository:** [github.com/myPOStech/mps-banking-hot-fix-ambulance](https://github.com/myPOStech/mps-banking-hot-fix-ambulance)
**Date:** 2026-06-16

---

## 1. Idea & system requirements *(1 page)*

**HotFixAmbulance** is a Claude Code slash command `/hot-fix-ambulance <apiName>` that triages production errors for a myPos .NET Web API end-to-end:

1. Pulls the last 24h of error logs from the corporate **Elasticsearch** cluster for the API.
2. **Groups** logs by `(ExceptionType, NormalizedMessage, Endpoint)` and **ranks them by severity** (Fatal > Error > Warning).
3. **Clones-on-demand** the related git repo via a configurable base URL (`config/apis.config.json`) and mines `origin/main` history for **"how-to-fix" hints** that match the error keywords / changed files.
4. **Opens a React 19 UI** showing a **12-column triage table** (10 raw log facts + an AI-derived *Purpose* column + an AI-derived *How-to-Fix* column).

### Why it matters
On-call engineers waste 10–20 minutes per incident piecing together "which logs, which commit fixed this last time, which file is to blame". This tool collapses that into one slash command.

### Hard requirements
- **Strict TDD** (test → fail → implement → green → refactor → commit). Enforced by a `pre-commit` hook that blocks the commit if `dotnet test`, `dotnet format`, `frontend test`, or `frontend lint` fails.
- **AI-driven authoring proof**: `.claude/agents/`, `.claude/skills/`, `.claude/hooks/` are first-class repo artifacts. Subagents (`test-author`, `log-analyzer`, `git-historian`) and skills (`tdd-cycle`, `elastic-query`, `serilog-mapping`) are version-controlled.
- **Backend**: .NET 10 SDK, C# latest, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`.
- **Frontend**: React 19 + Vite 6 + TypeScript + Tailwind 3 + TanStack Table v8 + TanStack Query v5.
- **Commits remain local** to the corporate repo (no `gh pr create` for production data).

### Top-level architecture

```
Claude Code  /hot-fix-ambulance demo-api
      │
      ▼
backend/src/HotFixAmbulance.Cli  ──► TriageService (Api project)
                                          │
            ┌─────────────────────────────┼───────────────────────────────┐
            ▼                             ▼                               ▼
   Elastic (ElasticLogIngestor)   Analysis (HeuristicAnalyzer)    GitInsights (FixHintBuilder)
            │                             │                               │
            └────────────► ErrorGroup[] + Purpose + HowToFix ◄─────────────┘
                                          │
                                          ▼
                                 Persistence (EF Core + SQLite)
                                          │
                                          ▼
                        Api project (Minimal API) ──► Frontend (React 19, /?analysisId=)
```

---

## 2. Modules

The system was decomposed into **nine technological modules** that map 1:1 to phases in [plan.md](../plan.md). Numbers in parentheses are the phase IDs.

| # | Module | Purpose |
|---|--------|---------|
| 0 | **Scaffold & AI plumbing** (Phase 0) | Repo layout, `.claude/` agents/skills/hooks, bootstrap script, dev-log. |
| 1 | **Core domain** (Phase 1) | `LogEntry`, `Severity`, `ErrorGroup` records — the shared vocabulary. |
| 2 | **Elastic ingestion** (Phase 2) | `ElasticsearchLogSource` + `ElasticLogIngestor` with Polly retries, `search_after` paging, `SerilogDocumentMapper`. |
| 3 | **Heuristic analysis** (Phase 3) | `MessageNormalizer`, `HeuristicAnalyzer`, `DefaultRules`, `LlmAnalysisStrategy` stub. |
| 4 | **GitInsights** (Phase 4) | `LibGit2SharpRepoCache`, `LibGit2SharpHistoryReader`, `FixHintBuilder`, `ApisConfig`. |
| 5 | **Persistence + Minimal API** (Phase 5) | EF Core 9 / SQLite, `TriageRun`, `TriageRunRepository`, ASP.NET Core Minimal API. |
| 6 | **CLI + slash command** (Phase 6) | `CliArgs` parser, `CliRenderer`, in-process Host wiring, `.claude/commands/hot-fix-ambulance.md`. |
| 7 | **React frontend** (Phase 7) | Vite + React 19 + TanStack Table v8 + Query v5 + Tailwind, 12-column `TriageTable`. |
| 8 | **demo-api + orchestrator** (Phase 8) | .NET 10 Web API with Serilog + ECS + Elastic sink and 3 instrumented endpoints; `scripts/demo.ps1`. |

The next section walks through each module with the same template: **Approach → Workflow → Tests → AI tool & key prompts**.

---

## 3. Development process per module

### Module 0 — Scaffold & AI plumbing
**Approach.** Build the AI-customization surface *first* so every later phase is forced to go through TDD via the pre-commit hook. Wrote `.claude/skills/tdd-cycle/SKILL.md` (mandatory 6-step cycle), `.claude/agents/{test-author,log-analyzer,git-historian}.md`, and `.claude/hooks/pre-commit.ps1`.

**Workflow.**
1. `scripts/bootstrap.ps1` installs the pre-commit hook as `.git/hooks/pre-commit`.
2. `.claude/commands/hot-fix-ambulance.md` defines the slash-command contract: `<apiName>` is required, `--lookback=24h`, `--no-open` flags, mandatory invocation of `log-analyzer` subagent on the JSON.

**Tests.** The hook itself was tested by intentionally introducing failures and confirming the commit was blocked.

**AI tool & prompts.**
- **Claude Code** (Sonnet 4.5) for the whole repo, because its agent/skill/hook surface *is* the deliverable.
- Prompt 1: *"Read plan.md Phase 0. Create the .claude/ tree per the listed file names. Hook must be idempotent and opt-out per gate via env vars."*
- Prompt 2: *"Write `.claude/skills/tdd-cycle/SKILL.md` formalizing the 6-step cycle (red → green → refactor → format → log → commit) used by every subsequent story."*

---

### Module 1 — Core domain
**Approach.** Pure record types — no I/O, no logging — so every other module can depend on them safely. Made them `sealed record` with `init`-only properties.

**Workflow.**
1. `test-author` subagent wrote 16 failing tests covering `Severities.TryParse`, `LogEntry` defaults, `ErrorGroup.FromLogs` aggregation, and `ErrorGroup.RankBySeverity`.
2. Implemented the three records and helpers.

**Tests.** 16 unit tests (xUnit + FluentAssertions). All passing.

**AI tool & prompts.**
- **Claude Code** with the `test-author` subagent.
- Prompt: *"Read .claude/skills/serilog-mapping/SKILL.md. Author the failing tests for LogEntry, Severity, and ErrorGroup.FromLogs that lock in the field set and severity ordering Fatal > Error > Warning."*

---

### Module 2 — Elastic ingestion
**Approach.** Use `Elastic.Clients.Elasticsearch` v8 with `search_after` paging, Polly retries on transport faults, and a pluggable `IElasticLogSource` so the analyzer can be tested with a substitute.

**Workflow.**
1. Failing tests for `SerilogDocumentMapper.TryMap` against synthetic `JsonElement` documents (timestamp formats, ECS-style `fields.Application`, missing required fields → null).
2. Implemented mapper + `ElasticsearchLogSource` partial class with `LoggerMessage` source-generated logging.
3. `AddHotFixElastic` extension binds `ElasticOptions` (with `DataAnnotations`) and constructs the client.

**Tests.** 14 new unit tests covering mapper edge cases and ingestor paging behavior.

**AI tool & prompts.**
- **Claude Code** + the **elastic-query** skill for the DSL templates.
- Prompt: *"Use the elastic-query skill. Write the failing test where SearchAsync yields 2 pages via search_after with ICollection<FieldValue>? cursors taken from `hits[^1].Sort`."*
- Pain point: Copilot kept proposing deprecated `client.Index(...)` calls; Claude Code with the skill loaded picked the v8 `.Indices(...)` API on the first try.

---

### Module 3 — Heuristic analysis
**Approach.** Pure functions: a `MessageNormalizer` (regex source-generators for GUIDs, quoted strings, numbers ≥ 4 digits), an `AnalysisRule` record (`Name`, `Purpose`, `Matches`), and a `HeuristicAnalyzer` that groups by `(ExceptionType, NormalizedMessage, Endpoint)` and labels each group via the first matching rule.

**Workflow.** Failing tests for normalizer (5), analyzer ranking + purpose enrichment (12), and an `LlmAnalysisStrategy` stub that throws `NotImplementedException` (1) so the strategy pattern compiles for the future Ollama path.

**Tests.** +18 tests.

**AI tool & prompts.**
- **Claude Code**.
- Prompt: *"Define DefaultRules.All with NullReference, Timeout, Deadlock, Validation, ServerError5xx. Each rule must match by ExceptionType.Contains OR a list of message fragments (OrdinalIgnoreCase)."*

---

### Module 4 — GitInsights
**Approach.** Clone-on-demand via LibGit2Sharp, mine `origin/main` with `repo.Commits.QueryBy(CommitFilter { SortBy = Topological | Time })`, and convert each `ErrorGroup` into search keywords via `FixHintBuilder` (e.g. message "Operation timed out" canonicalizes to "timeout").

**Workflow.** 11 failing tests first — 4 for `ApisConfig` JSON loading (case-insensitive dict, default branch "main"), 7 for `FixHintBuilder` (keyword extraction, "timed out" → "timeout" canonicalization, top-3 commit hint formatting `sha7 (yyyy-MM-dd) — subject`).

**Tests.** +11 unit tests. `LibGit2SharpRepoCache`/`HistoryReader` are integration-style and only exercised via the live demo.

**AI tool & prompts.**
- **Claude Code**.
- Prompt: *"Write FixHintBuilder.MessageTokens as `(Pattern, Canonical)` tuples so `'timed out'` and `'has timed out'` both map to the canonical keyword `timeout` that the Timeout rule already matches."*
- Pitfall: I first returned `Func<…,Credentials>` from `LibGit2SharpRepoCache` and got CS0029; the fix was `using LibGit2Sharp.Handlers;` + return `CredentialsHandler` directly. Also `LibGit2Sharp.LogLevel` shadowed `Microsoft.Extensions.Logging.LogLevel`, requiring full qualification in `LoggerMessage` attributes.

---

### Module 5 — Persistence + Minimal API
**Approach.** EF Core 9 + SQLite (works on net10 without issue), single `TriageRun` aggregate that persists the entire enriched `ErrorGroup[]` as a JSON column (`ErrorGroupsJson`). `TriageService` orchestrates Elastic → Analyzer → FixHintBuilder → Repo → `TriageResult`. Endpoints: `POST /api/triage/{apiName}`, `GET /api/triage/{apiName}/latest`, `/history`, `/runs/{id}`.

**Workflow.** 10 failing tests first (6 service + 4 repository); the integration test boots the whole API via `WebApplicationFactory<Program>` with a substituted `IElasticLogSource` and EF InMemory.

**Tests.** +10 unit + 2 integration. `JsonStringEnumConverter` registered so `severity` serializes as `"Error"` for React.

**AI tool & prompts.**
- **Claude Code**.
- Prompt: *"Refactor TriageService.RunAsync. FetchAsync now returns `Task<IReadOnlyList<LogEntry>>` (not IAsyncEnumerable), so the `await foreach` must collapse to a single `var logs = await _ingestor.FetchAsync(...)`."*

---

### Module 6 — CLI + slash command
**Approach.** A thin `HotFixAmbulance.Cli` console app that wires the exact same DI as the API (`AddHotFixElastic/.GitInsights/.Persistence`) and invokes `TriageService.RunAsync` in-process. JSON to stdout is the contract for the `log-analyzer` subagent; `--format table` is for humans.

**Workflow.** Wrote `CliArgs.Parse` first (TDD: 11 tests covering `--lookback 24h|60m|2d|N`, `--no-open`, `--format`, error cases) → then `CliRenderer` (1 test) → then the Host wiring in `Program.cs`. The slash command `.claude/commands/hot-fix-ambulance.md` invokes `dotnet run --project backend/src/HotFixAmbulance.Cli -- "<apiName>" --lookback "<lookback>" --format json` and hands the JSON to `log-analyzer`.

**Tests.** +15 unit tests.

**AI tool & prompts.**
- **Claude Code**.
- Prompt: *"Author the failing tests for CliArgs.Parse that lock in: first token must be an apiName (no leading `-`), suffixes h/m/d are honored, equals-form `--lookback=6h` works, invalid values throw via Usage, unknown flags produce a clear error."*

---

### Module 7 — React frontend
**Approach.** Vite + React 19 + TypeScript. TanStack Table v8 for the 12-column triage view (10 raw facts + Purpose + How-to-Fix), TanStack Query v5 to fetch by `?analysisId=<guid>`, Tailwind for styling, custom `severityRank` so Fatal sorts above Error sorts above Warning by default.

**Workflow.** Vitest + RTL tests authored first: column count = 12, default sort puts Fatal at the top, AI columns render, long messages are truncated with a `title` tooltip at 80 chars. Then `TriageTable.tsx`, `SeverityBadge.tsx`, `App.tsx`, `api.ts`.

**Tests.** 8 Vitest tests. ESLint 9 flat config + `--max-warnings=0`.

**AI tool & prompts.**
- **Claude Code** + **Copilot inline completion**.
- Prompt 1 (Claude Code): *"Scaffold a Vite React 19 + TS app under frontend/. Add TanStack Table v8 + Query v5 + Tailwind 3 + ESLint 9 flat. Vitest config in vitest.config.ts (NOT vite.config.ts, to avoid the `defineConfig` Plugin<any> type mismatch between vite and vitest's bundled vite)."*
- Prompt 2 (Claude Code): *"Write the failing TriageTable.test.tsx that asserts: getAllByRole('columnheader').length === 12, the first row's SeverityBadge text is 'Fatal' even though Warning is first in the data array, AI columns 'OOM under load' and 'beef456' are rendered, and a 200-char message becomes 80 chars + ellipsis with a title tooltip equal to the original."*

---

### Module 8 — demo-api + orchestrator
**Approach.** A self-contained .NET 10 Minimal API instrumented exactly per the `serilog-mapping` skill: `Application=demo-api`, `Version`, `MachineName` enrichers + `UseSerilogRequestLogging` diagnostic context. Sinks: Console always, File (rolling), Elasticsearch optional via `Elastic:Uri` with the ECS formatter. Three deliberately broken endpoints (`POST /orders`, `GET /payments/{id}`, `GET /users/{id}`).

**Workflow.** Built, started locally, hit the endpoints with `Invoke-WebRequest`, confirmed 500/504/500 plus correctly enriched Serilog events in the console. Then wrote `scripts/demo.ps1` which builds, starts demo-api hidden, polls `/health`, fires 9 erroneous requests, and runs the CLI.

**Tests.** Smoke-tested live; no unit tests (the value is in the structured Serilog events the next phase consumes).

**AI tool & prompts.**
- **Claude Code**.
- Prompt: *"Author demo-api/Program.cs using only the serilog-mapping skill. Sinks: Console (always), File (rolling daily, shared=true), Elasticsearch (only when Elastic:Uri is set) with `AutoRegisterTemplate=true` and the `EcsTextFormatter`. 3 endpoints producing NRE / TaskCanceledException / ArgumentOutOfRangeException."*

---

## 4. Challenges & tool comparison

### Biggest challenges

1. **Elasticsearch v8 client API drift.** The 8.19.0 client renamed `.Index(...)` → `.Indices(...)` and `.DateRange(...)` → `.Date(...)`. `TermsQueryField` requires `FieldValue[]` wrapping. Code analyzers (`TreatWarningsAsErrors=true`) treated every deprecation as an error, so I had to discover the new shape before *any* commit could pass.
2. **EF Core provider conflict in integration tests.** Registering `UseInMemoryDatabase` on top of `UseSqlite` triggered `InvalidOperationException: Services for database providers ... have been registered.` Fix: enumerate every `Microsoft.EntityFrameworkCore.*` `ServiceDescriptor` in `ConfigureServices` and `Remove()` them before re-adding InMemory.
3. **`Program` symbol collision in tests.** The integration project referenced both `HotFixAmbulance.Api` and `HotFixAmbulance.Cli`; both expose a top-level `Program` (one explicit `partial`, one synthesized by minimal hosting). `WebApplicationFactory<Program>` failed with CS0433. Fix: drop the Cli reference from the test project — the integration tests only need the Api host.
4. **Vitest + Vite type collision.** `defineConfig` from `vitest/config` and `vite` have incompatible plugin types. Final layout: `vite.config.ts` uses `vite`, `vitest.config.ts` uses `vitest/config`, `tsconfig.json` includes only `vite.config.ts`.
5. **PowerShell credential-helper noise.** Every `git push` exits with `git: 'credential-manager-core' is not a git command`, code 1, but the push actually succeeded. Resolved by always checking the `*..* main -> main` line, not `$LASTEXITCODE`.

### Which tool helped most, and why

- **Claude Code (Sonnet 4.5)** — Did 95% of the work. Its `.claude/` agents, skills, hooks, and slash-command surface *is* the proof of AI-driven authoring required by the rubric. The `test-author` subagent + `tdd-cycle` skill kept every commit honest; the `log-analyzer` subagent is the runtime client of the CLI output. When the Elasticsearch v8 API drift bit, loading the `elastic-query` skill once was enough to keep all subsequent suggestions on the new API surface.
- **GitHub Copilot inline completion** — Used inside `.tsx` files for repetitive JSX. Pleasant, but couldn't reason about cross-file invariants like "the same `Severity` enum must serialize as `"Error"` in JSON for the React `SeverityBadge` to color-code correctly".
- **Augment Code / Cursor** — Not used. The repo-aware multi-file edit story I needed was already covered by Claude Code in this workspace.

### What I'd improve

- **Phase L (LLM-backed analysis)** — `LlmAnalysisStrategy` is a NotImplementedException stub. Plug Ollama (`qwen2.5-coder:7b`) behind it for richer Purpose / HowToFix text and A/B against the heuristic strategy.
- **OpenAPI client for the frontend.** Today the React types in `frontend/src/types.ts` are hand-written. Adding `Microsoft.AspNetCore.OpenApi` + `openapi-typescript` would eliminate that drift.
- **Playwright e2e.** Vitest covers the table; an `?analysisId=demo` flow that mocks the API and asserts the rendered DOM would close the loop.
- **GitInsights live tests.** `LibGit2SharpHistoryReader` is exercised only via the demo. A small fixture repo on disk + a `HFA_RUN_LIVE=1` gate would push test count from 153 → ~170.

---

## 5. Working-system evidence

### A. Pre-commit gate — all four gates green
```
Gate          Status
----          ------
dotnet test   ok
dotnet format ok
frontend test ok
frontend lint ok
```

### B. Test totals (HEAD `2a98807`, Phase 12.G)
- **123 backend unit tests** (`HotFixAmbulance.UnitTests`)
- **11 backend integration tests** (`HotFixAmbulance.IntegrationTests`, via `WebApplicationFactory<Program>`)
- **19 frontend tests** (`Vitest` + RTL)
- **= 153 tests passing**

Growth since the original writeup (`b773ed7`, 96 tests): +35 backend + 9 integration + 11 frontend, driven by Phases 10–12 (real EF CRUD in demo-api, line-context + git-blame in How-to-fix, expandable cells, configurable analysis time range with `TimeWindow` value object + `TimeRangePicker` + API `?fromUtc/?toUtc` + CLI `--from/--to` + `TriageOptions` `MaxRangeDays=30` cap).

### C. demo-api Serilog smoke test
```
[14:07:28 ERR] Order request rejected because cart could not be resolved for null customer
[14:07:28 ERR] HTTP POST /orders responded 500 in 7.1228 ms
System.NullReferenceException: Object reference not set to an instance of an object.
[14:07:28 ERR] User lookup with invalid id -3 threw ArgumentOutOfRangeException
[14:07:28 ERR] HTTP GET /users/-3 responded 500 in 0.5953 ms
System.ArgumentOutOfRangeException: id must be non-negative (Parameter 'id') Actual value was -3.
[14:07:28 ERR] Payment provider call timed out for id=ab
[14:07:28 ERR] HTTP GET /payments/ab responded 504 in 64.5887 ms
```
All three error paths produce well-formed Serilog events with the enrichers (`Application`, `RequestPath`, `StatusCode`, `CorrelationId`) the backend ingestor consumes.

### D. Screenshots to capture and drop into `docs/screenshots/`
Capture the following live before pasting links into the Google Doc:

1. **Terminal — `pwsh scripts/demo.ps1 -WithElastic -KeepRunning`** showing demo-api startup, the 24 erroneous requests, the `es-bootstrap` reindex line, and the CLI ending with `Analysis id: <guid>`.
2. **Browser — `http://localhost:5173/?analysisId=<guid>&api=demo-api`** showing (a) the Run-analysis bar with API dropdown + `TimeRangePicker` (7 presets, 24h selected) + `Run analysis` button, (b) the centred amber `Logs from … → … (1 h)` pill in the header, (c) `Rerun this window` button, (d) the TanStack Table with Fatal sorted above Error above Warning and the How-to-fix column populated with the `Where to fix: … / code (from origin/main) / >> <line> / blame: <sha>` block.
3. **Browser — expand-modal** — click the three-dot expand on a Message or How-to-fix cell and screenshot the styled `CellDetailModal` (Copy + Close).
4. **Pre-commit hook output** — terminal screenshot of `git commit` running the four gates green (`dotnet test`, `dotnet format`, `frontend test`, `frontend lint`).
5. **GitHub commit history** — the Phase commits on `origin/main`, latest `Phase 12.G: share SQLite schema shim between API and CLI` (`2a98807`).

Save as PNG under `docs/screenshots/` and embed in the Google Doc.

---

## 6. Repository

- **Code:** [github.com/myPOStech/mps-banking-hot-fix-ambulance](https://github.com/myPOStech/mps-banking-hot-fix-ambulance)
- **Plan:** [plan.md](../plan.md)
- **Dev-log:** [docs/dev-log.md](dev-log.md) — one row per TDD cycle.
- **AI customizations:** `.claude/agents/`, `.claude/skills/`, `.claude/hooks/`, `.claude/commands/`.

**Submission format:** export this file to a Google Doc, set sharing to *Anyone with the link — Viewer*, then paste the link into the Softuni submission form.
