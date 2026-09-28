import Link from "next/link";
import { JobList } from "@/components/JobList";
import { ScenarioCard } from "@/components/ScenarioCard";
import { WiringPanel } from "@/components/WiringPanel";
import { SCENARIOS } from "@/lib/scenarios";

export default function OverviewPage() {
  return (
    <div className="space-y-10">
      <section className="space-y-4">
        <h1 className="text-2xl font-semibold">RelayForge</h1>
        <p className="max-w-2xl text-zinc-600 dark:text-zinc-400">
          A job scheduler you can watch work. You hand it a <strong>job</strong>: a set of
          steps, where each step says which other steps must finish first. RelayForge works
          out the order, runs whatever can run at the same time, retries the steps that fail,
          and gives up on the ones that never will — while showing you each change as it
          happens.
        </p>
        <p className="max-w-2xl text-sm text-zinc-500">
          Nothing here needs setting up and there is no data to fill in. Pick a demo below;
          each one runs a real job through the real engine and tells you what to look at.
        </p>
      </section>

      <section className="space-y-4">
        <div className="space-y-1">
          <h2 className="text-lg font-medium">Take the tour</h2>
          <p className="text-sm text-zinc-500">
            Six runs, in order. Together they cover everything the system does.
          </p>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          {SCENARIOS.map((scenario) => (
            <ScenarioCard key={scenario.id} scenario={scenario} />
          ))}
        </div>
        <p className="text-sm text-zinc-500">
          Want to build your own instead?{" "}
          <Link href="/jobs/new" className="underline">
            Write a job by hand
          </Link>
          , or look up a term in the{" "}
          <Link href="/concepts" className="underline">
            glossary
          </Link>
          .
        </p>
      </section>

      <section className="space-y-4">
        <h2 className="text-lg font-medium">What is running underneath</h2>
        <WiringPanel />
      </section>

      <section className="space-y-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="text-lg font-medium">Latest runs</h2>
          <Link href="/jobs" className="text-sm underline">
            See all runs
          </Link>
        </div>
        <JobList limit={5} />
      </section>
    </div>
  );
}
