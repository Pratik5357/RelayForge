type BadgeProps = {
  bg: string;
  fg: string;
  ring: string;
  size?: "md" | "lg";
  children: React.ReactNode;
};

export function Badge({ bg, fg, ring, size = "md", children }: BadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-[3px] font-semibold ${size === "lg" ? "px-3 py-1 text-sm" : "px-2 py-0.5 text-xs"}`}
      style={{ background: bg, color: fg, boxShadow: `inset 0 0 0 1px ${ring}` }}
    >
      {children}
    </span>
  );
}
