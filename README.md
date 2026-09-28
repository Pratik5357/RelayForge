# RelayForge

A mini distributed workflow engine (Temporal / Airflow in spirit). Jobs are DAGs of tasks. The system retries with backoff, reclaims crashed workers via leases, scales out over RabbitMQ, and exposes live status over REST + SignalR.

This is a learning / portfolio project. Code favors being obvious over being clever.

## Repo layout

```
/backend      .NET 8 solution — the engine (RelayForge.sln)
  /src        TaskScheduler.{Api,Worker,Domain,Infrastructure,Dashboard}
  /tests      unit + integration tests
/frontend     Next.js 16 dashboard (App Router, TypeScript, Tailwind)
/deploy       docker-compose, prometheus, grafana, k6
/docs/adr     architecture decision records
```

`PROJECT_SPEC.md` describes the original phase plan; the only deviation is that `src/` and `tests/` now live under `backend/` so the frontend can sit beside them.

## Run locally (no Docker)

Requires the .NET 8 SDK and Node 22.

**Backend** (terminal 1):

```bash
dotnet test backend/RelayForge.sln
dotnet run --project backend/src/TaskScheduler.Api
```

API: [http://localhost:5053/swagger](http://localhost:5053/swagger)

SQLite file `relayforge.dev.db` is created next to the API. In-process workers consume a `Channel<T>` — no Redis or RabbitMQ needed.

**Frontend** (terminal 2):

```bash
cd frontend
npm install
npm run dev
```

Dashboard: [http://localhost:3000](http://localhost:3000). It reads `NEXT_PUBLIC_API_BASE_URL` (see `frontend/.env.example`, default `http://localhost:5053`).

The overview page is a guided tour: six one-click demos that each run a real job and say what to watch for (ordering, parallelism, retry with backoff, dead-lettering, partial failure, cancellation). Nothing needs filling in — that is the intended way to see the system, and the intended way to demo it to someone else.

The Blazor dashboard from Phase 5 is still there if you want to compare the two:

```bash
dotnet run --project backend/src/TaskScheduler.Dashboard
```

### Submit a DAG

From the frontend: **Submit** → pick a sample → *Submit*. Or over HTTP:

```bash
curl -s http://localhost:5053/api/jobs -H 'Content-Type: application/json' -d '{
  "name": "pipeline",
  "tasks": [
    { "key": "extract", "type": "delay", "payload": { "milliseconds": 200 } },
    { "key": "transform", "type": "echo", "payload": { "step": "transform" }, "dependsOn": ["extract"] },
    { "key": "load", "type": "echo", "payload": { "step": "load" }, "dependsOn": ["transform"] }
  ]
}'
```

Handlers: `echo`, `delay` (`milliseconds`), `fail` (`succeedOnAttempt` for retry demos).

REST:

- `POST /api/jobs` submit
- `GET /api/jobs` list
- `GET /api/jobs/{id}` status (includes the DAG)
- `GET /api/jobs/{id}/tasks` tasks only
- `POST /api/jobs/{id}/cancel`
- `GET /api/system` effective wiring (database/queue/lock/worker config) + counts, for the dashboard's wiring panel
- `GET /metrics` Prometheus
- `GET /health`
- SignalR hub `/hubs/jobs` method `Subscribe(jobId)`, event `jobChanged`

## Run the full stack (Phase 3–6)

Docker was not available in the environment this repo was first built in. With Docker installed:

```bash
docker compose -f deploy/docker-compose.yml up --build
```

| Service | URL |
| --- | --- |
| Next.js dashboard | http://localhost:3001 |
| API | http://localhost:5053/swagger |
| Blazor dashboard | http://localhost:5081 |
| Grafana (admin/admin) | http://localhost:3000 |
| Prometheus | http://localhost:9090 |
| RabbitMQ UI | http://localhost:15672 |

Compose runs **two worker replicas**, Postgres, RabbitMQ, Redis (RedLock), Prometheus, and Grafana.

## Load test

```bash
k6 run -e BASE_URL=http://localhost:5053 deploy/k6/jobs.js
```

Recorded numbers (local SQLite + in-process workers, 10 VUs / 30s, machine-dependent — re-run k6 on your stack and replace these):

| Metric | Value |
| --- | --- |
| Checks | see k6 output after you run the script |
| http_req_duration p95 | *not recorded here yet — Docker/k6 were not installed when this README was written* |
| Throughput | *run `k6 run deploy/k6/jobs.js` against a live API and paste results* |

## Architecture

```
Browser (Next.js) → API → Postgres
                       → ITaskQueue (Channel or RabbitMQ)
                            → Workers (in-process or TaskScheduler.Worker)
                                 → Redis lock (optional)
                                 → TaskExecutor (lease claim, idempotency, handlers, retry/DLQ)
                                 → SignalR → Browser
```

Retry math lives in `ExponentialBackoffRetryPolicy` (hand-rolled exponential delay + jitter). `PollyRetryAdapter` maps the same delays onto a Polly pipeline.

## Phases in this repo

1. **Core engine** — EF models, Kahn topological sort, `Channel<T>` workers, submit/status  
2. **Reliability** — backoff + jitter, leases + heartbeats + reaper, dead-letter table, idempotency keys  
3. **Distributed scale** — MassTransit/RabbitMQ, worker process, RedLock  
4. **Observability** — Serilog, OpenTelemetry (OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set), prometheus-net, Grafana  
5. **API & dashboard** — list/cancel, Blazor live DAG view, Next.js dashboard  
6. **Deploy** — compose, GitHub Actions, k6, ADRs in `docs/adr`
