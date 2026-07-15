import { RotateCcw, RotateCw, FlipHorizontal, FlipVertical } from 'lucide-react';
import CustomSlider from '../common/CustomSlider';

interface RotateFlipBarProps {
  rotation: number;
  onRotationChange: (deg: number) => void;
  onFlipH: () => void;
  onFlipV: () => void;
}

const normalizeRotation = (value: number) => ((value % 360) + 360) % 360;

export default function RotateFlipBar({
  rotation,
  onRotationChange,
  onFlipH,
  onFlipV,
}: RotateFlipBarProps) {
  const normalizedRotation = normalizeRotation(rotation);

  return (
    <div className="flex min-w-0 flex-wrap items-center gap-3 rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest p-3">
      <div className="grid w-full grid-cols-2 gap-2 min-[310px]:grid-cols-4">
        <button
          type="button"
          onClick={() => onRotationChange((normalizedRotation + 270) % 360)}
          aria-label="左旋 90°"
          className="flex min-h-10 min-w-0 items-center justify-center gap-1 rounded-[12px] bg-surface-container-low px-2 text-on-surface-variant transition-colors hover:bg-surface-container-high no-scale"
          title="左旋 90°"
        >
          <RotateCcw size={18} />
          <span className="text-[11px] font-bold">左旋</span>
        </button>
        <button
          type="button"
          onClick={() => onRotationChange((normalizedRotation + 90) % 360)}
          aria-label="右旋 90°"
          className="flex min-h-10 min-w-0 items-center justify-center gap-1 rounded-[12px] bg-surface-container-low px-2 text-on-surface-variant transition-colors hover:bg-surface-container-high no-scale"
          title="右旋 90°"
        >
          <RotateCw size={18} />
          <span className="text-[11px] font-bold">右旋</span>
        </button>
        <button
          type="button"
          onClick={onFlipH}
          aria-label="水平镜像"
          className="flex min-h-10 min-w-0 items-center justify-center gap-1 rounded-[12px] bg-surface-container-low px-2 text-on-surface-variant transition-colors hover:bg-surface-container-high no-scale"
          title="水平镜像"
        >
          <FlipHorizontal size={18} />
          <span className="text-[11px] font-bold">水平</span>
        </button>
        <button
          type="button"
          onClick={onFlipV}
          aria-label="垂直镜像"
          className="flex min-h-10 min-w-0 items-center justify-center gap-1 rounded-[12px] bg-surface-container-low px-2 text-on-surface-variant transition-colors hover:bg-surface-container-high no-scale"
          title="垂直镜像"
        >
          <FlipVertical size={18} />
          <span className="text-[11px] font-bold">垂直</span>
        </button>
      </div>

      <div className="flex min-w-0 w-full items-center gap-3">
        <span className="text-[10px] font-bold text-on-surface-variant whitespace-nowrap">旋转</span>
        <div className="min-w-0 flex-grow">
          <CustomSlider
            min={0}
            max={270}
            step={90}
            value={normalizedRotation}
            onChange={onRotationChange}
            ariaLabel="旋转角度"
          />
        </div>
        <span className="text-[10px] font-mono-status text-on-surface-variant w-8 text-right">
          {normalizedRotation}°
        </span>
      </div>
    </div>
  );
}
