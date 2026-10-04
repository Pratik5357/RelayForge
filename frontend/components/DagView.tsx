import { toDagLevels } from "@/lib/dag";
import type { JobTaskDto } from "@/lib/types";
import { StateBadge } from "@/components/StateBadge";

const NODE_W = 216;
const NODE_H_BASE = 108;
const NODE_H_NOTE = 160;
const COL_PAD = 16; // space between a stage container and its nodes
const BAND_W = NODE_W + COL_PAD * 2;
const COL_GAP = 56; // space between stage containers
const ROW_GAP = 16;
const HEADER_H = 48;

function retryCountdown(nextAttemptAt: string | null, now: number): string | null {
  if (!nextAttemptAt) return null;
  const remainingMs = new Date(nextAttemptAt).getTime() - now;
  if (remainingMs <= 0) return "retrying…";
  return `retrying in ${Math.ceil(remainingMs / 1000)}s`;
}

const ROLE: Record<JobTaskDto["state"], string> = {
  Pending: "pending",
  Running: "running",
  Succeeded: "succeeded",
  Failed: "failed",
  DeadLettered: "deadlettered",
  Cancelled: "cancelled",
};

function HeatBar({ state }: { state: JobTaskDto["state"] }) {
  const role = ROLE[state];
  return (
    <div
      className="relative h-1.5 overflow-hidden rounded-[1px]"
      style={{
        background: state === "Pending" ? "transparent" : "var(--border)",
        boxShadow: state === "Pending" ? "inset 0 0 0 1px var(--state-pending-ring)" : undefined,
      }}
      aria-hidden
    >
      {state === "Running" ? (
        <div
          className="heat-sweep absolute inset-y-0 left-0 w-2/5"
          style={{ background: "var(--state-running-solid)" }}
        />
      ) : state !== "Pending" ? (
        <div className="absolute inset-0" style={{ background: `var(--state-${role}-solid)` }} />
      ) : null}
    </div>
  );
}

function StepNode({
  task,
  nameByTaskId,
  now,
  x,
  y,
  h,
  ticket,
}: {
  task: JobTaskDto;
  nameByTaskId: Map<string, string>;
  now: number;
  x: number;
  y: number;
  h: number;
  ticket: string;
}) {
  const countdown = retryCountdown(task.nextAttemptAt, now);
  const role = ROLE[task.state];

  return (
    <div
      className="absolute flex flex-col gap-2.5 rounded-[4px] bg-[var(--surface)] p-4"
      style={{
        left: x,
        top: y,
        width: NODE_W,
        height: h,
        borderColor: "var(--border-strong)",
        borderStyle: "solid",
        borderWidth: 1,
        background:
          task.state === "Failed" || task.state === "DeadLettered"
            ? `var(--state-${role}-bg)`
            : undefined,
      }}
    >
      <HeatBar state={task.state} />
      <div className="flex items-start justify-between gap-2">
        <span
          className={`truncate text-sm font-semibold ${task.state === "Cancelled" ? "line-through decoration-[var(--muted)]" : ""}`}
          title={task.name}
        >
          {task.name}
        </span>
        <span className="tnum shrink-0 font-mono text-[11px] text-[var(--muted)]" aria-hidden>
          {ticket}
        </span>
      </div>
      <div className="flex items-center justify-between gap-2">
        <StateBadge state={task.state} />
        <span className="tnum whitespace-nowrap font-mono text-xs text-[var(--muted)]">
          {task.attemptCount}/{task.maxAttempts} tries
        </span>
      </div>
      {(countdown || task.errorMessage) && (
        <p
          className="line-clamp-2 break-words text-xs leading-snug"
          style={{ color: countdown ? "var(--state-running-fg)" : "var(--state-failed-fg)" }}
          title={task.errorMessage ?? undefined}
        >
          {countdown ?? task.errorMessage}
        </p>
      )}
      <span className="sr-only">
        {task.dependsOn.length > 0
          ? `Waits for ${task.dependsOn.map((id) => nameByTaskId.get(id) ?? id).join(", ")}`
          : "Runs first"}
      </span>
    </div>
  );
}

