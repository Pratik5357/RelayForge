"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { ApiError, submitJob } from "@/lib/api";
import type { SubmitJobTaskRequest } from "@/lib/types";
import { EASE_OUT } from "@/lib/motion";
import { PageTransition } from "@/components/PageTransition";
import { Spinner } from "@/components/Spinner";
import { Arrow, Button } from "@/components/ui/Button";
import { FieldLabel, Input } from "@/components/ui/Input";
import { TaskDependencyPicker } from "@/components/TaskDependencyPicker";

type DraftTask = SubmitJobTaskRequest;

let keyCounter = 0;
function nextKey() {
  keyCounter += 1;
  return `task-${keyCounter}`;
}

const PRESETS: Record<string, { jobName: string; tasks: Omit<DraftTask, "key">[] }> = {
  flaky: {
    jobName: "Flaky step (retries into success)",
    tasks: [
      {
        name: "flaky-step",
        simulatedDurationMs: 1000,
        dependsOn: [],
        maxAttempts: 4,
        failUntilAttempt: 3,
      },
    ],
  },
  hopeless: {
    jobName: "Hopeless step (dead-letters)",
    tasks: [
      {
        name: "hopeless-step",
        simulatedDurationMs: 1000,
        dependsOn: [],
        maxAttempts: 3,
        failUntilAttempt: 10,
      },
    ],
  },
  partial: {
    jobName: "Partial failure (one branch succeeds, one dead-letters)",
    tasks: [
      { name: "start", simulatedDurationMs: 500, dependsOn: [] },
      { name: "healthy-branch", simulatedDurationMs: 1000, dependsOn: ["start"] },
      {
        name: "hopeless-branch",
        simulatedDurationMs: 1000,
        dependsOn: ["start"],
        maxAttempts: 3,
        failUntilAttempt: 10,
      },
    ],
  },
};

/** Stage number (1-based) per draft step key: 1 + the deepest stage it waits for. */
function stagesOf(tasks: DraftTask[]): Map<string, number> {
  const byKey = new Map(tasks.map((t) => [t.key, t]));
  const stage = new Map<string, number>();
  function of(task: DraftTask, seen: Set<string>): number {
    const cached = stage.get(task.key);
    if (cached !== undefined) return cached;
    if (seen.has(task.key)) return 1;
    seen.add(task.key);
    const parents = task.dependsOn.map((k) => byKey.get(k)).filter((t): t is DraftTask => Boolean(t));
    const value = parents.length ? 1 + Math.max(...parents.map((p) => of(p, seen))) : 1;
    stage.set(task.key, value);
    return value;
  }
  tasks.forEach((t) => of(t, new Set()));
  return stage;
}

