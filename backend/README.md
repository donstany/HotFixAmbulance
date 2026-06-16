# .NET 10 backend

Created in Phase 1 of [plan.md](../plan.md).

Projects:
- `src/HotFixAmbulance.Core` — domain types (LogEntry, ErrorGroup, Severity, AnalysisRequest, AnalysisResult, Recommendation, ApiDescriptor).
- `src/HotFixAmbulance.Elastic` — Elasticsearch ingestion (`Elastic.Clients.Elasticsearch` v8, Polly).
- `src/HotFixAmbulance.Analysis` — heuristic grouping & `Purpose` synthesis (`analysis-rules.json`).
- `src/HotFixAmbulance.GitInsights` — clone-on-demand cache + `FixHintBuilder` (LibGit2Sharp).
- `src/HotFixAmbulance.Persistence` — EF Core 10 + SQLite.
- `src/HotFixAmbulance.Api` — Minimal API with OpenAPI.
- `src/HotFixAmbulance.Cli` — `System.CommandLine` entrypoint invoked by the slash command.
- `tests/HotFixAmbulance.UnitTests`, `tests/HotFixAmbulance.IntegrationTests`.
