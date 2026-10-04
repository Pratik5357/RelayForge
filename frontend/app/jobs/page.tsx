"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ApiError, listJobs } from "@/lib/api";
import type { JobSummary } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { Callout } from "@/components/ui/Callout";
import { StateBadge } from "@/components/StateBadge";
import { Table, Tbody, Td, Th, Thead } from "@/components/ui/Table";

const REFRESH_INTERVAL_MS = 3000;

export default function JobsPage() {
  const [jobs, setJobs] = useState<JobSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function refresh() {
      try {
        const result = await listJobs();
        if (!cancelled) {
          setJobs(result);
          setError(null);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : "Could not reach the API.");
        }
      }
    }

    refresh();
    const interval = setInterval(refresh, REFRESH_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-4xl leading-none">Runs</h1>
          <p className="text-sm text-[var(--muted)]">Every job submitted so far, newest first.</p>
        </div>
        <Link href="/jobs/new">
          <Button>New run</Button>
        </Link>
      </div>

      {error && (
        <Callout tone="error" role="alert">
          {error}
        </Callout>
      )}

      {jobs === null && !error && (
        <p className="text-sm text-[var(--muted)]" role="status">Loading…</p>
      )}

      {jobs !== null && jobs.length === 0 && (
        <p className="rounded-[4px] border border-dashed border-[var(--border-strong)] px-4 py-10 text-center text-sm text-[var(--muted)]">
          Nothing has run yet.{" "}
          <Link href="/" className="font-semibold text-[var(--foreground)] underline">
            Start a tour demo
          </Link>{" "}
          or{" "}
          <Link href="/jobs/new" className="font-semibold text-[var(--foreground)] underline">
            build a job
          </Link>
          .
        </p>
      )}

      {jobs !== null && jobs.length > 0 && (
        <Table>
          <Thead>
            <tr>
              <Th>Run</Th>
              <Th>State</Th>
              <Th className="hidden sm:table-cell">Steps</Th>
              <Th className="hidden sm:table-cell">Started</Th>
            </tr>
          </Thead>
          <Tbody>
            {jobs.map((job) => (
              <tr key={job.id}>
                <Td>
                  <Link href={`/jobs/${job.id}`} className="block py-1 font-semibold hover:underline">
                    {job.name ?? job.id.slice(0, 8)}
                  </Link>
                </Td>
                <Td>
                  <StateBadge state={job.state} />
                </Td>
                <Td className="hidden font-mono sm:table-cell">{job.taskCount}</Td>
                <Td className="hidden font-mono text-xs text-[var(--muted)] sm:table-cell">
                  {new Date(job.createdAt).toLocaleTimeString()}
                </Td>
              </tr>
            ))}
          </Tbody>
        </Table>
      )}
    </div>
  );
}
