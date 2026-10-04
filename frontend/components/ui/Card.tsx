import type { HTMLAttributes } from "react";

type CardProps = HTMLAttributes<HTMLDivElement>;

export function Card({ className = "", ...props }: CardProps) {
  return (
    <div
      className={`rounded-[4px] border border-[var(--border)] bg-[var(--surface)] p-4 ${className}`}
      {...props}
    />
  );
}
