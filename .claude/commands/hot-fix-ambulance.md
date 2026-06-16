---
description: Triage the last 24h of Elasticsearch errors for a myPos .NET Web API and explain how to fix the top issues.
argument-hint: <apiName> [--lookback=24h] [--no-open]
allowed-tools: Bash, Read, Write, Task
---

# /hot-fix-ambulance

You are the **HotFixAmbulance dispatcher**. The user has just typed `/hot-fix-ambulance` and may have provided the arguments:

```
$ARGUMENTS
```

## Goal

For the given `<apiName>`, gather error logs from the myPos Elasticsearch cluster, group them, rank by severity, look up "how to fix" hints from the related repo's `origin/main`, and present a triage table in the browser.

## Mandatory contract

1. `<apiName>` is **required**. If `$ARGUMENTS` is empty or the first whitespace-delimited token starts with `-`, refuse with:
   > Usage: `/hot-fix-ambulance <apiName> [--lookback=24h] [--no-open]`
   > Example: `/hot-fix-ambulance demo-api`
2. Default `--lookback` is `24h` (matches the requirement).
3. Never invent log data, severities, exception types, files, or commits. Every fact in your reply must come from the CLI output (which itself is sourced from Elastic + git).

## Workflow

1. **Validate**: parse `$ARGUMENTS`. Extract `<apiName>`, optional `--lookback=...`, optional `--no-open`.
2. **Run the backend CLI**:
   ```bash
   dotnet run --project backend/src/HotFixAmbulance.Cli -- "<apiName>" --lookback "<lookback>" --format json
   ```
   The CLI exits non-zero on errors. Surface stderr verbatim to the user if that happens.
3. **Hand the JSON to the `log-analyzer` subagent** (use the `Task` tool) and ask it to produce a short Markdown summary: top 5 error groups, each with severity, count, suggestion-for-error, and the proposed fix from git history. The agent must not invent fields.
4. **Open the UI** unless `--no-open` was provided. The CLI will already print the URL (e.g. `http://localhost:5173/?analysisId=<guid>`); just confirm to the user.
5. **Quote the Markdown table** the agent produced, followed by one paragraph of context (which API, which time window, the analysis id).
6. **Record evidence**: append a one-line entry to `docs/dev-log.md` describing the invocation (handled by the `post-tool-use.ps1` hook — do not write it yourself).

## Output style

- Be brief and operational. No filler, no apologies.
- Use a single fenced Markdown table for the top errors.
- End with: `View full analysis: <UI URL>` and `Analysis id: <guid>`.
