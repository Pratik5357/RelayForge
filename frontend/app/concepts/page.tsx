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
    name: "Lease",
    definition:
      "A time-bound claim a worker holds on a step while running it. If the step's lease expires before it finishes — for instance because the process restarted — RelayForge treats it as crashed and gives it back to the queue.",
    seeIn: "Not directly visible, but it's what lets a task recover after the API process is restarted mid-run.",
  },
  {
    name: "Idempotency key",
    definition:
      "A stable identifier for a step that stays the same across every one of its retries, so two overlapping attempts at the same step can be told apart from two different steps.",
    seeIn: "Included in each task's data; it's what keeps a reclaimed lease from double-running a step.",
  },
  {
    name: "Cancellation",
    definition:
      "Stopping a job that's still going. Steps that haven't started yet are called off immediately; a step already in flight is left to finish naturally before the job is marked Cancelled.",
    seeIn: "Scenario 6, via the Cancel button on any running job's detail page.",
  },
];

export default function ConceptsPage() {
  return (
    <PageTransition className="page-enter flex flex-col gap-9">
      <GlossaryList terms={TERMS} />
    </PageTransition>
  );
}
