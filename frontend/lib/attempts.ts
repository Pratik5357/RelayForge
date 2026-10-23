import type { AttemptOutcome, JobDetail } from "./types";

export type Attempt = {
  /** Which try this was. */
  number: number;
  /** How many tries this bar stands for. Always 1 now that the server records every attempt. */
  count: number;
  start: number;
  end: number | null;
  outcome: "running" | "succeeded" | "failed" | "stopped";
  /** Short id of the worker that ran it. */
  worker: string;
  /** The server's word for how it ended (null while running). */
  result: AttemptOutcome | null;
  error: string | null;
  rejectedLateResult: string | null;
};

/** Tries per task id. */
export type AttemptHistory = Record<string, Attempt[]>;

const OUTCOME: Record<AttemptOutcome, Attempt["outcome"]> = {
  Succeeded: "succeeded",
  Failed: "failed",
  Fatal: "failed",
  LeaseLost: "failed",
  Cancelled: "stopped",
  Released: "stopped",
};

/** Every task's attempts, straight from the server's attempt history. */
export function attemptsOf(job: JobDetail): AttemptHistory {
  return Object.fromEntries(
    job.tasks.map((task) => [
      task.id,
      (task.attempts ?? []).map((a) => ({
        number: a.attempt,
        count: 1,
        start: new Date(a.startedAt).getTime(),
        end: a.endedAt ? new Date(a.endedAt).getTime() : null,
        outcome: a.outcome ? OUTCOME[a.outcome] : "running",
        worker: a.worker,
        result: a.outcome,
        error: a.error,
        rejectedLateResult: a.rejectedLateResult,
      })),
    ]),
  );
}
