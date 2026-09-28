"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, use, useState } from "react";
import { Callout } from "@/components/Callout";
import { DagView } from "@/components/DagView";
import { EventLog } from "@/components/EventLog";
import { StateBadge } from "@/components/StateBadge";
import { cancelJob } from "@/lib/api";
import { useLiveJob } from "@/lib/hooks";
import type { LiveSource } from "@/lib/live";
import { findScenario } from "@/lib/scenarios";
import { isJobFinished, type JobState } from "@/lib/types";

const SOURCE_LABEL: Record<LiveSource, string> = {
  connecting: "connecting…",
  signalr: "pushed live",
  polling: "re-read on a timer",
  stopped: "run is over",
};

/** Plain-language reading of where a finished (or unfinished) job ended up. */
const OUTCOME: Record<JobState, string> = {
  Pending: "Queued. No step has been picked up yet.",
  Running: "In progress. Watch the columns below turn green.",
  Succeeded: "Every step finished successfully.",
  Failed: "No step finished, and at least one ran out of attempts.",
  PartiallyFailed:
    "Some steps finished and at least one ran out of attempts — the job is reported as partly done rather than pretending it all worked.",
  Cancelled: "Cancelled. Steps that had not started were dropped.",
};

export default function JobDetailPage({ params }: PageProps<"/jobs/[id]">) {
  const { id } = use(params);

  return (
    <Suspense fallback={<p className="text-sm text-zinc-500">Loading…</p>}>
      <JobDetail id={id} />
    </Suspense>
  );
}

function JobDetail({ id }: { id: string }) {
  const scenario = findScenario(useSearchParams().get("scenario"));
  const { job, error, loading, source, events, refresh } = useLiveJob(id);
  const [cancelError, setCancelError] = useState<string | null>(null);

  async function handleCancel() {
    setCancelError(null);
    try {
      await cancelJob(id);
      refresh();
    } catch (err) {
      setCancelError(err instanceof Error ? err.message : String(err));
    }
  }

  if (loading) {
    return <p className="text-sm text-zinc-500">Loading…</p>;
  }

  if (error || !job) {
    return (
      <section className="space-y-3">
        <Link href="/jobs" className="text-sm hover:underline">
          ← All runs
        </Link>
        <Callout tone="error">{error ?? "This job does not exist."}</Callout>
      </section>
    );
  }

  const finished = isJobFinished(job.state);
  const done = job.tasks.filter((task) => task.state === "Succeeded").length;

  return (
    <section className="space-y-6">
      <Link href="/jobs" className="text-sm hover:underline">
        ← All runs
      </Link>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-xl font-semibold">{job.name}</h1>
          <p className="font-mono text-xs text-zinc-500">{job.id}</p>
        </div>
        <div className="flex items-center gap-3">
          <StateBadge state={job.state} />
          {!finished && (
            <button
              type="button"
              onClick={handleCancel}
              className="rounded-md border border-zinc-300 px-2.5 py-1 text-sm hover:bg-zinc-100 dark:border-zinc-700 dark:hover:bg-zinc-800"
            >
              Cancel
            </button>
          )}
        </div>
      </div>

      <p className="text-sm text-zinc-600 dark:text-zinc-400">{OUTCOME[job.state]}</p>

      {scenario && (
        <Callout tone="teach" title={`${scenario.title} — what to look for`}>
          <ul className="list-disc space-y-1 pl-5 text-zinc-700 dark:text-zinc-300">
            {scenario.watchFor.map((point) => (
              <li key={point}>{point}</li>
            ))}
          </ul>
          <p className="mt-2 text-zinc-600 dark:text-zinc-400">
            <span className="font-medium">Should end as:</span> {scenario.expected}
          </p>
        </Callout>
      )}

      {cancelError && <Callout tone="error">{cancelError}</Callout>}

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <Stat label="Steps finished" value={`${done} of ${job.tasks.length}`} />
        <Stat label="Started" value={new Date(job.createdAt).toLocaleTimeString()} />
        <Stat
          label="Total time"
          value={job.completedAt ? elapsed(job.createdAt, job.completedAt) : "still going"}
        />
        <Stat label="Updates" value={SOURCE_LABEL[source]} />
      </dl>

      <div className="space-y-3">
        <h2 className="text-lg font-medium">The steps</h2>
        <DagView tasks={job.tasks} />
      </div>

      <div className="space-y-3">
        <div className="space-y-1">
          <h2 className="text-lg font-medium">What the engine reported</h2>
          <p className="text-sm text-zinc-500">
            Each line was pushed to this page over a websocket the moment it happened — the
            page never asked for it.
          </p>
        </div>
        <EventLog events={events} source={source} />
      </div>

      {scenario && finished && (
        <Callout tone="info">
          Done with this one?{" "}
          <Link href="/" className="underline">
            Back to the tour
          </Link>{" "}
          for the next demo.
        </Callout>
      )}
    </section>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-3 dark:border-zinc-800 dark:bg-zinc-900">
      <dt className="text-xs text-zinc-500">{label}</dt>
      <dd className="mt-0.5 font-medium">{value}</dd>
    </div>
  );
}

function elapsed(from: string, to: string): string {
  const ms = new Date(to).getTime() - new Date(from).getTime();
  return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`;
}
