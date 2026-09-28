"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { initialSnapshot, watchJobs, type Snapshot } from "@/lib/live";
import type { JobStatus } from "@/lib/types";
import { JobsTable } from "./JobsTable";

const REFRESH_MS = 3000;

/** Live list of runs. `limit` trims it for the summary on the overview page. */
export function JobList({ limit }: { limit?: number }) {
  const [snapshot, setSnapshot] = useState<Snapshot<JobStatus[]>>(initialSnapshot);

  useEffect(() => watchJobs(setSnapshot, REFRESH_MS), []);

  if (snapshot.loading) {
    return <p className="text-sm text-zinc-500">Loading runs…</p>;
  }

  if (snapshot.error && !snapshot.data) {
    return (
      <p className="rounded-lg border border-red-300 bg-red-50 p-4 text-sm text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300">
        {snapshot.error}
      </p>
    );
  }

  const jobs = snapshot.data ?? [];

  if (jobs.length === 0) {
    return (
      <p className="rounded-lg border border-dashed border-zinc-300 p-4 text-sm text-zinc-500 dark:border-zinc-700">
        Nothing has run yet.{" "}
        <Link href="/" className="underline">
          Start with the tour
        </Link>{" "}
        — the first demo takes about a second.
      </p>
    );
  }

  return <JobsTable jobs={limit ? jobs.slice(0, limit) : jobs} />;
}
