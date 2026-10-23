import { PageTransition } from "@/components/PageTransition";
import { GlossaryList, type Term } from "./GlossaryList";


const TERMS: Term[] = [
  {
    name: "Job",
    definition: "The work you submit — made of one or more steps, with dependencies between them.",
    seeIn: "Any scenario on the tour, or your own run on New run.",
  },
  {
    name: "Step (task)",
    definition: "One unit of work inside a job. A job can have one step or many.",
    seeIn: "Each row on a run's steps board.",
  },
  {
    name: "DAG (dependency graph)",
    definition:
      "A map of which steps depend on which others, with no circular dependencies — that's what lets RelayForge figure out a safe order to run things in.",
    seeIn: "The stages on a run's page: each stage only starts once everything in the stage before it has succeeded.",
  },
  {
    name: "Dependency level",
    definition:
      "How many steps deep a step is from the steps it ultimately depends on. Steps in the same level can run at the same time.",
    seeIn: "Scenario 2 (parallelism) — two steps in the same level start together.",
  },
  {
    name: "Attempt",
    definition: "One try at running a step. A step can be given a budget of several attempts before it's given up on.",
    seeIn: "The \"N/M tries\" count on any step.",
  },
  {
    name: "Exponential backoff with jitter",
    definition:
      "Waiting a little longer before each retry (doubling the delay each time, up to a cap), with some randomness mixed in so retries don't all land at once.",
    seeIn: "Scenario 3 — watch the \"retrying in Ns\" delay grow between attempts.",
  },
  {
    name: "Dead letter",
    definition: "What happens to a step that's exhausted its retry budget without succeeding — it's set aside as a known failure instead of retried forever.",
    seeIn: "Scenario 4, and any step marked Parked.",
  },
  {
    name: "Lease and heartbeat",
    definition:
      "A short, time-bound claim a worker holds on a step while running it, renewed every few seconds by a heartbeat. A healthy step of any length keeps its lease; if the worker dies or freezes, the heartbeat stops, the lease runs out, and the step is reclaimed and tried again.",
    seeIn: "Scenario 7 — a worker crashes mid-step and another finishes it.",
  },
  {
    name: "Fencing token",
    definition:
      "The try number, checked on every write a worker makes after claiming a step. A worker that froze past its lease still thinks it owns the step; when it finally reports, its try number is out of date and the result is rejected instead of overwriting the newer try.",
    seeIn: "Scenario 8 — the frozen worker's late result is rejected.",
  },
  {
    name: "Idempotency key",
    definition:
      "A stable identifier for a step that stays the same across every one of its retries. Steps send it to outside systems (as an Idempotency-Key header), so a retried payment or email is recognised as a repeat instead of happening twice.",
    seeIn: "In each step's details, and in the header of every http.request call.",
  },
  {
    name: "Retryable vs fatal error",
    definition:
      "A timeout or a 503 might work next time, so it's retried. A 404 or a malformed request never will, so the step fails at once instead of burning through its retries.",
    seeIn: "Scenario 9 — the 404 fails on its first try.",
  },
  {
    name: "Outbox",
    definition:
      "Every change and its event are saved in the same database transaction, then the events are pushed out. Each event in a run is numbered, so the page can tell when it missed one and fetch it.",
    seeIn: "The # numbers in a run's Live events.",
  },
  {
    name: "Process isolation",
    definition:
      "Running a step in its own process instead of inside the worker. A step that ignores requests to stop can then be killed when its time limit is up, without taking the worker down with it.",
    seeIn: "Scenario 10 — the runaway step is killed at 3 seconds.",
  },
  {
    name: "Cancellation",
    definition:
      "Stopping a job that's still going. Steps that haven't started yet are called off immediately; steps already in flight are told to stop, then the job is marked Cancelled.",
    seeIn: "Scenario 6, via the Cancel button on any running job's detail page.",
  },
  {
    name: "Redrive",
    definition:
      "Giving a parked or failed step fresh tries after the cause is fixed. If the run had already ended, it reopens, and the steps that were waiting on that step run too.",
    seeIn: "The Redrive button in a parked step's details (try it after scenario 5).",
  },
  {
    name: "Schedule (cron)",
    definition:
      "A job that starts itself on a timetable. Each scheduled time can create at most one run, and you choose what happens to times missed while RelayForge was down and to runs that overlap.",
    seeIn: "The Schedules page. RelayForge also runs its own clean-up job there every night.",
  },
];

export default function ConceptsPage() {
  return (
    <PageTransition className="page-enter flex flex-col gap-9">
      <GlossaryList terms={TERMS} />
    </PageTransition>
  );
}
