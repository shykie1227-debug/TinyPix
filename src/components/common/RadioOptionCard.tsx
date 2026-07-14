interface RadioOption {
  label: string;
  desc?: string;
  value: string;
  badge?: string;
}

interface RadioOptionCardProps {
  options: RadioOption[];
  value: string;
  onChange: (v: string) => void;
  className?: string;
}

export default function RadioOptionCard({
  options,
  value,
  onChange,
  className = '',
}: RadioOptionCardProps) {
  return (
    <div className={`flex flex-col gap-2 ${className}`}>
      {options.map((option) => {
        const isSelected = option.value === value;
        return (
          <div
            key={option.value}
            role="radio"
            aria-checked={isSelected}
            tabIndex={0}
            onClick={() => onChange(option.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onChange(option.value);
              }
            }}
            className={`
              rounded-[18px] p-3 flex items-center justify-between
              transition-all duration-150 cursor-pointer
              ${isSelected
                ? 'border-2 border-secondary-fixed bg-secondary-container/10'
                : 'border border-outline-variant/20 hover:bg-surface-container-low'
              }
            `}
          >
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2">
                <span className="font-bold text-body-sm text-on-surface">
                  {option.label}
                </span>
                {option.badge && (
                  <span className="bg-secondary-fixed text-on-secondary-fixed text-[9px] px-1.5 py-0.5 rounded font-semibold">
                    {option.badge}
                  </span>
                )}
              </div>
              {option.desc && (
                <p className="text-[11px] text-on-surface-variant mt-0.5">
                  {option.desc}
                </p>
              )}
            </div>
            <input
              type="radio"
              value={option.value}
              checked={isSelected}
              onChange={() => onChange(option.value)}
              className="w-5 h-5 accent-primary ml-3 flex-shrink-0"
            />
          </div>
        );
      })}
    </div>
  );
}
