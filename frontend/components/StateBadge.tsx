const STATE_STYLES: Record<string, string> = {
  Pending:
    "bg-zinc-100 text-zinc-600 ring-zinc-300 dark:bg-zinc-800 dark:text-zinc-300 dark:ring-zinc-700",
  Running:
    "bg-blue-50 text-blue-700 ring-blue-300 dark:bg-blue-950 dark:text-blue-300 dark:ring-blue-800",
  Succeeded:
    "bg-emerald-50 text-emerald-700 ring-emerald-300 dark:bg-emerald-950 dark:text-emerald-300 dark:ring-emerald-800",
  Failed:
    "bg-red-50 text-red-700 ring-red-300 dark:bg-red-950 dark:text-red-300 dark:ring-red-800",
  PartiallyFailed:
    "bg-amber-50 text-amber-700 ring-amber-300 dark:bg-amber-950 dark:text-amber-300 dark:ring-amber-800",
  DeadLettered:
    "bg-fuchsia-50 text-fuchsia-700 ring-fuchsia-300 dark:bg-fuchsia-950 dark:text-fuchsia-300 dark:ring-fuchsia-800",
  Cancelled:
    "bg-zinc-100 text-zinc-500 ring-zinc-300 dark:bg-zinc-800 dark:text-zinc-400 dark:ring-zinc-700",
};

const FALLBACK = STATE_STYLES.Pending;

export function StateBadge({ state }: { state: string }) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset ${
        STATE_STYLES[state] ?? FALLBACK
      }`}
    >
      {state === "Running" && (
        <span className="size-1.5 animate-pulse rounded-full bg-current" />
      )}
      {state}
    </span>
  );
}