export default function NewJobPage() {
  const router = useRouter();
  const reduce = useReducedMotion();
  const [jobName, setJobName] = useState("");
  const [tasks, setTasks] = useState<DraftTask[]>([]);
  const [taskName, setTaskName] = useState("");
  const [durationSeconds, setDurationSeconds] = useState(2);
  const [dependsOn, setDependsOn] = useState<string[]>([]);
  const [maxAttempts, setMaxAttempts] = useState<string>("");
  const [failUntilAttempt, setFailUntilAttempt] = useState<string>("");
  const [stepError, setStepError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function addTask() {
    if (!taskName.trim()) {
      setStepError("Give the step a name first.");
      return;
    }
    setStepError(null);
    setTasks((prev) => [
      ...prev,
      {
        key: nextKey(),
        name: taskName.trim(),
        simulatedDurationMs: Math.max(0, Math.round(durationSeconds * 1000)),
        dependsOn,
        maxAttempts: maxAttempts.trim() ? Number(maxAttempts) : undefined,
        failUntilAttempt: failUntilAttempt.trim() ? Number(failUntilAttempt) : undefined,
      },
    ]);
    setTaskName("");
    setDurationSeconds(2);
    setDependsOn([]);
    setMaxAttempts("");
    setFailUntilAttempt("");
  }

  function loadPreset(id: keyof typeof PRESETS) {
    const p = PRESETS[id];
    // Preset tasks reference each other by name; re-key them through nextKey() and remap
    // dependsOn from names to the freshly generated keys.
    const keyByName = new Map(p.tasks.map((t) => [t.name, nextKey()]));
    setTasks(
      p.tasks.map((t) => ({
        ...t,
        key: keyByName.get(t.name)!,
        dependsOn: t.dependsOn.map((name) => keyByName.get(name)!),
      })),
    );
    setJobName(p.jobName);
    setDependsOn([]);
    setError(null);
  }

  function removeTask(key: string) {
    setTasks((prev) =>
      prev
        .filter((t) => t.key !== key)
        .map((t) => ({ ...t, dependsOn: t.dependsOn.filter((d) => d !== key) })),
    );
    setDependsOn((prev) => prev.filter((d) => d !== key));
  }

  async function handleSubmit() {
    setError(null);

    if (tasks.length === 0) {
      setError("Add at least one step before starting the run.");
      return;
    }

    setSubmitting(true);
    try {
      const job = await submitJob({
        name: jobName.trim() || undefined,
        tasks,
      });
      router.push(`/jobs/${job.id}`, { transitionTypes: ["nav-forward"] });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not start the run.");
      setSubmitting(false);
    }
  }

  const stage = stagesOf(tasks);
  const nameByKey = new Map(tasks.map((t) => [t.key, t.name]));
  const ordered = [...tasks].sort((a, b) => (stage.get(a.key) ?? 1) - (stage.get(b.key) ?? 1));
  const stageCount = tasks.length ? Math.max(...tasks.map((t) => stage.get(t.key) ?? 1)) : 0;

  // The Start run label morphs between idle and busy; blur hides the moment the two overlap.
  const labelMotion = reduce
    ? { initial: { opacity: 0 }, animate: { opacity: 1 }, exit: { opacity: 0 } }
    : {
        initial: { opacity: 0, filter: "blur(2px)", transform: "translateY(6px)" },
        animate: { opacity: 1, filter: "blur(0px)", transform: "translateY(0px)" },
        exit: { opacity: 0, filter: "blur(2px)", transform: "translateY(-6px)" },
      };

  return (
    <PageTransition className="page-enter flex flex-col gap-9">
      <div className="flex flex-col gap-2.5">
        <h1 className="text-[clamp(52px,6vw,72px)] leading-[0.95] tracking-[-0.045em]">New run</h1>
        <p className="max-w-[62ch] text-[17px] text-muted">
          Add a few steps and mark which ones wait for which. A chain shows ordering; one step
          with two independent children shows parallelism. Give a step a &ldquo;fail until
          try&rdquo; number to see retries, growing delays and parking.
        </p>
      </div>

      <section aria-labelledby="presets-heading" className="flex flex-wrap items-center gap-x-4 gap-y-2.5">
        <h2 id="presets-heading" className="text-[15px] font-bold">
          Quick-fill a reliability demo
        </h2>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" size="sm" onClick={() => loadPreset("flaky")}>
            Flaky step (retries into success)
          </Button>
          <Button variant="secondary" size="sm" onClick={() => loadPreset("hopeless")}>
            Hopeless step (gets parked)
          </Button>
          <Button variant="secondary" size="sm" onClick={() => loadPreset("partial")}>
            Partial failure (one branch parked)
          </Button>
        </div>
      </section>

      <div className="flex flex-wrap items-start gap-8">
        <div className="min-w-0 flex-[1.2_1_520px] overflow-hidden rounded-[18px] bg-paper shadow-[0_1px_0_rgb(18_20_18/0.06),0_20px_40px_-30px_rgb(18_20_18/0.45)]">
          <div className="flex flex-col gap-1 border-b-2 border-dashed border-[var(--hairline-soft)] px-[26px] py-5">
            <FieldLabel htmlFor="job-name">Run name (optional)</FieldLabel>
            <Input
              id="job-name"
              value={jobName}
              onChange={(e) => setJobName(e.target.value)}
              placeholder="e.g. Ordering demo"
              className="h-[52px] text-[22px] font-bold tracking-[-0.01em]"
            />
          </div>

          <section aria-labelledby="add-heading" className="flex flex-col gap-[18px] px-[26px] pb-[26px] pt-[22px]">
            <h3 id="add-heading" className="text-2xl tracking-[-0.02em]">
              Add a step
            </h3>
            <div className="grid grid-cols-[repeat(auto-fit,minmax(190px,1fr))] gap-x-5 gap-y-4">
              <div className="flex flex-col gap-1">
                <FieldLabel htmlFor="task-name">Name</FieldLabel>
                <Input
                  id="task-name"
                  value={taskName}
                  onChange={(e) => {
                    setTaskName(e.target.value);
                    setStepError(null);
                  }}
                  placeholder="e.g. fetch-data"
                  aria-invalid={stepError ? true : undefined}
                  aria-describedby={stepError ? "step-error" : undefined}
                />
              </div>
              <div className="flex flex-col gap-1">
                <FieldLabel htmlFor="task-duration">Duration (seconds)</FieldLabel>
                <Input
                  id="task-duration"
                  type="number"
                  min={0}
                  step={0.5}
                  value={durationSeconds}
                  onChange={(e) => setDurationSeconds(Number(e.target.value))}
                  className="font-mono"
                />
              </div>
              <div className="flex flex-col gap-1">
                <FieldLabel htmlFor="task-max">Max tries (optional)</FieldLabel>
                <Input
                  id="task-max"
                  type="number"
                  min={1}
                  value={maxAttempts}
                  onChange={(e) => setMaxAttempts(e.target.value)}
                  placeholder="default"
                  className="font-mono"
                />
              </div>
              <div className="flex flex-col gap-1">
                <FieldLabel htmlFor="task-fail">Fail until try # (optional)</FieldLabel>
                <Input
                  id="task-fail"
                  type="number"
                  min={1}
                  value={failUntilAttempt}
                  onChange={(e) => setFailUntilAttempt(e.target.value)}
                  placeholder="always succeeds"
                  className="font-mono"
                />
              </div>
            </div>
            <fieldset className="flex flex-col gap-2">
              <legend className="label mb-2 text-label">Waits for</legend>
              <TaskDependencyPicker
                options={tasks.map((t) => ({ key: t.key, name: t.name }))}
                selected={dependsOn}
                onChange={setDependsOn}
              />
            </fieldset>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
              <Button variant="primary" onClick={addTask}>
                <svg width="13" height="13" viewBox="0 0 14 14" aria-hidden>
                  <path d="M7 2v10M2 7h10" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                </svg>
                Add step
              </Button>
              {stepError && (
                <p id="step-error" role="alert" className="fade-up font-semibold text-[var(--error)]">
                  {stepError}
                </p>
              )}
            </div>
          </section>
        </div>

        <aside
          aria-labelledby="steps-heading"
          className="on-board board-reveal flex min-w-0 flex-[1_1_400px] flex-col self-stretch overflow-hidden rounded-[18px] bg-board text-board-text"
        >
          <div className="flex items-baseline justify-between gap-3 border-b border-board-rule px-[22px] py-[18px]">
            <h3 id="steps-heading" className="text-xl">
              Steps in this run
            </h3>
            <span className="text-[13px] text-board-muted">
              {tasks.length === 0
                ? "Empty"
                : `${tasks.length} ${tasks.length === 1 ? "step" : "steps"} · ${stageCount} ${stageCount === 1 ? "stage" : "stages"}`}
            </span>
          </div>

          {tasks.length === 0 ? (
            <p className="px-[22px] py-10 text-center text-board-muted">Add a step and it shows up here.</p>
          ) : (
            <ol>
              <AnimatePresence initial={false}>
                {ordered.map((task) => (
                  <motion.li
                    key={task.key}
                    layout="position"
                    initial={reduce ? { opacity: 0 } : { opacity: 0, transform: "translateY(6px) scale(0.98)" }}
                    animate={reduce ? { opacity: 1 } : { opacity: 1, transform: "translateY(0px) scale(1)" }}
                    exit={{ opacity: 0, transition: { duration: 0.15 } }}
                    transition={{ duration: 0.26, ease: EASE_OUT }}
                    className="grid grid-cols-[44px_minmax(0,1fr)_auto] items-center gap-x-3 border-b border-board-rule py-3 pl-[22px] pr-2.5"
                  >
                    <span className="font-mono text-xl font-semibold text-amber">{stage.get(task.key)}</span>
                    <span className="flex min-w-0 flex-col">
                      <span className="break-words font-bold">{task.name}</span>
                      <span className="text-[13px] text-board-muted">
                        {((task.simulatedDurationMs ?? 0) / 1000).toFixed(1)}s
                        {task.dependsOn.length > 0 &&
                          ` · waits for ${task.dependsOn.map((k) => nameByKey.get(k) ?? k).join(", ")}`}
                        {task.failUntilAttempt !== undefined && ` · fails until try ${task.failUntilAttempt}`}
                        {task.maxAttempts !== undefined && ` · max ${task.maxAttempts} tries`}
                      </span>
                    </span>
                    <button
                      type="button"
                      onClick={() => removeTask(task.key)}
                      aria-label={`Remove ${task.name}`}
                      className="press min-h-10 rounded px-3 text-sm font-bold text-board-soft hover:text-[var(--remark-failed)]"
                    >
                      Remove
                    </button>
                  </motion.li>
                ))}
              </AnimatePresence>
            </ol>
          )}

          <div className="mt-auto flex flex-col gap-2.5 border-t-2 border-dashed border-[var(--board-perf)] px-[22px] py-5">
            <p className="text-[13px] text-board-muted">
              The number on the left is the stage. Steps in the same stage run side by side.
            </p>
            {error && (
              <p role="alert" className="fade-up font-semibold text-[var(--remark-failed)]">
                {error}
              </p>
            )}
            <Button variant="go" size="lg" onClick={handleSubmit} disabled={submitting} className="disabled:cursor-wait disabled:opacity-100">
              <span className="grid">
                <AnimatePresence mode="popLayout" initial={false}>
                  <motion.span
                    key={submitting ? "busy" : "idle"}
                    {...labelMotion}
                    transition={{ duration: 0.2, ease: EASE_OUT }}
                    className="col-start-1 row-start-1 inline-flex items-center justify-center gap-2.5"
                  >
                    {submitting ? (
                      <>
                        <Spinner /> Starting…
                      </>
                    ) : (
                      <>
                        Start run <Arrow />
                      </>
                    )}
                  </motion.span>
                </AnimatePresence>
              </span>
            </Button>
          </div>
        </aside>
      </div>
    </PageTransition>
  );
}
