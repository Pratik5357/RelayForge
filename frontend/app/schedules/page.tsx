"use client";

import Link from "next/link";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  ApiError,
  createSchedule,
  deleteSchedule,
  listSchedules,
  setScheduleEnabled,
  triggerSchedule,
} from "@/lib/api";
import { SCENARIOS } from "@/lib/scenarios";
import type { ConcurrencyPolicy, MisfirePolicy, Schedule } from "@/lib/types";
import { PageTransition } from "@/components/PageTransition";
import { Remark } from "@/components/Remark";
import { Button } from "@/components/ui/Button";
import { FieldLabel, Input } from "@/components/ui/Input";

const MISFIRE: Record<MisfirePolicy, string> = {
  FireOnce: "Run once for all missed times",
  Skip: "Skip missed times",
  CatchUp: "Run every missed time",
};

const OVERLAP: Record<ConcurrencyPolicy, string> = {
  Allow: "Let runs overlap",
  Forbid: "Skip while the last run is going",
  Replace: "Cancel the last run, start the new one",
};

const SELECT =
  "h-12 w-full rounded-[4px] border-0 bg-field px-3 text-[17px] text-ink shadow-[inset_0_-2px_0_var(--field-rule)] focus:bg-[var(--field-focus)] focus:shadow-[inset_0_-3px_0_var(--ink)] focus:outline-none";

function when(iso: string | null) {
  return iso ? new Date(iso).toLocaleString([], { hour12: false, dateStyle: "medium", timeStyle: "short" }) : "—";
}

