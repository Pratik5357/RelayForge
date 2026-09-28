import type { SubmitJobRequest } from "./types";

/**
 * The guided tour. Each scenario is a job the engine can run right now, paired
 * with the one thing it is meant to prove. A first-time visitor should be able to
 * click through these in order and end up understanding the whole system, without
 * writing any JSON or knowing what to fill in.
 */
export type Scenario = {
  id: string;
  title: string;
  /** One line, plain language: what this job does. */
  summary: string;
  /** The distributed-systems idea on display. */
  concept: string;
  /** What to watch on the job page while it runs. */
  watchFor: string[];
  /** The state the job should end in, so a visitor knows if it worked. */
  expected: string;
  /** Roughly how long the run takes, so nobody thinks it hung. */
  runtime: string;
  job: SubmitJobRequest;
};

export const SCENARIOS: Scenario[] = [
  {
    id: "ordering",
    title: "1. Steps run in order",
    summary: "Three steps in a line: extract, then transform, then load.",
    concept: "Dependency ordering (topological sort)",
    watchFor: [
      "Each step only starts once the one before it succeeded.",
      "The DAG shows three columns, one step per column.",
    ],
    expected: "Succeeded, with all three steps green.",
    runtime: "about 1 second",
    job: {
      name: "demo-ordering",
      tasks: [
        { key: "extract", type: "delay", payload: { milliseconds: 400 } },
        { key: "transform", type: "delay", payload: { milliseconds: 400 }, dependsOn: ["extract"] },
        { key: "load", type: "echo", payload: { rows: 42 }, dependsOn: ["transform"] },
      ],
    },
  },
  {
    id: "parallel",
    title: "2. Independent steps run at the same time",
    summary: "One step fans out into two shards, then a merge step waits for both.",
    concept: "Parallelism and join (fan-out / fan-in)",
    watchFor: [
      "Both shards turn blue together — they only depend on seed, so nothing makes them wait for each other.",
      "merge stays grey until the slower shard finishes, then runs once.",
    ],
    expected: "Succeeded. Total time is about as long as the slowest shard, not the sum of both.",
    runtime: "about 2 seconds",
    job: {
      name: "demo-parallel",
      tasks: [
        { key: "seed", type: "echo", payload: { rows: 2 } },
        { key: "shard-a", type: "delay", payload: { milliseconds: 700 }, dependsOn: ["seed"] },
        { key: "shard-b", type: "delay", payload: { milliseconds: 1600 }, dependsOn: ["seed"] },
        { key: "merge", type: "echo", payload: { step: "merge" }, dependsOn: ["shard-a", "shard-b"] },
      ],
    },
  },
  {
    id: "retry",
    title: "3. A flaky step retries until it works",
    summary: "The first step fails twice on purpose and succeeds on its third attempt.",
    concept: "Exponential backoff with jitter",
    watchFor: [
      "The attempt counter on flaky-step climbs 1/4, 2/4, 3/4.",
      "The wait between attempts grows — that is the exponential backoff.",
      "The step that depends on it never runs early; it waits for the eventual success.",
    ],
    expected: "Succeeded, with flaky-step showing 3 of 4 attempts used.",
    runtime: "a few seconds, because of the backoff waits",
    job: {
      name: "demo-retry",
      tasks: [
        { key: "flaky-step", type: "fail", payload: { succeedOnAttempt: 3 }, maxAttempts: 4 },
        { key: "after-recovery", type: "echo", payload: { ok: true }, dependsOn: ["flaky-step"] },
      ],
    },
  },
  {
    id: "dead-letter",
    title: "4. A hopeless step gets parked, not retried forever",
    summary: "A step that always fails, allowed two attempts.",
    concept: "Dead-letter queue",
    watchFor: [
      "Two attempts, then the state becomes DeadLettered instead of retrying again.",
      "The error message from the last attempt is kept on the card.",
      "The dead-letter count in the wiring panel on the home page goes up.",
    ],
    expected: "Failed, with the step DeadLettered. That is the system working, not breaking.",
    runtime: "a few seconds",
    job: {
      name: "demo-dead-letter",
      tasks: [{ key: "always-fails", type: "fail", payload: {}, maxAttempts: 2 }],
    },
  },
  {
    id: "partial",
    title: "5. One broken branch does not sink the rest",
    summary: "Two independent branches, one of which always fails.",
    concept: "Per-job state derived from its tasks",
    watchFor: [
      "The healthy branch still finishes while the other one gives up.",
      "The job ends as PartiallyFailed — neither wholly done nor wholly lost.",
    ],
    expected: "PartiallyFailed: one step Succeeded, one DeadLettered.",
    runtime: "a few seconds",
    job: {
      name: "demo-partial-failure",
      tasks: [
        { key: "healthy-branch", type: "delay", payload: { milliseconds: 500 } },
        { key: "broken-branch", type: "fail", payload: {}, maxAttempts: 1 },
      ],
    },
  },
  {
    id: "cancel",
    title: "6. Cancel a run that is still going",
    summary: "A slow chain of steps, so there is time to press Cancel.",
    concept: "Cooperative cancellation",
    watchFor: [
      "Press Cancel while the first step is still blue.",
      "Steps that had not started yet go to Cancelled; the one already running is left to finish.",
    ],
    expected: "Cancelled, once the in-flight step settles.",
    runtime: "about 15 seconds if you let it run to the end",
    job: {
      name: "demo-cancel",
      tasks: [
        { key: "slow-step-1", type: "delay", payload: { milliseconds: 5000 } },
        { key: "slow-step-2", type: "delay", payload: { milliseconds: 5000 }, dependsOn: ["slow-step-1"] },
        { key: "slow-step-3", type: "delay", payload: { milliseconds: 5000 }, dependsOn: ["slow-step-2"] },
      ],
    },
  },
];

export function findScenario(id: string | null | undefined): Scenario | undefined {
  return id ? SCENARIOS.find((scenario) => scenario.id === id) : undefined;
}
