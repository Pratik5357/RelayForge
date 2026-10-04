import { ApiError, apiBaseUrl, getJob } from "./api";
import { isJobFinished, type JobDetail } from "./types";

export type JobChangedEvent = {
  jobId: string;
  jobState: string;
  taskId: string | null;
  taskName: string | null;
  taskState: string | null;
  message: string;
};

export type LiveSource = "connecting" | "signalr" | "polling" | "stopped";

type WatchJobCallbacks = {
  onUpdate: (job: JobDetail) => void;
  onEvent?: (evt: JobChangedEvent) => void;
  onSourceChange?: (source: LiveSource) => void;
  onError?: (message: string | null) => void;
};

const POLL_INTERVAL_MS = 1500;

/**
 * Watches one job's state: tries a SignalR push connection first, falls back to polling if
 * the connection can't be established or drops, and stops updating once the job is terminal.
 * Returns a cleanup function.
 */
export function watchJob(jobId: string, callbacks: WatchJobCallbacks): () => void {
  let stopped = false;
  let pollTimer: ReturnType<typeof setTimeout> | undefined;
  let connection: import("@microsoft/signalr").HubConnection | undefined;

  function setSource(source: LiveSource) {
    if (!stopped) callbacks.onSourceChange?.(source);
  }

  async function refreshOnce(): Promise<JobDetail | null> {
    try {
      const job = await getJob(jobId);
      if (stopped) return job;
      callbacks.onUpdate(job);
      callbacks.onError?.(null);
      return job;
    } catch (err) {
      if (!stopped) {
        callbacks.onError?.(err instanceof ApiError ? err.message : "Could not reach the API.");
      }
      return null;
    }
  }

  function startPolling() {
    if (stopped) return;
    setSource("polling");

    async function poll() {
      if (stopped) return;
      const job = await refreshOnce();
      if (stopped) return;
      if (job && isJobFinished(job.state)) {
        setSource("stopped");
        return;
      }
      pollTimer = setTimeout(poll, POLL_INTERVAL_MS);
    }

    poll();
  }

  async function start() {
    setSource("connecting");
    const initial = await refreshOnce();
    if (stopped) return;

    if (initial && isJobFinished(initial.state)) {
      setSource("stopped");
      return;
    }

    try {
      const signalR = await import("@microsoft/signalr");
      connection = new signalR.HubConnectionBuilder()
        .withUrl(`${apiBaseUrl}/hubs/jobs`, { withCredentials: true })
        .withAutomaticReconnect()
        .build();

      connection.on("jobChanged", (evt: JobChangedEvent) => {
        if (stopped || evt.jobId !== jobId) return;
        callbacks.onEvent?.(evt);
        void refreshOnce().then((job) => {
          if (!stopped && job && isJobFinished(job.state)) {
            setSource("stopped");
            connection?.stop().catch(() => {});
          }
        });
      });

      connection.onreconnecting(() => setSource("connecting"));
      connection.onreconnected(() => {
        setSource("signalr");
        connection?.invoke("Subscribe", jobId).catch(() => {});
      });
      connection.onclose(() => {
        if (!stopped) startPolling();
      });

      await connection.start();
      if (stopped) {
        await connection.stop();
        return;
      }
      await connection.invoke("Subscribe", jobId);
      setSource("signalr");
    } catch {
      if (!stopped) startPolling();
    }
  }

  void start();

  return function stop() {
    stopped = true;
    if (pollTimer) clearTimeout(pollTimer);
    connection?.stop().catch(() => {});
  };
}
