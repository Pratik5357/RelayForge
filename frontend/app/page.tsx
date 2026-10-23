import Link from "next/link";
import { SCENARIOS } from "@/lib/scenarios";
import { BoardingPass } from "@/components/BoardingPass";
import { ExampleBoard } from "@/components/ExampleBoard";
import { PageTransition } from "@/components/PageTransition";
import { Remark } from "@/components/Remark";
import { Reveal, RevealItem, RevealList } from "@/components/Reveal";
import { Arrow, buttonClasses } from "@/components/ui/Button";
import type { JobState, TaskState } from "@/lib/types";

const LEGEND: { state: TaskState | JobState; means: string }[] = [
  { state: "Pending", means: "waiting on an earlier step" },
  { state: "Ready", means: "queued for the next free worker" },
  { state: "Running", means: "working now" },
  { state: "Succeeded", means: "finished" },
  { state: "DeadLettered", means: "gave up, set aside" },
  { state: "Failed", means: "stopped with an error" },
  { state: "PartiallyFailed", means: "run finished with some steps failed" },
  { state: "Cancelled", means: "called off" },
];

export default function OverviewPage() {
  return (
    <PageTransition className="flex flex-col gap-26">
      {/* From md up the hero fills the first screen (minus the 64px bar and 56px top padding),
          so the next section starts below the fold instead of sitting half-revealed under it. */}
      <section className="flex flex-wrap content-center items-center gap-x-14 gap-y-12 md:min-h-[calc(100svh-120px)]">
        <div className="flex min-w-0 flex-[1_1_380px] flex-col gap-6 pt-3">
          <h1 className="headline-reveal text-[clamp(52px,6.4vw,92px)] leading-[0.95] tracking-[-0.04em]">
            Watch background work run.
          </h1>
          <p className="fade-up max-w-[46ch] text-[19px] text-muted [animation-delay:260ms]">
            Submit a job made of steps that depend on each other. RelayForge runs them in order,
            side by side where it can, retries the ones that stumble, and shows every move as it
            happens.
          </p>
          <div className="fade-up flex flex-wrap gap-2.5 [animation-delay:340ms]">
            <a href="#tour" className={buttonClasses("primary", "lg")}>
              Start the tour <Arrow direction="down" />
            </a>
            <Link href="/jobs/new" transitionTypes={["nav-forward"]} className={buttonClasses("secondary", "lg")}>
              Build a job by hand
            </Link>
          </div>
        </div>
        <div className="min-w-0 flex-[1.45_1_560px]">
          <ExampleBoard />
        </div>
      </section>

      <Reveal as="section" aria-labelledby="example-heading" className="flex flex-wrap gap-x-14 gap-y-8">
        <div className="flex flex-[1_1_300px] flex-col gap-2.5">
          <h2 id="example-heading" className="text-[44px] leading-none tracking-[-0.035em]">
            How to read a run
          </h2>
          <p className="max-w-[40ch] text-muted">
            Every step carries a remark, the way a departures board does. The remark always says
            the state in words; the light next to it only blinks while the step is working.
          </p>
        </div>
        <RevealList className="grid min-w-0 flex-[2_1_560px] grid-cols-[repeat(auto-fill,minmax(280px,1fr))] content-start gap-x-10">
          {LEGEND.map((item) => (
            <RevealItem key={item.state} className="flex items-center gap-4 border-t border-hairline py-3.5">
              <span className="flex w-[150px] shrink-0">
                <Remark state={item.state} />
              </span>
              {item.means}
            </RevealItem>
          ))}
          <RevealItem className="flex items-center gap-4 border-t border-hairline py-3.5">
            <span className="w-[150px] shrink-0 font-mono text-[17px]">2/3</span>
            tries used, out of the step&rsquo;s budget
          </RevealItem>
        </RevealList>
      </Reveal>

      <section id="tour" aria-labelledby="tour-heading" className="flex scroll-mt-6 flex-col gap-7">
        <Reveal className="flex flex-wrap items-end justify-between gap-x-8 gap-y-2">
          <h2 id="tour-heading" className="text-[44px] leading-none tracking-[-0.035em]">
            Take the tour
          </h2>
          <p className="text-muted">Six one-click demos. Each starts a real job and shows what happens as it runs.</p>
        </Reveal>
        <RevealList as="ol" stagger={0.07} className="grid grid-cols-[repeat(auto-fill,minmax(min(100%,520px),1fr))] gap-5">
          {SCENARIOS.map((scenario) => (
            <BoardingPass key={scenario.id} scenario={scenario} primary={scenario.id === SCENARIOS[0].id} />
          ))}
        </RevealList>
        <Reveal as="p" className="text-muted">
          Prefer your own?{" "}
          <Link href="/jobs/new" transitionTypes={["nav-forward"]} className="font-bold text-ink underline">
            Build a job by hand
          </Link>
          , or look something up in the{" "}
          <Link href="/concepts" transitionTypes={["nav-forward"]} className="font-bold text-ink underline">
            glossary
          </Link>
          .
        </Reveal>
      </section>
    </PageTransition>
  );
}
