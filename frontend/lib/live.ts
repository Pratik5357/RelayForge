import { ApiError, apiBaseUrl, getEvents, getJob } from "./api";
import { isJobFinished, type JobDetail } from "./types";

/**
 * One job event from the server's outbox. `sequence` is per job, starts at 1 and has no gaps,
 * so the page can drop a duplicate (delivery is at-least-once) and notice one it missed.
 */
export type JobChangedEvent = {
  jobId: string;
  sequence: number;
  type: string;
  jobState: string;
  taskId: string | null;
  taskName: string | null;
  taskState: string | null;
  attempt: number | null;
  message: string;
  at: string;
};

export type LiveSource = "connecting" | "signalr" | "polling" | "stopped";

type WatchJobCallbacks = {
  onUpdate: (job: JobDetail) => void;
  /** New events only, in sequence order, each exactly once. */
  onEvents?: (events: JobChangedEvent[]) => void;
  /** A step wrote log lines. */
  onLogs?: (taskId: string) => void;
  onSourceChange?: (source: LiveSource) => void;
  onError?: (message: string | null) => void;
};

const POLL_INTERVAL_MS = 1500;

/**
 * Watches one job: replays its event history, then follows SignalR pushes, falling back to
 * polling if the connection can't be made or drops. Pushes are numbered; a duplicate is ignored,
 * and a gap (or a reconnect) is filled from GET /api/jobs/{id}/events?after=N, so the event log
 * never silently misses a step. Returns a cleanup function.
 */
export function watchJob(jobId: string, callbacks: WatchJobCallbacks): () => void {
  let stopped = false;
  let pollTimer: ReturnType<typeof setTimeout> | undefined;
  let connection: import("@microsoft/signalr").HubConnection | undefined;
  let lastSequence = 0;
  // Serializes catch-ups so two can't deliver the same events.
  let catchingUp: Promise<void> = Promise.resolve();

  function setSource(source: LiveSource) {
    if (!stopped) callbacks.onSourceChange?.(source);
  }

  function deliver(events: JobChangedEvent[]) {
    const fresh = events.filter((e) => e.sequence > lastSequence).sort((a, b) => a.sequence - b.sequence);
    if (fresh.length === 0 || stopped) return;
    lastSequence = fresh[fresh.length - 1].sequence;
    callbacks.onEvents?.(fresh);
  }

  function catchUp(): Promise<void> {
    catchingUp = catchingUp.then(async () => {
      try {
        deliver(await getEvents(jobId, lastSequence));
      } catch {
        // The next push or poll tries again.
      }
    });
    return catchingUp;
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
      const [job] = await Promise.all([refreshOnce(), catchUp()]);
      if (stopped) return;
      if (job && isJobFinished(job.state)) {
        setSource("stopped");
        return;
      }
      pollTimer = setTimeout(poll, POLL_INTERVAL_MS);
    }

    void poll();
  }

  async function start() {
    setSource("connecting");
    await Promise.all([refreshOnce(), catchUp()]);
    if (stopped) return;

    try {
      const signalR = await import("@microsoft/signalr");
      connection = new signalR.HubConnectionBuilder()
        .withUrl(`${apiBaseUrl}/hubs/jobs`, { withCredentials: true })
        .withAutomaticReconnect()
        .build();

      connection.on("jobChanged", (evt: JobChangedEvent) => {
        if (stopped || evt.jobId !== jobId || evt.sequence <= lastSequence) return;

        if (evt.sequence === lastSequence + 1) {
          deliver([evt]);
        } else {
          // Missed one or more: fetch everything after the last one we have.
          void catchUp();
        }

        void refreshOnce();
      });

      connection.on("taskLogs", (msg: { jobId: string; taskId: string }) => {
        if (!stopped && msg.jobId === jobId) callbacks.onLogs?.(msg.taskId);
      });

      connection.onreconnecting(() => setSource("connecting"));
      connection.onreconnected(async () => {
        await connection?.invoke("Subscribe", jobId).catch(() => {});
        setSource("signalr");
        // Whatever happened while we were away.
        await Promise.all([catchUp(), refreshOnce()]);
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
      // Anything committed between the first load and the subscription.
      await Promise.all([catchUp(), refreshOnce()]);
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
