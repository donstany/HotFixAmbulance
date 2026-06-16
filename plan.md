# Plan: HotFixAmbulance — AI-driven `/hot-fix-ambulance` plugin

## TL;DR
Build a Claude Code slash command `/hot-fix-ambulance <apiName>` that triggers a .NET 10 backend to (1) pull last-24h error logs from an **existing myPos Elasticsearch** instance for `<apiName>`, (2) group + heuristically analyze errors and rank by severity, (3) clone-on-demand the related git repo via a configurable base URL and mine `origin/main` history for fix hints, then (4) open a React 19 + Vite UI showing a 12-column error table (10 facts + Purpose + How-to-Fix). Strict TDD throughout, with `.claude/` agents, skills, and hooks proving the work is AI-driven. Heuristic analysis ships in Milestone 1; pluggable LLM (Ollama/OpenAI) ships in Milestone 2. All commits remain local.

## Confirmed decisions
- **Plugin host**: Claude Code slash command (`.claude/commands/hot-fix-ambulance.md`).
- **Elasticsearch**: connect to an existing myPos cluster; secrets via `dotnet user-secrets` for dev and `.env` for the demo runner. No docker-compose for Elastic.
- **Git repo discovery**: clone on demand from a configurable base URL template (e.g., `https://github.com/myPOStech/{apiName}.git`) into a local cache dir; reuse if present, `git fetch origin` otherwise.
- **Analysis strategy**: heuristic/rule-based in Milestone 1 behind an `IAnalysisStrategy` abstraction; Milestone 2 adds LLM adapters (Ollama default, OpenAI/Azure optional).
- **Demo API**: scaffold `demo-api/` (.NET 10 minimal API + Serilog → Elastic sink + deliberately broken endpoints) so the end-to-end demo and screenshots are real.
- **Frontend delivery**: standalone Vite + React app on `http://localhost:5173`; CLI opens the browser with `?analysisId=<guid>`.
- **Repo layout**: single repo `HotFixAmbulance/` (corporate `myPOStech/hot-fix-ambulance`); all commits stay local until the exam.

## Open product choices (proposed, easy to tweak)
- **Severity ordering**: Fatal > Error > Warning, tiebreaker = occurrence count, then most-recent first.
- **12 columns** (10 main + 2 AI-derived). 10 main are the "important" facts capped per the requirement:
  1. First seen (UTC)
  2. Last seen (UTC)
  3. Severity
  4. Count
  5. Exception type
  6. Message (truncated, full on hover)
  7. Endpoint / route
  8. HTTP status
  9. Service version
  10. Distinct correlation IDs
  11. **Purpose** — backend heuristic explanation of what this error means
  12. **How to fix** — backend recommendation derived from recent `origin/main` commits touching the implicated symbol/file

## TDD workflow (must be followed for every story)
1. **Red (outer)**: write the failing acceptance test (integration test for backend, Playwright for UI).
2. **Red (inner)**: write the failing unit test for the unit you'll touch first.
3. **Green**: minimal implementation to pass — no extras.
4. **Refactor**: rename/extract while green; rerun tests.
5. **Local commit**: only if `dotnet test` + `npm test` + lint are green. Enforced by `.claude/hooks/pre-commit.ps1` (also wired as a Git pre-commit hook).
6. **Document**: append a 1-line entry to `docs/dev-log.md` per cycle for the exam write-up.

## Phased implementation
Phases are independently verifiable. Steps inside a phase run sequentially unless marked *(parallel)*.

### Phase 0 — Repository & AI tooling scaffold
0.0 Create `Project-assignment.md` at the workspace root with the verbatim Softuni "AI-Assisted Development — Exam" brief (cleaned of the MS Word `v\:* {behavior:...}` / `mso-*` style noise — keep only the Markdown headings and bullet content: Overview, Choose Your Own Project Topic, System Architecture – Modules, Development Process per Module, Challenges & Tool Comparison, Working System Evidence, Repository, Submission Format).
0.1 Create solution layout: `backend/`, `frontend/`, `demo-api/`, `.claude/`, `docs/`, `.editorconfig`, `.gitignore`, `README.md`.
0.2 Initialize git (`main` branch, local only); commit empty scaffold.
0.3 Author `.claude/commands/hot-fix-ambulance.md` (mandatory `apiName` arg, lookback param defaulting to `24h`, calls the CLI).
0.4 Author `.claude/agents/`:
   - `log-analyzer.md` — runs heuristic grouping on a JSON payload.
   - `git-historian.md` — read-only Explore-style subagent that summarizes recent commits for a path.
   - `test-author.md` — writes the failing test first per TDD step 1.
