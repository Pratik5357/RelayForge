import type { JobDetail } from "@/lib/types";
import type { AttemptHistory } from "@/lib/attempts";
import { RevealItem, RevealList } from "./Reveal";

type Segment = {
  key: string;
  kind: "ok" | "fail" | "running" | "wait";
  left: number;
  width: number;
  label: string;
  tip: string;
};

function secs(ms: number) {
  return `${(ms / 1000).toFixed(1)}s`;
}

/**
 * One lane per step across the run's own clock. Solid bars are tries that worked, hatched bars
 * tries that failed, dotted lines the wait before a retry — so parallel steps visibly start
 * together and a growing backoff visibly grows.
 */
export function RunTimeline({ job, attempts, now }: { job: JobDetail; attempts: AttemptHistory; now: number }) {
  const all = job.tasks.flatMap((t) => attempts[t.id] ?? []);
  if (all.length === 0) {
    return (
      <p className="rounded-2xl bg-paper px-6 py-8 text-muted shadow-[inset_0_0_0_1px_var(--hairline-soft)]">
        Nothing has started yet. Bars appear here as steps begin.
      </p>
    );
  }

  const origin = Math.min(...all.map((a) => a.start), job.startedAt ? new Date(job.startedAt).getTime() : Infinity);
  const finish = job.completedAt ? new Date(job.completedAt).getTime() : now;
  const latestRetry = Math.max(0, ...job.tasks.map((t) => (t.nextAttemptAt ? new Date(t.nextAttemptAt).getTime() : 0)));
  const end = Math.max(finish, latestRetry, origin + 1000);
  const span = end - origin;
  const pct = (ms: number) => ((ms - origin) / span) * 100;
  const cancelAt = job.cancellationRequestedAt ? pct(new Date(job.cancellationRequestedAt).getTime()) : null;

  const lanes = job.tasks.map((task) => {
    const tries = attempts[task.id] ?? [];
    const segments: Segment[] = [];
    tries.forEach((attempt, i) => {
      const stop = attempt.end ?? now;
      const kind = attempt.outcome === "running" ? "running" : attempt.outcome === "succeeded" ? "ok" : "fail";
      const label = attempt.count > 1 ? `tries 1–${attempt.count}` : `try ${attempt.number}`;
      const tip =
        attempt.count > 1
          ? `${attempt.count} tries over ${secs(stop - attempt.start)} · ${kind === "ok" ? "succeeded" : "did not succeed"}`
          : kind === "running"
          ? `Try ${attempt.number} · running now`
          : kind === "ok"
            ? `Try ${attempt.number} · succeeded in ${secs(stop - attempt.start)}`
            : attempt.outcome === "stopped"
              ? `Try ${attempt.number} · stopped after ${secs(stop - attempt.start)}`
              : `Try ${attempt.number} · failed after ${secs(stop - attempt.start)}`;
      segments.push({ key: `t${i}`, kind, left: pct(attempt.start), width: pct(stop) - pct(attempt.start), label, tip });

      const next = tries[i + 1];
      const waitEnd = next ? next.start : task.nextAttemptAt && attempt.end ? new Date(task.nextAttemptAt).getTime() : null;
      if (attempt.end && waitEnd && waitEnd > attempt.end) {
        segments.push({
          key: `w${i}`,
          kind: "wait",
          left: pct(attempt.end),
          width: pct(waitEnd) - pct(attempt.end),
          label: `waited ${secs(waitEnd - attempt.end)}`,
          tip: next ? `Waited ${secs(waitEnd - attempt.end)} before the next try` : `Next try in ${Math.max(0, Math.ceil((waitEnd - now) / 1000))}s`,
        });
      }
    });
    return { task, segments };
  });

  const tickEvery = span > 60_000 ? 10 : span > 20_000 ? 5 : 1;
  const ticks: number[] = [];
  for (let s = 0; s * 1000 <= span; s += tickEvery) ticks.push(s);

  return (
    <div className="overflow-x-auto rounded-2xl bg-paper shadow-[inset_0_0_0_1px_var(--hairline-soft)]">
      <RevealList as="div" stagger={0.08} className="relative min-w-[720px] px-[26px] pb-4 pt-[22px]">
        {cancelAt !== null && (
          // Drawn down the lanes at the moment Cancel was pressed: steps to its right never started.
          <span
            aria-hidden
            className="cancel-mark pointer-events-none absolute bottom-10 top-3 z-[2] border-l-2 border-dashed border-ink"
            style={{ left: `calc(26px + 180px + (100% - 52px - 180px) * ${cancelAt / 100})` }}
          >
            <span className="absolute -top-1 left-1.5 whitespace-nowrap rounded bg-ink px-1.5 py-0.5 text-[11px] font-bold uppercase tracking-[0.06em] text-white">
              Cancel pressed
            </span>
          </span>
        )}
        {lanes.map(({ task, segments }) => (
          <RevealItem as="div" key={task.id} distance={8} className="grid min-h-16 grid-cols-[180px_minmax(0,1fr)] items-center border-b border-[#eceeea]">
            <span className="truncate pr-4 font-bold">{task.name}</span>
            <div className="relative h-16">
              {segments.map((seg) =>
                seg.kind === "wait" ? (
                  <span
                    key={seg.key}
                    tabIndex={0}
                    role="img"
                    aria-label={seg.tip}
                    className="has-tip bar-draw absolute top-[28px] h-0 border-t-2 border-dotted border-ink outline-offset-4"
                    style={{ left: `${seg.left}%`, width: `${seg.width}%` }}
                  >
                    <span aria-hidden className="tip">
                      {seg.tip}
                    </span>
                    <span className="absolute left-0 top-[16px] whitespace-nowrap text-xs text-label">{seg.label}</span>
                  </span>
                ) : (
                  <span
                    key={seg.key}
                    tabIndex={0}
                    role="img"
                    aria-label={seg.tip}
                    className="has-tip bar-draw absolute top-[18px] h-[22px] rounded-[3px]"
                    style={{
                      left: `${seg.left}%`,
                      width: `max(${seg.width}%, 4px)`,
                      background:
                        seg.kind === "running"
                          ? "var(--amber)"
                          : seg.kind === "ok"
                            ? "var(--ink)"
                            : "repeating-linear-gradient(135deg, #d6361f 0 3px, #fff 3px 6px)",
                      boxShadow: seg.kind === "fail" ? "inset 0 0 0 1.5px #d6361f" : undefined,
                    }}
                  >
                    <span aria-hidden className="tip">
                      {seg.tip}
                    </span>
                    <span className="absolute left-0 top-[28px] whitespace-nowrap text-xs text-label">{seg.label}</span>
                  </span>
                ),
              )}
            </div>
          </RevealItem>
        ))}
        <div className="grid grid-cols-[180px_minmax(0,1fr)] pt-2">
          <span />
          <div className="relative h-[18px] font-mono text-xs text-label">
            {ticks.map((s) => (
              <span key={s} className="absolute -translate-x-1/2" style={{ left: `${pct(origin + s * 1000)}%` }}>
                {s}s
              </span>
            ))}
          </div>
        </div>
      </RevealList>
    </div>
  );
}

export function TimelineKey() {
  return (
    <div className="flex flex-wrap gap-x-5 gap-y-2 text-sm text-muted">
      <span className="inline-flex items-center gap-2">
        <span aria-hidden className="h-2.5 w-[22px] rounded-sm bg-ink" />
        try that worked
      </span>
      <span className="inline-flex items-center gap-2">
        <span
          aria-hidden
          className="h-2.5 w-[22px] rounded-sm shadow-[inset_0_0_0_1.5px_#d6361f]"
          style={{ background: "repeating-linear-gradient(135deg, #d6361f 0 3px, #fff 3px 6px)" }}
        />
        try that failed
      </span>
      <span className="inline-flex items-center gap-2">
        <span aria-hidden className="w-[22px] border-t-2 border-dotted border-ink" />
        waiting before a retry
      </span>
    </div>
  );
}
