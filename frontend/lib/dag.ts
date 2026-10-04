import type { JobTaskDto } from "./types";

export type DagLevel = {
  level: number;
  tasks: JobTaskDto[];
};

/**
 * Groups tasks into dependency-level columns for display: a task's level is 1 + the max level
 * of everything it depends on (0 if it has no dependencies). This mirrors the backend's own
 * leveling, just for layout -- it has no bearing on execution order, which the backend already
 * decides for real.
 */
export function toDagLevels(tasks: JobTaskDto[]): DagLevel[] {
  const levelById = new Map<string, number>();

  function levelOf(task: JobTaskDto, seen: Set<string>): number {
    const cached = levelById.get(task.id);
    if (cached !== undefined) return cached;

    if (seen.has(task.id)) {
      // Defensive only -- the backend rejects cycles at submission time, this just avoids an
      // infinite loop if that invariant were ever violated.
      return 0;
    }
    seen.add(task.id);

    if (task.dependsOn.length === 0) {
      levelById.set(task.id, 0);
      return 0;
    }

    const byId = new Map(tasks.map((t) => [t.id, t]));
    const depLevels = task.dependsOn.map((depId) => {
      const dep = byId.get(depId);
      return dep ? levelOf(dep, seen) : 0;
    });

    const level = 1 + Math.max(...depLevels);
    levelById.set(task.id, level);
    return level;
  }

  for (const task of tasks) {
    levelOf(task, new Set());
  }

  const byLevel = new Map<number, JobTaskDto[]>();
  for (const task of tasks) {
    const level = levelById.get(task.id) ?? 0;
    const bucket = byLevel.get(level) ?? [];
    bucket.push(task);
    byLevel.set(level, bucket);
  }

  return Array.from(byLevel.entries())
    .sort(([a], [b]) => a - b)
    .map(([level, levelTasks]) => ({ level, tasks: levelTasks }));
}