0.5 Author `.claude/skills/`:
   - `tdd-cycle/SKILL.md` — codifies the 6-step workflow.
   - `elastic-query/SKILL.md` — canonical KQL/DSL templates for error logs.
   - `serilog-mapping/SKILL.md` — field-name conventions from `Serilog.Sinks.Elasticsearch`.
0.6 Author `.claude/hooks/`:
   - `pre-commit.ps1` — runs `dotnet test`, `npm test`, eslint, dotnet format; blocks commit on failure.
   - `post-tool-use.ps1` — appends executed tool calls to `docs/dev-log.md` (exam evidence).
0.7 Symlink/install hooks into `.git/hooks/pre-commit` via a `bootstrap.ps1`.

### Phase 1 — Backend domain & test harness (TDD red bar)
1.1 Create `backend/HotFixAmbulance.sln` with projects: `Core`, `Elastic`, `Analysis`, `GitInsights`, `Persistence`, `Api`, `Cli`, `UnitTests`, `IntegrationTests`.
1.2 Add packages: xUnit, FluentAssertions, NSubstitute, Microsoft.AspNetCore.Mvc.Testing, Testcontainers, Verify.Xunit.
1.3 Define domain types in `Core`: `LogEntry`, `ErrorGroup`, `Severity`, `AnalysisRequest`, `AnalysisResult`, `Recommendation`, `ApiDescriptor`.
1.4 Write failing unit tests for `ErrorGroup` invariants and `Severity` comparer.
1.5 Implement just enough to green-bar; refactor.

### Phase 2 — Elastic ingestion module *(parallel with Phase 3 after 1.5)*
2.1 Failing integration test using a fake `IElasticLogSource` returning a fixture; asserts `ElasticLogIngestor` filters by `apiName`, time window, level ≥ Warning.
2.2 Implement `ElasticLogIngestor` using `Elastic.Clients.Elasticsearch` v8; config via `IOptions<ElasticOptions>` bound to user-secrets/.env.
2.3 Add fault-tolerance: retry with Polly, paging via `search_after`.
2.4 Manual smoke test against the real myPos cluster behind an opt-in flag (`HFA_RUN_LIVE=1`).

### Phase 3 — Heuristic analysis module *(parallel with Phase 2 after 1.5)*
3.1 Failing unit tests for `HeuristicAnalyzer`:
   - Groups by `(exceptionType, normalizedMessage, endpoint)`.
   - Sorts by severity then count.
   - Produces a non-empty `Purpose` for known patterns (null-ref, timeout, 5xx upstream, deadlock, validation).
3.2 Implement `HeuristicAnalyzer : IAnalysisStrategy`; templates live in `analysis-rules.json` for easy extension.
3.3 Plug into DI behind `IAnalysisStrategy`; second binding `LlmAnalysisStrategy` is a Milestone-2 stub that throws `NotImplementedException`.

### Phase 4 — Git insights module *(depends on Phase 1)*
4.1 Failing unit tests for `GitRepoCache` (clone if missing, fetch if present, idempotent) using a local bare repo fixture.
4.2 Failing unit tests for `FixHintBuilder` — given an `ErrorGroup` with a stack-frame file/symbol, returns the most recent matching commits from `origin/main` and synthesizes a "How to fix" sentence.
4.3 Implement with LibGit2Sharp; cache dir under `%LOCALAPPDATA%/HotFixAmbulance/repos/`.
4.4 Config: `apis.config.json` with `{ baseUrlTemplate, branch, authHeaderEnvVar }`.

### Phase 5 — Persistence + Backend API *(depends on 2 & 3 & 4)*
5.1 Add EF Core 10 + SQLite to `Persistence`; `AnalysisRun` and `AnalysisItem` tables.
5.2 Failing integration tests with `WebApplicationFactory`:
   - `POST /api/analyses { apiName, lookback }` → returns `{ id }`, persists run.
   - `GET /api/analyses/{id}` → returns ranked items with all 12 columns populated.
5.3 Implement minimal-API endpoints, request validation (FluentValidation), problem-details errors.
5.4 Add OpenAPI (`Microsoft.AspNetCore.OpenApi`) for the React client.

### Phase 6 — CLI + Claude slash command *(depends on Phase 5)*
6.1 `HotFixAmbulance.Cli` using `System.CommandLine`: `hot-fix-ambulance <apiName> [--lookback 24h] [--open/--no-open]`.
6.2 CLI starts/uses an already-running backend (`http://localhost:5080`), prints a Markdown summary table to stdout (so Claude can quote it), then opens `http://localhost:5173/?analysisId=<guid>`.
6.3 The slash command file in `.claude/commands/hot-fix-ambulance.md` shells out to the CLI and asks the `log-analyzer` subagent to commentate the result.
6.4 Failing CLI integration test using `Process.Start` against the test host.

