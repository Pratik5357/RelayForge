# RelayForge v2 design decisions

One entry per decision: the options considered, what was chosen, and why. Written as each phase lands.

## Phase 1: durable core

### Where "this task can run now" lives

- **Options:** an in-memory `Channel<T>` (v1); a broker such as RabbitMQ; the `JobTasks` table itself.
- **Chosen:** the table. A new `Ready` state means "every dependency succeeded, claim me". Workers claim with `UPDATE … WHERE "Id" = (SELECT … FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING …`.
- **Why:** readiness has to commit in the same transaction as the change that caused it, or a crash between the two loses it. v1 lost work exactly there: a job submitted just before a restart stayed `Pending` forever. With a broker, the state change and the publish are two systems, which needs an outbox to be safe. Postgres gives both in one commit. `SKIP LOCKED` lets many workers poll at once without blocking each other.
- **Revisit when:** claim throughput becomes the bottleneck. The roadmap's Phase 4 load test will measure where that is. A partial index on `ReadyAt WHERE "State" = 'Ready'` keeps each claim cheap however many finished tasks the table holds.

### Retries

- **Options:** keep a sweep that re-enqueues due retries (v1); store retries as `Ready` rows with a future `NextAttemptAt`.
- **Chosen:** `Ready` rows. The claim query skips them until they're due.
- **Why:** v1's sweep enqueued in memory and then cleared `NextAttemptAt`. A crash between the two left a `Pending` task that nothing would look at again. Without the sweep step, that gap is gone. The sweep now only reclaims expired leases.

### Waking idle workers

- **Options:** poll only; Postgres `LISTEN/NOTIFY`; an in-process signal plus a poll.
- **Chosen:** an in-process `WorkSignal` plus a 500 ms poll (`WorkerPool:PollIntervalMs`).
- **Why:** with one process, the signal makes new work start right away, and the poll covers what the signal can't see: retries coming due, and work promoted by another process. Losing a signal costs at most one poll interval and never loses a task. `LISTEN/NOTIFY` becomes worth adding when workers run in separate processes (Phase 4).

### Stale writes after a lease changes hands

- **Options:** trust the lease; check ownership by hand on each write; EF Core concurrency tokens.
- **Chosen:** both checks. Every post-claim write requires `State = Running`, the same `LeaseOwnerId` and the same `AttemptCount`. The attempt number is the fencing token. `LeaseOwnerId` and `AttemptCount` are also EF concurrency tokens, so any tracked save of a task based on a stale read fails with `DbUpdateConcurrencyException`.
- **Why:** a lease alone isn't safe, because a paused worker doesn't know its lease expired. Without a fence, a worker that stalls past its lease can overwrite the attempt that replaced it. Its side effects would run twice and its dependents would start while the newer attempt is still running.

### Serializing changes to one job

- **Options:** optimistic retries on the job row; `pg_advisory_xact_lock`; `SELECT … FOR UPDATE` on the job row.
- **Chosen:** the row lock, taken first by every unit that changes a job (`JobOrchestrator.ExecuteAsync`): submit, completion, lease reclaim, cancel.
- **Why:** with two completions at once, each can read the other's task as still `Running`. Then neither promotes the shared dependent, or both finalize the job. A row lock is the simplest primitive that's visible in `pg_locks`. It serializes one job only, so other jobs keep running in parallel. Lock order is always job row, then task rows, so these units can't deadlock each other. The claim touches only `Ready` rows and needs no job lock.

### Clocks

- **Chosen:** lease times and the claim's due check use the database clock (`now()`). The retry delay is still computed in the app (`NextAttemptAt = UtcNow + backoff`).
- **Why and known gap:** with one API process, the skew between the app clock and the database clock is negligible. Once workers run on several machines (Phase 4), retry times should move to `now() + interval` in SQL as well.

### Retry and release budgets vs the fencing token

- **Chosen:** `AttemptCount` only ever goes up. A graceful-shutdown hand-back and a redrive raise `MaxAttempts` instead of lowering `AttemptCount`.
- **Why:** the attempt number is the fencing token, so it must be monotonic. Reusing a number would let a stale worker from the earlier attempt with that number pass the fence. It also keeps `(TaskId, Attempt)` unique in the attempt history.

## Phase 2: leases for real work

### Lease length

