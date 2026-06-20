# scripts

PowerShell helpers consumed by humans and by Claude.

- `bootstrap.ps1` — wires `.claude/hooks/pre-commit.ps1` into `.git/hooks/pre-commit` and restores .NET / npm tooling.
- `demo.ps1` — end-to-end runner **on Qwen by default**: starts the Dockerized Qwen runtime (`infra/qwen`, model pre-pulled) + MSSQL (`infra/mssql`), starts `demo-api`, hammers endpoints to produce Elastic logs, launches `HotFixAmbulance.Api` (:5283) + `frontend` (:5173) wired to `Analysis:Strategy=Llm`, creates a triage run through the API, asserts it was analyzed by the LLM, and opens the UI showing the "🤖 Qwen" badge. Pass `-SkipLlm` for the old heuristic-only flow.

Files are added in Phase 0.7 and Phase 8.2 of [plan.md](../plan.md).
