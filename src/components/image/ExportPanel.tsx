import { useState, useEffect, useCallback, useMemo } from 'react';
import { useAppStore, type OutputFormat } from '../../stores/appStore';
import { isImageFormat } from '../../utils/mediaFormat';
import { formatBytes } from '../../utils/formatBytes';
import { Loader2, Download } from 'lucide-react';
import SegmentedControl from '../common/SegmentedControl';
import CustomSlider from '../common/CustomSlider';

interface ExportPanelProps {
  onProcess: () => void;
  estimateSizeBatch: (
    items: Array<{ path: string; format: string; quality: number }>
  ) => Promise<number>;
}

const FORMAT_OPTIONS = [
  { label: 'JPG', value: 'jpeg' },
  { label: 'PNG', value: 'png' },
  { label: 'WebP', value: 'webp' },
  { label: 'AVIF', value: 'avif' },
  { label: 'BMP', value: 'bmp' },
] as const;

type UiFormat = (typeof FORMAT_OPTIONS)[number]['value'];

const QUALITY_PRESETS = [
  { label: '轻度', value: 60 },
  { label: '标准', value: 85 },
  { label: '无损', value: 100 },
];

const uiFormatToOutputFormat = (ui: UiFormat): OutputFormat => {
  if (ui === 'bmp') return 'png';
  return ui;
};

const TRANSPARENCY_SUPPORTED_FORMATS = new Set(['png', 'webp', 'avif']);

