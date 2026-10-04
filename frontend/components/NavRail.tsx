"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const NAV = [
  { href: "/", label: "Overview", match: (p: string) => p === "/" },
  { href: "/jobs", label: "Runs", match: (p: string) => p === "/jobs" || (p.startsWith("/jobs/") && p !== "/jobs/new") },
  { href: "/jobs/new", label: "New run", match: (p: string) => p === "/jobs/new" },
  { href: "/concepts", label: "Glossary", match: (p: string) => p.startsWith("/concepts") },
] as const;

// The five oxide colours steel passes through as it is tempered; doubles as the brand mark.
const TEMPER = ["#7f8a96", "#e6c25a", "#cf8f4e", "#a97ae0", "#5b8def"];

export function NavRail() {
  const pathname = usePathname();

  return (
    <aside className="border-b border-[var(--border)] bg-[var(--rail)] md:sticky md:top-0 md:flex md:h-screen md:flex-col md:border-b-0 md:border-r">
      <div className="flex items-center justify-between gap-4 px-5 py-3 md:block md:px-5 md:pb-5 md:pt-10">
        <Link href="/" className="block">
          <span className="block text-lg font-semibold leading-none tracking-tight">RelayForge</span>
          <span className="mt-2 flex h-1 w-24 overflow-hidden rounded-[1px]" aria-hidden>
            {TEMPER.map((c) => (
              <span key={c} className="flex-1" style={{ background: c }} />
            ))}
          </span>
        </Link>
        <p className="hidden pt-4 text-xs leading-snug text-[var(--muted)] md:block">
          A small job scheduler, built to be watched while it runs.
        </p>
      </div>

      <nav
        aria-label="Primary"
        className="flex gap-1 overflow-x-auto px-2 pb-3 md:flex-col md:pb-0"
      >
        {NAV.map((item) => {
          const active = item.match(pathname);
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-current={active ? "page" : undefined}
              className={`whitespace-nowrap rounded-[3px] px-3 py-2 text-sm font-medium transition-colors duration-150 ${
                active
                  ? "bg-[var(--surface-raised)] text-[var(--foreground)] ring-1 ring-inset ring-[var(--border-strong)]"
                  : "text-[var(--muted)] hover:bg-[var(--surface)] hover:text-[var(--foreground)]"
              }`}
            >
              {active && <span aria-hidden className="mr-2 inline-block size-1.5 bg-[var(--focus)]" />}
              {item.label}
            </Link>
          );
        })}
      </nav>

      <p className="mt-auto hidden px-5 pb-6 text-[13px] leading-snug text-[var(--muted)] md:block">
        Colour shows where a step is in its life: grey waiting, gold working, blue done.
      </p>
    </aside>
  );
}
