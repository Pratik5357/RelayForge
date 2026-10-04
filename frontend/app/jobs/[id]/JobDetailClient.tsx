"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { ApiError, cancelJob } from "@/lib/api";
import { isJobFinished } from "@/lib/types";
import { findScenario } from "@/lib/scenarios";
import { useLiveJob } from "@/lib/hooks";
import { Callout } from "@/components/ui/Callout";
import { Button } from "@/components/ui/Button";
import { StateBadge } from "@/components/StateBadge";
import { DagView } from "@/components/DagView";
import { EventLog } from "@/components/EventLog";

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="px-4 py-3">
      <dt className="text-xs font-semibold text-[var(--muted)]">{label}</dt>
      <dd className="tnum mt-0.5 font-mono text-sm">{value}</dd>
    </div>
  );
}

function formatDuration(startedAt: string | null, completedAt: string | null): string {
  if (!startedAt) return "—";
  const end = completedAt ? new Date(completedAt) : new Date();
  const ms = end.getTime() - new Date(startedAt).getTime();
  return `${(ms / 1000).toFixed(1)}s`;
}

const SOURCE_LABEL: Record<string, string> = {
  connecting: "connecting…",
  signalr: "live",
  polling: "polling",
  stopped: "stopped",
};

export function JobDetailClient({ jobId }: { jobId: string }) {
  const scenario = findScenario(useSearchParams().get("scenario"));
  const { job, events, source, error } = useLiveJob(jobId);
  const [now, setNow] = useState(() => Date.now());
  const [cancelling, setCancelling] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);

  // Ticks once a second purely to re-render "retrying in Ns" countdowns between live pushes --
  // decoupled from the data itself, which comes from useLiveJob.
  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, []);

  async function handleCancel() {
    setCancelling(true);
    setCancelError(null);
    try {
      await cancelJob(jobId);
    } catch (err) {
      setCancelError(err instanceof ApiError ? err.message : "Could not cancel this job.");
    } finally {
      setCancelling(false);
    }
  }

  if (error) {
    return (
      <Callout tone="error" role="alert">
        {error}
      </Callout>
    );
  }

  if (!job) {
    return (
      <p className="text-sm text-[var(--muted)]" role="status">
        Loading…
      </p>
    );
  }

  const doneCount = job.tasks.filter((t) =>
    ["Succeeded", "Failed", "DeadLettered", "Cancelled"].includes(t.state),
  ).length;
  const finished = isJobFinished(job.state);
  const cancelling_ = Boolean(job.cancellationRequestedAt) && !finished;

  return (
    <div className="space-y-8">
      <Link href="/jobs" className="inline-block text-sm text-[var(--muted)] hover:text-[var(--foreground)]">
        ← All runs
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <h1 className="text-4xl leading-none">{job.name ?? job.id.slice(0, 8)}</h1>
            <StateBadge state={job.state} size="lg" />
          </div>
          <p className="mt-1 break-all font-mono text-xs text-[var(--muted)]">{job.id}</p>
        </div>
        <div className="flex items-center gap-3">
          {!finished && (
            <Button variant="secondary" size="sm" onClick={handleCancel} disabled={cancelling || cancelling_}>
              {cancelling ? "Cancelling…" : "Cancel run"}
            </Button>
          )}
        </div>
      </div>

      {scenario && (
        <Callout tone="teach" className="space-y-2">
          <p className="font-semibold">{scenario.title}: what to watch for</p>
          <ul className="list-inside list-disc space-y-1 text-[var(--muted)]">
            {scenario.watchFor.map((line, i) => (
              <li key={i}>{line}</li>
            ))}
          </ul>
          <p>
            <span className="font-semibold">Expected:</span>{" "}
            <span className="text-[var(--muted)]">{scenario.expected}</span>
          </p>
        </Callout>
      )}

      {cancelling_ && (
        <Callout tone="info" role="status">
          Cancelling. Waiting for the step that is mid-flight to finish…
        </Callout>
      )}
      {cancelError && (
        <Callout tone="error" role="alert">
          {cancelError}
        </Callout>
      )}

      <dl className="grid grid-cols-2 divide-x divide-y divide-[var(--border)] overflow-hidden rounded-[4px] border border-[var(--border)] bg-[var(--surface)] sm:grid-cols-4 sm:divide-y-0">
        <Stat label="Steps finished" value={`${doneCount} of ${job.tasks.length}`} />
        <Stat label="Started" value={job.startedAt ? new Date(job.startedAt).toLocaleTimeString() : "—"} />
        <Stat label="Total time" value={formatDuration(job.startedAt, job.completedAt)} />
        <Stat label="Updates" value={SOURCE_LABEL[source] ?? source} />
      </dl>

      <section aria-labelledby="graph-heading" className="space-y-3">
        <h2 id="graph-heading" className="text-2xl">
          Steps
        </h2>
        <div className="rounded-[4px] border border-[var(--border)] bg-[var(--surface)] p-6">
          <DagView tasks={job.tasks} now={now} />
        </div>
      </section>

      {job.tasks.some((t) => t.errorMessage) && (
        <section aria-labelledby="why-heading" className="space-y-3">
          <h2 id="why-heading" className="text-2xl">
            What went wrong
          </h2>
          <ul className="divide-y divide-[var(--border)] rounded-[4px] border border-[var(--border)] bg-[var(--surface)] text-sm">
            {job.tasks
              .filter((t) => t.errorMessage)
              .map((t) => (
                <li key={t.id} className="flex flex-wrap items-baseline gap-x-3 px-4 py-3">
                  <span className="font-semibold">{t.name}</span>
                  <StateBadge state={t.state} />
                  <span className="min-w-0 break-words text-[var(--muted)]">{t.errorMessage}</span>
                </li>
              ))}
          </ul>
        </section>
      )}

      <section aria-labelledby="events-heading" className="space-y-3">
        <h2 id="events-heading" className="text-2xl">
          Live events
        </h2>
        <EventLog events={events} source={source} finished={finished} />
      </section>

      {finished && (
        <p className="text-sm text-[var(--muted)]">
          This run is done.{" "}
          <Link href="/" className="font-semibold text-[var(--foreground)] underline">
            Back to the tour
          </Link>{" "}
          or{" "}
          <Link href="/jobs" className="font-semibold text-[var(--foreground)] underline">
            see all runs
          </Link>
          .
        </p>
      )}
    </div>
  );
}
