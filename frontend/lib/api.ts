import type { JobDetail, JobSummary, SubmitJobRequest } from "./types";

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

export async function cancelJob(id: string): Promise<JobDetail> {
  const response = await fetch(`${apiBaseUrl}/api/jobs/${id}/cancel`, {
    method: "POST",
  });
  return handleResponse<JobDetail>(response);
}

export { apiBaseUrl };
