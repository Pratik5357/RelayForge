import type { JobTaskDto } from "@/lib/types";
import type { Attempt } from "@/lib/attempts";
import type { JobChangedEvent } from "@/lib/live";
import { Remark } from "./Remark";
import { CloseButton } from "./Overlay";

function clock(ms: number | string | null) {
  if (ms === null) return "—";
  return new Date(ms).toLocaleTimeString([], { hour12: false });
}

const OUTCOME: Record<Attempt["outcome"], string> = {
  running: "running now",
  succeeded: "succeeded",
  failed: "failed",
  stopped: "stopped",
};

type StepSheetProps = {
  task: JobTaskDto;
  stage: number;
  waits: string;
  attempts: Attempt[];
  events: (JobChangedEvent & { at: number })[];
  onClose: () => void;
};

/** Everything about one step: its state, every try this page has seen, and its own events. */
export function StepSheet({ task, stage, waits, attempts, events, onClose }: StepSheetProps) {
  const mine = events.filter((e) => e.taskId === task.id || e.taskName === task.name).reverse();

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

      <div className="flex items-center gap-4 border-b border-board-rule px-6 py-5">
        <Remark state={task.state} size="lg" />
        <span className="tnum font-mono text-lg">
          {task.attemptCount}/{task.maxAttempts} tries
        </span>
      </div>

      <dl className="flex flex-col px-6 py-2">
        {[
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

      <section aria-labelledby="step-tries" className="flex flex-col gap-2.5 px-6 pb-7 pt-3">
        <h3 id="step-tries" className="label text-board-muted">
          What happened
        </h3>
        {attempts.length === 0 && mine.length === 0 ? (
          <p className="text-board-muted">Nothing yet. This step is waiting its turn.</p>
        ) : (
          <ol className="flex flex-col">
            {attempts.map((a, i) => (
              <li key={`a${i}`} className="grid grid-cols-[76px_minmax(0,1fr)] gap-3 border-b border-board-rule py-2.5">
                <span className="font-mono text-[13px] text-board-muted">{clock(a.start)}</span>
                <span>
                  <span className="font-bold">{a.count > 1 ? `Tries 1–${a.count}` : `Try ${a.number}`}</span>{" "}
                  <span className="text-board-soft">
                    {OUTCOME[a.outcome]}
                    {a.end !== null && ` after ${((a.end - a.start) / 1000).toFixed(1)}s`}
                  </span>
                </span>
              </li>
            ))}
            {mine.map((e, i) => (
              <li key={`e${i}`} className="grid grid-cols-[76px_minmax(0,1fr)] gap-3 border-b border-board-rule py-2.5">
                <span className="font-mono text-[13px] text-board-muted">{clock(e.at)}</span>
                <span>
                  <span className="font-bold">{e.taskState ?? e.jobState}</span>{" "}
                  <span className="text-board-soft">{e.message}</span>
                </span>
              </li>
            ))}
          </ol>
        )}
      </section>
    </>
  );
}
