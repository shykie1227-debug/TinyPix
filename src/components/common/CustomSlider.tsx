interface SliderMark {
  value: number;
  label: string;
}

interface CustomSliderProps {
  min: number;
  max: number;
  step?: number;
  value: number;
  onChange: (v: number) => void;
  marks?: SliderMark[];
  className?: string;
  disabled?: boolean;
  ariaLabel?: string;
}

export default function CustomSlider({
  min,
  max,
  step = 1,
  value,
  onChange,
  marks,
  className = '',
  disabled = false,
  ariaLabel,
}: CustomSliderProps) {
  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    onChange(Number(e.target.value));
  };

  return (
    <div className={`w-full ${className}`}>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={handleChange}
        disabled={disabled}
        aria-label={ariaLabel}
        className="slider-apple w-full"
      />
      {marks && marks.length > 0 && (
        <div className="slider-marks flex justify-between mt-2">
          {marks.map((mark) => (
            <span
              key={mark.value}
              className="text-[11px] text-on-surface-variant"
            >
              {mark.label}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
