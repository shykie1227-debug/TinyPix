import type { ReactNode } from 'react';
import { ChevronDown, Download, Loader2 } from 'lucide-react';
import { useAppStore, type ColorAdjust, type OutputFormat } from '../../stores/appStore';
import { IMAGE_OUTPUT_FORMATS } from '../../utils/imageCapabilities';
import { isImageFormat } from '../../utils/mediaFormat';
import { useImageProcessor } from '../../hooks/useImageProcessor';
import RotateFlipBar from './RotateFlipBar';

const RangeControl = ({
  label,
  value,
  min,
  max,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  onChange: (value: number) => void;
}) => (
  <label className="grid grid-cols-[52px_1fr_34px] items-center gap-2 text-xs text-on-surface-variant">
    <span>{label}</span>
    <input
      type="range"
      aria-label={label}
      min={min}
      max={max}
      value={value}
      onChange={(event) => onChange(Number(event.target.value))}
      className="custom-slider min-w-0"
    />
    <span className="text-right font-mono-status text-[10px]">{value}</span>
  </label>
);

const Section = ({ title, children, open = false }: { title: string; children: ReactNode; open?: boolean }) => (
  <details open={open} className="group rounded-[14px] border border-outline-variant/15 bg-surface-container-lowest">
    <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between px-4 text-sm font-semibold text-on-surface focus-visible:ring-2 focus-visible:ring-[#0071e3]/30">
      {title}
      <ChevronDown aria-hidden="true" size={16} className="text-on-surface-variant transition-transform group-open:rotate-180" />
    </summary>
    <div className="space-y-4 border-t border-outline-variant/10 px-4 py-4">{children}</div>
  </details>
);

export default function ImageControlsPanel() {
  const { files, options, setOptions, isProcessing, progress } = useAppStore();
  const { startProcess, cancelProcess } = useImageProcessor();
  const imageFiles = files.filter((file) => file.status === 'pending' && isImageFormat(file.format));
  const color = options.colorAdjust ?? { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 };
  const exactSize = options.resizeTargetW !== undefined || options.resizeTargetH !== undefined;

  const setColor = (key: keyof ColorAdjust, value: number) => {
    setOptions({ colorAdjust: { ...color, [key]: value } });
  };

  const handleProcess = () => {
    void startProcess({
      ...options,
      outputFormat: options.outputFormat,
      resizeEnabled: exactSize ? false : options.resizeEnabled,
      preserveTransparency: options.preserveTransparency,
      opacityPercent: options.opacityPercent,
      resizeTargetW: options.resizeTargetW,
      resizeTargetH: options.resizeTargetH,
      colorAdjust: color,
      flipH: options.flipH,
      flipV: options.flipV,
    });
  };

  return (
    <div className="flex min-w-0 flex-col gap-3" aria-label="图片处理参数">
      <section className="rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest p-4 shadow-[0_10px_30px_rgba(0,0,0,0.04)]">
        <h3 className="mb-3 text-sm font-semibold text-on-surface">格式与质量</h3>
        <div className="grid grid-cols-5 gap-1.5" role="radiogroup" aria-label="图片输出格式">
          {IMAGE_OUTPUT_FORMATS.map((format) => (
            <button
              type="button"
              role="radio"
              aria-checked={options.outputFormat === format.value}
              key={format.value}
              onClick={() => setOptions({
                outputFormat: format.value as OutputFormat,
                preserveTransparency: format.supportsTransparency ? options.preserveTransparency : false,
              })}
              className={`min-h-10 min-w-0 rounded-lg px-1 text-[11px] font-bold ${
                options.outputFormat === format.value
                  ? 'bg-primary text-on-primary'
                  : 'bg-surface-container-low text-on-surface hover:bg-surface-container-high'
              }`}
            >
              {format.label}
            </button>
          ))}
        </div>
        <label className="mt-4 block text-xs text-on-surface-variant">
          <span className="mb-2 flex justify-between"><span>质量</span><span>{options.quality}%</span></span>
          <input
            aria-label="输出质量"
            type="range"
            min={1}
            max={100}
            value={options.quality}
            onChange={(event) => setOptions({ quality: Number(event.target.value) })}
            className="custom-slider w-full"
          />
        </label>
      </section>

      <Section title="裁切与方向" open>
        <p className="text-xs leading-5 text-on-surface-variant">在左侧预览中拖动裁切框；旋转和镜像会同步显示并写入成品。</p>
        <RotateFlipBar
          rotation={options.rotateDegrees}
          onRotationChange={(degrees) => setOptions({ rotateDegrees: degrees as 0 | 90 | 180 | 270 })}
          onFlipH={() => setOptions({ flipH: !options.flipH })}
          onFlipV={() => setOptions({ flipV: !options.flipV })}
        />
      </Section>

      <Section title="尺寸">
        <div className="grid grid-cols-2 gap-3">
          <label className="text-xs text-on-surface-variant">
            <span className="mb-1 block">精确宽度</span>
            <input
              aria-label="精确宽度"
              type="number"
              min={1}
              max={16384}
              value={options.resizeTargetW ?? ''}
              onChange={(event) => setOptions({ resizeTargetW: event.target.value ? Number(event.target.value) : undefined })}
              className="min-h-10 w-full rounded-lg border border-outline-variant/20 bg-surface-container-low px-3 text-sm text-on-surface"
              placeholder="自动"
            />
          </label>
          <label className="text-xs text-on-surface-variant">
            <span className="mb-1 block">精确高度</span>
            <input
              aria-label="精确高度"
              type="number"
              min={1}
              max={16384}
              value={options.resizeTargetH ?? ''}
              onChange={(event) => setOptions({ resizeTargetH: event.target.value ? Number(event.target.value) : undefined })}
              className="min-h-10 w-full rounded-lg border border-outline-variant/20 bg-surface-container-low px-3 text-sm text-on-surface"
              placeholder="自动"
            />
          </label>
        </div>
        {!exactSize && (
          <label className="block text-xs text-on-surface-variant">
            <span className="mb-1 block">最长边像素</span>
            <input
              aria-label="最长边像素"
              type="number"
              min={1}
              max={16384}
              value={options.resizeMaxPx}
              onChange={(event) => setOptions({ resizeEnabled: true, resizeMaxPx: Number(event.target.value) })}
              className="min-h-10 w-full rounded-lg border border-outline-variant/20 bg-surface-container-low px-3 text-sm text-on-surface"
            />
          </label>
        )}
        <button type="button" onClick={() => setOptions({ resizeTargetW: undefined, resizeTargetH: undefined })} className="text-xs font-semibold text-on-surface underline underline-offset-2">
          恢复等比最长边
        </button>
      </Section>

      <Section title="色彩调整">
        <RangeControl label="亮度" value={color.brightness} min={-100} max={100} onChange={(value) => setColor('brightness', value)} />
        <RangeControl label="对比度" value={color.contrast} min={-100} max={100} onChange={(value) => setColor('contrast', value)} />
        <RangeControl label="饱和度" value={color.saturation} min={-100} max={100} onChange={(value) => setColor('saturation', value)} />
        <RangeControl label="锐化" value={color.sharpness} min={0} max={100} onChange={(value) => setColor('sharpness', value)} />
      </Section>

      <Section title="隐私与透明度">
        <RangeControl label="透明度" value={options.opacityPercent} min={0} max={100} onChange={(value) => setOptions({ opacityPercent: value })} />
        <label className="flex min-h-10 items-center gap-3 text-xs text-on-surface">
          <input type="checkbox" checked={options.stripExif} onChange={() => setOptions({ stripExif: !options.stripExif })} />
          清除 EXIF、GPS 和设备信息
        </label>
        <label className={`flex min-h-10 items-center gap-3 text-xs text-on-surface ${['jpeg', 'bmp'].includes(options.outputFormat) ? 'opacity-50' : ''}`}>
          <input
            type="checkbox"
            checked={options.preserveTransparency}
            disabled={['jpeg', 'bmp'].includes(options.outputFormat)}
            onChange={() => setOptions({ preserveTransparency: !options.preserveTransparency })}
          />
          保留透明通道
        </label>
      </Section>

      {isProcessing && (
        <div className="rounded-xl bg-surface-container-low p-3" role="status" aria-live="polite">
          <div className="h-1.5 overflow-hidden rounded-full bg-surface-container-highest">
            <div className="h-full rounded-full bg-secondary-fixed transition-[width]" style={{ width: `${progress}%` }} />
          </div>
          <p className="mt-2 text-center text-xs text-on-surface-variant">处理中 {progress}%</p>
        </div>
      )}

      <button
        type="button"
        onClick={isProcessing ? () => void cancelProcess() : handleProcess}
        disabled={!isProcessing && imageFiles.length === 0}
        className="flex min-h-11 w-full items-center justify-center gap-2 rounded-full bg-primary px-5 text-sm font-semibold text-on-primary transition-opacity hover:opacity-80 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {isProcessing ? <Loader2 size={18} className="animate-spin" /> : <Download size={18} />}
        {isProcessing ? '取消处理' : '开始处理'}
      </button>
    </div>
  );
}
