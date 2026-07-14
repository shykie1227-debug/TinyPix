interface ChipButtonOption {
  label: string;
  value: string;
  colSpan?: number;
}

interface ChipButtonProps {
  options: ChipButtonOption[];
  value: string;
  onChange: (v: string) => void;
  className?: string;
  gridCols?: number;
}

export default function ChipButton({
  options,
  value,
  onChange,
  className = '',
  gridCols,
}: ChipButtonProps) {
  const containerClass = gridCols
    ? `grid grid-cols-${gridCols} gap-3 ${className}`
    : `flex gap-2 flex-wrap ${className}`;

  return (
    <div className={containerClass}>
      {options.map((option) => {
        const isSelected = option.value === value;
        const colSpanClass = option.colSpan ? `col-span-${option.colSpan}` : '';
        return (
          <button
            key={option.value}
            type="button"
            onClick={() => onChange(option.value)}
            className={`
              rounded-[18px] py-3 px-2 font-label-caps text-[11px]
              transition-opacity duration-150 cursor-pointer border-none
              ${colSpanClass}
              ${isSelected
                ? 'bg-primary text-on-primary font-bold'
                : 'bg-surface-container-low text-on-surface hover:bg-surface-container-high'
              }
            `}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
