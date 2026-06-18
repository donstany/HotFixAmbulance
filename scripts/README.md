# scripts

PowerShell helpers consumed by humans and by Claude.

- `bootstrap.ps1` — wires `.claude/hooks/pre-commit.ps1` into `.git/hooks/pre-commit` and restores .NET / npm tooling.
- `demo.ps1` — end-to-end runner: starts Dockerized MSSQL (`infra/mssql`), starts `demo-api`, hammers endpoints to produce Elastic logs, launches `HotFixAmbulance.Api` + `frontend`, invokes the CLI, captures screenshots.

Files are added in Phase 0.7 and Phase 8.2 of [plan.md](../plan.md).
