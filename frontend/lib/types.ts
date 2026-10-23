export type JobState =
  | "Pending"
  | "Running"
  | "Succeeded"
  | "Failed"
  | "PartiallyFailed"
  | "Cancelled";

export type TaskState =
  | "Pending"
  | "Ready"
  | "Running"
  | "Succeeded"
  | "Failed"
  | "DeadLettered"
  | "Cancelled";

export type JobSummary = {
  id: string;
  name: string | null;
  scenarioKey: string | null;
  state: JobState;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  taskCount: number;
  pausedAt?: string | null;
  definitionId?: string | null;
  scheduledFor?: string | null;
};

export type AttemptOutcome = "Succeeded" | "Failed" | "Fatal" | "LeaseLost" | "Cancelled" | "Released";

/** One attempt as the server recorded it. outcome is null while it is still running. */
export type TaskAttemptDto = {
  attempt: number;
  worker: string;
  startedAt: string;
  endedAt: string | null;
  outcome: AttemptOutcome | null;
  error: string | null;
  /** What a worker that had lost its lease tried to report anyway (rejected). */
  rejectedLateResult: string | null;
};

export type TaskIsolation = "InProcess" | "Process" | "Container";

/** What the attempt history proves. duplicateCompletions must stay 0. */
export type Invariants = {
  attempts: number;
  succeededAttempts: number;
  duplicateCompletions: number;
  leasesReclaimed: number;
  staleResultsRejected: number;
};

export type TaskLogLine = {
  attempt: number;
  sequence: number;
  stream: "stdout" | "stderr";
  line: string;
  at: string;
};

export type JobTaskDto = {
  id: string;
  name: string;
  state: TaskState;
  dependsOn: string[];
  startedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
  attemptCount: number;
  maxAttempts: number;
  nextAttemptAt: string | null;
  idempotencyKey: string;
  key: string;
  type: string;
  isolation: TaskIsolation;
  timeoutMs: number;
  priority: number;
  payload: unknown;
  output: unknown;
  attempts: TaskAttemptDto[];
};

export type JobDetail = {
  id: string;
  name: string | null;
  scenarioKey: string | null;
  state: JobState;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  cancellationRequestedAt: string | null;
  tasks: JobTaskDto[];
  pausedAt: string | null;
  tenantId: string | null;
  definitionId: string | null;
  scheduledFor: string | null;
  lastEventSequence: number;
  invariants: Invariants;
};

export type SubmitJobTaskRequest = {
  key: string;
  name: string;
  /** For "simulated" tasks without a payload (the v1 shape). */
  simulatedDurationMs?: number;
  dependsOn: string[];
  maxAttempts?: number;
  failUntilAttempt?: number;
  /** Handler type; defaults to "simulated". */
  type?: string;
  payload?: unknown;
  isolation?: TaskIsolation;
  timeoutMs?: number;
  priority?: number;
};

export type SubmitJobRequest = {
  name?: string;
  scenarioKey?: string;
  tasks: SubmitJobTaskRequest[];
  priority?: number;
  tenantId?: string;
};

export type MisfirePolicy = "FireOnce" | "Skip" | "CatchUp";
export type ConcurrencyPolicy = "Allow" | "Forbid" | "Replace";

export type Schedule = {
  id: string;
  name: string;
  cron: string;
  timeZone: string;
  misfirePolicy: MisfirePolicy;
  concurrencyPolicy: ConcurrencyPolicy;
  enabled: boolean;
  isSystem: boolean;
  nextFireAt: string;
  lastFiredAt: string | null;
  template: SubmitJobRequest;
  recentRuns: JobSummary[];
};

export type CreateScheduleRequest = {
  name: string;
  cron: string;
  timeZone?: string;
  template: SubmitJobRequest;
  misfirePolicy?: MisfirePolicy;
  concurrencyPolicy?: ConcurrencyPolicy;
};

/** Not started yet: waiting on a dependency (Pending) or queued for a worker (Ready). */
export function isWaiting(state: TaskState): boolean {
  return state === "Pending" || state === "Ready";
}

export const TERMINAL_JOB_STATES: JobState[] = [
  "Succeeded",
  "Failed",
  "PartiallyFailed",
  "Cancelled",
];

export function isJobFinished(state: JobState): boolean {
  return TERMINAL_JOB_STATES.includes(state);
}
