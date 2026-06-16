# .claude

This folder is what makes HotFixAmbulance **AI-driven**: it teaches Claude Code how to drive the project end-to-end.

- `commands/hot-fix-ambulance.md` — the user-facing slash command.
- `agents/` — focused subagents (`log-analyzer`, `git-historian`, `test-author`).
- `skills/` — reusable skills (`tdd-cycle`, `elastic-query`, `serilog-mapping`).
- `hooks/` — `pre-commit.ps1`, `post-tool-use.ps1`.

See [plan.md](../plan.md) — Phase 0.3 through 0.7.
