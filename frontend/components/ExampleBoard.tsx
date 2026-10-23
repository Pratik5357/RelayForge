"use client";

import { useEffect, useRef, useState } from "react";
import { motion, useInView, useReducedMotion } from "motion/react";
import type { TaskState } from "@/lib/types";
import { RERANK } from "@/lib/motion";
import { Remark } from "./Remark";
import { ChangeGlow } from "./ChangeGlow";
import { buttonClasses } from "./ui/Button";

type Frame = { state: TaskState; tries: string; note?: string };

const STEPS = [
  { id: "setup", stage: 1, waits: "—" },
  { id: "shard-a", stage: 2, waits: "setup" },
  { id: "shard-b", stage: 2, waits: "setup" },
  { id: "merge-results", stage: 3, waits: "shard-a, shard-b" },
] as const;

const LAST_FRAME = 10;
const HOLD_FRAMES = 6;

/**
 * Illustrative only: one of every look a step can have, played as a short loop. Each tick
 * changes one thing, except tick 4, where both shards start together (that is the point).
 */
function frameAt(t: number): Frame[] {
  const setup: Frame = t === 0 ? { state: "Ready", tries: "0/3" } : t < 3 ? { state: "Running", tries: "1/3" } : { state: "Succeeded", tries: "1/3" };
  const a: Frame = t < 4 ? { state: "Pending", tries: "0/3" } : { state: "Running", tries: "1/3" };
  const b: Record<number, Frame> = {
    4: { state: "Running", tries: "1/3" },
    5: { state: "Ready", tries: "1/3", note: "Retrying in 2s" },
    6: { state: "Ready", tries: "1/3", note: "Retrying in 1s" },
    7: { state: "Running", tries: "2/3" },
    8: { state: "Ready", tries: "2/3", note: "Retrying in 4s" },
    9: { state: "Running", tries: "3/3" },
    10: { state: "DeadLettered", tries: "3/3", note: "gave up after 3 tries" },
  };
  return [setup, a, t < 4 ? { state: "Pending", tries: "0/3" } : b[t], { state: "Pending", tries: "0/3" }];
}

function rank(frame: Frame) {
  if (frame.state === "Running") return 0;
  if (frame.state === "Ready" && frame.note) return 1;
  if (frame.state === "Pending" || frame.state === "Ready") return 2;
  return 3;
}

const NOTE: Partial<Record<TaskState, string>> = {
  Pending: "Waiting on an earlier step",
  Ready: "Up next",
  Running: "Working now",
  Succeeded: "Finished",
};

const COLUMNS = "grid-cols-[56px_minmax(0,1.3fr)_minmax(0,1.3fr)_64px_150px]";

export function ExampleBoard() {
  const [tick, setTick] = useState(0);
  const reduce = useReducedMotion();
  // Auto-advancing content needs a pause (WCAG 2.2.2). With reduced motion it starts paused.
  const [paused, setPaused] = useState<boolean | null>(null);
  const isPaused = paused ?? Boolean(reduce);
  const ref = useRef<HTMLDivElement>(null);
  // Only tick while the board is on screen; nobody needs it running behind the fold.
  const inView = useInView(ref, { margin: "-10% 0px" });

  useEffect(() => {
    if (isPaused || !inView) return;
    const interval = setInterval(() => setTick((t) => (t >= LAST_FRAME + HOLD_FRAMES ? 0 : t + 1)), 1000);
    return () => clearInterval(interval);
  }, [isPaused, inView]);

  const frame = frameAt(Math.min(tick, LAST_FRAME));
  const rows = STEPS.map((step, i) => ({ ...step, ...frame[i], order: i }))
    .sort((x, y) => rank(x) - rank(y) || x.order - y.order);

  return (
    <div ref={ref} className="flex flex-col gap-3">
      <div className="board-reveal overflow-x-auto rounded-xl">
        <div className="on-board min-w-[600px] rounded-xl bg-board text-board-text shadow-[0_30px_50px_-30px_rgb(18_20_18/0.7)]">
          <div className="flex items-center justify-between gap-4 border-b border-board-rule px-[22px] py-4">
            <p className="flex flex-wrap items-baseline gap-x-3">
              <span className="text-[22px] font-extrabold tracking-[-0.01em]">setup → merge-results</span>
              <span className="text-[13px] text-board-muted">Example only, not a live run</span>
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                aria-pressed={isPaused}
                onClick={() => setPaused(!isPaused)}
                className={buttonClasses("board", "sm")}
              >
                <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden>
                  {isPaused ? (
                    <path d="M4 2.5v9l7.5-4.5z" fill="currentColor" />
                  ) : (
                    <path d="M4.5 2.5v9M9.5 2.5v9" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                  )}
                </svg>
                {isPaused ? "Play" : "Pause"}
              </button>
              <button type="button" onClick={() => setTick(0)} className={buttonClasses("board", "sm")}>
                <svg width="14" height="14" viewBox="0 0 14 14" aria-hidden>
                  <path d="M2.5 7a4.5 4.5 0 1 0 1.4-3.3M2.5 1.8v2.4h2.4" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                Replay
              </button>
            </div>
          </div>
          <div className={`label grid ${COLUMNS} gap-x-3.5 border-b border-board-rule px-[22px] py-2.5 text-board-muted`}>
            <span>Stage</span>
            <span>Step</span>
            <span>Waits for</span>
            <span className="text-right">Tries</span>
            <span>Remark</span>
          </div>
          <ol>
            {rows.map((row) => (
              <motion.li
                key={row.id}
                layout="position"
                transition={reduce ? { duration: 0 } : RERANK}
                className={`relative isolate grid ${COLUMNS} min-h-[72px] items-center gap-x-3.5 border-b border-board-rule px-[22px] last:border-b-0`}
              >
                <ChangeGlow token={`${row.state}:${row.tries}`} />
                <span className="font-mono text-[22px] font-semibold">{row.stage}</span>
                <span className="flex min-w-0 flex-col">
                  <span className="text-lg font-bold tracking-[-0.01em]">{row.id}</span>
                  <span
                    className="text-[13px]"
                    style={{
                      color: row.note?.startsWith("Retrying")
                        ? "var(--amber)"
                        : row.state === "DeadLettered"
                          ? "var(--remark-deadlettered)"
                          : "var(--board-muted)",
                    }}
                  >
                    {row.note ?? NOTE[row.state] ?? ""}
                  </span>
                </span>
                <span className="text-[15px] text-board-soft">{row.waits}</span>
                <span className="tnum text-right font-mono text-[17px]">{row.tries}</span>
                <Remark state={row.state} />
              </motion.li>
            ))}
          </ol>
        </div>
      </div>
      <p className="text-sm text-muted">
        Steps that are working move to the top. A finished step settles below, so you can follow
        the change instead of hunting for it.
      </p>
    </div>
  );
}
