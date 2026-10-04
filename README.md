# RelayForge

RelayForge is a small distributed job scheduler, built to be watched while it runs — not just read about. You submit a "job" made of one or more steps (with dependencies between them), and RelayForge runs those steps, tracks their progress, and shows you exactly what's happening as it happens. See `docs/PROJECT_OVERVIEW.md` for the plain-language pitch and `docs/PROJECT_SPEC.md` for the original learning-project spec.

## Current status: Phase 1 of 3 — Core Engine

This is a from-scratch rebuild. Phase 1 covers:

- DAG (dependency graph) modeling and execution, backed by PostgreSQL via EF Core.
- Topological ordering and cycle detection.
- An in-process worker pool (`System.Threading.Channels`) that runs independent steps in parallel and respects dependencies.
- A minimal API to submit a job and check its status.
- A frontend to submit a job by hand and watch it run (polling-based).

**Not built yet** (later phases): retry with backoff, dead-lettering, lease/heartbeat crash recovery, job cancellation, live push updates (SignalR), the six-scenario guided demo tour, and a real DAG graph visualization. Distributed workers (RabbitMQ/Redis) and the observability/deployment stack (Prometheus, Grafana, Docker, CI) are deferred until Docker is back in the picture.

**Known Phase 1 limitation:** the worker queue is purely in-memory. If the API process restarts mid-job, that job stays stuck `Running` with no further progress — there's no lease/reclaim mechanism yet.

> Full list of settings, example values and troubleshooting: see [ENVIRONMENT.md](ENVIRONMENT.md).

## Prerequisites

- .NET 8 SDK
- Node.js 18+ and npm
- A PostgreSQL database reachable via connection string. If you don't already have one, run it in a **dedicated Docker container just for the database** (nothing else is containerized):

  ```bash
  docker run -d --name relayforge-postgres \
    -e "POSTGRES_PASSWORD=<choose-a-password>" \
    -e "POSTGRES_DB=relayforge" \
    -p 5433:5432 \
    postgres:16
  ```

  (Port `5433`, not the default `5432`, so it doesn't collide with a local Postgres.) Any other reachable PostgreSQL instance also works; you just need its connection string.

## Backend setup

```bash
cd backend
```

1. Set your real connection string as a local user secret (never committed — `appsettings.Development.json` only holds a placeholder):

   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5433;Database=relayforge;Username=postgres;Password=<your-password>" --project src/RelayForge.Api
   ```

2. Apply the initial migration (creates `Jobs`, `JobTasks`, `TaskDependencies` tables):

   ```bash
   dotnet tool run dotnet-ef database update -p src/RelayForge.Infrastructure -s src/RelayForge.Api
   ```

3. Run the API:

   ```bash
   dotnet run --project src/RelayForge.Api
   ```

   It listens on `https://localhost:7054` (and `http://localhost:5284`) by default. Confirm it's up: `GET https://localhost:7054/health` should return `{"status":"ok"}`.

Run the domain tests any time with:

```bash
dotnet test backend/tests/RelayForge.Domain.Tests
```

## Frontend setup

```bash
cd frontend
cp .env.local.example .env.local   # points NEXT_PUBLIC_API_URL at the API above
npm install
npm run dev
```

Open `http://localhost:3000` — it redirects to `/jobs`.

## Trying it out

On `/jobs/new`, add a few tasks and mark dependencies between them:

- A **chain** (B depends on A, C depends on B) demonstrates ordering — steps run strictly in sequence.
- **One task with two independent children** (both depend only on the first, not on each other) demonstrates parallelism — both children start together and the job finishes in roughly the time of the slower one, not the sum of both.

Submit, and you'll land on the job's live-updating detail page.

## Project layout

```
RelayForge/
├── README.md
├── docs/                 Original spec + plain-language project overview
├── backend/
│   ├── RelayForge.sln
│   ├── src/
│   │   ├── RelayForge.Api/            Minimal API host
│   │   ├── RelayForge.Domain/         Entities, enums, DAG algorithms (no EF/ASP dependency)
│   │   └── RelayForge.Infrastructure/ EF Core, in-process worker pool, orchestration
│   └── tests/RelayForge.Domain.Tests/ Unit tests for the DAG logic
└── frontend/              Next.js (TypeScript, App Router, Tailwind)
```
