# Local Elasticsearch for HotFixAmbulance

This folder holds everything you need to run a **local, insecure** single-node
Elasticsearch 8.15 cluster suitable for the HotFixAmbulance demo. It is not
suitable for anything else.

## Files

| File | Purpose |
|---|---|
| `docker-compose.yml` | Single-node ES 8.15.0 on `http://localhost:9200`, security disabled, 512m heap, data persisted to the `hfa-es-data` named volume. |
| `ecs_to_fields.pipeline.json` | Ingest pipeline that rewrites ECS-shaped documents (as emitted by `Elastic.CommonSchema.Serilog`) into the field shape that `HotFixAmbulance.Elastic.SerilogDocumentMapper` consumes. |
| `bootstrap.ps1` | Idempotent setup: waits for the cluster, installs/refreshes the pipeline, reindexes today's `logs-yyyy.MM.dd` index into `hfa-mapped` through the pipeline, and binds the `logs-mapped-<date>` alias so the CLI's `logs-*` wildcard matches. |

## Why the pipeline exists

`demo-api` configures Serilog with `Elastic.CommonSchema.Serilog`'s
`EcsTextFormatter`, which produces documents shaped like:

```json
{ "@timestamp": "...", "log": { "level": "Error" }, "labels": { "Application": "demo-api" },
  "url": { "path": "/orders" }, "http": { "request": { "method": "POST" }, "response": { "status_code": 500 } } }
```

`SerilogDocumentMapper`, however, was written against the classic Serilog
Elasticsearch sink shape and expects:

```json
{ "@timestamp": "...", "level": "Error", "fields": { "Application": "demo-api",
  "RequestPath": "/orders", "RequestMethod": "POST", "StatusCode": 500 } }
```

Rather than patching either side, the ingest pipeline rewrites incoming ECS
docs into the mapper's shape at index time. The reshaped docs land in
`hfa-mapped`; the `logs-mapped-<date>` alias makes them visible to queries
against `logs-*`.

## Typical usage

```powershell
# 1. Start the cluster (first run pulls ~700MB; subsequent runs are instant).
docker compose -f infra/elasticsearch/docker-compose.yml up -d

# 2. Run the demo wired to Elastic. demo-api will write ECS docs to logs-<today>.
powershell -File scripts/demo.ps1 -WithElastic -KeepRunning

# 3. Open the React UI — the analysis id printed by the CLI shows up there.
#    The demo script also pre-loads it into the SQLite store via the API.

# Tear down (keeps data):
docker compose -f infra/elasticsearch/docker-compose.yml down
# Tear down (wipes data):
docker compose -f infra/elasticsearch/docker-compose.yml down -v
```

## Running `bootstrap.ps1` directly

```powershell
# Use today's UTC date by default:
powershell -File infra/elasticsearch/bootstrap.ps1

# Reshape a specific day's index:
powershell -File infra/elasticsearch/bootstrap.ps1 -SourceDate 2026.06.16

# Point at a non-default cluster:
powershell -File infra/elasticsearch/bootstrap.ps1 -ElasticUri http://es.local:9200
```

The script prints a sanity probe at the end (`hits(level in Fatal/Error/Warning, app=demo-api)`).
If that's `0` after `demo-api` has been hammered, double-check that demo-api
was launched with `HFA_Elastic__Uri=http://localhost:9200`.
