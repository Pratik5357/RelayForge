import { Badge } from "./ui/Badge";
import type { JobState, TaskState } from "@/lib/types";

const ROLE_BY_STATE: Record<JobState | TaskState, string> = {
  Pending: "pending",
  Running: "running",
  Succeeded: "succeeded",
  Failed: "failed",
  PartiallyFailed: "partiallyfailed",
  Cancelled: "cancelled",
  DeadLettered: "deadlettered",
};

const LABEL: Partial<Record<JobState | TaskState, string>> = {
  PartiallyFailed: "Partly failed",
  DeadLettered: "Parked",
};

/** One glyph shape per state, so state never depends on colour alone. */
export function StateGlyph({ state, size = 12 }: { state: JobState | TaskState; size?: number }) {
  const common = {
    width: size,
    height: size,
    viewBox: "0 0 12 12",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 1.6,
    "aria-hidden": true,
  } as const;

  switch (state) {
    case "Running":
      return (
        <svg {...common} className="heat-pulse">
          <circle cx="6" cy="6" r="4.6" />
          <circle cx="6" cy="6" r="1.8" fill="currentColor" stroke="none" />
        </svg>
      );
    case "Succeeded":
      return (
        <svg {...common}>
          <circle cx="6" cy="6" r="5" fill="currentColor" stroke="none" />
          <path d="M3.6 6.2l1.7 1.7 3.2-3.5" stroke="var(--background)" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      );
    case "Failed":
      return (
        <svg {...common}>
          <path d="M6 .9l5.1 5.1L6 11.1.9 6z" fill="currentColor" stroke="none" />
          <path d="M4.3 4.3l3.4 3.4M7.7 4.3L4.3 7.7" stroke="var(--background)" strokeLinecap="round" />
        </svg>
      );
    case "PartiallyFailed":
      return (
        <svg {...common}>
          <circle cx="6" cy="6" r="5" fill="currentColor" stroke="none" />
          <path d="M6 1v10" stroke="var(--background)" strokeLinecap="round" />
          <path d="M6 1a5 5 0 0 1 0 10z" fill="var(--background)" stroke="none" />
        </svg>
      );
    case "DeadLettered":
      return (
        <svg {...common}>
          <rect x="1" y="1" width="10" height="10" rx="2" fill="currentColor" stroke="none" />
          <path d="M3.6 6h4.8" stroke="var(--background)" strokeLinecap="round" />
        </svg>
      );
    case "Cancelled":
      return (
        <svg {...common}>
          <circle cx="6" cy="6" r="4.8" />
          <path d="M2.6 9.4l6.8-6.8" strokeLinecap="round" />
        </svg>
      );
    default:
      return (
        <svg {...common}>
          <circle cx="6" cy="6" r="4.6" strokeDasharray="2.2 2" />
        </svg>
      );
  }
}

export function StateBadge({
  state,
  size = "md",
}: {
  state: JobState | TaskState;
  size?: "md" | "lg";
}) {
  const role = ROLE_BY_STATE[state] ?? "pending";

  return (
    <Badge
      bg={`var(--state-${role}-bg)`}
      fg={`var(--state-${role}-fg)`}
      ring={`var(--state-${role}-ring)`}
      size={size}
    >
      <StateGlyph state={state} size={size === "lg" ? 14 : 12} />
      {LABEL[state] ?? state}
    </Badge>
  );
}
