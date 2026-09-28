# RelayForge frontend

Next.js 16 (App Router, TypeScript, Tailwind v4) dashboard for the RelayForge engine in `../backend`.

```bash
npm install
npm run dev     # http://localhost:3000
npm run lint
npm run build
```

The backend has to be running — `dotnet run --project ../backend/src/TaskScheduler.Api` — and reachable at `NEXT_PUBLIC_API_BASE_URL` (copy `.env.example` to `.env.local`; it defaults to `http://localhost:5053`).

## Pages

| Route | What it does |
| --- | --- |
| `/` | Overview: what the system is, the six-demo guided tour, the wiring panel, latest runs |
| `/jobs` | Every run, polling `GET /api/jobs` every 3s |
| `/jobs/[id]` | Live DAG by dependency level, event log, cancel. `?scenario=<id>` adds that demo's "what to look for" |
| `/jobs/new` | Hand-written job JSON, with a reference for the three handlers |
| `/concepts` | Glossary: every term in plain words plus where to see it in the UI |

## Written for someone who has never seen it

The dashboard assumes no knowledge of the engine, so nothing asks the visitor to invent input:

- `lib/scenarios.ts` is the tour. Each entry pairs a runnable job with the one idea it demonstrates, what to watch while it runs, and the state it should end in. **The expected outcomes are load-bearing copy** — if you change a scenario's payload, re-run it and check the claim still holds.
- The wiring panel (`components/WiringPanel.tsx`) reads `GET /api/system` and says which database, queue and lock provider the backend is actually using, so a visitor can tell the single-process setup from the full stack.
- The event log on a run page shows the raw `jobChanged` pushes with the engine's slugs spelled out in English.

## How the live view works

`lib/live.ts` holds the framework-free watchers; `lib/useLiveJob.ts` is the hook that subscribes a component to one job.

A job detail page opens a SignalR connection to `/hubs/jobs`, calls `Subscribe(jobId)`, and re-reads the job whenever the API pushes `jobChanged`. If the hub cannot be reached it falls back to polling every 1.5s, and either way it stops once the job reaches a terminal state. The current mode is shown in the "Updates" tile.

Everything runs client-side against the API's CORS-open endpoints, so there is no server-side proxy to keep in sync. Types in `lib/types.ts` mirror the DTOs in `backend/src/TaskScheduler.Infrastructure/Jobs/JobService.cs` — change them together.
