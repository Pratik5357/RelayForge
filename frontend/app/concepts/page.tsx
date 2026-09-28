import Link from "next/link";

type Entry = {
  term: string;
  plain: string;
  where: string;
};

const ENTRIES: Entry[] = [
  {
    term: "Job",
    plain:
      "One submission: a name plus a set of steps. It is the thing you watch on a run page.",
    where: "Every row on the Runs page is a job.",
  },
  {
    term: "Step (task)",
    plain:
      "One unit of work inside a job — the API calls these tasks. A step names the steps it must wait for, and nothing else about ordering is specified.",
    where: "Each card in a run's columns is one step.",
  },
  {
    term: "DAG",
    plain:
      "Short for directed acyclic graph: the shape you get when steps point at the steps they depend on, with no loops. It is what lets the engine work out an order by itself.",
    where:
      "The columns on a run page. A job whose steps depend on each other in a circle is rejected when you submit it.",
  },
  {
    term: "Dependency level",
    plain:
      "A column. Everything in one column depends only on earlier columns, so all of it can run at once.",
    where: "Demo 2 in the tour, where two shards share a column.",
  },
  {
    term: "Attempt",
    plain:
      "One try at running a step. A step gets a fixed number of attempts before the engine stops trying.",
    where: "The 'attempts used' line on each step card.",
  },
  {
    term: "Exponential backoff with jitter",
    plain:
      "The waiting rule between attempts: each retry waits roughly twice as long as the last, plus a small random amount. Doubling stops a struggling dependency from being hammered; the randomness stops every retry in the fleet from firing at the same instant.",
    where: "Demo 3 — the pauses between attempts visibly grow.",
  },
  {
    term: "Dead letter",
    plain:
      "Where a step goes when it has used every attempt. It is set aside for a human instead of being retried forever, and the job is reported as failed rather than stuck.",
    where: "Demo 4, and the dead-lettered count on the home page.",
  },
  {
    term: "Lease",
    plain:
      "A time-limited claim a worker puts on a step while running it. If the worker dies, the claim expires and another worker picks the step up — that is how a crash mid-run recovers without anyone intervening.",
    where: "The lease length is in the wiring panel on the home page.",
  },
  {
    term: "Heartbeat",
    plain:
      "A periodic 'still alive' note each worker writes. Missing heartbeats are how the system notices a worker is gone.",
    where: "'Workers seen' in the wiring panel.",
  },
  {
    term: "Idempotency key",
    plain:
      "A per-step fingerprint recorded on success. If the same step somehow gets delivered twice, the second delivery is recognised and skipped instead of doing the work again.",
    where:
      "Not visible in the UI by design — it is what keeps a retry from double-charging a card, so to speak.",
  },
  {
    term: "At-least-once delivery",
    plain:
      "The guarantee a message queue actually gives: a message will arrive, possibly more than once. Idempotency keys and leases are the reason that is safe here.",
    where: "Applies when the queue in the wiring panel says RabbitMq.",
  },
  {
    term: "Worker",
    plain:
      "The thing that takes a ready step, runs it, and records the outcome. Workers can run inside the API process or as separate processes on other machines; the code is the same either way.",
    where: "'Worker slots' in the wiring panel tells you which mode is running.",
  },
];

export default function ConceptsPage() {
  return (
    <section className="space-y-6">
      <div className="space-y-2">
        <h1 className="text-xl font-semibold">Glossary</h1>
        <p className="max-w-2xl text-sm text-zinc-600 dark:text-zinc-400">
          Every term this dashboard uses, in plain words, with where to see the thing itself.
          If you are starting from scratch,{" "}
          <Link href="/" className="underline">
            the tour
          </Link>{" "}
          is the faster route.
        </p>
      </div>

      <dl className="space-y-4">
        {ENTRIES.map((entry) => (
          <div
            key={entry.term}
            className="rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900"
          >
            <dt className="font-medium">{entry.term}</dt>
            <dd className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">{entry.plain}</dd>
            <dd className="mt-2 text-xs text-zinc-500">
              <span className="font-medium">Where to see it:</span> {entry.where}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
