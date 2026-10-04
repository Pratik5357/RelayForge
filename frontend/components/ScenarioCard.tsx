"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, submitJob } from "@/lib/api";
import type { Scenario } from "@/lib/scenarios";
import { Button } from "@/components/ui/Button";

/** One tour scenario as a list row: what it shows, how long it takes, and a Run button. */
export function ScenarioCard({ scenario, primary = false }: { scenario: Scenario; primary?: boolean }) {
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
      setError(err instanceof ApiError ? err.message : "Could not start this scenario.");
      setBusy(false);
    }
  }

  return (
    <li className="grid gap-x-4 gap-y-2 px-4 py-4 sm:grid-cols-[1fr_auto] sm:items-center">
      <div className="min-w-0">
        <h3 className="text-sm font-semibold">{scenario.title}</h3>
        <p className="mt-1 max-w-prose text-sm text-[var(--muted)]">{scenario.summary}</p>
        <p className="mt-1.5 text-xs text-[var(--muted)]">
          Shows <span className="text-[var(--foreground)]">{scenario.concept}</span>
          <span className="tnum font-mono"> · {scenario.runtime}</span>
        </p>
        {error && (
          <p role="alert" className="mt-1.5 text-xs text-[var(--state-failed-fg)]">
            {error}
          </p>
        )}
      </div>
      <Button size="sm" variant={primary ? "primary" : "secondary"} onClick={run} disabled={busy} className="justify-self-start sm:justify-self-end">
        {busy ? "Starting…" : "Run it"}
      </Button>
    </li>
  );
}
