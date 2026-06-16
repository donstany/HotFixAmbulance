# demo-api

Sample .NET 10 minimal Web API used as the target for `/hot-fix-ambulance demo-api`. Emits intentional errors to Elasticsearch via Serilog so the end-to-end demo and exam screenshots are reproducible. Created in Phase 8 of [plan.md](../plan.md).

Endpoints (Phase 8.1):
- `POST /orders` — throws `NullReferenceException` for certain payloads.
- `GET /payments/{id}` — simulates upstream timeout.
- `GET /users/{id}` — returns 500 on negative ids.
