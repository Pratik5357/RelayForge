"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  initialSnapshot,
  watchJob,
  watchSystem,
  type JobWatcher,
  type LiveSource,
  type Snapshot,
} from "./live";
import type { JobChangedEvent, JobStatus, SystemInfo } from "./types";

/** Keeps the last few pushes so the job page can show what the engine reported. */
const EVENT_LOG_LIMIT = 40;

export type LoggedEvent = JobChangedEvent & { at: string };

/** Subscribes to one job for as long as the component is mounted. */
export function useLiveJob(id: string) {
  const [snapshot, setSnapshot] = useState<Snapshot<JobStatus>>(initialSnapshot);
  const [source, setSource] = useState<LiveSource>("connecting");
  const [events, setEvents] = useState<LoggedEvent[]>([]);
  const watcher = useRef<JobWatcher | null>(null);

  const onEvent = useCallback((event: JobChangedEvent) => {
    setEvents((previous) =>
      [...previous, { ...event, at: new Date().toISOString() }].slice(-EVENT_LOG_LIMIT),
    );
  }, []);

  useEffect(() => {
    const active = watchJob(id, { emit: setSnapshot, onSource: setSource, onEvent });
    watcher.current = active;
    return () => {
      watcher.current = null;
      active.stop();
    };
  }, [id, onEvent]);

  const refresh = useCallback(() => watcher.current?.refresh(), []);

  return {
    job: snapshot.data,
    error: snapshot.error,
    loading: snapshot.loading,
    source,
    events,
    refresh,
  };
}

/** Polls `GET /api/system` for the "how this instance is wired" panel. */
export function useSystemInfo(intervalMs = 5000) {
  const [snapshot, setSnapshot] = useState<Snapshot<SystemInfo>>(initialSnapshot);

  useEffect(() => watchSystem(setSnapshot, intervalMs), [intervalMs]);

  return snapshot;
}
