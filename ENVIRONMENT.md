# Environment and configuration

Everything you need to set to run RelayForge from a fresh clone. There are three places to configure: the database, the backend (.NET API) and the frontend (Next.js). No real secrets are committed to this repo.

## What you need installed

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 8 | Backend API |
| Node.js | 18 or newer (20+ recommended) | Frontend |
| Docker | any recent | Local PostgreSQL (skip if you have another PostgreSQL) |

## Quick start (three terminals)

1. **Database:** start PostgreSQL and set the connection string (below).
2. **Backend:** `cd backend && dotnet tool restore && dotnet tool run dotnet-ef database update -p src/RelayForge.Infrastructure -s src/RelayForge.Api && dotnet run --project src/RelayForge.Api`
3. **Frontend:** `cd frontend && cp .env.local.example .env.local && npm install && npm run dev`, then open http://localhost:3000

Check the API is up: `http://localhost:5284/health` should return `{"status":"ok"}`.

## 1. Database (PostgreSQL)

Run PostgreSQL in Docker with the compose file at the repo root:

```bash
cp .env.example .env        # the local password already matches appsettings.Development.json
docker compose up -d        # starts relayforge-postgres on localhost:5433
```

- Data is kept in the `relayforge-pgdata` volume across restarts; `docker compose down -v` wipes it.
- Port `5433` on your machine maps to `5432` in the container, so it does not clash with a Postgres you may already run.
- Any reachable PostgreSQL works instead. You only need its connection string.

## 2. Backend settings (`backend/src/RelayForge.Api`)

| Setting | Required | Example value | Notes |
|---|---|---|---|
| `ConnectionStrings:Default` | Preset for local | `Host=localhost;Port=5433;Database=relayforge;Username=postgres;Password=<from .env.example>` | `appsettings.Development.json` already holds this for the local Docker database. Override it (user secret or env var) for any other database, and never commit a real hosted database password. |
| `Cors:AllowedOrigins` | **In production** | `https://app.example.com` | Your deployed frontend URL(s). Env var form: `Cors__AllowedOrigins__0=https://app.example.com`. Defaults to `http://localhost:3000`. |
| `Database:MigrateOnStartup` | No (default `false`) | `true` | Applies pending migrations when the API starts. |
| `WorkerPool:RunInApi` | No (default `true`) | `false` | Whether the API process also runs workers, the lease sweep and the scheduler. Set `false` when separate `RelayForge.Worker` processes do that (the Compose stack). |
| `WorkerPool:Concurrency` | No (default `4`) | `4` | How many steps one process runs at the same time. |
| `WorkerPool:PollIntervalMs` | No (default `500`) | `500` | How often an idle worker checks the database for due work, such as a retry whose wait has passed. New steps wake workers immediately. |
| `WorkerPool:ShutdownGraceMs` | No (default `5000`) | `5000` | On shutdown, how long running steps may finish before they're handed back (without using up a try). |
| `Reliability:BaseBackoffMs` | No (default `500`) | `500` | First retry delay. It doubles each retry. |
| `Reliability:MaxBackoffMs` | No (default `8000`) | `8000` | Longest retry delay. |
| `Reliability:DefaultMaxAttempts` | No (default `4`) | `4` | Tries per step when a step does not set its own. |
| `Reliability:LeaseDurationMs` | No (default `15000`) | `15000` | How long a worker can go without a heartbeat before its step is treated as crashed and reclaimed. Heartbeats run every third of this. |
| `Reliability:SweepIntervalMs` | No (default `1000`) | `1000` | How often the system looks for crashed steps. |
| `Reliability:DefaultTimeoutMs` | No (default `300000`) | `300000` | Time limit for one try of a step that doesn't set its own `timeoutMs`. |
| `Fairness:AgingSeconds` | No (default `60`) | `60` | A waiting step's priority goes up by 1 for every this many seconds it waits, so nothing starves. |
| `Fairness:MaxRunningPerTenant` | No (default `0` = off) | `5` | Soft cap on running steps per `tenantId`. |
| `Harness:RunnerPath` | No | `/app/RelayForge.Runner.dll` | Runner for `"isolation": "Process"`. Defaults to the one shipped next to the API/worker. |
| `Harness:ContainerImage` | No (default `relayforge-runner`) | `relayforge-runner` | Image for `"isolation": "Container"` (build it with `docker build --target runner -t relayforge-runner backend`). Needs the `docker` CLI on the worker's machine. |
| `Retention:Days` / `Retention:Cron` | No (defaults `7`, `0 3 * * *`) | `7` | The built-in retention schedule, created the first time a scheduler starts. Change it later on the Schedules page. |
| `SignalR:Redis` | Only for 2+ API instances | `localhost:6379` | Redis backplane, so a browser on one API instance gets pushes sent by another. |
| `Otel:TracesEndpoint` | No | `http://localhost:4317` | OTLP/gRPC endpoint for traces (Jaeger). Off when unset. |
| `Otel:MetricsEndpoint` | No | `http://localhost:9090/api/v1/otlp/v1/metrics` | OTLP/HTTP endpoint for metrics (Prometheus with `--web.enable-otlp-receiver`). Off when unset. |
| `ASPNETCORE_ENVIRONMENT` | No | `Development` | Set automatically by `dotnet run` through `launchSettings.json`. |

