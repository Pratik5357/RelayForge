import { isWaiting, type JobTaskDto } from "@/lib/types";
import { toDagLevels } from "@/lib/dag";
import { Remark } from "./Remark";
import { RevealItem, RevealList } from "./Reveal";
import { ChangeGlow } from "./ChangeGlow";

export function retryCountdown(nextAttemptAt: string | null, now: number): string | null {
  if (!nextAttemptAt) return null;
  const remainingMs = new Date(nextAttemptAt).getTime() - now;
  if (remainingMs <= 0) return "Retrying…";
  return `Retrying in ${Math.ceil(remainingMs / 1000)}s`;
}

const NOTE: Partial<Record<JobTaskDto["state"], string>> = {
  Pending: "Waiting on an earlier step",
  Ready: "Up next",
  Running: "Working now",
  Succeeded: "Finished",
  Cancelled: "Called off",
};

const COLUMNS = "grid-cols-[56px_minmax(0,1.3fr)_minmax(0,1fr)_76px_176px]";

/**
 * The run's steps as board rows in stage order: stage, step, what it waits for, tries and remark.
 * Each row opens that step's details.
 */
export function StepsBoard({ tasks, now, onOpen }: { tasks: JobTaskDto[]; now: number; onOpen: (taskId: string) => void }) {
  const levels = toDagLevels(tasks);
  const nameById = new Map(tasks.map((t) => [t.id, t.name]));

  return (
    <div className="overflow-x-auto rounded-xl">
      <div className="on-board min-w-[660px] rounded-xl bg-board text-board-text">
        <div className={`label grid ${COLUMNS} gap-x-4 border-b border-board-rule px-6 py-3 text-board-muted`} aria-hidden>
          <span>Stage</span>
          <span>Step</span>
          <span>Waits for</span>
          <span className="text-right">Tries</span>
          <span>Remark</span>
        </div>
        <RevealList as="ol" aria-label="Steps">
          {levels.flatMap(({ level, tasks: levelTasks }) =>
            levelTasks.map((task) => {
              const countdown = isWaiting(task.state) ? retryCountdown(task.nextAttemptAt, now) : null;
              const note = countdown ?? (task.errorMessage && task.state !== "Succeeded" ? task.errorMessage : NOTE[task.state]);
              const noteColor = countdown
                ? "var(--amber)"
                : task.state === "DeadLettered"
                  ? "var(--remark-deadlettered)"
                  : task.state === "Failed"
                    ? "var(--remark-failed)"
                    : "var(--board-muted)";
              const waits = task.dependsOn.map((id) => nameById.get(id) ?? id).join(", ") || "—";

              return (
                <RevealItem key={task.id} distance={10} className="relative isolate border-b border-board-rule last:border-b-0">
                  <ChangeGlow token={`${task.state}:${task.attemptCount}`} />
                  <button
                    type="button"
                    aria-haspopup="dialog"
                    onClick={() => onOpen(task.id)}
                    className={`grid w-full ${COLUMNS} min-h-[72px] cursor-pointer items-center gap-x-4 px-6 py-2 text-left transition-colors duration-150 hover:bg-board-raised`}
                  >
                    <span className="font-mono text-[22px] font-semibold">{level + 1}</span>
                    <span className="flex min-w-0 flex-col">
                      <span className="truncate text-lg font-bold">{task.name}</span>
                      <span className="line-clamp-2 text-[13px]" style={{ color: noteColor }}>
                        {note}
                      </span>
                    </span>
                    <span className="truncate text-[15px] text-board-soft">{waits}</span>
                    <span className="tnum text-right font-mono text-[17px]">
                      {task.attemptCount}/{task.maxAttempts}
                    </span>
                    <span className="flex items-center justify-between gap-2">
                      <Remark state={task.state} />
                      <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden>
                        <path d="M5 3l4 4-4 4" fill="none" stroke="var(--board-muted)" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
                      </svg>
                    </span>
                  </button>
                </RevealItem>
              );
            }),
          )}
        </RevealList>
      </div>
    </div>
  );
}
