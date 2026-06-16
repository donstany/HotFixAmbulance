---
name: serilog-mapping
description: Authoritative mapping between Serilog log-event properties (as emitted by myPos .NET APIs) and HotFixAmbulance's `LogEntry` domain type. Use when adding/changing Elastic ingestion or the demo-api logger setup.
---

# Skill: Serilog → LogEntry mapping

Both the **demo-api** (`Serilog.Sinks.Elasticsearch`) and the **backend Elastic ingestor** must agree on these field names. If you change a name on one side, change it on the other in the same commit.

## Required Serilog enrichers on every myPos Web API

```csharp
builder.Host.UseSerilog((ctx, sp, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Application", ctx.HostingEnvironment.ApplicationName) // <-- apiName
    .Enrich.WithProperty("Version",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")
    .WriteTo.Console()
    .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(cfg.GetSection("Elastic:Uri").Value!))
    {
        IndexFormat = "logs-{0:yyyy.MM.dd}",
        AutoRegisterTemplate = true,
        TypeName = null,
        CustomFormatter = new EcsTextFormatter()
    }));
```

Request-scoped enrichers (correlation id, status code, route) are added by `app.UseSerilogRequestLogging()` with:

```csharp
opts.EnrichDiagnosticContext = (diag, http) =>
{
    diag.Set("RequestMethod", http.Request.Method);
    diag.Set("RequestPath", http.Request.Path.Value);
    diag.Set("StatusCode", http.Response.StatusCode);
    diag.Set("CorrelationId",
        http.Request.Headers.TryGetValue("X-Correlation-Id", out var v)
            ? v.ToString()
            : http.TraceIdentifier);
};
```

## Mapping table

| `LogEntry` property | Source on the Elastic document          | Notes |
|---------------------|-----------------------------------------|-------|
| `TimestampUtc`      | `@timestamp`                            | parse as UTC, fail if missing |
| `Severity`          | `level`                                 | map `Fatal/Error/Warning`; ignore others |
| `ApiName`           | `fields.Application`                    | required; if missing the doc is skipped |
| `ServiceVersion`    | `fields.Version`                        | optional |
| `ExceptionType`     | `fields.ExceptionType` ?? `exceptions[0].type` | optional |
| `Message`           | `message` (templated) — fallback `exceptions[0].message` | normalize whitespace |
| `RequestMethod`     | `fields.RequestMethod`                  | optional |
| `Endpoint`          | `fields.RequestPath`                    | optional |
| `HttpStatus`        | `fields.StatusCode`                     | optional, int |
| `CorrelationId`     | `fields.CorrelationId`                  | optional |
| `StackFile`         | first non-framework frame from `exceptions[0].stacktrace` | used by `FixHintBuilder` |
| `StackSymbol`       | method name from the same frame         | used by `FixHintBuilder` |

## Normalization rules used by `HeuristicAnalyzer`

The grouping key is `(ExceptionType, NormalizedMessage, Endpoint)` where `NormalizedMessage` is built by:

1. Lowercase.
2. Replace GUIDs, numbers ≥ 4 digits, and quoted strings with `<n>`, `<id>`, `<str>`.
3. Collapse repeated whitespace.

Keep this list in sync with `analysis-rules.json`.
