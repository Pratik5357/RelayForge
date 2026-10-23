"use client";

import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import type { JobChangedEvent, LiveSource } from "@/lib/live";
import { EASE_OUT } from "@/lib/motion";

export function EventLog({
  events,
  source,
  finished = false,
}: {
  events: JobChangedEvent[];
  source: LiveSource;
  finished?: boolean;
}) {
  const reduce = useReducedMotion();

  if (events.length === 0) {
    return (
      <p className="rounded-2xl border-2 border-dashed border-hairline px-6 py-6 text-muted">
        {finished
          ? "No events were recorded for this run."
          : source === "polling"
            ? "No pushed updates yet. This run is being checked every few seconds instead."
            : "Waiting for the first update…"}
      </p>
    );
  }

  const newestFirst = [...events].reverse();

  return (
    <ol className="max-h-[340px] overflow-y-auto rounded-2xl bg-paper shadow-[inset_0_0_0_1px_var(--hairline-soft)]">
      <AnimatePresence initial={false}>
        {newestFirst.map((evt) => (
          <motion.li
            key={evt.sequence}
            layout="position"
            initial={reduce ? { opacity: 0 } : { opacity: 0, transform: "translateY(-6px)", filter: "blur(2px)" }}
            animate={reduce ? { opacity: 1 } : { opacity: 1, transform: "translateY(0px)", filter: "blur(0px)" }}
            transition={{ duration: 0.3, ease: EASE_OUT }}
            className="grid grid-cols-[84px_minmax(0,1fr)_auto] items-baseline gap-x-4 gap-y-1 border-b border-[#eceeea] px-6 py-3 last:border-b-0"
          >
            <span className="font-mono text-[13px] text-label">
              {new Date(evt.at).toLocaleTimeString([], { hour12: false })}
            </span>
            <span className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="font-bold">{evt.taskName ?? "job"}</span>
              <span className="font-mono text-[13px]">{evt.taskState ?? evt.jobState}</span>
              {evt.message && <span className="text-muted">{evt.message}</span>}
            </span>
            <span className="tnum font-mono text-xs text-label" title="Event number: gap-free per run, so a missed one is noticed and fetched">
              #{evt.sequence}
            </span>
          </motion.li>
        ))}
      </AnimatePresence>
    </ol>
  );
}
