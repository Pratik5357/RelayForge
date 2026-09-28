"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Callout } from "@/components/Callout";
import { submitJob } from "@/lib/api";
import { SCENARIOS } from "@/lib/scenarios";
import type { SubmitJobRequest } from "@/lib/types";

const STARTING_POINT: SubmitJobRequest = {
  name: "my-first-job",
  tasks: [
    { key: "step-one", type: "delay", payload: { milliseconds: 500 } },
    { key: "step-two", type: "echo", payload: { anything: "you like" }, dependsOn: ["step-one"] },
  ],
};

export default function NewJobPage() {
  const router = useRouter();
  const [body, setBody] = useState(() => JSON.stringify(STARTING_POINT, null, 2));
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    let parsed: SubmitJobRequest;
    try {
      parsed = JSON.parse(body) as SubmitJobRequest;
    } catch (err) {
      setError(`That is not valid JSON: ${err instanceof Error ? err.message : String(err)}`);
      setSubmitting(false);
      return;
    }

    try {
      const job = await submitJob(parsed);
      router.push(`/jobs/${job.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      setSubmitting(false);
    }
  }

  return (
    <section className="space-y-5">
      <div className="space-y-1">
        <h1 className="text-xl font-semibold">Write a job by hand</h1>
        <p className="text-sm text-zinc-500">
          This is the raw request the API takes. If you just want to see the system work,{" "}
          <Link href="/" className="underline">
            the tour
          </Link>{" "}
          does it for you.
        </p>
      </div>

      <Callout tone="teach" title="The three things that matter">
        <ul className="list-disc space-y-1 pl-5 text-zinc-700 dark:text-zinc-300">
          <li>
            <code className="font-mono">key</code> — a name for the step, unique within the job.
            Other steps refer to it by this.
          </li>
          <li>
            <code className="font-mono">dependsOn</code> — the keys this step waits for. Leave it
            out and the step starts immediately. Circular waits are rejected.
          </li>
          <li>
            <code className="font-mono">type</code> — which built-in worker runs it. There are
            three, meant for demonstrating behaviour rather than doing real work.
          </li>
        </ul>
      </Callout>

      <div className="overflow-x-auto rounded-lg border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-zinc-200 text-xs uppercase tracking-wide text-zinc-500 dark:border-zinc-800">
            <tr>
              <th className="px-4 py-2 font-medium">type</th>
              <th className="px-4 py-2 font-medium">what it does</th>
              <th className="px-4 py-2 font-medium">payload</th>
            </tr>
          </thead>
          <tbody className="[&_td]:px-4 [&_td]:py-2 [&_tr]:border-b [&_tr]:border-zinc-100 dark:[&_tr]:border-zinc-800 [&_tr:last-child]:border-0">
            <tr>
              <td className="font-mono">echo</td>
              <td>Succeeds at once, returning the payload as its result.</td>
              <td className="font-mono text-xs text-zinc-500">anything</td>
            </tr>
            <tr>
              <td className="font-mono">delay</td>
              <td>Waits, then succeeds. Use it to make a run long enough to watch.</td>
              <td className="font-mono text-xs text-zinc-500">{`{ "milliseconds": 500 }`}</td>
            </tr>
            <tr>
              <td className="font-mono">fail</td>
              <td>
                Throws on purpose. With <code className="font-mono">succeedOnAttempt</code> it
                starts working on that attempt; without it, it never does.
              </td>
              <td className="font-mono text-xs text-zinc-500">{`{ "succeedOnAttempt": 3 }`}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <p className="text-sm text-zinc-500">
        Optional per step: <code className="font-mono">maxAttempts</code> (defaults to 3) caps how
        many tries a failing step gets before it is dead-lettered.
      </p>

      <form onSubmit={handleSubmit} className="space-y-3">
        <label htmlFor="job-json" className="block text-sm font-medium">
          Request body
        </label>
        <textarea
          id="job-json"
          value={body}
          onChange={(event) => setBody(event.target.value)}
          spellCheck={false}
          rows={18}
          className="w-full rounded-lg border border-zinc-200 bg-white p-3 font-mono text-xs dark:border-zinc-800 dark:bg-zinc-900"
        />

        {error && <Callout tone="error">{error}</Callout>}

        <div className="flex flex-wrap items-center gap-3">
          <button
            type="submit"
            disabled={submitting}
            className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-700 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
          >
            {submitting ? "Submitting…" : "Run this job"}
          </button>
          <span className="text-xs text-zinc-500">Or load a tour scenario to edit:</span>
          {SCENARIOS.map((scenario) => (
            <button
              key={scenario.id}
              type="button"
              onClick={() => setBody(JSON.stringify(scenario.job, null, 2))}
              className="rounded-md border border-zinc-300 px-2 py-0.5 text-xs hover:bg-zinc-100 dark:border-zinc-700 dark:hover:bg-zinc-800"
            >
              {scenario.id}
            </button>
          ))}
        </div>
      </form>
    </section>
  );
}
