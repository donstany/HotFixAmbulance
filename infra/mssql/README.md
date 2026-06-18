# MSSQL (Demo)

Dockerized SQL Server used by `demo-api` to make database failures realistic.

## Start

```powershell
docker compose -f infra/mssql/docker-compose.yml up -d
```

## Stop

```powershell
docker compose -f infra/mssql/docker-compose.yml down
```

## Connection details

- Host: `localhost,14333`
- Database: `HotFixDemo`
- User: `sa`
- Password: `Your_strong_Password123!` (override with `MSSQL_SA_PASSWORD`)

`demo-api` defaults to this connection string through `appsettings.json` and can be overridden with:

- `HFA_ConnectionStrings__DemoDb`
- `HFA_Database__Provider`
