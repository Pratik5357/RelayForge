import { ViewTransition, type ReactNode } from "react";

/**
 * Route-level slide. Navigations tagged nav-forward / nav-back (see lib/motion navDirection)
 * slide the page toward where you're heading; untagged ones (browser back, refresh) don't.
 * The header is anchored separately with view-transition-name: site-header.
 */
export function PageTransition({ children, className = "page-enter" }: { children: ReactNode; className?: string }) {
  return (
    <ViewTransition
      enter={{ "nav-forward": "nav-forward", "nav-back": "nav-back", default: "none" }}
      exit={{ "nav-forward": "nav-forward", "nav-back": "nav-back", default: "none" }}
      default="none"
    >
      <div className={className}>{children}</div>
    </ViewTransition>
  );
}
