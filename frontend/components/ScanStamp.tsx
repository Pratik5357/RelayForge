"use client";

import { motion, useReducedMotion } from "motion/react";
import type { JobState } from "@/lib/types";
import { stateLabel } from "./Remark";

/**
 * The end of a tour demo: the run's pass gets stamped, like a boarding pass at the gate,
 * saying whether the outcome matched what the demo set out to show. It thumps down only when
 * the run finished while you were watching; a run opened after it ended shows the stamp at rest.
 */
export function ScanStamp({ expected, actual, live }: { expected: JobState; actual: JobState; live: boolean }) {
  const reduce = useReducedMotion();
  const matched = expected === actual;
  const color = matched ? "var(--remark-succeeded)" : "var(--remark-failed)";

  const rest = { opacity: 1, transform: "rotate(-5deg) scale(1)" };
  const from = reduce ? { opacity: 0, transform: "rotate(-5deg) scale(1)" } : { opacity: 0, transform: "rotate(-12deg) scale(1.35)" };

  return (
    <motion.div
      role="status"
      initial={live ? from : false}
      animate={rest}
      // Rare and earned: a short, slightly springy landing. Delay lets the final remark flip first.
      transition={reduce ? { duration: 0.2 } : { type: "spring", duration: 0.5, bounce: 0.3, delay: 0.35 }}
      className="inline-flex flex-col gap-0.5 self-start rounded-md px-3 py-2 font-bold uppercase tracking-[0.08em]"
      style={{ color, boxShadow: `inset 0 0 0 2px ${color}`, transformOrigin: "30% 50%" }}
    >
      <span className="text-[13px]">{matched ? "As expected" : "Not as expected"}</span>
      <span className="text-[11px] font-semibold tracking-[0.06em] text-board-soft">
        Expected {stateLabel(expected)}
        {matched ? "" : ` · got ${stateLabel(actual)}`}
      </span>
    </motion.div>
  );
}
