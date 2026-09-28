"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { submitJob } from "@/lib/api";
import type { Scenario } from "@/lib/scenarios";

/**
 * One step of the guided tour. Running it submits the scenario's job and hands the
 * visitor to the live view, carrying the scenario id so that page can repeat what
 * to watch for.
 */
export function ScenarioCard({ scenario }: { scenario: Scenario }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run() {
    setBusy(true);
    setError(null);
    try {
      const job = await submitJob(scenario.job);
      router.push(`/jobs/${job.id}?scenario=${scenario.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
      <div className="space-y-1">
        <h3 className="font-medium">{scenario.title}</h3>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">{scenario.summary}</p>
      </div>

      <p className="text-xs text-zinc-500">
        <span className="font-medium text-zinc-600 dark:text-zinc-400">Shows:</span>{" "}
        {scenario.concept} · takes {scenario.runtime}
      </p>

      <div className="mt-auto flex items-center gap-3">
        <button
          type="button"
          onClick={run}
          disabled={busy}
          className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
        >
          {busy ? "Starting…" : "Run it"}
        </button>
        {error && <span className="text-xs text-red-600 dark:text-red-400">{error}</span>}
      </div>
    </div>
  );
}
