import type { JobStatus, SubmitJobRequest, SystemInfo } from "./types";

/**
 * Base URL of the TaskScheduler.Api service. Requests run from the browser, so
 * this has to be an origin the browser can reach (the API allows any origin).
 */
export const apiBaseUrl = (
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5053"
).replace(/\/$/, "");

export const hubUrl = `${apiBaseUrl}/hubs/jobs`;

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl}${path}`, {
      ...init,
      cache: "no-store",
      headers: { "Content-Type": "application/json", ...init?.headers },
    });
  } catch {
    throw new ApiError(`Cannot reach the API at ${apiBaseUrl}.`, 0);
  }

  if (!response.ok) {
    throw new ApiError(await errorMessage(response), response.status);
  }

  return (await response.json()) as T;
}

/** The API returns `{ error }` for validation failures and plain text otherwise. */
async function errorMessage(response: Response): Promise<string> {
  const body = await response.text();
  if (body) {
    try {
      const parsed = JSON.parse(body) as { error?: string; title?: string };
      const message = parsed.error ?? parsed.title;
      if (message) {
        return message;
      }
    } catch {
      return body;
    }
  }
  return `${response.status} ${response.statusText}`;
}

export function listJobs(take = 50): Promise<JobStatus[]> {
  return request<JobStatus[]>(`/api/jobs?take=${take}`);
}

export function getJob(id: string): Promise<JobStatus> {
  return request<JobStatus>(`/api/jobs/${id}`);
}

export function submitJob(job: SubmitJobRequest): Promise<JobStatus> {
  return request<JobStatus>("/api/jobs", {
    method: "POST",
    body: JSON.stringify(job),
  });
}

export function cancelJob(id: string): Promise<JobStatus> {
  return request<JobStatus>(`/api/jobs/${id}/cancel`, { method: "POST" });
}

export function getSystemInfo(): Promise<SystemInfo> {
  return request<SystemInfo>("/api/system");
}