export default function SchedulesPage() {
  const [schedules, setSchedules] = useState<Schedule[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      setSchedules(await listSchedules());
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the API.");
    }
  }, []);

  useEffect(() => {
    // Loads on mount, then keeps "next run" and recent runs fresh.
    const first = setTimeout(refresh, 0);
    const interval = setInterval(refresh, 5000);
    return () => {
      clearTimeout(first);
      clearInterval(interval);
    };
  }, [refresh]);

  async function act(id: string, action: () => Promise<unknown>) {
    setBusy(id);
    try {
      await action();
      await refresh();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "That didn't work.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <PageTransition className="page-enter flex flex-col gap-10">
      <div className="flex flex-col gap-2">
        <h1 className="text-[clamp(52px,6vw,72px)] leading-[0.95] tracking-[-0.045em]">Schedules</h1>
        <p className="max-w-[62ch] text-[17px] text-muted">
          Jobs that start themselves on a timetable, written as cron. Each scheduled time can create
          at most one run, even if two schedulers race for it.
        </p>
      </div>

      {error && (
        <p role="alert" className="rounded-xl bg-board px-5 py-4 text-board-text">
          <span className="font-bold text-[var(--remark-failed)]">Problem.</span> {error}
        </p>
      )}

      {schedules === null && !error && (
        <div role="status" aria-label="Loading schedules" className="board-reveal h-32 rounded-xl bg-board" />
      )}

      {schedules !== null && (
        <ul className="flex flex-col gap-4">
          {schedules.map((s) => (
            <li key={s.id} className="on-board flex flex-col gap-4 rounded-xl bg-board px-6 py-5 text-board-text">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="flex min-w-0 flex-col gap-1">
                  <span className="text-xl font-bold tracking-[-0.01em]">
                    {s.name}
                    {s.isSystem && <span className="ml-2 align-middle text-xs font-semibold uppercase tracking-[0.07em] text-amber">built in</span>}
                  </span>
                  <span className="font-mono text-sm text-board-muted">
                    {s.cron} · {s.timeZone}
                  </span>
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button variant="board" size="sm" disabled={busy === s.id} onClick={() => act(s.id, () => triggerSchedule(s.id))}>
                    Run now
                  </Button>
                  <Button variant="board" size="sm" disabled={busy === s.id} onClick={() => act(s.id, () => setScheduleEnabled(s.id, !s.enabled))}>
                    {s.enabled ? "Disable" : "Enable"}
                  </Button>
                  {!s.isSystem && (
                    <Button variant="board" size="sm" disabled={busy === s.id} onClick={() => act(s.id, () => deleteSchedule(s.id))}>
                      Delete
                    </Button>
                  )}
                </div>
              </div>

              <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm sm:grid-cols-4">
                {[
                  ["Next run", s.enabled ? when(s.nextFireAt) : "disabled"],
                  ["Last run", when(s.lastFiredAt)],
                  ["Missed times", MISFIRE[s.misfirePolicy]],
                  ["Overlap", OVERLAP[s.concurrencyPolicy]],
                ].map(([term, value]) => (
                  <div key={term} className="flex flex-col gap-0.5">
                    <dt className="label text-board-muted">{term}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>

              {s.recentRuns.length > 0 && (
                <ul className="flex flex-col border-t border-board-rule pt-3">
                  {s.recentRuns.map((run) => (
                    <li key={run.id}>
                      <Link
                        href={`/jobs/${run.id}`}
                        transitionTypes={["nav-forward"]}
                        className="flex items-center justify-between gap-4 py-1.5 text-board-text no-underline hover:underline"
                      >
                        <span className="tnum font-mono text-sm">{when(run.scheduledFor ?? run.createdAt)}</span>
                        <Remark state={run.state} />
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      )}

      <NewSchedule onCreated={refresh} />
    </PageTransition>
  );
}

function NewSchedule({ onCreated }: { onCreated: () => Promise<void> }) {
  const [name, setName] = useState("");
  const [cron, setCron] = useState("*/5 * * * *");
  const [timeZone, setTimeZone] = useState(() => Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC");
  const [scenarioId, setScenarioId] = useState(SCENARIOS[0].id);
  const [misfire, setMisfire] = useState<MisfirePolicy>("FireOnce");
  const [overlap, setOverlap] = useState<ConcurrencyPolicy>("Forbid");
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const scenario = SCENARIOS.find((s) => s.id === scenarioId)!;
    setSaving(true);
    setFormError(null);
    try {
      await createSchedule({
        name: name.trim(),
        cron: cron.trim(),
        timeZone,
        template: { ...scenario.job, name: undefined },
        misfirePolicy: misfire,
        concurrencyPolicy: overlap,
      });
      setName("");
      await onCreated();
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : "Could not create the schedule.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section aria-labelledby="new-schedule" className="rounded-[18px] bg-paper px-[26px] py-6 shadow-[0_1px_0_rgb(18_20_18/0.06),0_20px_40px_-30px_rgb(18_20_18/0.45)]">
      <h2 id="new-schedule" className="mb-4 text-2xl tracking-[-0.02em]">
        New schedule
      </h2>
      <form onSubmit={submit} className="grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-x-5 gap-y-4">
        <div className="flex flex-col gap-1">
          <FieldLabel htmlFor="s-name">Name</FieldLabel>
          <Input id="s-name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. nightly-report" />
        </div>
        <div className="flex flex-col gap-1">
          <FieldLabel htmlFor="s-cron">When (cron)</FieldLabel>
          <Input id="s-cron" required value={cron} onChange={(e) => setCron(e.target.value)} className="font-mono" aria-describedby="s-cron-hint" />
          <span id="s-cron-hint" className="text-xs text-muted">
            minute hour day month weekday · &ldquo;0 2 * * *&rdquo; = 02:00 daily
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <FieldLabel htmlFor="s-tz">Time zone</FieldLabel>
          <Input id="s-tz" required value={timeZone} onChange={(e) => setTimeZone(e.target.value)} placeholder="e.g. Asia/Kolkata" />
        </div>
        <div className="flex flex-col gap-1">
          <FieldLabel htmlFor="s-job">Job to run</FieldLabel>
          <select id="s-job" className={SELECT} value={scenarioId} onChange={(e) => setScenarioId(e.target.value)}>
            {SCENARIOS.map((s) => (
              <option key={s.id} value={s.id}>
                {s.title}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <FieldLabel htmlFor="s-misfire">If runs were missed</FieldLabel>
          <select id="s-misfire" className={SELECT} value={misfire} onChange={(e) => setMisfire(e.target.value as MisfirePolicy)}>
            {Object.entries(MISFIRE).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <FieldLabel htmlFor="s-overlap">If the last run is still going</FieldLabel>
          <select id="s-overlap" className={SELECT} value={overlap} onChange={(e) => setOverlap(e.target.value as ConcurrencyPolicy)}>
            {Object.entries(OVERLAP).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <div className="col-span-full flex flex-wrap items-center gap-4">
          <Button type="submit" disabled={saving}>
            {saving ? "Saving…" : "Create schedule"}
          </Button>
          {formError && (
            <p role="alert" className="text-[var(--remark-failed)]">
              {formError}
            </p>
          )}
        </div>
      </form>
    </section>
  );
}
