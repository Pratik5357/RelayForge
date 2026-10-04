export type JobState =
  | "Pending"
  | "Running"
  | "Succeeded"
  | "Failed"
  | "PartiallyFailed"
  | "Cancelled";

export type TaskState =
  | "Pending"
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
};

export type SubmitJobTaskRequest = {
  key: string;
  name: string;
  simulatedDurationMs: number;
  dependsOn: string[];
  maxAttempts?: number;
  failUntilAttempt?: number;
};

export type SubmitJobRequest = {
  name?: string;
  scenarioKey?: string;
  tasks: SubmitJobTaskRequest[];
};

export const TERMINAL_JOB_STATES: JobState[] = [
  "Succeeded",
  "Failed",
  "PartiallyFailed",
  "Cancelled",
];

export function isJobFinished(state: JobState): boolean {
  return TERMINAL_JOB_STATES.includes(state);
}
