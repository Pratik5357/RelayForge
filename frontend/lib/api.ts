import type { CreateScheduleRequest, JobDetail, JobSummary, Schedule, SubmitJobRequest, TaskLogLine } from "./types";
import type { JobChangedEvent } from "./live";

const apiBaseUrl = process.env.NEXT_PUBLIC_API_URL ?? "https://localhost:7054";

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T;
  if (!response.ok) {
    let detail = response.statusText;
    try {
      const body = await response.json();
      detail = body.detail ?? body.title ?? detail;
    } catch {
      // response had no JSON body; fall back to statusText
    }
    throw new ApiError(detail, response.status);
  }
  return (await response.json()) as T;
}

export async function submitJob(request: SubmitJobRequest): Promise<JobDetail> {
  const response = await fetch(`${apiBaseUrl}/api/jobs`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  return handleResponse<JobDetail>(response);
}

export async function listJobs(take = 20): Promise<JobSummary[]> {
  const response = await fetch(`${apiBaseUrl}/api/jobs?take=${take}`, {
    cache: "no-store",
  });
  return handleResponse<JobSummary[]>(response);
}

export async function getJob(id: string): Promise<JobDetail> {
  const response = await fetch(`${apiBaseUrl}/api/jobs/${id}`, {
    cache: "no-store",
  });
  return handleResponse<JobDetail>(response);
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: "POST",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return handleResponse<T>(response);
}

async function get<T>(path: string): Promise<T> {
  return handleResponse<T>(await fetch(`${apiBaseUrl}${path}`, { cache: "no-store" }));
}

export const cancelJob = (id: string) => post<JobDetail>(`/api/jobs/${id}/cancel`);
export const pauseJob = (id: string) => post<JobDetail>(`/api/jobs/${id}/pause`);
export const resumeJob = (id: string) => post<JobDetail>(`/api/jobs/${id}/resume`);

/** Gives a dead-lettered or failed step a fresh retry budget; reopens a finished run if needed. */
export const redriveTask = (taskId: string, attempts?: number) =>
  post<JobDetail>(`/api/tasks/${taskId}/redrive`, attempts ? { attempts } : {});

/** A run's events after sequence `after`, oldest first. */
export const getEvents = (jobId: string, after = 0) => get<JobChangedEvent[]>(`/api/jobs/${jobId}/events?after=${after}`);

/** Log lines of one step: all attempts, or one attempt after a sequence number. */
export const getTaskLogs = (taskId: string, attempt?: number, after = 0) =>
  get<TaskLogLine[]>(`/api/tasks/${taskId}/logs${attempt === undefined ? "" : `?attempt=${attempt}&after=${after}`}`);

export const listSchedules = () => get<Schedule[]>("/api/schedules");
export const createSchedule = (request: CreateScheduleRequest) => post<Schedule>("/api/schedules", request);
export const setScheduleEnabled = (id: string, enabled: boolean) => post<Schedule>(`/api/schedules/${id}/${enabled ? "enable" : "disable"}`);
export const triggerSchedule = (id: string) => post<JobDetail>(`/api/schedules/${id}/trigger`);
export async function deleteSchedule(id: string): Promise<void> {
  await handleResponse<void>(await fetch(`${apiBaseUrl}/api/schedules/${id}`, { method: "DELETE" }));
}

export { apiBaseUrl };
