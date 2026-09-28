# ADR 0002: Lease-based locking instead of ZooKeeper

## Status
Accepted (Phase 2)

## Context
Workers crash. A task left `Running` must be reclaimable. ZooKeeper/etcd ephemeral nodes are the classic membership solution; database leases and Redis locks are simpler.

## Decision
Store a `LeaseUntil` timestamp on each task row. Workers heartbeat separately. A reaper returns expired running tasks to `Pending` and re-enqueues them. In the multi-worker phase, Redis RedLock is an extra mutex around claim so two consumers do not execute the same message concurrently.

## Consequences
- No ZooKeeper ensemble in docker-compose.
- Clock skew can delay or hasten reclaim; leases are long enough (30s default) to absorb that.
- Postgres/SQLite `ExecuteUpdate` is the source of truth for ownership; Redis is a speed bump, not the system of record.
