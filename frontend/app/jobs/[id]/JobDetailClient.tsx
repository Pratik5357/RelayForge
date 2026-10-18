"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { ApiError, cancelJob } from "@/lib/api";
import { isJobFinished, type JobState } from "@/lib/types";
import { findScenario } from "@/lib/scenarios";
import { useLiveJob } from "@/lib/hooks";
import { toDagLevels } from "@/lib/dag";
import { PageTransition } from "@/components/PageTransition";
import { Remark } from "@/components/Remark";
import { StepsBoard } from "@/components/StepsBoard";
import { StepSheet } from "@/components/StepSheet";
import { RunTimeline, TimelineKey } from "@/components/RunTimeline";
import { EventLog } from "@/components/EventLog";
import { Overlay } from "@/components/Overlay";
import { Perforation } from "@/components/Perforation";
import { Reveal } from "@/components/Reveal";
import { ScanStamp } from "@/components/ScanStamp";
import { Arrow, Button } from "@/components/ui/Button";

function formatDuration(startedAt: string | null, completedAt: string | null, now: number): string {
  if (!startedAt) return "—";
  const end = completedAt ? new Date(completedAt).getTime() : now;
  return `${((end - new Date(startedAt).getTime()) / 1000).toFixed(1)}s`;
}

// Two stats per row on phones, four across from sm up; a hairline divides neighbours.
const STAT_CELL = [
  "pr-4",
  "pl-4 shadow-[inset_1px_0_0_var(--hairline-soft)]",
  "pr-4 sm:pl-4 sm:shadow-[inset_1px_0_0_var(--hairline-soft)]",
  "pl-4 shadow-[inset_1px_0_0_var(--hairline-soft)]",
];

const SOURCE_LABEL: Record<string, string> = {
  connecting: "connecting…",
  signalr: "live",
  polling: "polling",
  stopped: "stopped",
};

