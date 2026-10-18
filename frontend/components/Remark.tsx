"use client";

import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import type { JobState, TaskState } from "@/lib/types";
import { EASE_OUT } from "@/lib/motion";

type State = JobState | TaskState;

const LABEL: Record<State, string> = {
  Pending: "Pending",
  Running: "Running",
  Succeeded: "Succeeded",
  Failed: "Failed",
  PartiallyFailed: "Partly failed",
  DeadLettered: "Parked",
  Cancelled: "Cancelled",
};

/** The words a state is shown as everywhere (remarks, events, stamps). */
export function stateLabel(state: State): string {
  return LABEL[state];
}

/** States whose lamp is lit (solid); the rest show a hollow ring. */
const LIT: Record<State, boolean> = {
  Pending: false,
  Running: true,
  Succeeded: true,
  Failed: true,
  PartiallyFailed: true,
  DeadLettered: true,
  Cancelled: false,
};

/**
 * A step's or run's state as a departures-board remark: always spelled out in words, with a
 * lamp that only blinks while working. A new state flips in like a board flap.
 */
export function Remark({ state, size = "sm" }: { state: State; size?: "sm" | "lg" }) {
  const reduce = useReducedMotion();
  const color = `var(--remark-${state.toLowerCase()})`;

  return (
    <span
      className={`relative inline-flex items-center overflow-hidden rounded-[4px] bg-board [perspective:320px] ${
        size === "lg" ? "h-[34px]" : "h-[26px]"
      }`}
    >
      <AnimatePresence mode="popLayout" initial={false}>
        <motion.span
          key={state}
          initial={reduce ? { opacity: 0 } : { opacity: 0.15, transform: "rotateX(-88deg)" }}
          animate={reduce ? { opacity: 1 } : { opacity: 1, transform: "rotateX(0deg)" }}
          exit={{ opacity: 0, transition: { duration: 0.12 } }}
          transition={{ duration: 0.24, ease: EASE_OUT }}
          style={{ color, transformOrigin: "50% 0" }}
          className={`inline-flex items-center gap-2 whitespace-nowrap pl-[9px] pr-[11px] font-bold uppercase tracking-[0.07em] ${
            size === "lg" ? "text-sm" : "text-xs"
          } ${state === "Cancelled" ? "line-through" : ""}`}
        >
          <span
            aria-hidden
            className={`size-[7px] rounded-full border-[1.5px] border-current ${state === "Running" ? "lamp-blink" : ""}`}
            style={{ background: LIT[state] ? "currentColor" : "transparent" }}
          />
          {LABEL[state]}
        </motion.span>
      </AnimatePresence>
    </span>
  );
}
