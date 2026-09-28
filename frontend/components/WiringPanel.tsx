"use client";

import { useSystemInfo } from "@/lib/hooks";
import { apiBaseUrl } from "@/lib/api";

/**
 * Says out loud which pieces the running backend is actually using, because the
 * same code runs either as one process with SQLite or as the full Postgres +
 * RabbitMQ + Redis stack, and you cannot tell from the outside otherwise.
 */
export function WiringPanel() {
  const { data, error, loading } = useSystemInfo();

  if (loading) {
    return <p className="text-sm text-zinc-500">Checking the backend…</p>;
  }

  if (error || !data) {
    return (
      <div className="rounded-lg border border-red-300 bg-red-50 p-4 text-sm text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300">
        <p className="font-medium">The backend is not answering.</p>
        <p className="mt-1">
          {error} Start it with{" "}
          <code className="font-mono">dotnet run --project backend/src/TaskScheduler.Api</code>, then
          reload. This page expects it at <code className="font-mono">{apiBaseUrl}</code>.
        </p>
      </div>
    );
  }

  const { wiring, counts } = data;
  const distributed = wiring.queue.toLowerCase() === "rabbitmq";

  return (
    <div className="space-y-3 rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="font-medium">How this instance is wired</h2>
        <p className="text-xs text-zinc-500">
          {distributed
            ? "Full stack: work travels through a real message broker."
            : "Single process: work travels through an in-memory queue."}
        </p>
      </div>

      <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm sm:grid-cols-3">
        <Row label="Database" value={wiring.database} hint="where jobs and tasks are stored" />
        <Row label="Queue" value={wiring.queue} hint="how ready tasks reach a worker" />
        <Row
          label="Task locking"
          value={wiring.lockProvider}
          hint="stops two workers claiming one task"
        />
        <Row
          label="Worker slots"
          value={String(wiring.workerCount)}
          hint={wiring.inProcessWorkers ? "inside the API process" : "separate worker processes"}
        />
        <Row
          label="Lease"
          value={`${wiring.leaseSeconds}s`}
          hint="a claim this old is treated as a crash"
        />
        <Row
          label="Workers seen"
          value={String(counts.liveWorkers)}
          hint={`heartbeat every ${wiring.heartbeatSeconds}s`}
        />
      </dl>

      <div className="flex flex-wrap gap-x-5 gap-y-1 border-t border-zinc-100 pt-3 text-xs text-zinc-500 dark:border-zinc-800">
        <span>
          <strong className="font-medium text-zinc-700 dark:text-zinc-300">{counts.jobs}</strong> jobs
          submitted
        </span>
        <span>
          <strong className="font-medium text-zinc-700 dark:text-zinc-300">{counts.tasks}</strong>{" "}
          tasks total
        </span>
        <span>
          <strong className="font-medium text-zinc-700 dark:text-zinc-300">
            {counts.deadLetters}
          </strong>{" "}
          dead-lettered
        </span>
      </div>
    </div>
  );
}

function Row({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div>
      <dt className="text-xs text-zinc-500">{label}</dt>
      <dd className="font-medium">{value}</dd>
      <dd className="text-xs text-zinc-500">{hint}</dd>
    </div>
  );
}