export function DagView({ tasks, now }: { tasks: JobTaskDto[]; now: number }) {
  const levels = toDagLevels(tasks);
  const nameByTaskId = new Map(tasks.map((t) => [t.id, t.name]));

  const hasNote = (t: JobTaskDto) =>
    Boolean(t.errorMessage) || Boolean(t.nextAttemptAt && new Date(t.nextAttemptAt).getTime() > now);
  const levelOf = new Map<string, number>();
  const rowOf = new Map<string, number>();
  const rowCount = Math.max(1, ...levels.map((l) => l.tasks.length));
  // Each row is as tall as its tallest node, so nodes in a row align and a note never clips.
  const rowH = Array.from({ length: rowCount }, (_, row) =>
    levels.some((l) => l.tasks[row] && hasNote(l.tasks[row])) ? NODE_H_NOTE : NODE_H_BASE,
  );
  const rowY = rowH.map((_, row) =>
    HEADER_H + rowH.slice(0, row).reduce((sum, h) => sum + h + ROW_GAP, 0),
  );
  const heightOf = new Map<string, number>();
  const pos = new Map<string, { x: number; y: number }>();
  levels.forEach(({ level, tasks: levelTasks }) => {
    levelTasks.forEach((task, row) => {
      levelOf.set(task.id, level);
      rowOf.set(task.id, row);
      heightOf.set(task.id, rowH[row]);
      pos.set(task.id, { x: level * (BAND_W + COL_GAP) + COL_PAD, y: rowY[row] });
    });
  });

  const width = levels.length * BAND_W + Math.max(0, levels.length - 1) * COL_GAP;
  const height = HEADER_H + rowH.reduce((sum, h) => sum + h, 0) + (rowCount - 1) * ROW_GAP + COL_PAD;
  const byId = new Map(tasks.map((t) => [t.id, t]));

  const edges = tasks.flatMap((task) =>
    task.dependsOn.flatMap((depId) => {
      const from = pos.get(depId);
      const to = pos.get(task.id);
      const dep = byId.get(depId);
      if (!from || !to || !dep) return [];
      const x1 = from.x + NODE_W;
      const y1 = from.y + (heightOf.get(depId) ?? NODE_H_BASE) / 2;
      const x2 = to.x;
      const y2 = to.y + (heightOf.get(task.id) ?? NODE_H_BASE) / 2;
      const mid = (x1 + x2) / 2;
      return [
        {
          id: `${depId}-${task.id}`,
          x2,
          y2,
          d: `M${x1} ${y1} C${mid} ${y1} ${mid} ${y2} ${x2} ${y2}`,
          done: dep.state === "Succeeded",
          waiting: task.state === "Pending",
        },
      ];
    }),
  );

  return (
    <div className="overflow-x-auto pb-2">
      <p className="mb-2 text-xs text-[var(--muted)] md:hidden">
        {levels.length > 1 ? "Scroll sideways to follow the later stages →" : ""}
      </p>
      <div className="relative" style={{ width, height }}>
        {levels.map(({ level }) => (
          <div
            key={`band-${level}`}
            aria-hidden
            className="absolute rounded-[3px] bg-[var(--background)]"
            style={{ left: level * (BAND_W + COL_GAP), top: 0, width: BAND_W, height }}
          />
        ))}
        {levels.map(({ level }) => (
          <p
            key={level}
            className="absolute text-xs font-semibold text-[var(--muted)]"
            style={{ left: level * (BAND_W + COL_GAP) + COL_PAD, top: 16, width: NODE_W }}
          >
            {level === 0 ? "Stage 1 · runs first" : `Stage ${level + 1} · after stage ${level}`}
          </p>
        ))}
        <svg className="absolute inset-0" width={width} height={height} aria-hidden>
          {edges.map((e) => (
            <g key={e.id}>
              <path
                d={e.d}
                fill="none"
                stroke={e.done ? "var(--state-succeeded-solid)" : "var(--border-strong)"}
                strokeWidth={e.done ? 2 : 1.5}
                strokeDasharray={e.waiting && !e.done ? "4 4" : undefined}
              />
              <circle
                cx={e.x2}
                cy={e.y2}
                r={3}
                fill={e.done ? "var(--state-succeeded-solid)" : "var(--border-strong)"}
              />
            </g>
          ))}
        </svg>
        {tasks.map((task) => {
          const p = pos.get(task.id);
          if (!p) return null;
          return (
            <StepNode
              key={task.id}
              task={task}
              nameByTaskId={nameByTaskId}
              now={now}
              x={p.x}
              y={p.y}
              h={heightOf.get(task.id) ?? NODE_H_BASE}
              ticket={`${(levelOf.get(task.id) ?? 0) + 1}.${(rowOf.get(task.id) ?? 0) + 1}`}
            />
          );
        })}
      </div>
    </div>
  );
}
