# HotFixAmbulance â€” development log

Append-only timeline of TDD cycles. The latest entry is at the bottom. Entries are written automatically by [.claude/hooks/post-tool-use.ps1](../.claude/hooks/post-tool-use.ps1) and serve as exam evidence that the work is AI-driven.

| When (UTC) | Phase | Cycle step | Note |
| --- | --- | --- | --- |
| 2026-06-16 | 0.1 | scaffold | Created repo skeleton, `.gitignore`, `.editorconfig`, README, placeholder folders. |
| 2026-06-16 10:23 | 1.1â€“1.5 | green | dotnet â€” 9-project sln + Core domain (Severity, LogEntry, ErrorGroup) with 16 passing tests |
| 2026-06-16 10:31 | 2.1–2.3 | green | dotnet — Elastic module: LogQuery, IElasticLogSource, ElasticLogIngestor (TDD, 8 tests), ElasticsearchLogSource v8 + Polly + search_after, SerilogDocumentMapper (4 tests). 30/30 unit tests passing. |
| 2026-06-16 10:35 | 3.1–3.3 | green | dotnet — Analysis module: MessageNormalizer, IAnalysisStrategy, AnalysisRule, DefaultRules (NullRef/Timeout/Deadlock/Validation/5xx), HeuristicAnalyzer, LlmAnalysisStrategy stub. TDD with 18 new tests. 48/48 unit tests passing. |
| 2026-06-16 10:41 | 4.1–4.3 | green | dotnet — GitInsights module: ApisConfig, IGitRepoCache + LibGit2SharpRepoCache (LocalAppData cache), IGitHistoryReader + LibGit2SharpHistoryReader, FixHintBuilder. TDD with 11 new tests. 59/59 unit tests passing. |
| 2026-06-16 10:49 | 5.1–5.3 | green | dotnet — Persistence (EF Core SQLite + TriageRun + TriageRunRepository), Api (TriageService orchestrator + Minimal API endpoints). TDD with 10 new unit tests + 2 integration tests via WebApplicationFactory. 69 unit + 2 integration tests passing. |
| 2026-06-16 10:56 | 6.1-6.2 | green | dotnet — CLI wiring (CliArgs parser + CliRenderer + in-process Host running TriageService). 15 new unit tests (14 CliArgs + 1 CliRenderer). Slash command .claude/commands/hot-fix-ambulance.md already invokes the CLI. 84 unit + 2 integration tests passing. |
