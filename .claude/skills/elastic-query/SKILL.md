---
name: elastic-query
description: Canonical Elasticsearch DSL templates used by HotFixAmbulance to pull the last N hours of error logs for a specific .NET Web API. Use when writing or reviewing `ElasticLogIngestor` queries.
---

# Skill: Elasticsearch query templates

myPos APIs ship logs through `Serilog.Sinks.Elasticsearch` into indices named `logs-<env>-<yyyy.MM.dd>` (the wildcard `logs-*` is the default in `.env.example`). Below are the **only** query shapes HotFixAmbulance is allowed to issue.

## Fields we depend on

| Logical name        | Elastic field                            |
|---------------------|------------------------------------------|
| Timestamp           | `@timestamp`                             |
| Severity            | `level` (string: `Fatal|Error|Warning|…`)|
| API name            | `fields.Application` (`keyword`)         |
| Service version     | `fields.Version` (`keyword`)             |
| Exception type      | `fields.ExceptionType` (`keyword`)       |
| Exception message   | `exceptions.message` or `message`        |
| HTTP method         | `fields.RequestMethod`                   |
| HTTP path           | `fields.RequestPath` (`keyword`)         |
| HTTP status         | `fields.StatusCode` (`integer`)          |
| Correlation id      | `fields.CorrelationId` (`keyword`)       |

If a field is missing the code must degrade gracefully (`null`) — never throw.

## Top-error query (default for `/hot-fix-ambulance`)

```json
{
  "size": 1000,
  "track_total_hits": true,
  "sort": [{ "@timestamp": "desc" }],
  "_source": [
    "@timestamp", "level", "message",
    "exceptions.type", "exceptions.message",
    "fields.Application", "fields.Version",
    "fields.ExceptionType",
    "fields.RequestMethod", "fields.RequestPath",
    "fields.StatusCode", "fields.CorrelationId"
  ],
  "query": {
    "bool": {
      "filter": [
        { "term":  { "fields.Application": "<apiName>" } },
        { "terms": { "level": ["Fatal", "Error", "Warning"] } },
        { "range": { "@timestamp": { "gte": "now-<lookback>", "lte": "now" } } }
      ]
    }
  }
}
```

## Paging

Use `search_after` with the `@timestamp` sort value from the last hit. Cap at 10 pages × 1000 = 10 000 hits to bound memory; record a `Truncated = true` flag on `AnalysisRun` when the cap is reached.

## Hard rules

- Always parameterize `<apiName>` and `<lookback>` — never string-concatenate user input into the JSON.
- Always filter by `fields.Application`. A query without it is a bug.
- Allowed levels: `Fatal`, `Error`, `Warning`. Reject anything else at the API boundary.
- Lookback is parsed as Go-duration-ish: `24h`, `7d`, `90m`. Validate with a regex before passing on.
