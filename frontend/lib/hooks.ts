"use client";

import { useEffect, useState } from "react";
import { watchJob, type JobChangedEvent, type LiveSource } from "./live";
import { advanceHistory, EMPTY_SNAPSHOT } from "./attempts";
import type { JobDetail } from "./types";

export function useLiveJob(jobId: string) {
  const [job, setJob] = useState<JobDetail | null>(null);
  const [events, setEvents] = useState<(JobChangedEvent & { at: number })[]>([]);
  const [source, setSource] = useState<LiveSource>("connecting");
  const [error, setError] = useState<string | null>(null);
  const [observed, setObserved] = useState(EMPTY_SNAPSHOT);

  // Relies on the caller remounting this hook's component (e.g. `key={jobId}`) when jobId
  // changes, so state naturally resets to its initial values instead of being reset here.
  useEffect(() => {
    const stop = watchJob(jobId, {
      onUpdate: (next) => {
        setJob(next);
        setObserved((prev) => advanceHistory(prev, next, Date.now()));
      },
      onEvent: (evt) => setEvents((prev) => [...prev, { ...evt, at: Date.now() }]),
      onSourceChange: setSource,
      onError: setError,
    });

    return stop;
  }, [jobId]);

  return { job, events, source, error, attempts: observed.history };
}
