interface SegmentedControlOption {
  label: string;
  value: string;
}

interface SegmentedControlProps {
  options: SegmentedControlOption[];
  value: string;
  onChange: (v: string) => void;
  className?: string;
}

export default function SegmentedControl({
  options,
  value,
  onChange,
  className = '',
}: SegmentedControlProps) {
  return (
    <div className={`inline-flex w-full bg-black/4 rounded-[9px] p-[2px] ${className}`}>
      {options.map((option) => {
        const isActive = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="tab"
            aria-selected={isActive}
            onClick={() => onChange(option.value)}
            className={`flex-1 py-1.5 px-2 text-[11px] font-bold rounded-[7px] transition-opacity cursor-pointer border-none ${
              isActive
                ? 'bg-white text-on-surface shadow-[0_1px_3px_rgba(0,0,0,0.08),0_1px_2px_rgba(0,0,0,0.06)]'
                : 'bg-transparent text-on-surface-variant hover:opacity-80'
            }`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