### Phase 7 — React frontend *(parallel with Phase 6 after Phase 5)*
7.1 `npm create vite@latest frontend -- --template react-ts`; add TanStack Table v8, TanStack Query v5, Tailwind, Vitest, RTL, Playwright, ESLint, Prettier.
7.2 Failing Vitest unit tests for `<ErrorTable />`: renders ≤10 main columns plus 2 AI columns, severity badge color, truncated message tooltip.
7.3 Failing Playwright e2e: visits `/?analysisId=demo`, mocks the API, asserts table contents and sort order.
7.4 Implement `AnalysisView` page → `useAnalysisQuery` → `ErrorTable`. Generate API client from OpenAPI.
7.5 Column visibility toggle (defaults to 12 visible; user can hide); no horizontal scroll up to 1440px.

### Phase 8 — Demo API + end-to-end demo
8.1 `demo-api/`: .NET 10 minimal API with `Serilog.Sinks.Elasticsearch`, 3 endpoints (`/orders` throws `NullReferenceException` on certain inputs, `/payments` simulates upstream timeout, `/users/{id}` 500s on negative ids). Seed git history with realistic commits that mention the same files.
8.2 Compose script `scripts/demo.ps1`: starts demo-api, hammers endpoints to produce logs, starts backend + frontend, runs `/hot-fix-ambulance demo-api`.
8.3 Capture screenshots: terminal showing slash command output, browser showing 12-column table, log entry in `docs/dev-log.md`. Save under `docs/screenshots/`.

### Phase 9 — Exam deliverable *(depends on Phase 8)*
9.1 Draft `docs/exam.md` covering the Softuni rubric: 1-page idea, modules, per-module approach/workflow/tests/AI tool/prompts (2–3 each), challenges & tool comparison, screenshots, repo link.
9.2 Export to Google Doc, share view-only; paste link into submission.

## Relevant files / artifacts to create
- `.claude/commands/hot-fix-ambulance.md` — slash command with mandatory `$ARG1` validation.
- `.claude/agents/{log-analyzer,git-historian,test-author}.md` — focused subagents.
- `.claude/skills/{tdd-cycle,elastic-query,serilog-mapping}/SKILL.md` — reusable AI skills.
- `.claude/hooks/{pre-commit.ps1,post-tool-use.ps1}` — quality + evidence hooks.
- `backend/HotFixAmbulance.sln` and 8 projects above.
- `backend/src/HotFixAmbulance.Analysis/analysis-rules.json` — heuristic catalogue.
- `backend/src/HotFixAmbulance.GitInsights/apis.config.json` — `{apiName}` → git base URL.
- `frontend/src/components/ErrorTable.tsx`, `frontend/src/pages/AnalysisView.tsx`.
- `demo-api/Program.cs`, `demo-api/appsettings.json`.
- `scripts/demo.ps1`, `scripts/bootstrap.ps1`.
- `docs/exam.md`, `docs/dev-log.md`, `docs/screenshots/`.

## Verification
1. `dotnet test backend/HotFixAmbulance.sln` — all unit + integration tests green; coverage report shows ≥80% line coverage on `Core`, `Analysis`, `GitInsights`.
2. `npm --prefix frontend test -- --run` — Vitest green.
3. `npm --prefix frontend run e2e` — Playwright green against mocked backend.
4. `HFA_RUN_LIVE=1 dotnet test --filter Category=Live` — opt-in test hits a real Elastic cluster and a real disposable git repo.
5. `scripts/demo.ps1` — produces logs, runs the slash command, opens the browser; screenshots captured automatically into `docs/screenshots/`.
6. `.git/hooks/pre-commit` blocks a commit when any test is intentionally broken (manual verification step).
7. Manual: invoke `/hot-fix-ambulance demo-api` inside Claude Code; confirm subagents and skills are referenced in the transcript (exam evidence).

## Scope boundaries
- **In**: heuristic analysis, single-tenant local dev, SQLite persistence, single demo API, English UI only.
- **Out (Milestone 1)**: LLM-driven analysis, authentication/authorization on the backend, multi-tenant Elastic, deployment pipelines, dockerized backend, mobile UI.
- **Out (entirely)**: writing back to git, auto-creating PRs, real incident paging.

## Further considerations (recommendation → alternatives)
1. **Column 11/12 source of truth** — Recommendation: backend always returns them; frontend only renders. Alternative: frontend calls a second `/recommend` endpoint on demand (more complex, no benefit for the exam).
2. **Frontend column toggling** — Recommendation: ship all 12 visible by default, allow hiding. Alternative: enforce strict 10-visible cap; the two AI columns are then always shown and the user toggles among the other 10.
3. **Pre-commit hook scope** — Recommendation: run only affected tests (fast). Alternative: run full suite (safer, slower); reassess after Phase 5 once suite size is known.
