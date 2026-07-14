import * as Slider from '@radix-ui/react-slider';

interface SliderControlProps {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  onChange: (v: number) => void;
  format: (v: number) => string;
  ariaLabel?: string;
}

/**
 * 模块8：参数滑块模块（SliderControlModule）
 * 基于 Radix UI Slider（@radix-ui/react-slider v1.4.0，5.62 kB）
 * 替代自研 input[type=range]，提供完整的键盘/触摸/屏幕阅读器支持
 */
export default function SliderControl({
  label,
  value,
  min,
  max,
  step,
  onChange,
  format,
  ariaLabel,
}: SliderControlProps) {
  return (
    <div className="w-full">
      <div className="flex items-center justify-between mb-2">
        <span className="text-body-sm text-on-surface-variant">{label}</span>
        <span className="text-[12px] font-mono-status text-primary font-bold">
          {format(value)}
        </span>
      </div>
      <Slider.Root
        className="relative flex items-center select-none touch-none w-full h-5"
        value={[value]}
        min={min}
        max={max}
        step={step}
        onValueChange={(values) => {
          if (values.length > 0) onChange(values[0]);
        }}
        aria-label={ariaLabel || label}
      >
        <Slider.Track className="bg-surface-container-high relative grow rounded-full h-1.5">
          <Slider.Range className="absolute bg-primary rounded-full h-full" />
        </Slider.Track>
        <Slider.Thumb
          className="block w-4 h-4 bg-primary rounded-full shadow-md transition-opacity hover:opacity-80 active:opacity-70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary-fixed focus-visible:ring-offset-2"
          aria-label={ariaLabel || label}
        />
      </Slider.Root>
      <div className="flex justify-between mt-1">
        <span className="text-[10px] text-on-surface-variant/40">{format(min)}</span>
        <span className="text-[10px] text-on-surface-variant/40">{format(max)}</span>
      </div>
    </div>
  );
}
