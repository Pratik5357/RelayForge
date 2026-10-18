"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { ApiError, listJobs } from "@/lib/api";
import { isJobFinished, type JobState, type JobSummary } from "@/lib/types";
import { EASE_OUT, RERANK } from "@/lib/motion";
import { PageTransition } from "@/components/PageTransition";
import { Remark } from "@/components/Remark";
import { useToast } from "@/components/Toaster";
import { buttonClasses } from "@/components/ui/Button";

const REFRESH_INTERVAL_MS = 3000;
const COLUMNS = "grid-cols-[104px_minmax(0,1fr)_72px_170px]";

export default function JobsPage() {
  const [jobs, setJobs] = useState<JobSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const toast = useToast();
  const reduce = useReducedMotion();
  // Last seen state per run, so a run that finishes between refreshes can be announced.
  const seen = useRef<Map<string, JobState> | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function refresh() {
      try {
        const result = await listJobs();
        if (cancelled) return;
        const previous = seen.current;
        if (previous) {
          for (const job of result) {
            const before = previous.get(job.id);
            if (before && !isJobFinished(before) && isJobFinished(job.state)) {
              toast({
                title: job.name ?? job.id.slice(0, 8),
                state: job.state,
                href: `/jobs/${job.id}`,
                linkLabel: "View run",
              });
            }
          }
        }
        seen.current = new Map(result.map((j) => [j.id, j.state]));
        setJobs(result);
        setError(null);
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
  }, [toast]);

  return (
    <PageTransition className="page-enter flex flex-col gap-7">
      <div className="flex flex-wrap items-end justify-between gap-x-8 gap-y-4">
        <div className="flex flex-col gap-2">
          <h1 className="text-[clamp(52px,6vw,72px)] leading-[0.95] tracking-[-0.045em]">Runs</h1>
          <p className="text-[17px] text-muted">Every job submitted so far, newest first.</p>
        </div>
        <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
          <span role="status" className="inline-flex items-center gap-2.5 text-sm text-muted">
            <span className="relative size-2">
              <span className="ping absolute inset-0 rounded-full bg-ink" />
              <span className="absolute inset-0 rounded-full bg-ink" />
            </span>
            Checks for new runs every 3 seconds
          </span>
          <Link href="/jobs/new" transitionTypes={["nav-forward"]} className={buttonClasses("primary", "md")}>
            <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden>
              <path d="M7 2v10M2 7h10" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
            </svg>
            New run
          </Link>
        </div>
      </div>

      {error && (
        <p role="alert" className="rounded-xl bg-board px-5 py-4 text-board-text">
          <span className="font-bold text-[var(--remark-failed)]">Can&rsquo;t load runs.</span> {error}
        </p>
      )}

      {jobs === null && !error && (
        <div role="status" aria-label="Loading runs" className="board-reveal flex flex-col gap-2 rounded-xl bg-board p-6">
          {[0, 1, 2].map((i) => (
            <span key={i} className="h-3 rounded-sm bg-board-control" style={{ width: `${80 - i * 15}%` }} />
          ))}
        </div>
      )}

      {jobs !== null && jobs.length === 0 && (
        <div className="on-board board-reveal flex flex-col items-center gap-4 rounded-xl bg-board px-6 py-11 text-center text-board-text">
          <div aria-hidden className="flex w-[min(100%,360px)] flex-col gap-1.5">
            <span className="h-2.5 rounded-sm bg-board-control" />
            <span className="h-2.5 w-[70%] rounded-sm bg-board-control" />
            <span className="h-2.5 w-[84%] rounded-sm bg-board-control" />
          </div>
          <p className="text-[17px]">
            Nothing has run yet.{" "}
            <Link href="/" transitionTypes={["nav-back"]} className="font-bold text-amber underline">
              Start a tour demo
            </Link>{" "}
            or{" "}
            <Link href="/jobs/new" transitionTypes={["nav-forward"]} className="font-bold text-amber underline">
              build a job
            </Link>
            .
          </p>
        </div>
      )}

      {jobs !== null && jobs.length > 0 && (
        <div className="board-reveal overflow-x-auto rounded-xl">
          <div className="on-board min-w-[640px] rounded-xl bg-board text-board-text shadow-[0_30px_50px_-34px_rgb(18_20_18/0.7)]">
            <div className={`label grid ${COLUMNS} gap-x-5 border-b border-board-rule px-[26px] py-3.5 text-board-muted`} aria-hidden>
              <span>Started</span>
              <span>Run</span>
              <span className="text-right">Steps</span>
              <span>Remark</span>
            </div>
            <ol aria-label="Runs">
              <AnimatePresence>
                {jobs.map((job, i) => (
                  <motion.li
                    key={job.id}
                    layout="position"
                    initial={reduce ? { opacity: 0 } : { opacity: 0, transform: "translateY(-12px)" }}
                    animate={reduce ? { opacity: 1 } : { opacity: 1, transform: "translateY(0px)" }}
                    exit={{ opacity: 0, transition: { duration: 0.15 } }}
                    // First load: rows arrive top-down, 40ms apart (capped). Later arrivals land at
                    // index 0, so they enter straight away.
                    transition={
                      reduce
                        ? { duration: 0.15 }
                        : {
                            ...RERANK,
                            opacity: { duration: 0.3, ease: EASE_OUT, delay: Math.min(i * 0.04, 0.4) },
                            transform: { duration: 0.4, ease: EASE_OUT, delay: Math.min(i * 0.04, 0.4) },
                          }
                    }
                    className="border-b border-board-rule last:border-b-0"
                  >
                    <Link
                      href={`/jobs/${job.id}`}
                      transitionTypes={["nav-forward"]}
                      className={`group grid ${COLUMNS} min-h-[76px] items-center gap-x-5 px-[26px] text-board-text no-underline transition-colors duration-150 hover:bg-board-raised`}
                    >
                      <span className="tnum font-mono text-xl">{new Date(job.createdAt).toLocaleTimeString([], { hour12: false })}</span>
                      <span className="flex min-w-0 flex-col">
                        <span className="truncate text-lg font-bold tracking-[-0.01em] group-hover:underline">
                          {job.name ?? job.id.slice(0, 8)}
                        </span>
                        <span className="font-mono text-xs text-board-muted">{job.id.slice(0, 8)}</span>
                      </span>
                      <span className="tnum text-right font-mono text-xl">{job.taskCount}</span>
                      <Remark state={job.state} />
                    </Link>
                  </motion.li>
                ))}
              </AnimatePresence>
            </ol>
          </div>
        </div>
      )}
    </PageTransition>
  );
}
