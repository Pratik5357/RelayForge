"use client";

import { useEffect, useState } from "react";
import { watchJob, type JobChangedEvent, type LiveSource } from "./live";
import type { JobDetail } from "./types";

export function useLiveJob(jobId: string) {
  const [job, setJob] = useState<JobDetail | null>(null);
  const [events, setEvents] = useState<JobChangedEvent[]>([]);
  const [source, setSource] = useState<LiveSource>("connecting");
  const [error, setError] = useState<string | null>(null);

  // Relies on the caller remounting this hook's component (e.g. `key={jobId}`) when jobId
  // changes, so state naturally resets to its initial values instead of being reset here.
  useEffect(() => {
    const stop = watchJob(jobId, {
      onUpdate: setJob,
      onEvent: (evt) => setEvents((prev) => [...prev, evt]),
      onSourceChange: setSource,
      onError: setError,
    });

    return stop;
  }, [jobId]);

  return { job, events, source, error };
}
