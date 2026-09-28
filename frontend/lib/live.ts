import { getJob, getSystemInfo, hubUrl, listJobs } from "./api";
import {
  isJobFinished,
  type JobChangedEvent,
  type JobStatus,
  type SystemInfo,
} from "./types";

/** Where the current data is coming from, surfaced in the UI. */
export type LiveSource = "connecting" | "signalr" | "polling" | "stopped";

export type Snapshot<T> = {
  data: T | null;
  error: string | null;
  loading: boolean;
};

/**
 * Snapshots are pushed as updater functions so a transient fetch error keeps the
 * last good data on screen. React's `setState` accepts exactly this shape.
 */
type Emit<T> = (update: (previous: Snapshot<T>) => Snapshot<T>) => void;

export const initialSnapshot = <T>(): Snapshot<T> => ({
  data: null,
  error: null,
  loading: true,
});

function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Re-reads the job list on an interval until stopped. */
export function watchJobs(emit: Emit<JobStatus[]>, intervalMs: number): () => void {
  let stopped = false;

  const tick = async () => {
    try {
      const jobs = await listJobs();
      if (!stopped) {
        emit((previous) => ({ ...previous, data: jobs, error: null, loading: false }));
      }
    } catch (error) {
      if (!stopped) {
        emit((previous) => ({ ...previous, error: describe(error), loading: false }));
      }
    }
  };

  void tick();
  const timer = setInterval(() => void tick(), intervalMs);

  return () => {
    stopped = true;
    clearInterval(timer);
  };
}

/** Re-reads the wiring/counts panel on an interval until stopped. */
export function watchSystem(emit: Emit<SystemInfo>, intervalMs: number): () => void {
  let stopped = false;

  const tick = async () => {
    try {
      const info = await getSystemInfo();
      if (!stopped) {
        emit((previous) => ({ ...previous, data: info, error: null, loading: false }));
      }
    } catch (error) {
      if (!stopped) {
        emit((previous) => ({ ...previous, error: describe(error), loading: false }));
      }
    }
  };

  void tick();
  const timer = setInterval(() => void tick(), intervalMs);

  return () => {
    stopped = true;
    clearInterval(timer);
  };
}

export type JobWatcher = {
  /** Forces a re-read, e.g. right after a cancel. */
  refresh: () => void;
  stop: () => void;
};

/**
 * Follows one job. The API pushes `jobChanged` over SignalR as tasks move
 * through the DAG; if the hub cannot be reached we fall back to polling so the
 * page still shows progress. Both stop once the job reaches a terminal state.
 */
export type JobHandlers = {
  emit: Emit<JobStatus>;
  onSource: (source: LiveSource) => void;
  /** Called for every `jobChanged` push, so the page can show an event log. */
  onEvent?: (event: JobChangedEvent) => void;
};

export function watchJob(
  id: string,
  { emit, onSource, onEvent }: JobHandlers,
  pollMs = 1500,
): JobWatcher {
  let stopped = false;
  let finished = false;
  let poll: ReturnType<typeof setInterval> | undefined;
  let connection: import("@microsoft/signalr").HubConnection | undefined;

  const read = async () => {
    if (stopped) return;
    try {
      const job = await getJob(id);
      if (stopped) return;
      finished = isJobFinished(job.state);
      emit((previous) => ({ ...previous, data: job, error: null, loading: false }));
      if (finished) {
        onSource("stopped");
      }
    } catch (error) {
      if (stopped) return;
      emit((previous) => ({ ...previous, error: describe(error), loading: false }));
    }
  };

  const startPolling = () => {
    if (stopped || poll) return;
    if (!finished) {
      onSource("polling");
    }
    poll = setInterval(() => {
      if (!finished) void read();
    }, pollMs);
  };

  void read();

  // Imported lazily so the SignalR client stays out of the initial bundle.
  void import("@microsoft/signalr")
    .then(async ({ HubConnectionBuilder, LogLevel }) => {
      if (stopped) return;
      connection = new HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      connection.on("jobChanged", (event: JobChangedEvent) => {
        onEvent?.(event);
        void read();
      });
      connection.onreconnecting(() => onSource("connecting"));
      connection.onreconnected(() => onSource(finished ? "stopped" : "signalr"));
      connection.onclose(() => startPolling());

      await connection.start();
      await connection.invoke("Subscribe", id);
      if (stopped) return;
      onSource(finished ? "stopped" : "signalr");
    })
    .catch(startPolling);

  return {
    refresh: () => void read(),
    stop: () => {
      stopped = true;
      if (poll) clearInterval(poll);
      void connection?.stop();
    },
  };
}