- **Options:** a lease sized from the task's expected duration (v1); a long fixed lease; a short lease renewed by a heartbeat.
- **Chosen:** a 15 s lease (`Reliability:LeaseDurationMs`), renewed every 5 s by a heartbeat loop with its own DbContext. The renewal is fenced like every other write.
- **Why:** real work has no known duration. A long lease means a crashed worker's task sits for that long. A short one without renewal reclaims healthy long tasks and creates zombies. Renewing at a third of the lease survives one slow renewal. Detection time (15 s) is traded against false positives (two missed renewals in a row).

### Cancelling running work

- **Chosen:** cooperative. The cancel commits `CancellationRequestedAt`, then sends `NOTIFY relayforge_cancel`, and every worker cancels its attempts for that job at once. The heartbeat also reports a pending cancel, as a fallback when the notification is lost.
- **Limit:** a handler that ignores its token can't be stopped in-process (.NET can't safely abort a thread). After `CancelGraceMs` the worker stops waiting and records the outcome, and the handler keeps running in the background. Process isolation (Phase 5) is the hard stop.

### Graceful shutdown

- **Chosen:** on stop, consumers stop claiming. In-flight attempts get `ShutdownGraceMs`, then are cancelled and handed back to `Ready` with outcome `Released`, without using up a try. A hard kill skips this; the lease expiring covers it.

### Attempt history

- **Chosen:** a `TaskAttempts` row is inserted in the claim statement itself (a data-modifying CTE), so an attempt can't run without a record. Its outcome is written in the same transaction as the task's. A rejected late result is noted on the attempt it came from, which is what the demo's "stale results rejected" count reads.
- **Chaos scenarios:** the simulated handler can stop its own heartbeat. With `crashOnAttempt` it then throws an exception the worker treats as "write nothing". With `pauseOnAttempt` it sleeps past the lease and reports success anyway. To the rest of the system these look exactly like a dead process and a frozen one.

## Phase 3: real work

### Handlers

- **Chosen:** `ITaskHandler` (in `RelayForge.Handlers`, with no EF or ASP.NET dependency) keyed by task `Type`, with a JSON payload validated at submit. A result is `Success(output)`, `Retryable(error, retryAfter)` or `Fatal(error)`. Fatal ends the task as `Failed`, which blocks dependents exactly as `DeadLettered` does.
- **Why a separate assembly:** the out-of-process runner must run the same handlers without the database.
- **HTTP classification:** timeouts, connection errors, 408, 429 (honoring `Retry-After`) and 5xx are retryable; other 4xx are fatal. The task's `IdempotencyKey` is sent as `Idempotency-Key`, which is how at-least-once execution becomes exactly-once effects at receivers that support it.
- **Outputs:** stored as `jsonb` on the task and handed to dependents by key. This turns the DAG into a pipeline.

### Events: transactional outbox

- **Options:** push after commit (v1, a dual write); outbox plus dispatcher.
- **Chosen:** `JobOrchestrator` writes every event to `OutboxEvents` in the same transaction as the change, numbered from `Jobs.EventSequence` under the job row lock (gap-free per job). A dispatcher in the API claims undelivered rows with `SKIP LOCKED`, pushes them to SignalR, and marks them delivered.
- **Delivery:** at least once. The browser drops sequences it has seen. On a gap or a reconnect, it fetches `GET /api/jobs/{id}/events?after=N`. The same endpoint replays a run's history when a page opens late.
- **Not done:** outgoing webhooks. They would be a second dispatcher over the same table.

### Cross-process wake-ups

- **Chosen:** Postgres `LISTEN/NOTIFY` on four channels (ready, outbox, cancel, logs), with one listener connection per process. NOTIFY is transactional (delivered only on commit) and reaches every process. Each loop still polls, so a dropped notification costs latency, never correctness.

## Phase 4: scale out

### Process split

- **Chosen:** `RelayForge.Api` (HTTP, SignalR, outbox dispatcher) and `RelayForge.Worker` (worker pool, sweep, scheduler; no HTTP). `WorkerPool:RunInApi=true` keeps the single-process deployment (Railway) working unchanged.
- **SignalR with several API instances:** not solved. There's no Redis backplane, so the API stays at one instance; any number of workers is fine. The dispatcher would need the backplane, or a sticky "one dispatcher per client" design, before the API can scale.

### Leader election

