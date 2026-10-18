/**
 * The tear line and notches between a pass and its stub. The pass is a size container, so the
 * line runs down the stub's left edge when they sit side by side and across its top when the
 * stub wraps underneath. `at` is the pass width (from its @container) where they sit side by side.
 */
export function Perforation({ tone, at }: { tone: "board" | "paper"; at: "pass" | "run" }) {
  const line = tone === "board" ? "var(--board-perf)" : "var(--hairline)";
  // Class names are spelled out in full so Tailwind can see them.
  const classes =
    at === "pass"
      ? {
          line: "inset-x-3.5 top-0 border-t-2 @min-[452px]:inset-x-auto @min-[452px]:inset-y-3.5 @min-[452px]:left-0 @min-[452px]:border-l-2 @min-[452px]:border-t-0",
          second: "-right-[9px] -top-[9px] @min-[452px]:-bottom-[9px] @min-[452px]:-left-[9px] @min-[452px]:right-auto @min-[452px]:top-auto",
        }
      : {
          line: "inset-x-4 top-0 border-t-2 @min-[820px]:inset-x-auto @min-[820px]:inset-y-4 @min-[820px]:left-0 @min-[820px]:border-l-2 @min-[820px]:border-t-0",
          second: "-right-[9px] -top-[9px] @min-[820px]:-bottom-[9px] @min-[820px]:-left-[9px] @min-[820px]:right-auto @min-[820px]:top-auto",
        };

  return (
    <span aria-hidden>
      <span className={`absolute border-dashed ${classes.line}`} style={{ borderColor: line }} />
      <span className="absolute -left-[9px] -top-[9px] size-[18px] rounded-full bg-concourse" />
      <span className={`absolute size-[18px] rounded-full bg-concourse ${classes.second}`} />
    </span>
  );
}
