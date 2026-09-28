const TONES = {
  info: "border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900",
  teach:
    "border-blue-200 bg-blue-50/60 dark:border-blue-900 dark:bg-blue-950/40",
  error:
    "border-red-300 bg-red-50 text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300",
} as const;

export function Callout({
  title,
  tone = "info",
  children,
}: {
  title?: string;
  tone?: keyof typeof TONES;
  children: React.ReactNode;
}) {
  return (
    <div className={`rounded-lg border p-4 text-sm ${TONES[tone]}`}>
      {title && <p className="mb-1.5 font-medium">{title}</p>}
      {children}
    </div>
  );
}