**How to set the connection string (pick one):**

- **User secrets (recommended for local use, stored outside the repo):**
  ```bash
  cd backend
  dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5433;Database=relayforge;Username=postgres;Password=<your-password>" --project src/RelayForge.Api
  ```
- **Environment variable** (also how you would set it on a server; double underscore replaces the colon):
  ```bash
  export ConnectionStrings__Default="Host=localhost;Port=5433;Database=relayforge;Username=postgres;Password=<your-password>"
  ```
  Any setting in the table can be set this way, for example `Reliability__DefaultMaxAttempts=6`.

`appsettings.Development.json` only contains a placeholder (`REPLACE-ME`). Do not put your real password in it.

**Create the tables** (run once, and again after pulling new migrations):
```bash
cd backend
dotnet tool restore
dotnet tool run dotnet-ef database update -p src/RelayForge.Infrastructure -s src/RelayForge.Api
```

**Ports:** the API listens on `http://localhost:5284` (and `https://localhost:7054` with the https profile).

## 3. Frontend settings (`frontend/.env.local`)

Copy the example: `cp .env.local.example .env.local`.

| Variable | Required | Example value | Notes |
|---|---|---|---|
| `NEXT_PUBLIC_API_URL` | Yes | `http://localhost:5284` | Where the browser finds the API. It is read when the app starts, so restart `npm run dev` after changing it. |

`NEXT_PUBLIC_` variables are visible to anyone using the site, so never put a secret in one.

## Things that commonly go wrong

- **"Could not reach the API" in the browser:** the backend is not running, or `NEXT_PUBLIC_API_URL` points at the wrong port. Use `http://localhost:5284` unless you started the https profile.
- **Works on `localhost:3000` but not from your phone or another machine:** the backend only accepts requests from `http://localhost:3000` (CORS, in `backend/src/RelayForge.Api/Program.cs`). Add your origin there to use another address. Next.js also needs that address listed under `allowedDevOrigins` in `frontend/next.config.ts`.
- **API fails at startup with a database error:** the connection string is empty or wrong, the container is not running (`docker ps`), or the migration has not been applied.
- **`docker compose up` fails with "Set POSTGRES_PASSWORD":** create `.env` from `.env.example`.
- **A run seems stuck after you restarted the API:** a step that was mid-try is reclaimed once its lease expires (`Reliability:LeaseDurationMs`, 15 s by default), then retried. Steps that hadn't started yet resume right away. A normal stop (Ctrl+C) hands running steps back immediately.
- **`docker compose --profile stack up` fails with "mkdir /host_mnt/...: operation not permitted":** that is macOS refusing Docker Desktop access to the folder. The stack itself uses no bind mounts, so this only comes from an older checkout. Pull and rebuild.
- **Port 5433 or 3000 already in use:** change the left side of `5433:5432` in `docker-compose.yml`, or start Next.js with `npm run dev -- -p 3001` (then the CORS origin above must change too).

## Deploying (Railway backend, Vercel frontend)

**Railway (API + Postgres):**
1. Add a PostgreSQL service, then a service from this repo with **Root Directory** `backend` (it builds `backend/Dockerfile`).
2. On the API service set variables:
   - `DATABASE_URL` = `${{Postgres.DATABASE_URL}}` (the API converts the `postgres://` URL itself; or set `ConnectionStrings__Default` instead)
   - `Database__MigrateOnStartup` = `true`
   - `Cors__AllowedOrigins__0` = your Vercel URL, e.g. `https://relayforge.vercel.app` (no trailing slash)
3. Keep the API to **one replica**: the job queue is in Postgres, but live updates (SignalR) have no backplane yet, so a browser connected to one replica would miss events from another. Separate worker processes need the Dockerfile's `worker` stage, while Railway builds its last stage (the API), so on Railway the API keeps running the workers itself (`WorkerPool__RunInApi` stays `true`). Health check path: `/health`.

**Keeping a free Supabase database awake:** Supabase pauses free projects after a week without queries. The running API queries the database every second, so this only matters if the API is down for days. Keep the API always on (don't enable Railway's app sleeping). `GET /health/db` runs a real query, so it's a quick way to check the API can reach the database.

**Vercel (frontend):** set **Root Directory** to `frontend` and the env var `NEXT_PUBLIC_API_URL` to the Railway public URL (`https://<name>.up.railway.app`), then redeploy. Preview deployments use different URLs, so add each one you use to `Cors__AllowedOrigins__N`.
