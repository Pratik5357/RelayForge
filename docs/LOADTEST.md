# Load test: throughput vs workers

**Result: adding a second worker process made RelayForge slower, not faster (143 → 98 tasks/s).** The bottleneck is the per-job row lock that serializes every change to one job, not the claim query or worker CPU.

## Setup

- Stack: `docker compose --profile stack up -d --build --scale worker=N` on one MacBook (Docker Desktop, arm64). Postgres, the API and every worker share the same machine.
- Each worker process runs `WorkerPool:Concurrency=4` consumers.
- Load: `tools/RelayForge.LoadTest --jobs 1000 --tasks 10 --duration 10`. That's 1,000 jobs, each with a root task, 8 parallel middle tasks and a sink task (10,000 simulated tasks of 10 ms each), submitted 16 at a time.
- Throughput is end to end: tasks finished ÷ (last job finished − first submit).
- `pg_stat_statements` was reset before each run.

## Results

| Worker processes | Consumers | Tasks/s | Wall time | Avg wait, job row lock | Avg claim query |
|---|---|---|---|---|---|
| 1 | 4 | 143 | 70 s | 1.1 ms | 3.4 ms |
| 2 | 8 | 98 | 102 s | 11.5 ms | 11.4 ms |

The 4- and 8-worker runs were stopped. The machine was already above 80% CPU, and two points were enough to show the trend.

## Why it plateaus (and then drops)

Top statements by total time, 2 workers:

| Statement | Calls | Avg | Total |
|---|---|---|---|
| `SELECT 1 FROM "Jobs" WHERE "Id" = $1 FOR UPDATE` | 21,000 | 11.5 ms | 242 s |
| Claim (`WITH candidate AS (… FOR UPDATE OF t SKIP LOCKED) …`) | 10,028 | 11.4 ms | 115 s |
| Job list for the load tool's polling | 810 | 8.8 ms | 7 s |

1. **The job row lock.** Every unit that changes a job takes its row lock first: start, completion, reclaim and cancel. This is the F5 fix. In this DAG, the 8 middle tasks of a job become Ready together, run together and finish together. Their 16 start and completion units then queue on that one row. With one worker process only 4 consumers compete for it. With two processes, 8 can be busy on the same job at once, and each waits behind the others. Lock time went up 10×, while useful work per task stayed the same.
2. **The claim query.** It got 3× slower with more consumers. The partial index on Ready rows keeps the scan small. But the priority aging term (`priority + age / AgingSeconds`) depends on `now()`, so no index can serve the `ORDER BY`. Every claim sorts all Ready rows, and with more consumers more claims run during the same sorts.
3. **Shared hardware.** Postgres, the API, the workers and the load generator all ran on one laptop. Extra worker processes took CPU from Postgres.

## What would raise the ceiling

- **Shorter time under the job lock.** The started event doesn't need the lock: it could be numbered with a per-job sequence (`UPDATE "Jobs" SET "EventSequence" = "EventSequence" + 1 RETURNING`) instead. That would remove about half of the lock acquisitions. The advance step could also load only the dependents of the finished task instead of the whole job.
- **Cheaper claims.** Claim a batch per round trip (`LIMIT n`) and hand the rows to local consumers. Also replace aging with a stored `EffectivePriority` that a periodic job bumps, so the claim can use an index on `(EffectivePriority DESC, ReadyAt)`.
- **Real hardware separation** before reading too much into absolute numbers: Postgres on its own machine, and the workers elsewhere.

## Reproduce

```bash
docker compose --profile stack up -d --build --scale worker=1
docker exec relayforge-postgres psql -U postgres -d relayforge -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements"
dotnet run -c Release --project backend/tools/RelayForge.LoadTest -- --api http://localhost:8080 --jobs 1000 --tasks 10 --duration 10
docker exec relayforge-postgres psql -U postgres -d relayforge -c \
  "SELECT calls, round(mean_exec_time::numeric,2) AS avg_ms, left(query, 80) FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 5"
```

Scale with `docker compose --profile stack up -d --scale worker=N worker`. A run of 10,000 tasks keeps a laptop busy for one to two minutes.
