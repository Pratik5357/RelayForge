import type { ButtonHTMLAttributes } from "react";

type Variant = "primary" | "secondary" | "go" | "board";
type Size = "sm" | "md" | "lg";

const VARIANT: Record<Variant, string> = {
  primary: "bg-ink text-white hover:bg-[var(--ink-hover)]",
  secondary: "bg-transparent text-ink shadow-[inset_0_0_0_1.5px_var(--ink)] hover:bg-ink hover:text-white",
  go: "bg-amber text-ink font-extrabold hover:bg-[var(--amber-hover)]",
  board: "bg-board-control text-board-text hover:bg-amber hover:text-ink",
};

const SIZE: Record<Size, string> = {
  sm: "min-h-11 px-4 text-sm",
  md: "min-h-12 px-[22px] text-base",
  lg: "min-h-[54px] px-6 text-[17px]",
};

export function buttonClasses(variant: Variant = "primary", size: Size = "md") {
  return `press inline-flex items-center justify-center gap-2.5 rounded-md font-bold no-underline disabled:cursor-not-allowed disabled:opacity-50 ${VARIANT[variant]} ${SIZE[size]}`;
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: Variant;
  size?: Size;
};

export function Button({ variant = "primary", size = "md", className = "", type = "button", ...props }: ButtonProps) {
  return <button type={type} className={`${buttonClasses(variant, size)} ${className}`} {...props} />;
}

export function Arrow({ direction = "right" }: { direction?: "right" | "left" | "down" }) {
  const d =
    direction === "down"
      ? "M8 3v10M4 9l4 4 4-4"
      : direction === "left"
        ? "M13 8H3M7 4 3 8l4 4"
        : "M3 8h10M9 4l4 4-4 4";
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden>
      <path d={d} fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
