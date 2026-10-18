import type { JobDetail, JobTaskDto, TaskState } from "./types";

export type Attempt = {
  /** Which try this was, as reported by the task when it started. */
  number: number;
  /**
   * How many tries this bar stands for. More than 1 only for a step that had already finished
   * when the page opened: its tries weren't seen one by one, so they are drawn as one span.
   */
  count: number;
  start: number;
  end: number | null;
  outcome: "running" | "succeeded" | "failed" | "stopped";
};

/** Tries per task id, as observed by this page. The API reports only the current try. */
export type AttemptHistory = Record<string, Attempt[]>;

type Snapshot = { history: AttemptHistory; states: Record<string, TaskState> };

export const EMPTY_SNAPSHOT: Snapshot = { history: {}, states: {} };

function time(iso: string | null): number | null {
  return iso ? new Date(iso).getTime() : null;
}

function outcomeOf(task: JobTaskDto): Attempt["outcome"] {
  if (task.state === "Succeeded") return "succeeded";
  if (task.state === "Cancelled") return "stopped";
  return "failed";
}

/**
 * Folds one fresh job snapshot into the observed history. A step seen entering Running opens a
 * try; seen leaving it closes the try with its outcome. A step that finished before the page
 * opened gets a single try spanning startedAt..completedAt, since earlier tries weren't seen.
 */
export function advanceHistory(prev: Snapshot, job: JobDetail, now: number): Snapshot {
  const history: AttemptHistory = { ...prev.history };
  const states: Record<string, TaskState> = {};

  for (const task of job.tasks) {
    states[task.id] = task.state;
    const before = prev.states[task.id];
    const tries = [...(history[task.id] ?? [])];
    const open = tries.length > 0 && tries[tries.length - 1].end === null ? tries[tries.length - 1] : null;

    if (before === undefined) {
      // First sighting.
      const started = time(task.startedAt);
      if (task.state === "Running") {
        tries.push({
          number: Math.max(task.attemptCount, 1),
          count: 1,
          start: task.attemptCount <= 1 && started ? started : now,
          end: null,
          outcome: "running",
        });
      } else if (started && task.state !== "Pending") {
        tries.push({
          number: Math.max(task.attemptCount, 1),
          count: Math.max(task.attemptCount, 1),
          start: started,
          end: time(task.completedAt) ?? now,
          outcome: outcomeOf(task),
        });
      }
    } else if (task.state === "Running" && (before !== "Running" || (open && open.number !== task.attemptCount))) {
      if (open) tries[tries.length - 1] = { ...open, end: now, outcome: "failed" };
      tries.push({ number: Math.max(task.attemptCount, 1), count: 1, start: now, end: null, outcome: "running" });
    } else if (task.state !== "Running" && open) {
      tries[tries.length - 1] = {
        ...open,
        end: time(task.completedAt) ?? now,
        outcome: task.state === "Pending" ? "failed" : outcomeOf(task),
      };
    } else if (before === "Pending" && task.state !== "Pending" && task.state !== "Running" && !open) {
      // Started and finished between two updates: draw it from the task's own timestamps.
      const started = time(task.startedAt);
      if (started) {
        const seen = tries.length;
        tries.push({
          number: Math.max(task.attemptCount, 1),
          count: Math.max(task.attemptCount - seen, 1),
          start: tries.length ? tries[tries.length - 1].end ?? started : started,
          end: time(task.completedAt) ?? now,
          outcome: outcomeOf(task),
        });
      }
    }

    history[task.id] = tries;
  }

  return { history, states };
}
