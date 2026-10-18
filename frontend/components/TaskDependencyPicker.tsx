type TaskOption = {
  key: string;
  name: string;
};

type TaskDependencyPickerProps = {
  options: TaskOption[];
  selected: string[];
  onChange: (next: string[]) => void;
};

export function TaskDependencyPicker({ options, selected, onChange }: TaskDependencyPickerProps) {
  if (options.length === 0) {
    return <p className="text-[15px] text-muted">Nothing yet. The first step starts straight away.</p>;
  }

  function toggle(key: string) {
    if (selected.includes(key)) {
      onChange(selected.filter((k) => k !== key));
    } else {
      onChange([...selected, key]);
    }
  }

  return (
    <div className="flex flex-wrap gap-2">
      {options.map((option) => (
        <label
          key={option.key}
          className="press flex min-h-10 cursor-pointer items-center gap-2 rounded px-3.5 text-sm font-bold text-ink shadow-[inset_0_0_0_1.5px_var(--ink)] has-checked:bg-ink has-checked:text-white has-focus-visible:outline-2 has-focus-visible:outline-offset-3 has-focus-visible:outline-ink"
        >
          <input
            type="checkbox"
            className="size-3.5 accent-[var(--amber)]"
            checked={selected.includes(option.key)}
            onChange={() => toggle(option.key)}
          />
          {option.name}
        </label>
      ))}
    </div>
  );
}
