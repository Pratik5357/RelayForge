"use client";

import { useEffect, useRef, useState } from "react";
import type { JobTaskDto, TaskLogLine } from "@/lib/types";
import type { Attempt } from "@/lib/attempts";
import type { JobChangedEvent } from "@/lib/live";
import { ApiError, getTaskLogs, redriveTask } from "@/lib/api";
import { Remark } from "./Remark";
import { CloseButton } from "./Overlay";
import { Button } from "./ui/Button";

function clock(ms: number | string | null) {
  if (ms === null) return "—";
  return new Date(ms).toLocaleTimeString([], { hour12: false });
}

const RESULT: Record<string, string> = {
  running: "running now",
  Succeeded: "succeeded",
  Failed: "failed, will retry if tries remain",
  Fatal: "failed in a way retrying can't fix",
  LeaseLost: "worker went silent; its lease ran out and the step was reclaimed",
  Cancelled: "stopped: the run was cancelled",
  Released: "handed back by a worker that was shutting down (didn't use up a try)",
};

const ISOLATION: Record<string, string> = {
  InProcess: "inside the worker",
  Process: "in its own process",
  Container: "in its own container",
};

type StepSheetProps = {
  task: JobTaskDto;
  stage: number;
  waits: string;
  attempts: Attempt[];
  events: JobChangedEvent[];
  logTick: number;
  onClose: () => void;
};

