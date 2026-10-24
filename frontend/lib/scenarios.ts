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
      "Three steps, each taking a few seconds. Submit it, then press Cancel while one is still running — watch the step in flight get told to stop, and the ones that haven't started get called off.",
    concept: "Cooperative cancellation",
    watchFor: [
      "Press Cancel while slow-step-1 shows Running.",
      "slow-step-2 and slow-step-3 (not yet started) immediately show Cancelled.",
      "slow-step-1 stops within a moment instead of running to the end: its worker is notified straight away.",
      "The job itself ends Cancelled.",
    ],
    expected: "Cancelled — the in-flight step is stopped, the rest are called off.",
    runtime: "~5s (cancel partway through)",
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
  {
    id: "worker-crash",
    title: "7. A worker crashes mid-step",
    summary:
      "The worker running this step goes silent halfway through, as if its machine died: no heartbeat, nothing written. Watch the lease run out, the step get reclaimed, and a different worker finish it — exactly once.",
    concept: "Leases, heartbeats and reclaim",
    watchFor: [
      "charge-card goes Running on one worker, then that worker stops heartbeating.",
      "About 15 seconds later its lease expires and the step is reclaimed.",
      "Try 2 runs on a different worker and succeeds.",
      "Guarantees: 3 tries in all (2 for charge-card), 2 successes, 1 lease reclaimed, 0 duplicate completions.",
    ],
    expected: "Succeeded — reclaimed after the crash, completed once.",
    runtime: "~20s",
    job: {
      name: "7. A worker crashes mid-step",
      scenarioKey: "worker-crash",
      tasks: [
        { key: "charge", name: "charge-card", type: "simulated", payload: { durationMs: 2000, crashOnAttempt: 1 }, dependsOn: [] },
        { key: "receipt", name: "send-receipt", type: "simulated", payload: { durationMs: 1000 }, dependsOn: ["charge"] },
      ],
    },
  },
  {
    id: "zombie",
    title: "8. A frozen worker wakes up too late",
    summary:
      "This step's worker freezes (think: a long garbage-collection pause) for longer than its lease. The step is reclaimed and finished by another worker. Then the frozen one wakes up and reports success anyway. Watch RelayForge reject it.",
    concept: "Fencing tokens",
    watchFor: [
      "Try 1 freezes; its lease expires and the step is reclaimed.",
      "Try 2 succeeds on another worker, and the run finishes.",
      "About 25 seconds in, the frozen worker reports. The event log shows its stale result rejected.",
      "Guarantees: 1 success, 1 stale result rejected, 0 duplicate completions.",
    ],
    expected: "Succeeded — the late result from the frozen worker is rejected.",
    runtime: "~30s",
    job: {
      name: "8. A frozen worker wakes up too late",
      scenarioKey: "zombie",
      tasks: [
        { key: "report", name: "generate-report", type: "simulated", payload: { durationMs: 1000, pauseOnAttempt: 1, pauseMs: 25000 }, dependsOn: [] },
      ],
    },
  },
  {
    id: "http",
    title: "9. Real HTTP calls: retry what can work, stop what can't",
    summary:
      "Two real web requests. One fetches a GitHub repository and passes the answer on to the next step. The other asks for a page that doesn't exist: a 404 can never succeed, so it fails at once instead of retrying.",
    concept: "Retryable vs fatal errors",
    watchFor: [
      "fetch-repo succeeds; open it to see the response stored as its output.",
      "missing-page fails on its first try with HTTP 404 and is not retried.",
      "announce runs after fetch-repo, with fetch-repo's output as its input.",
    ],
    expected: "PartiallyFailed — one request succeeded, the 404 failed without retries.",
    runtime: "~5s",
    job: {
      name: "9. Real HTTP calls",
      scenarioKey: "http",
      tasks: [
        { key: "fetch-repo", name: "fetch-repo", type: "http.request", payload: { url: "https://api.github.com/repos/dotnet/runtime", headers: { "User-Agent": "RelayForge" } }, dependsOn: [] },
        { key: "missing-page", name: "missing-page", type: "http.request", payload: { url: "https://api.github.com/repos/this-org-does-not-exist-404/nope", headers: { "User-Agent": "RelayForge" } }, dependsOn: [], maxAttempts: 4 },
        { key: "announce", name: "announce", type: "simulated", payload: { durationMs: 800 }, dependsOn: ["fetch-repo"] },
      ],
    },
  },
  {
    id: "runaway",
    title: "10. A runaway step is force-stopped",
    summary:
      "This step ignores every request to stop. Running inside the worker, nothing could stop it. So it runs in its own process, and when its 3-second time limit is up, the process is killed.",
    concept: "Process isolation and timeouts",
    watchFor: [
      "Open runaway-step: its log shows the runner process start and keep working.",
      "After 3 seconds the log shows the runner being killed; the try counts as timed out.",
      "The second try is killed the same way, and the step is parked.",
    ],
    expected: "Failed — both tries killed at their time limit.",
    runtime: "~8s",
    job: {
      name: "10. A runaway step is force-stopped",
      scenarioKey: "runaway",
      tasks: [
        { key: "runaway", name: "runaway-step", type: "simulated", payload: { durationMs: 600000, ignoreCancellation: true }, isolation: "Process", timeoutMs: 3000, maxAttempts: 2, dependsOn: [] },
      ],
    },
  },
];

export function findScenario(id: string | null | undefined): Scenario | undefined {
  if (!id) return undefined;
  return SCENARIOS.find((s) => s.id === id);
}
