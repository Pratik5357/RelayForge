import type { JobChangedEvent, LiveSource } from "@/lib/live";

export function EventLog({
  events,
  source,
  finished = false,
}: {
  events: JobChangedEvent[];
  source: LiveSource;
  finished?: boolean;
}) {
  if (events.length === 0) {
    return (
      <p className="rounded-[4px] border border-dashed border-[var(--border-strong)] px-4 py-5 text-sm text-[var(--muted)]">
        {finished
          ? "This run finished before any live updates arrived. The steps above show how it ended."
          : source === "polling"
          ? "No pushed updates yet. This run is being checked every few seconds instead."
          : "Waiting for the first update…"}
      </p>
    );
  }

  const reversed = [...events].reverse();

  return (
    <ul className="max-h-72 divide-y divide-[var(--border)] overflow-y-auto rounded-[4px] border border-[var(--border)] bg-[var(--surface)] text-sm">
      {reversed.map((evt, i) => (
        <li key={i} className="flex flex-wrap items-baseline gap-x-2 px-3 py-2">
          <span className="font-semibold">{evt.taskName ?? "job"}</span>
          <span className="font-mono text-xs text-[var(--muted)]">{evt.taskState ?? evt.jobState}</span>
          {evt.message && <span className="text-[var(--muted)]">{evt.message}</span>}
        </li>
      ))}
    </ul>
  );
}
