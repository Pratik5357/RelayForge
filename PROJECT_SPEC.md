# Distributed Task Scheduler / Workflow Engine

## Overview
A backend portfolio project: a mini distributed workflow engine (similar in spirit to Temporal / Airflow), built to learn and demonstrate distributed systems concepts — DAG execution, fault tolerance, message queues, distributed locking, and observability.

**Goal**: Not a CRUD app. This system schedules and executes jobs made of dependent tasks (a DAG), survives worker crashes, scales across multiple worker instances, and is fully observable in production-like fashion.

## Tech Stack
- **Language/Framework**: C# / .NET 8+
- **API**: ASP.NET Core Web API (minimal APIs)
- **Persistence**: PostgreSQL + EF Core
- **Message Broker**: RabbitMQ via MassTransit
- **Distributed Locking**: Redis (RedLock.net)
- **Background Workers**: .NET Worker Service (`BackgroundService`)
- **Retry Policies**: Polly (study first, partially reimplement to learn internals)
- **Real-time updates**: SignalR
- **Observability**: Serilog, OpenTelemetry, Prometheus (`prometheus-net`), Grafana
- **Containerization**: Docker + docker-compose
- **CI/CD**: GitHub Actions
- **Load Testing**: k6

## Domain Model
- **Job**: a unit of work submitted by a user. Contains one or more Tasks.
- **Task**: an individual step within a Job. Has dependencies on other Tasks (forms a DAG).
- **TaskState**: `Pending`, `Running`, `Succeeded`, `Failed`, `DeadLettered`
- **JobState**: `Pending`, `Running`, `Succeeded`, `Failed`, `PartiallyFailed`
- **Worker**: a process that claims and executes Tasks; emits heartbeats.
- **Lease**: a time-bound claim a Worker holds on a Task, used to detect crashed workers.

## Build Phases (each phase = an independently demoable checkpoint)

### Phase 1 — Core Engine
- Model Jobs/Tasks/Dependencies in Postgres via EF Core
- Topological sort to determine execution order
- In-process worker pool (use `Channel<T>`) executing tasks respecting dependencies
- Minimal API: submit job, get job/task status
- **Concepts learned**: EF Core relationships, topological sort, async/await, in-memory channels

### Phase 2 — Reliability
- Retry with exponential backoff + jitter (Polly)
- Worker heartbeats + lease timeout so a crashed worker's task is reclaimed
- Dead-letter table for tasks exceeding max retries
- Idempotency keys so retried tasks don't double-execute side effects
- **Concepts learned**: lease-based locking, idempotency, failure recovery

### Phase 3 — Distributed Scale
- Replace in-memory queue with RabbitMQ (MassTransit)
- Run multiple worker instances (separate processes/containers)
- Redis distributed lock (RedLock.net) to prevent double-claiming a task
- **Concepts learned**: message broker delivery guarantees (at-least-once), distributed locks

### Phase 4 — Observability
- Serilog structured logging
- OpenTelemetry tracing across API → queue → worker
- Prometheus metrics: queue depth, task latency, failure rate; Grafana dashboard
- **Concepts learned**: what separates a toy project from a production-grade system

### Phase 5 — API & Live Dashboard
- Full REST API: submit, status, cancel, list jobs
- Blazor (or simple SPA) dashboard showing DAG execution live via SignalR
- **Concepts learned**: SignalR/WebSockets, API design

### Phase 6 — Deployment & Load Test
- docker-compose: API + workers + Postgres + RabbitMQ + Redis + Grafana
- GitHub Actions CI/CD pipeline
- k6 load test; record real throughput/latency numbers in README
- Architecture Decision Records (ADRs) explaining key tradeoffs (e.g., RabbitMQ vs Kafka, lease-based locking vs Zookeeper)

## Suggested Repo Structure
```
/src
  /TaskScheduler.Api          -> ASP.NET Core Web API
  /TaskScheduler.Worker       -> Worker Service (task execution)
  /TaskScheduler.Domain       -> Job/Task/DAG domain models
  /TaskScheduler.Infrastructure -> EF Core, RabbitMQ, Redis integrations
  /TaskScheduler.Dashboard    -> Blazor/SPA frontend (Phase 5)
/tests
  /TaskScheduler.UnitTests
  /TaskScheduler.IntegrationTests
/deploy
  docker-compose.yml
  /k6                         -> load test scripts
/docs
  /adr                        -> architecture decision records
README.md
```

## Working Agreement for CLI/Assistant
- Build incrementally, phase by phase, per the plan above — do not skip ahead to Phase 3+ concerns (queues, distributed locks) before Phase 1/2 are solid.
- Favor explicit, readable code over cleverness; this is a learning project.
- After each phase, ensure the system is runnable end-to-end via `docker-compose up` and update the README with what changed.
- Write unit tests for domain logic (DAG resolution, retry policy) and integration tests for worker/queue behavior.
- Document non-obvious decisions as ADRs in `/docs/adr`.
