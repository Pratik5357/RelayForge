"use client";

import { motion, useReducedMotion, type Variants } from "motion/react";
import type { ReactNode } from "react";
import { EASE_OUT } from "@/lib/motion";

// Fires once, a little before the element is fully on screen, so content is settled by the
// time the reader gets to it. Scroll reveals never replay on the way back up.
const VIEWPORT = { once: true, margin: "0px 0px -12% 0px" } as const;

type Tag = "div" | "section" | "ul" | "ol" | "li" | "dl" | "p";

function variants(reduce: boolean | null, distance: number, delay = 0): Variants {
  return reduce
    ? { hidden: { opacity: 0 }, shown: { opacity: 1, transition: { duration: 0.2, delay } } }
    : {
        hidden: { opacity: 0, transform: `translateY(${distance}px)` },
        shown: { opacity: 1, transform: "translateY(0px)", transition: { duration: 0.5, ease: EASE_OUT, delay } },
      };
}

/** A block that rises into place the first time it scrolls into view. */
export function Reveal({
  as = "div",
  children,
  className,
  delay = 0,
  distance = 24,
  id,
  ...aria
}: {
  as?: Tag;
  children: ReactNode;
  className?: string;
  delay?: number;
  distance?: number;
  id?: string;
  "aria-labelledby"?: string;
  "aria-label"?: string;
}) {
  const reduce = useReducedMotion();
  const MotionTag = motion[as];
  return (
    <MotionTag
      id={id}
      className={className}
      variants={variants(reduce, distance, delay)}
      initial="hidden"
      whileInView="shown"
      viewport={VIEWPORT}
      {...aria}
    >
      {children}
    </MotionTag>
  );
}

/**
 * A list whose items arrive one after another when it scrolls into view. Children should be
 * RevealItem. The stagger is short (40ms) and capped by the list length, never blocking clicks.
 */
export function RevealList({
  as = "ul",
  children,
  className,
  stagger = 0.04,
  ...aria
}: {
  as?: Tag;
  children: ReactNode;
  className?: string;
  stagger?: number;
  "aria-label"?: string;
}) {
  const MotionTag = motion[as];
  return (
    <MotionTag
      className={className}
      initial="hidden"
      whileInView="shown"
      viewport={VIEWPORT}
      variants={{ hidden: {}, shown: { transition: { staggerChildren: stagger } } }}
      {...aria}
    >
      {children}
    </MotionTag>
  );
}

export function RevealItem({
  as = "li",
  children,
  className,
  distance = 16,
  id,
}: {
  as?: Tag;
  children: ReactNode;
  className?: string;
  distance?: number;
  id?: string;
}) {
  const reduce = useReducedMotion();
  const MotionTag = motion[as];
  return (
    <MotionTag id={id} className={className} variants={variants(reduce, distance)}>
      {children}
    </MotionTag>
  );
}
