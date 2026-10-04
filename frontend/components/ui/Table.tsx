import type { HTMLAttributes, TdHTMLAttributes, ThHTMLAttributes } from "react";

export function Table(props: HTMLAttributes<HTMLTableElement>) {
  return (
    <div className="overflow-x-auto rounded-[4px] border border-[var(--border)] bg-[var(--surface)]">
      <table className="w-full min-w-full divide-y divide-[var(--border)] text-sm" {...props} />
    </div>
  );
}

export function Thead(props: HTMLAttributes<HTMLTableSectionElement>) {
  return <thead className="bg-[var(--surface-raised)]" {...props} />;
}

export function Tbody(props: HTMLAttributes<HTMLTableSectionElement>) {
  return <tbody className="divide-y divide-[var(--border)]" {...props} />;
}

export function Th({ className = "", ...props }: ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      scope="col"
      className={`px-4 py-2.5 text-left text-xs font-semibold text-[var(--muted)] ${className}`}
      {...props}
    />
  );
}

export function Td({ className = "", ...props }: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={`px-4 py-2.5 align-middle ${className}`} {...props} />;
}
