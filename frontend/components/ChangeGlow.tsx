"use client";

import { useState } from "react";
import { motion } from "motion/react";
import { EASE_OUT } from "@/lib/motion";

/**
 * A departures-board row "stays lit" for a moment after its state changes, so the eye finds
 * the change instead of hunting for it. Renders nothing until `token` changes after mount.
 * Opacity only, so it stays on under reduced motion.
 */
export function ChangeGlow({ token }: { token: string }) {
  const [seen, setSeen] = useState(token);
  const [changes, setChanges] = useState(0);

  // Track the previous token during render (React's "store info from previous renders").
  if (seen !== token) {
    setSeen(token);
    setChanges((n) => n + 1);
  }

  if (changes === 0) return null;

  return (
    <motion.span
      key={changes}
      aria-hidden
      className="pointer-events-none absolute inset-0 -z-10 bg-[rgb(255_194_46/0.16)]"
      initial={{ opacity: 1 }}
      animate={{ opacity: 0 }}
      transition={{ duration: 1.8, ease: EASE_OUT, delay: 0.25 }}
    />
  );
}
