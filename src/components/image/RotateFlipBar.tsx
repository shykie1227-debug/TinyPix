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
    <div className="flex items-center gap-3 bg-surface-container-lowest rounded-[18px] p-4 border border-outline-variant/10">
      <div className="flex gap-2">
        <button
          type="button"
          onClick={() => onRotationChange((normalizedRotation + 270) % 360)}
          aria-label="左旋 90°"
          className="flex items-center gap-1.5 px-3 py-2 bg-surface-container-low rounded-[12px] hover:bg-surface-container-high transition-colors no-scale text-on-surface-variant"
          title="左旋 90°"
        >
          <RotateCcw size={18} />
          <span className="text-[11px] font-bold">左旋</span>
        </button>
        <button
          type="button"
          onClick={() => onRotationChange((normalizedRotation + 90) % 360)}
          aria-label="右旋 90°"
          className="flex items-center gap-1.5 px-3 py-2 bg-surface-container-low rounded-[12px] hover:bg-surface-container-high transition-colors no-scale text-on-surface-variant"
          title="右旋 90°"
        >
          <RotateCw size={18} />
          <span className="text-[11px] font-bold">右旋</span>
        </button>
        <button
          type="button"
          onClick={onFlipH}
          aria-label="水平镜像"
          className="flex items-center gap-1.5 px-3 py-2 bg-surface-container-low rounded-[12px] hover:bg-surface-container-high transition-colors no-scale text-on-surface-variant"
          title="水平镜像"
        >
          <FlipHorizontal size={18} />
          <span className="text-[11px] font-bold">水平</span>
        </button>
        <button
          type="button"
          onClick={onFlipV}
          aria-label="垂直镜像"
          className="flex items-center gap-1.5 px-3 py-2 bg-surface-container-low rounded-[12px] hover:bg-surface-container-high transition-colors no-scale text-on-surface-variant"
          title="垂直镜像"
        >
          <FlipVertical size={18} />
          <span className="text-[11px] font-bold">垂直</span>
        </button>
      </div>

      <div className="w-px h-8 bg-outline-variant/30" />

      <div className="flex-grow flex items-center gap-3 ml-4">
        <span className="text-[10px] font-bold text-on-surface-variant whitespace-nowrap">旋转</span>
        <div className="flex-grow">
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
