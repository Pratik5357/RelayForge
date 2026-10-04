import { type InputHTMLAttributes, forwardRef } from "react";

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function Input({ className = "", ...props }, ref) {
    return (
      <input
        ref={ref}
        className={`w-full rounded-[3px] border border-[var(--border-strong)] bg-[var(--background)] h-9 px-3 text-sm text-[var(--foreground)] transition-colors duration-150 hover:border-[var(--muted)] focus-visible:border-[var(--focus)] focus-visible:outline-offset-0 ${className}`}
        {...props}
      />
    );
  },
);

export function FieldLabel({ children, htmlFor }: { children: React.ReactNode; htmlFor?: string }) {
  return (
    <label htmlFor={htmlFor} className="mb-1 block text-xs font-semibold text-[var(--muted)]">
      {children}
    </label>
  );
}
