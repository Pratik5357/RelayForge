import type { InputHTMLAttributes, LabelHTMLAttributes } from "react";

export function FieldLabel({ className = "", ...props }: LabelHTMLAttributes<HTMLLabelElement>) {
  return <label className={`label block text-label ${className}`} {...props} />;
}

export function Input({ className = "", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={`h-12 w-full rounded-[4px] border-0 bg-field px-3 text-[17px] text-ink shadow-[inset_0_-2px_0_var(--field-rule)] transition-[box-shadow,background-color] duration-150 hover:shadow-[inset_0_-2px_0_var(--muted)] focus:bg-[var(--field-focus)] focus:shadow-[inset_0_-3px_0_var(--ink)] focus:outline-none ${className}`}
      {...props}
    />
  );
}