export default function ExportPanel({ onProcess, estimateSizeBatch }: ExportPanelProps) {
  const { files, options, setOptions, isProcessing, progress } = useAppStore();
  const [estimatedSize, setEstimatedSize] = useState<number | null>(null);
  const [selectedUiFormat, setSelectedUiFormat] = useState<UiFormat>('jpeg');

  const imageFiles = useMemo(
    () => files.filter((f) => isImageFormat(f.format)),
    [files]
  );

  const firstFile = imageFiles[0];
  const pendingCount = imageFiles.filter((f) => f.status === 'pending').length;

  const transparencySupported = TRANSPARENCY_SUPPORTED_FORMATS.has(selectedUiFormat);

  const handleFormatChange = useCallback(
    (format: string) => {
      const uiFormat = format as UiFormat;
      setSelectedUiFormat(uiFormat);
      setOptions({ outputFormat: uiFormatToOutputFormat(uiFormat) });
    },
    [setOptions]
  );

  const handleQualityChange = useCallback(
    (quality: number) => {
      setOptions({ quality });
    },
    [setOptions]
  );

  const handleToggleStripExif = useCallback(() => {
    setOptions({ stripExif: !options.stripExif });
  }, [options.stripExif, setOptions]);

  const handleTogglePreserveTransparency = useCallback(() => {
    if (!transparencySupported) return;
    setOptions({ preserveTransparency: !options.preserveTransparency });
  }, [options.preserveTransparency, setOptions, transparencySupported]);

  const updateEstimate = useCallback(async () => {
    if (imageFiles.length === 0) {
      setEstimatedSize(null);
      return;
    }
    try {
      const total = await estimateSizeBatch(
        imageFiles.map((f) => ({
          path: f.path,
          format: options.outputFormat,
          quality: options.quality,
        }))
      );
      setEstimatedSize(total);
    } catch {
      setEstimatedSize(null);
    }
  }, [imageFiles, options.outputFormat, options.quality, estimateSizeBatch]);

  useEffect(() => {
    updateEstimate();
  }, [updateEstimate]);

  const isDisabled = pendingCount === 0 || isProcessing;

  const activePreset = QUALITY_PRESETS.find((p) => p.value === options.quality)?.value ?? null;

  return (
    <div className="flex flex-col gap-10">
      <div className="space-y-4">
        <label className="font-label-caps text-label-caps uppercase text-primary/40 block">
          输入文件
        </label>
        <div className="bg-surface-container-lowest rounded-[14px] p-4 border border-outline-variant/10 space-y-3">
          {imageFiles.length > 0 ? (
            <>
              <div className="flex justify-between items-center">
                <span className="text-[11px] text-on-surface-variant">文件名</span>
                <span className="text-[11px] font-bold text-primary truncate ml-4 max-w-[60%]">
                  {firstFile.name}
                </span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-[11px] text-on-surface-variant">格式</span>
                <span className="format-badge bg-secondary-fixed/15 text-on-secondary-fixed-variant">
                  {firstFile.format.toUpperCase()}
                </span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-[11px] text-on-surface-variant">大小</span>
                <span className="text-[11px] font-mono-status text-on-surface-variant">
                  {formatBytes(firstFile.originalSize)}
                </span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-[11px] text-on-surface-variant">尺寸</span>
                <span className="text-[11px] font-mono-status text-on-surface-variant">
                  —
                </span>
              </div>
              {imageFiles.length > 1 && (
                <div className="pt-2 border-t border-outline-variant/10 text-center">
                  <span className="text-[10px] text-on-surface-variant">
                    等 {imageFiles.length} 个文件
                  </span>
                </div>
              )}
            </>
          ) : (
            <div className="py-4 text-center">
              <span className="text-[11px] text-on-surface-variant opacity-60">
                暂无文件
              </span>
            </div>
          )}
        </div>
      </div>

      <div className="space-y-4">
        <label className="font-label-caps text-label-caps uppercase text-primary/40 block">
          输出格式
        </label>
        <SegmentedControl
          options={FORMAT_OPTIONS.map((o) => ({ label: o.label, value: o.value }))}
          value={selectedUiFormat}
          onChange={handleFormatChange}
        />
        <div className="flex items-center justify-between bg-surface-container-lowest rounded-[12px] px-4 py-3 border border-outline-variant/10">
          <span className="text-[11px] text-on-surface-variant">预计输出大小</span>
          <span className="text-[11px] font-bold text-secondary">
            {estimatedSize !== null && imageFiles.length > 0
              ? `~${formatBytes(estimatedSize)}`
              : '—'}
          </span>
        </div>
      </div>

      <div className="space-y-4">
        <div className="flex justify-between items-center">
          <label className="font-label-caps text-label-caps uppercase text-primary/40">
            输出质量
          </label>
          <span className="text-[11px] font-mono-status bg-secondary-fixed/20 px-2 py-0.5 rounded text-on-secondary-fixed-variant">
            {options.quality}%
          </span>
        </div>
        <CustomSlider
          min={1}
          max={100}
          step={1}
          value={options.quality}
          onChange={handleQualityChange}
        />
        <div className="grid grid-cols-3 gap-2">
          {QUALITY_PRESETS.map((preset) => (
            <button
              key={preset.value}
              type="button"
              role="button"
              onClick={() => handleQualityChange(preset.value)}
              className={`py-2 text-[11px] font-bold rounded-[12px] transition-all cursor-pointer no-scale ${
                activePreset === preset.value
                  ? 'bg-primary text-on-primary'
                  : 'border border-outline-variant/30 hover:bg-surface-container-low text-on-surface'
              }`}
            >
              {preset.label}
            </button>
          ))}
        </div>
      </div>

      <div className="space-y-4">
        <label className="font-label-caps text-label-caps uppercase text-primary/40 block">
          导出选项
        </label>
        <div className="space-y-2">
          <label
            htmlFor="preserve-transparency"
            className={`flex items-center gap-3 bg-surface-container-lowest rounded-[12px] px-4 py-3 border border-outline-variant/10 cursor-pointer transition-colors no-scale ${
              transparencySupported
                ? 'hover:bg-surface-container-low'
                : 'opacity-50 cursor-not-allowed'
            }`}
          >
            <input
              id="preserve-transparency"
              type="checkbox"
              checked={options.preserveTransparency}
              onChange={handleTogglePreserveTransparency}
              disabled={!transparencySupported}
              className="w-4 h-4 rounded accent-black cursor-pointer"
            />
            <span className="text-[12px] text-on-surface">保留透明通道</span>
          </label>
          <label
            htmlFor="strip-exif"
            className="flex items-center gap-3 bg-surface-container-lowest rounded-[12px] px-4 py-3 border border-outline-variant/10 cursor-pointer hover:bg-surface-container-low transition-colors no-scale"
          >
            <input
              id="strip-exif"
              type="checkbox"
              aria-label="清除 EXIF 信息"
              checked={options.stripExif}
              onChange={handleToggleStripExif}
              className="w-4 h-4 rounded accent-black cursor-pointer"
            />
            <span className="text-[12px] text-on-surface">清除 EXIF 信息</span>
            <span className="text-[10px] text-on-surface-variant opacity-60 ml-auto">
              移除设备、GPS、时间等元数据
            </span>
          </label>
        </div>
      </div>

      <div className="pt-4">
        {isProcessing && (
          <div className="mb-3">
            <div className="w-full bg-surface-container-high rounded-full h-1.5">
              <div
                className="bg-secondary-fixed h-full rounded-full transition-all duration-300"
                style={{ width: `${progress}%` }}
              />
            </div>
            <p className="text-on-surface-variant text-center text-[11px] mt-1.5">
              处理中... {progress}%
            </p>
          </div>
        )}
        <button
          type="button"
          onClick={onProcess}
          disabled={isDisabled}
          className={`btn-apple btn-apple-primary rounded-[980px] w-full py-3.5 font-semibold text-body-sm flex items-center justify-center gap-2 no-scale ${
            isDisabled ? 'opacity-50 cursor-not-allowed' : ''
          }`}
        >
          {isProcessing ? (
            <>
              <Loader2 size={18} className="animate-spin" />
              处理中...
            </>
          ) : (
            <>
              <Download size={18} />
              开始转换导出
            </>
          )}
        </button>
      </div>
    </div>
  );
}