- **Chosen:** a session-level `pg_try_advisory_lock` held on a dedicated connection, for the sweep (key 727001) and the scheduler (727002). If the leader dies, Postgres closes its connection, frees the lock, and a follower takes over within 5 s. The integration test kills the leader host and watches the follower take over.
- **Why it's only an efficiency measure:** reclaim re-checks the lease under the job lock, and scheduled runs are deduplicated by a unique index. Two leaders briefly overlapping waste work but corrupt nothing.

### Observability

- **Tracing across the queue:** a table carries no trace context, so the submit request's W3C `traceparent` is stored on the job, and each `task.execute` span uses it as its parent. Scheduled runs start their own `schedule.fire` span.
- **Metrics:** queue depth (sampled by the sweep leader), queue wait (Ready to claimed), attempt duration, attempts by outcome, retries, dead letters, reclaims, stale results and submissions. They're exported over OTLP to Prometheus's native receiver, so neither host needs a scrape endpoint or a prerelease exporter package.
- **Result:** see [LOADTEST.md](LOADTEST.md). On one laptop the per-job row lock is the bottleneck. The fixes are listed there; none is implemented yet.

## Phase 5: platform

### Schedules

- **Chosen:** a `JobDefinitions` table with cron (Cronos), an IANA time zone, a template (the same JSON `POST /api/jobs` takes) and two policies. The leader-elected scheduler fires due definitions once a second. A unique index on `(DefinitionId, ScheduledFor)` makes "one run per scheduled time" a database fact. A crash between creating a run and advancing `NextFireAt` just repeats an insert that gets rejected.
- **Misfires:** `FireOnce` (one run, for the latest missed time), `Skip` (only a time under a minute old) and `CatchUp` (every missed time, oldest first, capped at 24).
- **Overlap:** `Allow`, `Forbid` (skip while a run is active) and `Replace` (cancel it, then start), borrowed from Kubernetes CronJob.
- **Daylight saving:** handled by Cronos given the zone; tests pin both cases. A spring-forward time that doesn't exist fires once, right after the jump. A fall-back time that happens twice fires once, at its first occurrence.

### Execution harness

- **Options:** in-process (default), `AssemblyLoadContext`, child process, container.
- **Chosen:** in-process by default; `"isolation": "Process"` runs `RelayForge.Runner` as a child process; `"Container"` runs the runner image under `docker run --memory --cpus --network none --read-only` (with network only for `http.request`). The invocation goes in on stdin, log lines come out on stdout and stderr, and the result comes back as one marked line. On timeout or cancel the process tree is killed.
- **Why not AssemblyLoadContext:** it helps with loading plugins, but it isn't isolation. The code still shares the process and can't be stopped.
- **Container mode** is implemented but not covered by the automated tests: it needs Docker on the worker's machine, which Railway doesn't allow.
- **Logs:** `TaskLogs` rows written in batches every 300 ms, capped at 1,000 lines per attempt, with a `NOTIFY` so the API can push "new lines" to watching browsers.

### Operator actions

- **Redrive:** moves a `DeadLettered` or `Failed` task back to `Ready` with a fresh budget. If the job had finished, it reopens. Every `Cancelled` task in a job nobody cancelled was cancelled only because it was blocked, so they all go back to `Pending`, and the normal advance step re-blocks the ones still stuck behind another failure. That keeps the reopen logic to a few lines.
- **Pause:** `PausedAt` on the job. Advance promotes nothing and the claim query skips the job. Running attempts finish.
- **Idempotent submit:** an `Idempotency-Key` header is stored with a SHA-256 hash of the body, in the same transaction as the job. The same key and body return the first job (200); the same key with a different body gets 422. A race between two identical requests is settled by the key's primary key.
- **Priority and aging:** claim order is `priority + seconds waited / AgingSeconds`. That expression depends on `now()`, so no index serves the sort. It's fine at this scale, and LOADTEST.md shows where it starts to cost.
- **Tenant caps:** a soft cap, checked in the claim query, so two workers can both see one free slot and both claim. A hard cap would need a per-tenant counter row locked by the claim, which puts a lock on the hottest path. Soft is the right default here.
- **Retention:** a built-in schedule (`relayforge.retention`, 03:00 UTC) runs the `retention` handler. It deletes runs finished more than 7 days ago, 1,000 at a time, each batch in its own short transaction. At large scale the next step would be monthly partitions of `JobTasks`, dropped whole.
