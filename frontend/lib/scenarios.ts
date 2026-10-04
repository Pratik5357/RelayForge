import type { SubmitJobRequest } from "./types";

export type Scenario = {
  id: string;
  title: string;
  summary: string;
  concept: string;
  watchFor: string[];
  expected: string;
  runtime: string;
  job: SubmitJobRequest;
};

export const SCENARIOS: Scenario[] = [
  {
    id: "ordering",
    title: "1. Steps run in order",
    summary:
      "A three-step chain, each one depending on the last. Watch them start strictly in sequence.",
    concept: "Dependency ordering (topological sort)",
    watchFor: [
      "fetch-data starts first and finishes before anything else starts.",
      "transform-data only starts once fetch-data has succeeded.",
      "load-data only starts once transform-data has succeeded.",
    ],
    expected: "Succeeded — all 3 steps green, run strictly one after another.",
    runtime: "~6s",
    job: {
      name: "1. Steps run in order",
      scenarioKey: "ordering",
      tasks: [
        { key: "fetch", name: "fetch-data", simulatedDurationMs: 2000, dependsOn: [] },
        { key: "transform", name: "transform-data", simulatedDurationMs: 2000, dependsOn: ["fetch"] },
        { key: "load", name: "load-data", simulatedDurationMs: 2000, dependsOn: ["transform"] },
      ],
    },
  },
  {
    id: "parallel",
    title: "2. Independent steps run at the same time",
    summary:
      "One setup step, then two branches that don't depend on each other. They run concurrently, not one after the other.",
    concept: "Parallelism and fan-out",
    watchFor: [
      "shard-a and shard-b both go Running at roughly the same moment, right after setup succeeds.",
      "Total time is close to setup + the slower shard, not the sum of every step.",
    ],
    expected: "Succeeded — total time ≈ slowest branch, not the sum of all branches.",
    runtime: "~5s",
    job: {
      name: "2. Independent steps run at the same time",
      scenarioKey: "parallel",
      tasks: [
        { key: "setup", name: "setup", simulatedDurationMs: 1500, dependsOn: [] },
        { key: "shard-a", name: "shard-a", simulatedDurationMs: 3000, dependsOn: ["setup"] },
        { key: "shard-b", name: "shard-b", simulatedDurationMs: 3500, dependsOn: ["setup"] },
      ],
    },
  },
  {
    id: "retry",
    title: "3. A flaky step retries until it works",
    summary:
      "This step fails on its first two attempts, then succeeds on the third — watch it retry with a growing delay between attempts instead of giving up immediately.",
    concept: "Exponential backoff with jitter",
    watchFor: [
      "The attempt count climbs: 1 of 4, then 2 of 4, then 3 of 4.",
      "There's a growing pause (\"retrying in Ns\") between each attempt.",
      "It finally succeeds on attempt 3 — the job doesn't fail.",
    ],
    expected: "Succeeded — flaky-step shows 3 of 4 attempts used.",
    runtime: "~5s",
    job: {
      name: "3. A flaky step retries until it works",
      scenarioKey: "retry",
      tasks: [
        {
          key: "flaky",
          name: "flaky-step",
          simulatedDurationMs: 1000,
          dependsOn: [],
          maxAttempts: 4,
          failUntilAttempt: 3,
        },
      ],
    },
  },
  {
    id: "dead-letter",
    title: "4. A hopeless step gets parked, not retried forever",
    summary:
      "This step never succeeds. Watch RelayForge give it exactly 3 chances, then stop and set it aside instead of retrying forever.",
    concept: "Dead-letter queue",
    watchFor: [
      "The attempt count climbs to 3 of 3, with a growing delay each time.",
      "After the 3rd failure, the step is marked DeadLettered — no 4th attempt happens.",
    ],
    expected: "Failed — hopeless-step shows DeadLettered.",
    runtime: "~6s",
    job: {
      name: "4. A hopeless step gets parked, not retried forever",
      scenarioKey: "dead-letter",
      tasks: [
        {
          key: "hopeless",
          name: "hopeless-step",
          simulatedDurationMs: 1000,
          dependsOn: [],
          maxAttempts: 3,
          failUntilAttempt: 10,
        },
      ],
    },
  },
  {
    id: "partial",
    title: "5. One broken branch does not sink the rest",
    summary:
      "Two branches split off from one setup step. One branch is healthy; the other is hopeless and dead-letters. The healthy branch still gets credit for succeeding.",
    concept: "Per-job state derived honestly from its tasks",
    watchFor: [
      "healthy-branch runs to Succeeded.",
      "hopeless-branch exhausts its attempts and ends DeadLettered.",
      "The job's own state reflects the mix, rather than one failure sinking everything.",
    ],
    expected: "PartiallyFailed — one branch Succeeded, the other DeadLettered.",
    runtime: "~6s",
    job: {
      name: "5. One broken branch does not sink the rest",
      scenarioKey: "partial",
      tasks: [
        { key: "start", name: "start", simulatedDurationMs: 1000, dependsOn: [] },
        { key: "healthy", name: "healthy-branch", simulatedDurationMs: 1500, dependsOn: ["start"] },
        {
          key: "hopeless",
          name: "hopeless-branch",
          simulatedDurationMs: 1000,
          dependsOn: ["start"],
          maxAttempts: 3,
          failUntilAttempt: 10,
        },
      ],
    },
  },
  {
    id: "cancel",
    title: "6. Cancel a run that is still going",
    summary:
      "Three steps, each taking a few seconds. Submit it, then press Cancel while one is still running — watch what happens to the step in flight versus the ones that haven't started.",
    concept: "Cooperative cancellation",
    watchFor: [
      "Press Cancel while slow-step-1 shows Running.",
      "slow-step-2 and slow-step-3 (not yet started) immediately show Cancelled.",
      "slow-step-1 is left alone and finishes naturally.",
      "Once it settles, the job itself ends Cancelled.",
    ],
    expected: "Cancelled — in-flight step finishes, the rest are called off.",
    runtime: "~15s (cancel partway through)",
    job: {
      name: "6. Cancel a run that is still going",
      scenarioKey: "cancel",
      tasks: [
        { key: "slow1", name: "slow-step-1", simulatedDurationMs: 6000, dependsOn: [] },
        { key: "slow2", name: "slow-step-2", simulatedDurationMs: 6000, dependsOn: ["slow1"] },
        { key: "slow3", name: "slow-step-3", simulatedDurationMs: 6000, dependsOn: ["slow2"] },
      ],
    },
  },
];

export function findScenario(id: string | null | undefined): Scenario | undefined {
  if (!id) return undefined;
  return SCENARIOS.find((s) => s.id === id);
}
