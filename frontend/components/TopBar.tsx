"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useSyncExternalStore } from "react";
import { motion, useReducedMotion } from "motion/react";
import { EASE_IN_OUT, navDirection } from "@/lib/motion";

const NAV = [
  { href: "/", label: "Overview", match: (p: string) => p === "/" },
  { href: "/jobs", label: "Runs", match: (p: string) => p === "/jobs" || (p.startsWith("/jobs/") && p !== "/jobs/new") },
  { href: "/jobs/new", label: "New run", match: (p: string) => p === "/jobs/new" },
  { href: "/concepts", label: "Glossary", match: (p: string) => p.startsWith("/concepts") },
] as const;

function formatClock(date: Date) {
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false });
}

/** Wall clock in the bar, the way a departures board shows the time. Rendered after mount only. */
function subscribeToSeconds(onTick: () => void) {
  const interval = setInterval(onTick, 1000);
  return () => clearInterval(interval);
}

function Clock() {
  const label = useSyncExternalStore(
    subscribeToSeconds,
    () => formatClock(new Date()),
    () => "--:--:--",
  );

  return (
    <span className="hidden items-baseline gap-2 sm:flex">
      <span className="text-xs text-board-muted">Local time</span>
      <span className="tnum min-w-[8ch] font-mono text-[15px]">{label}</span>
    </span>
  );
}

/** The mark: three board slats and the amber lamp. */
export function Mark() {
  return (
    <svg width="26" height="22" viewBox="0 0 26 22" aria-hidden>
      <rect width="26" height="6" rx="1.5" fill="currentColor" />
      <rect y="8" width="18" height="6" rx="1.5" fill="currentColor" fillOpacity=".55" />
      <rect y="16" width="22" height="6" rx="1.5" fill="currentColor" fillOpacity=".3" />
      <circle cx="23" cy="11" r="3" fill="var(--amber)" />
    </svg>
  );
}

export function TopBar() {
  const pathname = usePathname();
  const reduce = useReducedMotion();

  return (
    <header className="on-board bg-board text-board-text" style={{ viewTransitionName: "site-header" }}>
      <div className="mx-auto flex min-h-16 max-w-[1320px] flex-wrap items-center gap-x-9 gap-y-1 px-4 sm:px-6 lg:px-10">
        <Link
          href="/"
          transitionTypes={navDirection(pathname, "/")}
          className="flex min-h-14 items-center gap-2.5 text-board-text no-underline sm:min-h-16"
        >
          <Mark />
          <span className="text-xl font-extrabold tracking-[-0.02em]">RelayForge</span>
        </Link>

        <nav
          aria-label="Primary"
          className="-mx-4 flex basis-full gap-x-6 overflow-x-auto px-4 sm:mx-0 sm:flex-1 sm:basis-auto sm:overflow-visible sm:px-0"
        >
          {NAV.map((item) => {
            const active = item.match(pathname);
            return (
              <Link
                key={item.href}
                href={item.href}
                transitionTypes={navDirection(pathname, item.href)}
                aria-current={active ? "page" : undefined}
                className={`relative flex min-h-12 shrink-0 items-center whitespace-nowrap text-[15px] no-underline transition-colors duration-150 sm:min-h-16 ${
                  active ? "font-bold text-white" : "font-semibold text-board-muted hover:text-white"
                }`}
              >
                {item.label}
                {active && (
                  <motion.span
                    layoutId="nav-underline"
                    aria-hidden
                    className="absolute inset-x-0 bottom-0 h-[3px] bg-amber"
                    transition={reduce ? { duration: 0 } : { duration: 0.3, ease: EASE_IN_OUT }}
                  />
                )}
              </Link>
            );
          })}
        </nav>

        <Clock />
      </div>
    </header>
  );
}