/** Everything about one step: its state, every try the server recorded, its logs and its events. */
export function StepSheet({ task, stage, waits, attempts, events, logTick, onClose }: StepSheetProps) {
  const mine = events.filter((e) => e.taskId === task.id).reverse();
  const [redriving, setRedriving] = useState(false);
  const [redriveError, setRedriveError] = useState<string | null>(null);
  const canRedrive = task.state === "DeadLettered" || task.state === "Failed";

  async function redrive() {
    setRedriving(true);
    setRedriveError(null);
    try {
      await redriveTask(task.id);
    } catch (err) {
      setRedriveError(err instanceof ApiError ? err.message : "Could not redrive this step.");
    } finally {
      setRedriving(false);
    }
  }

  return (
    <>
      <div className="flex items-start justify-between gap-4 border-b border-board-rule px-6 pb-5 pt-6">
        <div className="flex min-w-0 flex-col gap-1.5">
          <span className="label text-board-muted">
            Stage {stage} · waits for {waits}
          </span>
          <h2 id="step-sheet-title" className="break-words text-[32px] leading-[1.05] tracking-[-0.03em]">
            {task.name}
          </h2>
        </div>
        <CloseButton onClick={onClose} label="Close step details" />
      </div>

      <div className="flex flex-wrap items-center gap-4 border-b border-board-rule px-6 py-5">
        <Remark state={task.state} size="lg" />
        <span className="tnum font-mono text-lg">
          {task.attemptCount}/{task.maxAttempts} tries
        </span>
        {canRedrive && (
          <Button variant="board" size="sm" onClick={redrive} disabled={redriving} className="ml-auto">
            {redriving ? "Redriving…" : "Redrive"}
          </Button>
        )}
      </div>
      {canRedrive && (
        <p className="border-b border-board-rule px-6 py-3 text-sm text-board-soft">
          Redrive gives this step fresh tries. If the run already ended, it reopens, and the steps
          that were waiting on this one get their chance too.
          {redriveError && <span className="mt-1 block text-[var(--remark-failed)]">{redriveError}</span>}
        </p>
      )}

      <dl className="flex flex-col px-6 py-2">
        {[
          ["Kind", task.type],
          ["Runs", ISOLATION[task.isolation] ?? task.isolation],
          ["Time limit per try", `${(task.timeoutMs / 1000).toFixed(task.timeoutMs < 10_000 ? 1 : 0)}s`],
          ["Started", clock(task.startedAt)],
          ["Finished", clock(task.completedAt)],
          ["Idempotency key", task.idempotencyKey],
        ].map(([term, value]) => (
          <div key={term} className="flex justify-between gap-4 border-b border-board-rule py-3">
            <dt className="text-board-muted">{term}</dt>
            <dd className="break-all text-right font-mono text-[13px]">{value}</dd>
          </div>
        ))}
      </dl>

      {task.errorMessage && (
        <section aria-labelledby="step-error" className="flex flex-col gap-2 px-6 py-4">
          <h3 id="step-error" className="label text-board-muted">
            Error
          </h3>
          <p className="text-[var(--remark-failed)]">{task.errorMessage}</p>
        </section>
      )}

      <section aria-labelledby="step-tries" className="flex flex-col gap-2.5 px-6 pb-4 pt-3">
        <h3 id="step-tries" className="label text-board-muted">
          Every try
        </h3>
        {attempts.length === 0 ? (
          <p className="text-board-muted">No tries yet. This step is waiting its turn.</p>
        ) : (
          <ol className="flex flex-col">
            {attempts.map((a) => (
              <li key={a.number} className="grid grid-cols-[76px_minmax(0,1fr)] gap-3 border-b border-board-rule py-2.5">
                <span className="font-mono text-[13px] text-board-muted">{clock(a.start)}</span>
                <span className="flex flex-col gap-0.5">
                  <span>
                    <span className="font-bold">Try {a.number}</span>{" "}
                    <span className="font-mono text-[13px] text-board-muted">on worker {a.worker}</span>
                  </span>
                  <span className="text-board-soft">
                    {RESULT[a.result ?? "running"]}
                    {a.end !== null && ` · ${((a.end - a.start) / 1000).toFixed(1)}s`}
                  </span>
                  {a.error && a.result !== "Succeeded" && <span className="text-sm text-board-muted">{a.error}</span>}
                  {a.rejectedLateResult && (
                    <span className="text-sm text-amber">
                      Later reported &ldquo;{a.rejectedLateResult}&rdquo; after losing its lease. Rejected: a newer try owned the step.
                    </span>
                  )}
                </span>
              </li>
            ))}
          </ol>
        )}
      </section>

      <TaskLogs taskId={task.id} tick={logTick} />

      {task.output !== null && task.output !== undefined && (
        <section aria-labelledby="step-output" className="flex flex-col gap-2 px-6 pb-4">
          <h3 id="step-output" className="label text-board-muted">
            Output (passed to the steps that wait for this one)
          </h3>
          <pre className="max-h-48 overflow-auto rounded-md bg-board-raised p-3 font-mono text-xs text-board-soft">
            {JSON.stringify(task.output, null, 2)}
          </pre>
        </section>
      )}

      {mine.length > 0 && (
        <section aria-labelledby="step-events" className="flex flex-col gap-2.5 px-6 pb-7">
          <h3 id="step-events" className="label text-board-muted">
            Events
          </h3>
          <ol className="flex flex-col">
            {mine.map((e) => (
              <li key={e.sequence} className="grid grid-cols-[76px_minmax(0,1fr)] gap-3 border-b border-board-rule py-2.5">
                <span className="font-mono text-[13px] text-board-muted">{clock(e.at)}</span>
                <span className="text-board-soft">{e.message}</span>
              </li>
            ))}
          </ol>
        </section>
      )}
    </>
  );
}

/** The step's log lines, refetched whenever the server says it wrote more. */
function TaskLogs({ taskId, tick }: { taskId: string; tick: number }) {
  const [lines, setLines] = useState<TaskLogLine[] | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;
    getTaskLogs(taskId)
      .then((result) => {
        if (!cancelled) setLines(result);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [taskId, tick]);

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: "nearest" });
  }, [lines]);

  return (
    <section aria-labelledby="step-logs" className="flex flex-col gap-2 px-6 pb-4">
      <h3 id="step-logs" className="label text-board-muted">
        Log
      </h3>
      {lines === null || lines.length === 0 ? (
        <p className="text-board-muted">{lines === null ? "Loading…" : "Nothing logged yet."}</p>
      ) : (
        <div className="max-h-64 overflow-auto rounded-md bg-board-raised p-3 font-mono text-xs leading-relaxed" role="log" aria-live="polite">
          {lines.map((l) => (
            <div key={`${l.attempt}-${l.sequence}`} className="grid grid-cols-[28px_minmax(0,1fr)] gap-2">
              <span className="text-board-muted">#{l.attempt}</span>
              <span className={`break-words ${l.stream === "stderr" ? "text-amber" : "text-board-soft"}`}>{l.line}</span>
            </div>
          ))}
          <div ref={bottom} />
        </div>
      )}
    </section>
  );
}
