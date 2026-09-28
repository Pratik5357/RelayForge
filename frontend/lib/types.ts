// Mirrors the DTOs in backend/src/TaskScheduler.Infrastructure/Jobs/JobService.cs.

export const TASK_STATES = [
  "Pending",
  "Running",
  "Succeeded",
  "Failed",
  "DeadLettered",
  "Cancelled",
] as const;

export const JOB_STATES = [
  "Pending",
  "Running",
  "Succeeded",
  "Failed",
  "PartiallyFailed",
  "Cancelled",
] as const;

export type TaskState = (typeof TASK_STATES)[number];
export type JobState = (typeof JOB_STATES)[number];

export type TaskStatus = {
  id: string;
  key: string;
  /** Handler type: echo, delay or fail. */
  type: string;
  state: TaskState;
  attemptCount: number;
  maxAttempts: number;
  lastError: string | null;
  resultJson: string | null;
  dependsOn: string[];
};

export type JobStatus = {
  id: string;
  name: string;
  state: JobState;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  tasks: TaskStatus[];
};

export type SubmitTask = {
  key: string;
  type: string;
  payload?: unknown;
  dependsOn?: string[];
  maxAttempts?: number;
};

export type SubmitJobRequest = {
  name: string;
  tasks: SubmitTask[];
};

/** Response of `GET /api/system` (see SystemInfoService). */
export type SystemInfo = {
  wiring: {
    database: string;
    queue: string;
    lockProvider: string;
    workerCount: number;
    inProcessWorkers: boolean;
    leaseSeconds: number;
    heartbeatSeconds: number;
  };
  counts: {
    jobs: number;
    tasks: number;
    deadLetters: number;
    liveWorkers: number;
    jobsByState: Record<string, number>;
    tasksByState: Record<string, number>;
  };
  serverTime: string;
};

/** Payload of the SignalR `jobChanged` message (see JobEventsHub). */
export type JobChangedEvent = {
  jobId: string;
  jobState: string;
  taskId: string | null;
  taskKey: string | null;
  taskState: string | null;
  message: string;
};

const TERMINAL_JOB_STATES: JobState[] = [
  "Succeeded",
  "Failed",
  "PartiallyFailed",
  "Cancelled",
];

export function isJobFinished(state: JobState): boolean {
  return TERMINAL_JOB_STATES.includes(state);
}
