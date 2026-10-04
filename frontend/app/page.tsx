import Link from "next/link";
import { SCENARIOS } from "@/lib/scenarios";
import { EXAMPLE_STEPS } from "@/lib/example";
import { ScenarioCard } from "@/components/ScenarioCard";
import { DagView } from "@/components/DagView";
import { buttonClasses } from "@/components/ui/Button";
import { StateBadge } from "@/components/StateBadge";
import type { JobState, TaskState } from "@/lib/types";

const LEGEND: { state: TaskState | JobState; means: string }[] = [
  { state: "Pending", means: "waiting its turn" },
  { state: "Running", means: "working now" },
  { state: "Succeeded", means: "finished" },
  { state: "DeadLettered", means: "gave up, set aside" },
  { state: "Failed", means: "stopped with an error" },
  { state: "PartiallyFailed", means: "run finished with some steps failed" },
  { state: "Cancelled", means: "called off" },
];

export default function OverviewPage() {
  return (
    <div className="space-y-12">
      <section className="space-y-3">
        <h1 className="text-5xl leading-none">Watch background work run.</h1>
        <p className="max-w-2xl text-[var(--muted)]">
          Submit a job made of steps that depend on each other. RelayForge runs them in order,
          side by side where it can, retries the ones that stumble, and shows every move as it
          happens.
        </p>
        <a
          href="#tour"
          className={buttonClasses("primary", "md")}
        >
          Start the tour
        </a>
      </section>

      <section aria-labelledby="example-heading" className="space-y-6">
        <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
          <h2 id="example-heading" className="text-2xl">
            How to read a run
          </h2>
          <p className="text-xs text-[var(--muted)]">Example only, not a live run</p>
        </div>
        <div className="rounded-[4px] border border-[var(--border)] bg-[var(--surface)] p-6">
          <DagView tasks={EXAMPLE_STEPS} now={0} />
        </div>
        <ul className="grid gap-x-8 gap-y-3 pt-2 text-sm text-[var(--muted)] sm:grid-cols-2 lg:grid-cols-3">
          {LEGEND.map((item) => (
            <li key={item.state} className="flex items-center gap-3">
              <span className="flex w-[7rem] shrink-0">
                <StateBadge state={item.state} />
              </span>
              {item.means}
            </li>
          ))}
        </ul>
      </section>

      <section id="tour" aria-labelledby="tour-heading" className="scroll-mt-8 space-y-4">
        <div>
          <h2 id="tour-heading" className="text-2xl">
            Take the tour
          </h2>
          <p className="text-sm text-[var(--muted)]">
            Six one-click demos. Each starts a real job and shows what happens as it runs.
          </p>
        </div>
        <ol className="divide-y divide-[var(--border)] rounded-[4px] border border-[var(--border)] bg-[var(--surface)]">
          {SCENARIOS.map((scenario) => (
            <ScenarioCard key={scenario.id} scenario={scenario} primary={scenario.id === SCENARIOS[0].id} />
          ))}
        </ol>
        <p className="text-sm text-[var(--muted)]">
          Prefer your own?{" "}
          <Link href="/jobs/new" className="font-semibold text-[var(--foreground)] underline">
            Build a job by hand
          </Link>
          , or look something up in the{" "}
          <Link href="/concepts" className="font-semibold text-[var(--foreground)] underline">
            glossary
          </Link>
          .
        </p>
      </section>
    </div>
  );
}
