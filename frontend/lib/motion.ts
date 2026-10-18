/** Shared motion tokens. CSS mirrors these as --ease-out / --ease-in-out / --ease-drawer. */
export const EASE_OUT = [0.23, 1, 0.32, 1] as const;
export const EASE_IN_OUT = [0.77, 0, 0.175, 1] as const;
export const EASE_DRAWER = [0.32, 0.72, 0, 1] as const;

/** Board rows slide to their new rank instead of vanishing and reappearing. */
export const RERANK = { type: "spring", duration: 0.45, bounce: 0 } as const;

/** Position of a route in the nav, used to pick a slide direction for page transitions. */
export function routeIndex(pathname: string): number {
  if (pathname === "/") return 0;
  if (pathname === "/jobs/new") return 2;
  if (pathname.startsWith("/jobs")) return 1;
  if (pathname.startsWith("/concepts")) return 3;
  return 0;
}

/** Transition type for a navigation from `from` to `to`, used by <Link transitionTypes>. */
export function navDirection(from: string, to: string): string[] {
  const a = routeIndex(from);
  const b = routeIndex(to);
  if (a === b) return [];
  return [b > a ? "nav-forward" : "nav-back"];
}
