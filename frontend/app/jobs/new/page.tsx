"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, submitJob } from "@/lib/api";
import type { SubmitJobTaskRequest } from "@/lib/types";
import { Button } from "@/components/ui/Button";
import { Callout } from "@/components/ui/Callout";
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

export default function NewJobPage() {
  const router = useRouter();
  const [jobName, setJobName] = useState("");
  const [tasks, setTasks] = useState<DraftTask[]>([]);
  const [taskName, setTaskName] = useState("");
  const [durationSeconds, setDurationSeconds] = useState(2);
  const [dependsOn, setDependsOn] = useState<string[]>([]);
  const [maxAttempts, setMaxAttempts] = useState<string>("");
  const [failUntilAttempt, setFailUntilAttempt] = useState<string>("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function addTask() {
    if (!taskName.trim()) {
      return;
    }
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
    setError(null);
  }

  function removeTask(key: string) {
    setTasks((prev) =>
      prev
        .filter((t) => t.key !== key)
        .map((t) => ({ ...t, dependsOn: t.dependsOn.filter((d) => d !== key) })),
    );
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
      router.push(`/jobs/${job.id}`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not start the run.");
      setSubmitting(false);
    }
  }

  return (
    <div className="max-w-3xl space-y-8">
      <div>
        <h1 className="text-4xl leading-none">New run</h1>
        <p className="text-[var(--muted)]">
          Add a few steps and mark which ones wait for which. A chain shows ordering; one step
          with two independent children shows parallelism. Give a step a &ldquo;fail until
          try&rdquo; number to see retries, growing delays and parking.
        </p>
      </div>

      <section className="space-y-3" aria-labelledby="presets-heading">
        <h3 id="presets-heading" className="text-sm font-semibold">
          Quick-fill a reliability demo
        </h3>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={() => loadPreset("flaky")}>
            Flaky step (retries into success)
          </Button>
          <Button type="button" variant="secondary" size="sm" onClick={() => loadPreset("hopeless")}>
            Hopeless step (gets parked)
          </Button>
          <Button type="button" variant="secondary" size="sm" onClick={() => loadPreset("partial")}>
            Partial failure (one branch parked)
          </Button>
        </div>
      </section>

      <section className="space-y-2">
        <FieldLabel htmlFor="job-name">Run name (optional)</FieldLabel>
        <Input
          id="job-name"
          value={jobName}
          onChange={(e) => setJobName(e.target.value)}
          placeholder="e.g. Ordering demo"
        />
      </section>

      <section className="space-y-4 rounded-[4px] border border-[var(--border)] bg-[var(--surface)] p-5">
        <h3 className="text-sm font-semibold">Add a step</h3>
        <div className="grid gap-4 sm:grid-cols-[2fr_1fr]">
          <div>
            <FieldLabel htmlFor="task-name">Name</FieldLabel>
            <Input
              id="task-name"
              value={taskName}
              onChange={(e) => setTaskName(e.target.value)}
              placeholder="e.g. fetch-data"
            />
          </div>
          <div>
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
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
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
          <div>
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
        <div>
          <p className="mb-1 text-xs font-semibold text-[var(--muted)]">Waits for</p>
          <TaskDependencyPicker
            options={tasks.map((t) => ({ key: t.key, name: t.name }))}
            selected={dependsOn}
            onChange={setDependsOn}
          />
        </div>
        <Button type="button" variant="secondary" size="sm" onClick={addTask}>
          Add step
        </Button>
      </section>

      {tasks.length > 0 && (
        <section className="space-y-3" aria-labelledby="steps-heading">
          <h3 id="steps-heading" className="text-sm font-semibold">
            Steps in this run
          </h3>
          <ul className="divide-y divide-[var(--border)] rounded-[4px] border border-[var(--border)] bg-[var(--surface)]">
            {tasks.map((task) => (
              <li key={task.key} className="flex items-center justify-between gap-4 px-4 py-2.5 text-sm">
                <div className="min-w-0">
                  <span className="font-semibold">{task.name}</span>{" "}
                  <span className="text-xs text-[var(--muted)]">
                    {(task.simulatedDurationMs / 1000).toFixed(1)}s
                    {task.dependsOn.length > 0 && `, waits for ${task.dependsOn.join(", ")}`}
                    {task.failUntilAttempt !== undefined &&
                      `, fails until try ${task.failUntilAttempt}`}
                    {task.maxAttempts !== undefined && `, max ${task.maxAttempts} tries`}
                  </span>
                </div>
                <button
                  type="button"
                  onClick={() => removeTask(task.key)}
                  className="shrink-0 text-xs font-semibold text-[var(--muted)] transition-colors duration-150 hover:text-[var(--state-failed-fg)]"
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {error && (
        <Callout tone="error" role="alert">
          {error}
        </Callout>
      )}

      <Button onClick={handleSubmit} disabled={submitting}>
        {submitting ? "Starting…" : "Start run"}
      </Button>
    </div>
  );
}
