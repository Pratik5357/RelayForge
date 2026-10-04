import type { HTMLAttributes } from "react";

type CalloutTone = "info" | "teach" | "error";

type CalloutProps = HTMLAttributes<HTMLDivElement> & {
  tone?: CalloutTone;
};

const toneClasses: Record<CalloutTone, string> = {
  info: "border-[var(--border)] bg-[var(--surface)]",
  teach:
    "border-[var(--state-running-ring)] bg-[var(--state-running-bg)] text-[var(--foreground)]",
  error:
    "border-[var(--state-failed-ring)] bg-[var(--state-failed-bg)] text-[var(--state-failed-fg)]",
};

export function Callout({ tone = "info", className = "", ...props }: CalloutProps) {
  return (
    <div
      className={`rounded-[4px] border p-4 text-sm ${toneClasses[tone]} ${className}`}
      {...props}
    />
  );
}
