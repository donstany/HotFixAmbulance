# .NET 10 backend

Projects:

- `src/HotFixAmbulance.Core` — domain types (`Severity`, `LogEntry`, `ErrorGroup` (incl. `Suggestion`, `HowToFix`, `AnalyzedBy`), `TimeWindow`).
- `src/HotFixAmbulance.Elastic` — Elasticsearch ingestion (`Elastic.Clients.Elasticsearch` v8) + severity/time-window filtering.
- `src/HotFixAmbulance.Analysis` — deterministic grouping & ranking behind `IAnalysisStrategy` (`HeuristicAnalyzer`, `SuggestionBuilder`, `DefaultRules`).
- `src/HotFixAmbulance.GitInsights` — clone-on-demand cache + `FixHintBuilder` (LibGit2Sharp) producing blame + related-commit evidence.
- `src/HotFixAmbulance.Llm` — LLM adapter: `ILlmClient` / `OllamaLlmClient` (talks to the Qwen runtime on `:11434`), `LlmPromptBuilder`, `LlmOptions`. Never throws — returns `null` on any failure.
- `src/HotFixAmbulance.Persistence` — EF Core 10 + SQLite (`TriageRun` with `AnalyzedBy`).
- `src/HotFixAmbulance.Api` — Minimal API + the `TriageService` pipeline and the `IGroupEnricher` seam (`GitFixHintEnricher` default, `LlmGroupEnricher` when `Analysis:Strategy=Llm`, with graceful fallback). Paged groups via `GroupPager`.
- `src/HotFixAmbulance.Cli` — one-shot triage entrypoint (`CliArgs` parser) invoked by the slash command and `scripts/demo.ps1`.
- `tests/HotFixAmbulance.UnitTests`, `tests/HotFixAmbulance.IntegrationTests`.

## Analysis strategy

The two AI columns (`Suggestion` / `HowToFix`) are filled by an `IGroupEnricher` selected from `Analysis:Strategy`:

- `Llm` → `LlmGroupEnricher`: asks Qwen (`qwen2.5:3b`) for `{suggestion, howToFix}` JSON grounded in git evidence; tags the group `AnalyzedBy = "Llm"`; falls back to the heuristic on any model failure.
- default → `GitFixHintEnricher`: deterministic git-history hint; tags `AnalyzedBy = "Heuristic"`.

Run the LLM strategy locally with the Dockerized Qwen runtime in [../infra/qwen/README.md](../infra/qwen/README.md) (started automatically by `scripts/demo.ps1`).
