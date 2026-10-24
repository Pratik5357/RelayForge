# RelayForge

RelayForge is a small distributed job scheduler, built to be watched while it runs — not just read about. You submit a "job" made of one or more steps (with dependencies between them), and RelayForge runs those steps, tracks their progress, and shows you exactly what's happening as it happens. See `docs/PROJECT_OVERVIEW.md` for the plain-language pitch and `docs/PROJECT_SPEC.md` for the original learning-project spec.

## Current status: v2

v2 turns RelayForge from a demo of job semantics into an engine that does real work, scales across processes and can be operated. The plan it follows is the five-phase v2 roadmap. The decisions behind each phase are in [docs/DESIGN-v2.md](docs/DESIGN-v2.md).

- **Tasks do real work.** Each task has a handler `type` and a JSON `payload`. Built-in types are `simulated` (all demos), `delay`, `http.request` and `retention`. A handler returns success with an output, a retryable failure or a fatal failure. Outputs are passed to dependent tasks by key.
- **Postgres is the queue.** Task states: `Pending` (waiting on a dependency) → `Ready` (claimable) → `Running` → `Succeeded`, `Failed` (fatal), `DeadLettered` (out of retries) or `Cancelled`. A retry is a `Ready` row with a future `NextAttemptAt`.
- **Leases are heartbeated.** A claim lasts 15 s and is renewed every 5 s, so a healthy task of any length keeps it, and a dead worker's task is reclaimed within 15 s. Cancel stops running tasks too.
- **Every attempt is recorded.** You can see who ran it, when and how it ended. The demo has two chaos scenarios (worker crash, frozen "zombie" worker) and a guarantees panel.
- **Events go through an outbox.** They're numbered per job, delivered at least once, and replayable with `GET /api/jobs/{id}/events?after=N`.
- **Workers scale out** as separate `RelayForge.Worker` processes. The lease sweep and the scheduler are leader-elected with a Postgres advisory lock.
- **Tasks can run isolated** in their own process (or a container), which is killed at the time limit. Each attempt streams live logs.
- **Schedules** run jobs on cron, with misfire and overlap policies. RelayForge runs its own nightly retention job this way.
- **Operator actions:** redrive a failed step (it reopens the run), pause and resume a run, idempotent submit (`Idempotency-Key` header), priority with aging, and per-tenant running caps.
- **Observability:** OpenTelemetry traces span the API and the workers across the queue, and metrics feed a Grafana dashboard.

### Guarantees

- **No ready work is lost on a crash or restart.** Readiness is a row in the database, not an entry in process memory.
- **A task's outcome, its consequences and its events commit together.** One transaction marks a task done, promotes its dependents (or finalizes the job) and writes the outbox events.
- **A stale worker can't overwrite a newer attempt.** Every write after a claim is fenced on the lease owner and the attempt number. A late result is recorded as rejected and changes nothing.
- **Mutations of one job are serialized** by its row lock, so concurrent completions can't both (or neither) finalize it.
- **One scheduled time creates at most one run**, enforced by a unique index rather than by the scheduler's lock.
- **Execution is at-least-once.** Handlers with side effects should send the task's `IdempotencyKey` downstream, as `http.request` does in the `Idempotency-Key` header.

How far it scales on one machine, and why: [docs/LOADTEST.md](docs/LOADTEST.md). In short, on capped CPUs, two worker processes give 1.8× the throughput of one, and then Postgres CPU is the limit.

> Full list of settings, example values and troubleshooting: see [ENVIRONMENT.md](ENVIRONMENT.md).

## Prerequisites

- .NET 8 SDK
- Node.js 18+ and npm
- A PostgreSQL database reachable via connection string. If you don't already have one, run the **database-only Docker Compose service** at the repo root (nothing else is containerized):

  ```bash
  cp .env.example .env
  docker compose up -d
  ```

  (Port `5433`, not the default `5432`, so it doesn't collide with a local Postgres.) Any other reachable PostgreSQL instance also works; you just need its connection string.

## Backend setup

```bash
cd backend
```

1. Nothing to configure for the local Docker database: `appsettings.Development.json` already points at it (`localhost:5433`, password from `.env.example`). To use a different database instead, override it with a user secret:

   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" "Host=...;Port=...;Database=...;Username=...;Password=..." --project src/RelayForge.Api
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

Run the tests with:

```bash
dotnet test backend/tests/RelayForge.Domain.Tests        # pure DAG logic, no dependencies
dotnet test backend/tests/RelayForge.IntegrationTests    # real host + Postgres; needs Docker running
```

The integration tests start a throwaway Postgres container with Testcontainers and prove the guarantees above. They cover a restart between submit and claim, worker crashes and zombies, concurrent completions, cancel and graceful shutdown, handlers (including real HTTP), the outbox, several worker hosts with leader failover, schedules (including daylight-saving edge cases), the process harness, redrive, pause, fairness and retention.

## Running the scale-out stack

One command runs the API, several worker processes, Jaeger, Prometheus and Grafana in Docker:

```bash
docker compose --profile stack up -d --build --scale worker=3
```

| Service | URL |
|---|---|
| API | http://localhost:8080 (point the frontend at it with `NEXT_PUBLIC_API_URL=http://localhost:8080`) |
| Jaeger (traces) | http://localhost:16686, service `relayforge-api` |
| Prometheus | http://localhost:9090 |
| Grafana (dashboard) | http://localhost:3001 |

`docker compose up -d` without the profile still starts only Postgres, as before. The stack uses the same database, so stop your locally run API first. After the first API has migrated the database, `--scale api=2` adds a second API on port 8081; Redis carries live updates between them.

To try container isolation (`"isolation": "Container"`), build the runner image once: `docker build --target runner -t relayforge-runner backend`. The worker that runs those tasks needs the `docker` CLI, so run it on your machine (`dotnet run`), not inside Compose.

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
│   │   ├── RelayForge.Api/            HTTP + SignalR host, outbox dispatcher (runs workers too by default)
│   │   ├── RelayForge.Worker/         Worker-only host: worker pool, lease sweep, scheduler
│   │   ├── RelayForge.Runner/         Runs one task attempt in its own process (process/container isolation)
│   │   ├── RelayForge.Handlers/       Task handler contract and the built-in handlers
│   │   ├── RelayForge.Domain/         Entities, enums, DAG algorithms (no EF/ASP dependency)
│   │   └── RelayForge.Infrastructure/ EF Core, Postgres queue, orchestration, scheduling, telemetry
│   ├── tests/
│   │   ├── RelayForge.Domain.Tests/       Unit tests for the DAG logic
│   │   └── RelayForge.IntegrationTests/   Testcontainers tests for every v2 guarantee
│   └── tools/RelayForge.LoadTest/     Load generator (see docs/LOADTEST.md)
├── deploy/                Prometheus and Grafana config for the Compose stack
└── frontend/              Next.js (TypeScript, App Router, Tailwind)
```
