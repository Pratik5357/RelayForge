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
    return <p className="text-xs text-[var(--muted)]">No earlier steps to wait for yet.</p>;
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
          className="flex cursor-pointer items-center gap-2 rounded-[3px] border border-[var(--border-strong)] px-2.5 py-1.5 text-xs transition-colors duration-150 hover:border-[var(--muted)] has-checked:border-[var(--focus)] has-checked:bg-[var(--state-running-bg)] has-focus-visible:outline has-focus-visible:outline-2 has-focus-visible:outline-[var(--focus)]"
        >
          <input
            type="checkbox"
            className="size-3.5 accent-[#e6c25a]"
            checked={selected.includes(option.key)}
            onChange={() => toggle(option.key)}
          />
          {option.name} <span className="font-mono text-[var(--muted)]">{option.key}</span>
        </label>
      ))}
    </div>
  );
}
