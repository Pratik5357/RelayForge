import type { TaskStatus } from "./types";

/**
 * Groups tasks into dependency levels: level 0 has no dependencies, and every
 * later level runs only once the levels before it are done. This mirrors the
 * layering the backend's Kahn topological sort produces, and gives the DAG view
 * its columns. The API already rejects cycles, but any task we cannot place
 * (missing or cyclic dependency) is pushed onto the last level rather than
 * dropped.
 */
export function toDagLevels(tasks: TaskStatus[]): TaskStatus[][] {
  const levelByKey = new Map<string, number>();
  const known = new Set(tasks.map((t) => t.key));
  let remaining = tasks;

  while (remaining.length > 0) {
    const ready = remaining.filter((task) =>
      task.dependsOn.every((dep) => !known.has(dep) || levelByKey.has(dep)),
    );

    if (ready.length === 0) {
      // Cycle or unresolvable dependency — place the rest together at the end.
      const last = Math.max(-1, ...levelByKey.values()) + 1;
      for (const task of remaining) {
        levelByKey.set(task.key, last);
      }
      break;
    }

    for (const task of ready) {
      const depLevels = task.dependsOn
        .map((dep) => levelByKey.get(dep))
        .filter((level): level is number => level !== undefined);
      levelByKey.set(task.key, depLevels.length === 0 ? 0 : Math.max(...depLevels) + 1);
    }

    remaining = remaining.filter((task) => !levelByKey.has(task.key));
  }

  const levels: TaskStatus[][] = [];
  for (const task of tasks) {
    const level = levelByKey.get(task.key) ?? 0;
    (levels[level] ??= []).push(task);
  }

  return levels.filter((level) => level !== undefined && level.length > 0);
}
