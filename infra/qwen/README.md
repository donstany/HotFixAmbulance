# Local Qwen runtime for HotFixAmbulance

Runs **Qwen** (`qwen2.5:3b`) locally, CPU-only, so the demo can exercise
HotFixAmbulance's LLM analysis strategy (`Analysis:Strategy=Llm`) and show the
"🤖 Qwen" badge in the UI. Qwen is served by the [Ollama](https://ollama.com)
runtime image, which exposes the `/api/chat` endpoint `OllamaLlmClient` speaks.
Not suitable for anything else.

## Files

| File | Purpose |
|---|---|
| `docker-compose.yml` | CPU-only Qwen runtime on `http://localhost:11434`, models in the `hfa-qwen-data` named volume. |
| `docker-compose.corp.yml` | Overlay for corporate networks: mounts a host CA bundle and sets `SSL_CERT_FILE` so the container trusts an intercepting proxy. |
| `export-host-ca.ps1` | Exports the host's trusted CA stores to `corp-ca-bundle.crt` (git-ignored, machine-specific) for the overlay above. |
| `bootstrap.ps1` | Idempotent: waits for the runtime, pulls `qwen2.5:3b` if absent, runs a `/api/chat` JSON sanity probe. |

## Corporate networks (TLS interception)

If `ollama pull` fails with `x509: certificate signed by unknown authority`, a
corporate proxy is MITM-terminating TLS and the container does not trust the
corporate root CA. Export the host CA bundle and start the runtime with the
overlay (this is what `scripts/demo.ps1` does automatically):

```powershell
powershell -File infra/qwen/export-host-ca.ps1
docker compose -f infra/qwen/docker-compose.yml -f infra/qwen/docker-compose.corp.yml up -d
powershell -File infra/qwen/bootstrap.ps1
```

`corp-ca-bundle.crt` is machine-specific and git-ignored. On a network without
interception, skip the overlay and use `docker-compose.yml` alone.

## Why a bootstrap step exists

The runtime container starts with **no models**. It answers on `:11434`
immediately, but `OllamaLlmClient` gets a 404 until `qwen2.5:3b` is pulled.
`bootstrap.ps1` pulls it into the volume so it survives `down`/`up`.

## Typical usage

```powershell
# Started automatically by scripts/demo.ps1. To run it standalone:
docker compose -f infra/qwen/docker-compose.yml up -d
powershell -File infra/qwen/bootstrap.ps1

# Tear down (keeps the model volume):
docker compose -f infra/qwen/docker-compose.yml down
# Tear down (wipes the model volume — next run re-downloads ~2GB):
docker compose -f infra/qwen/docker-compose.yml down -v
```

## Running `bootstrap.ps1` directly

```powershell
powershell -File infra/qwen/bootstrap.ps1                       # default model qwen2.5:3b
powershell -File infra/qwen/bootstrap.ps1 -Model qwen2.5:7b     # a larger Qwen
powershell -File infra/qwen/bootstrap.ps1 -OllamaUri http://localhost:11434
```

The script prints `chat probe: ok` on success. On a pull failure check
`docker logs hfa-qwen` and that Docker Desktop has enough disk.
