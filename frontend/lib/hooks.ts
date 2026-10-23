"use client";

import { useEffect, useMemo, useState } from "react";
import { watchJob, type JobChangedEvent, type LiveSource } from "./live";
import { attemptsOf } from "./attempts";
import type { JobDetail } from "./types";

export function useLiveJob(jobId: string) {
  const [job, setJob] = useState<JobDetail | null>(null);
  const [events, setEvents] = useState<JobChangedEvent[]>([]);
  const [source, setSource] = useState<LiveSource>("connecting");
  const [error, setError] = useState<string | null>(null);
  // Bumped per task whenever it writes log lines, so an open log panel knows to fetch more.
  const [logTicks, setLogTicks] = useState<Record<string, number>>({});

  // Relies on the caller remounting this hook's component (e.g. `key={jobId}`) when jobId
  // changes, so state naturally resets to its initial values instead of being reset here.
  useEffect(() => {
    const stop = watchJob(jobId, {
      onUpdate: setJob,
      onEvents: (fresh) => setEvents((prev) => [...prev, ...fresh]),
      onLogs: (taskId) => setLogTicks((prev) => ({ ...prev, [taskId]: (prev[taskId] ?? 0) + 1 })),
      onSourceChange: setSource,
      onError: setError,
    });

    return stop;
  }, [jobId]);

  const attempts = useMemo(() => (job ? attemptsOf(job) : {}), [job]);

  return { job, setJob, events, source, error, attempts, logTicks };
}
