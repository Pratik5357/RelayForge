import type { LoggedEvent } from "@/lib/hooks";
import type { LiveSource } from "@/lib/live";

/** The engine publishes terse slugs; spell them out for a first-time reader. */
const MESSAGES: Record<string, string> = {
  succeeded: "finished successfully",
  "retry-scheduled": "failed — another attempt has been scheduled after a backoff wait",
  "dead-lettered": "gave up: every allowed attempt was used, so it was moved to the dead-letter table",
  cancelled: "cancelled before it started",
};

function explain(message: string): string {
  return MESSAGES[message] ?? message;
}

/**
 * The raw `jobChanged` pushes, newest first. This is the SignalR path made
 * visible: every line arrived over the websocket without the page asking.
 */
export function EventLog({
  events,
  source,
}: {
  events: LoggedEvent[];
  source: LiveSource;
}) {
  if (events.length === 0) {
    return (
      <p className="rounded-lg border border-dashed border-zinc-300 p-4 text-sm text-zinc-500 dark:border-zinc-700">
        {source === "polling"
          ? "No live connection, so this page is re-reading the job on a timer instead. The DAG above still updates."
          : "Waiting for the first push from the engine…"}
      </p>
    );
  }

  return (
    <ol className="divide-y divide-zinc-100 overflow-hidden rounded-lg border border-zinc-200 bg-white text-sm dark:divide-zinc-800 dark:border-zinc-800 dark:bg-zinc-900">
      {[...events].reverse().map((event, index) => (
        <li key={`${event.at}-${index}`} className="flex flex-wrap items-baseline gap-x-3 px-4 py-2">
          <time className="font-mono text-xs text-zinc-500">
            {new Date(event.at).toLocaleTimeString()}
          </time>
          {event.taskKey && (
            <span className="font-mono text-xs font-medium">{event.taskKey}</span>
          )}
          <span className="text-zinc-600 dark:text-zinc-400">{explain(event.message)}</span>
          <span className="ml-auto text-xs text-zinc-400">job now {event.jobState}</span>
        </li>
      ))}
    </ol>
  );
}
