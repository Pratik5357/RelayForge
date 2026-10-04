import type { JobTaskDto } from "./types";

function step(
  id: string,
  name: string,
  state: JobTaskDto["state"],
  dependsOn: string[],
  extra: Partial<JobTaskDto> = {},
): JobTaskDto {
  return {
    id,
    name,
    state,
    dependsOn,
    startedAt: null,
    completedAt: null,
    errorMessage: null,
    attemptCount: state === "Pending" ? 0 : 1,
    maxAttempts: 3,
    nextAttemptAt: null,
    idempotencyKey: id,
    ...extra,
  };
}

/** Illustrative only (not a real run): one of every look a step can have, shown on the Overview. */
export const EXAMPLE_STEPS: JobTaskDto[] = [
  step("setup", "setup", "Succeeded", []),
  step("shard-a", "shard-a", "Running", ["setup"]),
  step("shard-b", "shard-b", "DeadLettered", ["setup"], {
    attemptCount: 3,
    errorMessage: "gave up after 3 tries",
  }),
  step("merge", "merge-results", "Pending", ["shard-a", "shard-b"]),
];
