# demo-api

Sample .NET 10 minimal Web API used as the target for `/hot-fix-ambulance demo-api`. Emits intentional errors to Elasticsearch via Serilog so the end-to-end demo and exam screenshots are reproducible. Created in Phase 8 of [plan.md](../plan.md).

Database setup:
- Default provider is SQL Server (`Database:Provider=SqlServer`).
- Local demo expects Dockerized MSSQL at `localhost,14333` (see [infra/mssql/README.md](../infra/mssql/README.md)).
- Can fall back to SQLite in-memory by setting `HFA_Database__Provider=Sqlite`.

Endpoints (Phase 8.1):
- `POST /orders` — throws `NullReferenceException` for certain payloads.
- `GET /payments/{id}` — simulates upstream timeout.
- `GET /payments/{id}/settlement` — simulates upstream `HTTP 503` from external dependency.
- `GET /users/{id}` — returns 500 on negative ids.
- `POST /invoices/duplicate` — real EF Core + MSSQL unique-constraint write failure (`DbUpdateException`).
- `GET /invoices/reprice` — simulated production database timeout with descriptive message.
- `GET /pricing/preview` — code-path `InvalidOperationException` (not a null reference).