export function JobDetailClient({ jobId }: { jobId: string }) {
  const scenario = findScenario(useSearchParams().get("scenario"));
  const { job, events, source, error, attempts } = useLiveJob(jobId);
  const [now, setNow] = useState(() => Date.now());
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);
  const [openStep, setOpenStep] = useState<string | null>(null);
  // Keeps the last opened step's content on screen while the panel animates closed.
  const [shownStep, setShownStep] = useState<string | null>(null);
  // Whether this page saw the run unfinished: only then does the end-of-demo stamp land with
  // motion. A run opened after it ended shows the stamp at rest.
  const [sawRunning, setSawRunning] = useState(false);
  if (job && !isJobFinished(job.state) && !sawRunning) setSawRunning(true);

  // Ticks once a second purely to re-render "retrying in Ns" countdowns and the live timeline
  // between pushes -- decoupled from the data itself, which comes from useLiveJob.
  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, []);

  async function confirmCancel() {
    setConfirmOpen(false);
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

  function openSheet(taskId: string) {
    setShownStep(taskId);
    setOpenStep(taskId);
  }

  if (error && !job) {
    return (
      <PageTransition>
        <p role="alert" className="rounded-xl bg-board px-5 py-4 text-board-text">
          <span className="font-bold text-[var(--remark-failed)]">Can&rsquo;t load this run.</span> {error}
        </p>
      </PageTransition>
    );
  }

  if (!job) {
    return (
      <PageTransition>
        <div role="status" aria-label="Loading run" className="flex flex-col gap-4">
          <span className="h-14 w-2/3 rounded-md bg-hairline/60" />
          <span className="board-reveal h-40 rounded-2xl bg-board" />
        </div>
      </PageTransition>
    );
  }

  const doneCount = job.tasks.filter((t) => ["Succeeded", "Failed", "DeadLettered", "Cancelled"].includes(t.state)).length;
  const finished = isJobFinished(job.state);
  const cancelRequested = Boolean(job.cancellationRequestedAt) && !finished;
  const failures = job.tasks.filter((t) => t.errorMessage);
  // Scenario copy leads with the state the demo should end in ("PartiallyFailed — …").
  const expectedState = scenario ? (scenario.expected.split(/\s/)[0] as JobState) : null;

  const levels = toDagLevels(job.tasks);
  const stageOf = new Map(levels.flatMap(({ level, tasks }) => tasks.map((t) => [t.id, level + 1] as const)));
  const nameById = new Map(job.tasks.map((t) => [t.id, t.name]));
  const sheetTask = job.tasks.find((t) => t.id === shownStep) ?? null;

  const stats = [
    ["Steps finished", `${doneCount} of ${job.tasks.length}`],
    ["Started", job.startedAt ? new Date(job.startedAt).toLocaleTimeString([], { hour12: false }) : "—"],
    ["Total time", formatDuration(job.startedAt, job.completedAt, now)],
    ["Updates", SOURCE_LABEL[source] ?? source],
  ];

  return (
    <PageTransition className="page-enter flex flex-col gap-14">
      <div className="flex flex-col gap-4">
        <Link
          href="/jobs"
          transitionTypes={["nav-back"]}
          className="inline-flex min-h-11 items-center gap-1.5 self-start font-semibold text-muted no-underline hover:text-ink"
        >
          <Arrow direction="left" /> All runs
        </Link>

        <article className="board-reveal @container flex flex-wrap overflow-hidden rounded-[18px] bg-paper shadow-[0_1px_0_rgb(18_20_18/0.06),0_20px_40px_-28px_rgb(18_20_18/0.45)]">
          <div className="flex min-w-0 flex-[999_1_560px] flex-col gap-[22px] px-8 py-7">
            <h1 className="text-[clamp(36px,4.2vw,56px)] leading-none tracking-[-0.04em]">
              {job.name ?? job.id.slice(0, 8)}
            </h1>
            <dl className="grid grid-cols-2 gap-y-3 border-t border-[var(--hairline-soft)] sm:grid-cols-4 sm:gap-y-0">
              {stats.map(([term, value], i) => (
                <div key={term} className={`flex flex-col gap-0.5 pt-3 ${STAT_CELL[i]}`}>
                  <dt className="label text-label">{term}</dt>
                  <dd className="tnum font-mono text-[26px] font-medium">{value}</dd>
                </div>
              ))}
            </dl>
          </div>
          <div className="on-board relative flex flex-[1_0_260px] flex-col justify-between gap-5 bg-board py-7 pl-[34px] pr-7 text-board-text">
            <Perforation tone="board" at="run" />
            <div className="flex flex-col gap-2.5">
              <Remark state={job.state} size="lg" />
              <span className="break-all font-mono text-xs leading-normal text-board-muted">{job.id}</span>
            </div>
            {!finished ? (
              <Button
                variant="board"
                onClick={() => setConfirmOpen(true)}
                disabled={cancelling || cancelRequested}
                aria-haspopup="dialog"
              >
                {cancelling || cancelRequested ? "Cancelling…" : "Cancel run"}
              </Button>
            ) : (
              <div className="flex flex-col gap-3">
                {expectedState && <ScanStamp expected={expectedState} actual={job.state} live={sawRunning} />}
                <p className="text-sm text-board-soft">This run is done.</p>
              </div>
            )}
          </div>
        </article>
      </div>

      {(cancelRequested || cancelError) && (
        <div className="-mt-8 flex flex-col gap-2">
          {cancelRequested && (
            <p role="status" className="fade-up rounded-xl bg-board px-[18px] py-3.5 text-board-text">
              Cancelling. Waiting for the step that is mid-flight to finish…
            </p>
          )}
          {cancelError && (
            <p role="alert" className="fade-up rounded-xl bg-board px-[18px] py-3.5 text-board-text">
              <span className="font-bold text-[var(--remark-failed)]">Couldn&rsquo;t cancel.</span> {cancelError}
            </p>
          )}
        </div>
      )}

      {scenario && (
        <Reveal as="section" aria-label="What to watch for" className="flex flex-wrap gap-x-14 gap-y-5 border-y border-hairline py-[26px] [border-top:2px_solid_var(--ink)]">
          <p className="flex-[1_1_260px] text-2xl font-extrabold leading-[1.15] tracking-[-0.02em]">
            {scenario.title}: what to watch for
          </p>
          <ul className="flex flex-[2_1_420px] list-disc flex-col gap-2.5 pl-5 marker:text-label">
            {scenario.watchFor.map((line, i) => (
              <li key={i}>{line}</li>
            ))}
          </ul>
          <p className="flex-[1_1_260px]">
            <span className="font-bold">Expected:</span> {scenario.expected}
          </p>
        </Reveal>
      )}

      <Reveal as="section" aria-labelledby="time-heading" className="flex flex-col gap-4">
        <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
          <h2 id="time-heading" className="text-[40px] leading-none tracking-[-0.035em]">
            Timeline
          </h2>
          <TimelineKey />
        </div>
        <RunTimeline job={job} attempts={attempts} now={now} />
        <p className="text-sm text-muted">
          Steps that start at the same moment are running side by side. Each failed try waits a
          little longer than the one before it. Drawn from what this page has seen, so open a run
          while it&rsquo;s going to catch every try.
        </p>
      </Reveal>

      <Reveal as="section" aria-labelledby="graph-heading" className="flex flex-col gap-4">
        <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
          <h2 id="graph-heading" className="text-[40px] leading-none tracking-[-0.035em]">
            Steps
          </h2>
          <span className="text-sm text-muted">Open a step to see every try</span>
        </div>
        <StepsBoard tasks={job.tasks} now={now} onOpen={openSheet} />
      </Reveal>

      {failures.length > 0 && (
        <Reveal as="section" aria-labelledby="why-heading" className="flex flex-col gap-4">
          <h2 id="why-heading" className="text-[40px] leading-none tracking-[-0.035em]">
            What went wrong
          </h2>
          <ul className="rounded-2xl bg-paper shadow-[inset_0_0_0_1px_var(--hairline-soft)]">
            {failures.map((t) => (
              <li key={t.id} className="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-[#eceeea] px-6 py-[18px] last:border-b-0">
                <span className="text-[17px] font-bold">{t.name}</span>
                <Remark state={t.state} />
                <span className="min-w-0 break-words text-muted">{t.errorMessage}</span>
              </li>
            ))}
          </ul>
        </Reveal>
      )}

      <Reveal as="section" aria-labelledby="events-heading" className="flex flex-col gap-4">
        <h2 id="events-heading" className="text-[40px] leading-none tracking-[-0.035em]">
          Live events
        </h2>
        <EventLog events={events} source={source} finished={finished} />
      </Reveal>

      {finished && (
        <Reveal as="p" className="text-muted">
          This run is done.{" "}
          <Link href="/" transitionTypes={["nav-back"]} className="font-bold text-ink underline">
            Back to the tour
          </Link>{" "}
          or{" "}
          <Link href="/jobs" transitionTypes={["nav-back"]} className="font-bold text-ink underline">
            see all runs
          </Link>
          .
        </Reveal>
      )}

      <Overlay
        open={openStep !== null}
        onClose={() => setOpenStep(null)}
        variant="sheet"
        labelledBy="step-sheet-title"
      >
        {sheetTask && (
          <StepSheet
            task={sheetTask}
            stage={stageOf.get(sheetTask.id) ?? 1}
            waits={sheetTask.dependsOn.map((id) => nameById.get(id) ?? id).join(", ") || "nothing"}
            attempts={attempts[sheetTask.id] ?? []}
            events={events}
            onClose={() => setOpenStep(null)}
          />
        )}
      </Overlay>

      <Overlay
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        variant="dialog"
        role="alertdialog"
        labelledBy="cancel-title"
        describedBy="cancel-desc"
      >
        <div className="flex flex-col gap-3.5">
          <h2 id="cancel-title" className="text-[30px] leading-[1.05] tracking-[-0.03em]">
            Cancel this run?
          </h2>
          <p id="cancel-desc" className="text-muted">
            Steps that haven&rsquo;t started yet are called off right away. A step already in flight
            is left to finish, then the run is marked Cancelled.
          </p>
          <div className="mt-2 flex flex-wrap justify-end gap-2.5">
            <Button variant="secondary" onClick={() => setConfirmOpen(false)} autoFocus>
              Keep running
            </Button>
            <Button variant="primary" onClick={confirmCancel}>
              Cancel run
            </Button>
          </div>
        </div>
      </Overlay>
    </PageTransition>
  );
}
