import { toDagLevels } from "@/lib/dag";
import type { TaskStatus } from "@/lib/types";
import { StateBadge } from "./StateBadge";

/**
 * The job's steps grouped into dependency levels, left to right. Everything in one
 * column is free to run at the same time; a column cannot start until the column
 * before it is done.
 */
export function DagView({ tasks }: { tasks: TaskStatus[] }) {
  const levels = toDagLevels(tasks);

  return (
    <div className="space-y-3">
      <p className="text-sm text-zinc-500">
        Read left to right. Steps in the same column can run at the same time; a column
        waits for the one before it.
      </p>

      <div className="flex items-start gap-4 overflow-x-auto pb-2">
        {levels.map((level, index) => (
          <div key={index} className="flex min-w-60 shrink-0 flex-col gap-3">
            <div className="text-xs font-medium uppercase tracking-wide text-zinc-500">
              {index === 0 ? "Runs first" : `Waits for column ${index}`}
            </div>
            {level.map((task) => (
              <TaskCard key={task.id} task={task} />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

function TaskCard({ task }: { task: TaskStatus }) {
  const retried = task.attemptCount > 1;

  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-3 dark:border-zinc-800 dark:bg-zinc-900">
      <div className="flex items-center justify-between gap-2">
        <span className="font-mono text-sm font-semibold">{task.key}</span>
        <StateBadge state={task.state} />
      </div>

      <dl className="mt-2 space-y-1 text-xs text-zinc-500">
        <div className="flex gap-1">
          <dt>does</dt>
          <dd className="font-mono text-zinc-700 dark:text-zinc-300">{task.type}</dd>
        </div>
        <div className="flex gap-1">
          <dt>attempts used</dt>
          <dd
            className={`font-mono ${
              retried ? "text-amber-600 dark:text-amber-400" : "text-zinc-700 dark:text-zinc-300"
            }`}
          >
            {task.attemptCount} of {task.maxAttempts}
          </dd>
        </div>
        {task.dependsOn.length > 0 && (
          <div className="flex gap-1">
            <dt>waits for</dt>
            <dd className="font-mono text-zinc-700 dark:text-zinc-300">
              {task.dependsOn.join(", ")}
            </dd>
          </div>
        )}
      </dl>

      {task.state === "DeadLettered" && (
        <p className="mt-2 text-xs text-fuchsia-700 dark:text-fuchsia-300">
          Out of attempts — parked in the dead-letter table for a human to look at.
        </p>
      )}

      {task.lastError && (
        <p className="mt-2 rounded bg-red-50 p-2 font-mono text-xs break-words text-red-700 dark:bg-red-950 dark:text-red-300">
          {task.lastError}
        </p>
      )}

      {task.resultJson && task.state === "Succeeded" && (
        <p className="mt-2 rounded bg-zinc-50 p-2 font-mono text-xs break-all text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400">
          {task.resultJson}
        </p>
      )}
    </div>
  );
}
