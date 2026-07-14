import { useState, useCallback, useEffect, useMemo } from 'react';
import { convertFileSrc } from '@tauri-apps/api/core';
import ReactCrop, { type Crop } from 'react-image-crop';
import 'react-image-crop/dist/ReactCrop.css';
import { Link, ChevronUp, Zap } from 'lucide-react';
import { useAppStore } from '../../stores/appStore';
import { isImageFormat } from '../../utils/mediaFormat';
import { useImageProcessor } from '../../hooks/useImageProcessor';

const ID_PHOTO_PRESETS = [
  { label: '一寸', w: 25, h: 35 },
  { label: '二寸', w: 35, h: 49 },
  { label: '小一寸', w: 22, h: 32 },
  { label: '小二寸', w: 35, h: 45 },
  { label: '大一寸', w: 33, h: 48 },
  { label: '护照/签证', w: 33, h: 48 },
];

const RATIO_PRESETS = [
  { label: '1:1', w: 1, h: 1 },
  { label: '3:2', w: 3, h: 2 },
  { label: '4:3', w: 4, h: 3 },
  { label: '3:4', w: 3, h: 4 },
  { label: '16:9', w: 16, h: 9 },
  { label: '9:16', w: 9, h: 16 },
  { label: '5:7', w: 5, h: 7 },
  { label: '2:3', w: 2, h: 3 },
  { label: '自由', w: 0, h: 0 },
];

type CropTab = 'id' | 'ratio' | 'custom';

const DEFAULT_CROP: Crop = {
  unit: '%',
  x: 10,
  y: 10,
  width: 80,
  height: 80,
};

export default function EditPanel() {
  const { files, options, setOptions, isProcessing } = useAppStore();
  const [cropTab, setCropTab] = useState<CropTab>('id');
  const [cropCollapsed, setCropCollapsed] = useState(false);
  const [crop, setCrop] = useState<Crop>(DEFAULT_CROP);
  const [selectedIdPreset, setSelectedIdPreset] = useState<number | null>(null);
  const [selectedRatioPreset, setSelectedRatioPreset] = useState<number>(8); // 自由 default
  const [customW, setCustomW] = useState(295);
  const [customH, setCustomH] = useState(413);
  const [lockAspect, setLockAspect] = useState(true);
  const [resizeW, setResizeW] = useState(3840);
  const [resizeH, setResizeH] = useState(2160);
  const [lockResizeAspect, setLockResizeAspect] = useState(true);
  const [cropImageRef, setCropImageRef] = useState<HTMLImageElement | null>(null);
  const [editPreviewSrc, setEditPreviewSrc] = useState('');
  const [editPreviewFailed, setEditPreviewFailed] = useState(false);

  // 色彩调整从 store 读取,变更同步回 store
  const colorAdjust = options.colorAdjust ?? { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 };
  const brightness = colorAdjust.brightness;
  const contrast = colorAdjust.contrast;
  const saturation = colorAdjust.saturation;
  const sharpness = colorAdjust.sharpness;
  const setBrightness = (v: number) => setOptions({ colorAdjust: { ...colorAdjust, brightness: v } });
  const setContrast = (v: number) => setOptions({ colorAdjust: { ...colorAdjust, contrast: v } });
  const setSaturation = (v: number) => setOptions({ colorAdjust: { ...colorAdjust, saturation: v } });
  const setSharpness = (v: number) => setOptions({ colorAdjust: { ...colorAdjust, sharpness: v } });

  const imageFiles = useMemo(
    () => files.filter((f) => isImageFormat(f.format) && f.status === 'pending'),
    [files]
  );
  const firstFile = imageFiles[0];
  const assetUrl = useMemo(
    () => (firstFile?.path ? convertFileSrc(firstFile.path) : ''),
    [firstFile?.path]
  );

  const { exportWithCrop, startProcess } = useImageProcessor();

  useEffect(() => {
    setEditPreviewSrc(assetUrl || firstFile?.path || '');
    setEditPreviewFailed(false);
  }, [assetUrl, firstFile?.path]);

  const cssFilter = useMemo(() => {
    const filters = [];
    if (brightness !== 0) filters.push(`brightness(${1 + brightness / 100})`);
    if (contrast !== 0) filters.push(`contrast(${1 + contrast / 100})`);
    if (saturation !== 0) filters.push(`saturate(${1 + saturation / 100})`);
    return filters.length > 0 ? filters.join(' ') : 'none';
  }, [brightness, contrast, saturation]);

  const handleCropTab = (tab: CropTab) => {
    setCropTab(tab);
    if (cropCollapsed) setCropCollapsed(false);
  };

  const handleCrop = useCallback(
    (c: Crop) => {
      setCrop(c);
    },
    []
  );

  const handleCropComplete = useCallback(
    (c: Crop) => {
      setCrop(c);
      if (
        typeof c.x === 'number' &&
        typeof c.y === 'number' &&
        typeof c.width === 'number' &&
        typeof c.height === 'number'
      ) {
        setOptions({
          cropPercent: {
            x: Math.round(c.x),
            y: Math.round(c.y),
            width: Math.round(c.width),
            height: Math.round(c.height),
          },
        });
      }
    },
    [setOptions]
  );

  const handleEditPreviewError = useCallback(() => {
    if (firstFile?.path && editPreviewSrc !== firstFile.path) {
      setEditPreviewSrc(firstFile.path);
      return;
    }
    setEditPreviewFailed(true);
  }, [editPreviewSrc, firstFile?.path]);

  const handleIdPreset = (index: number) => {
    setSelectedIdPreset(index);
    setSelectedRatioPreset(-1);
  };

  const handleRatioPreset = (index: number) => {
    setSelectedRatioPreset(index);
    setSelectedIdPreset(-1);
    const preset = RATIO_PRESETS[index];
    if (preset.w > 0 && cropImageRef) {
      const imgAspect = cropImageRef.naturalWidth / cropImageRef.naturalHeight;
      const targetAspect = preset.w / preset.h;
      let newW, newH;
      if (imgAspect > targetAspect) {
        newH = 100;
        newW = (targetAspect / imgAspect) * 100;
      } else {
        newW = 100;
        newH = (imgAspect / targetAspect) * 100;
      }
      const crop: Crop = {
        unit: '%',
        x: (100 - newW) / 2,
        y: (100 - newH) / 2,
        width: newW,
        height: newH,
      };
      setCrop(crop);
      handleCropComplete(crop);
    }
  };

  const handleCustomWChange = (val: number) => {
    setCustomW(val);
    if (lockAspect && customH > 0 && customW > 0) {
      setCustomH(Math.round((val * customH) / customW));
    }
  };

  const handleCustomHChange = (val: number) => {
    setCustomH(val);
    if (lockAspect && customW > 0 && customH > 0) {
      setCustomW(Math.round((val * customW) / customH));
    }
  };

  const handleResizeWChange = (val: number) => {
    setResizeW(val);
    if (lockResizeAspect && resizeH > 0 && resizeW > 0) {
      setResizeH(Math.round((val * resizeH) / resizeW));
    }
  };

  const handleResizeHChange = (val: number) => {
    setResizeH(val);
    if (lockResizeAspect && resizeW > 0 && resizeH > 0) {
      setResizeW(Math.round((val * resizeW) / resizeH));
    }
  };

  const handleResizePreset = (percent: number) => {
    if (firstFile) {
      const aspect = resizeW / resizeH;
      const newW = Math.round(firstFile.originalSize * percent / 100) || resizeW;
      setResizeW(newW);
      setResizeH(Math.round(newW / aspect));
    }
  };

  const handleExport = useCallback(async () => {
    if (imageFiles.length === 0 || isProcessing) return;

    // 优先：使用 exportWithCrop 走统一抽象，附带进度反馈
    if (crop && crop.width > 0 && crop.height > 0 && cropImageRef) {
      for (const file of imageFiles) {
        await exportWithCrop({
          path: file.path,
          crop,
          imageRef: cropImageRef,
          outputFormat: 'png',
          quality: 85,
        });
      }
      return;
    }

    // 无裁切：走批量导出，保持与原行为一致
    await startProcess({
      outputFormat: 'png',
      quality: 85,
      resizeEnabled: false,
      resizeMaxPx: 1920,
      stripExif: false,
      rotateDegrees: 0,
    });
  }, [imageFiles, isProcessing, crop, cropImageRef, exportWithCrop, startProcess]);

  return (
    <div className="flex flex-col gap-10">
      {/* 1. 裁切预览 + 裁切预设 */}
      <div className="space-y-5">
        <label className="font-label-caps text-label-caps uppercase text-primary/40 block">
          裁切
        </label>

        {/* 裁切预览 */}
        <div className="aspect-video bg-surface-container-low rounded-[18px] relative overflow-hidden border border-outline-variant/10 crop-grid-overlay">
          {firstFile && editPreviewSrc && !editPreviewFailed ? (
            <ReactCrop
              crop={crop}
              onChange={handleCrop}
              onComplete={handleCropComplete}
              keepSelection
            >
              <img
                ref={setCropImageRef}
                src={editPreviewSrc}
                alt={firstFile.name}
                className="block max-h-full max-w-full object-contain mx-auto"
                style={{ filter: cssFilter }}
                onError={handleEditPreviewError}
              />
            </ReactCrop>
          ) : firstFile ? (
            <div className="absolute inset-0 flex flex-col items-center justify-center px-6 text-center text-on-surface-variant">
              <span className="text-sm font-semibold text-on-surface">图片预览加载失败</span>
              <span className="mt-1 text-xs">请确认文件仍在本地磁盘，导出功能仍可继续使用。</span>
            </div>
          ) : (
            <div className="absolute inset-0 flex items-center justify-center text-on-surface-variant">
              拖入图片以裁切
            </div>
          )}
        </div>

        {/* 裁切预设卡片 */}
        <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 overflow-hidden">
          {/* Tabs */}
          <div className="flex items-center p-4 pb-0">
            <div className="flex bg-surface-container rounded-[12px] p-1 flex-1">
              <button
                type="button"
                onClick={() => handleCropTab('id')}
                className={`crop-tab px-3 py-1.5 text-[11px] font-bold rounded-[980px] no-scale whitespace-nowrap ${
                  cropTab === 'id'
                    ? 'bg-primary text-on-primary'
                    : 'text-on-surface-variant hover:opacity-60'
                }`}
              >
                证件照
              </button>
              <button
                type="button"
                onClick={() => handleCropTab('ratio')}
                className={`crop-tab px-3 py-1.5 text-[11px] font-bold rounded-[980px] no-scale whitespace-nowrap ${
                  cropTab === 'ratio'
                    ? 'bg-primary text-on-primary'
                    : 'text-on-surface-variant hover:opacity-60'
                }`}
              >
                常用比例
              </button>
              <button
                type="button"
                onClick={() => handleCropTab('custom')}
                className={`crop-tab px-3 py-1.5 text-[11px] font-bold rounded-[980px] no-scale whitespace-nowrap ${
                  cropTab === 'custom'
                    ? 'bg-primary text-on-primary'
                    : 'text-on-surface-variant hover:opacity-60'
                }`}
              >
                自定义
              </button>
            </div>
          </div>

          {/* Content */}
          <div
            className="p-4 space-y-1.5"
            style={{
              maxHeight: cropCollapsed ? 0 : 320,
              overflow: cropCollapsed ? 'hidden' : 'visible',
              transition: 'max-height 300ms ease',
            }}
          >
            {/* 证件照 tab */}
            {cropTab === 'id' && (
              <div className="space-y-1.5">
                {ID_PHOTO_PRESETS.map((preset, i) => (
                  <div
                    key={preset.label}
                    onClick={() => handleIdPreset(i)}
                    className={`flex items-center p-2 rounded-[12px] cursor-pointer transition-colors ${
                      selectedIdPreset === i
                        ? 'bg-secondary-fixed/10 border-2 border-secondary-fixed'
                        : 'bg-surface-container-low border border-outline-variant/10 hover:bg-surface-container-high'
                    }`}
                  >
                    <span
                      className={`w-1.5 h-1.5 rounded-full mr-2 flex-shrink-0 ${
                        selectedIdPreset === i ? 'bg-secondary-fixed' : 'bg-outline-variant'
                      }`}
                    />
                    <span className="text-xs font-bold flex-1">{preset.label}</span>
                    <span className="text-[10px] font-mono-status text-on-surface-variant">
                      {preset.w}x{preset.h}
                    </span>
                  </div>
                ))}
              </div>
            )}

            {/* 常用比例 tab */}
            {cropTab === 'ratio' && (
              <div className="grid grid-cols-3 gap-2">
                {RATIO_PRESETS.map((preset, i) => (
                  <button
                    key={preset.label}
                    type="button"
                    onClick={() => handleRatioPreset(i)}
                    className={`py-2.5 rounded-[12px] text-[11px] font-bold no-scale transition-colors ${
                      selectedRatioPreset === i
                        ? 'bg-primary text-on-primary'
                        : 'bg-surface-container-low hover:bg-surface-container-high text-on-surface'
                    }`}
                  >
                    {preset.label}
                  </button>
                ))}
              </div>
            )}

            {/* 自定义 tab */}
            {cropTab === 'custom' && (
              <div className="space-y-3">
                <div className="flex items-center gap-2">
                  <div className="flex-1">
                    <label className="text-[10px] text-on-surface-variant block mb-1">
                      宽度 (px)
                    </label>
                    <input
                      type="number"
                      min={1}
                      value={customW}
                      onChange={(e) => handleCustomWChange(Math.max(1, Number(e.target.value)))}
                      className="w-full bg-surface-container-low border border-outline-variant/20 rounded-[12px] p-2 text-center text-sm font-mono-status"
                    />
                  </div>
                  <button
                    type="button"
                    onClick={() => setLockAspect(!lockAspect)}
                    className={`mt-4 no-scale hover:opacity-60 transition-opacity ${lockAspect ? 'text-secondary' : 'text-on-surface-variant'}`}
                    aria-label="锁定比例"
                  >
                    <Link size={18} />
                  </button>
                  <div className="flex-1">
                    <label className="text-[10px] text-on-surface-variant block mb-1">
                      高度 (px)
                    </label>
                    <input
                      type="number"
                      min={1}
                      value={customH}
                      onChange={(e) => handleCustomHChange(Math.max(1, Number(e.target.value)))}
                      className="w-full bg-surface-container-low border border-outline-variant/20 rounded-[12px] p-2 text-center text-sm font-mono-status"
                    />
                  </div>
                </div>
                <p className="text-[10px] text-on-surface-variant opacity-50">
                  输入自定义像素尺寸，锁定比例可保持宽高比
                </p>
              </div>
            )}
          </div>

          {/* Collapse toggle */}
          <div className="flex justify-center py-2 border-t border-outline-variant/10">
            <button
              type="button"
              onClick={() => setCropCollapsed(!cropCollapsed)}
              className="flex items-center gap-1 text-[10px] font-bold text-on-surface-variant hover:opacity-60 no-scale"
            >
              <ChevronUp
                size={16}
                style={{ transition: 'transform 200ms', transform: cropCollapsed ? 'rotate(180deg)' : 'none' }}
              />
              {cropCollapsed ? '展开' : '收起'}
            </button>
          </div>
        </div>
      </div>

      {/* 2. 调整尺寸 */}
      <div className="space-y-3">
        <label className="font-label-caps text-label-caps uppercase text-primary/40 block">
          调整尺寸
        </label>
        <div className="flex items-center gap-1.5">
          <div className="flex-1">
            <label className="text-[10px] text-on-surface-variant block mb-1">宽度 (PX)</label>
            <input
              type="number"
              min={1}
              value={resizeW}
              onChange={(e) => handleResizeWChange(Math.max(1, Number(e.target.value)))}
              className="w-full bg-surface-container-low border-none rounded-lg p-2 text-center text-sm font-mono-status focus:ring-1 focus:ring-secondary-fixed"
            />
          </div>
          <button
            type="button"
            onClick={() => setLockResizeAspect(!lockResizeAspect)}
            className={`mt-4 no-scale transition-colors ${lockResizeAspect ? 'text-secondary-fixed' : 'text-on-surface-variant'}`}
            aria-label="锁定尺寸比例"
          >
            <Link size={18} />
          </button>
          <div className="flex-1">
            <label className="text-[10px] text-on-surface-variant block mb-1">高度 (PX)</label>
            <input
              type="number"
              min={1}
              value={resizeH}
              onChange={(e) => handleResizeHChange(Math.max(1, Number(e.target.value)))}
              className="w-full bg-surface-container-low border-none rounded-lg p-2 text-center text-sm font-mono-status focus:ring-1 focus:ring-secondary-fixed"
            />
          </div>
        </div>
        <div className="grid grid-cols-4 gap-1">
          {[50, 75, 100, 200].map((pct) => (
            <button
              key={pct}
              type="button"
              onClick={() => handleResizePreset(pct)}
              className={`text-[11px] border rounded py-1.5 no-scale transition-colors ${
                pct === 100
                  ? 'bg-primary text-white border-primary'
                  : 'border-outline-variant/30 hover:bg-surface-container-low text-on-surface'
              }`}
            >
              {pct}%
            </button>
          ))}
        </div>
      </div>

      {/* 3. 色彩调整 */}
      <div className="space-y-4">
        <label className="font-label-caps text-label-caps uppercase text-primary/40 block">
          色彩调整
        </label>
        <div className="space-y-3">
          {/* 亮度 */}
          <div className="flex items-center gap-3">
            <span className="text-[11px] text-on-surface-variant w-10 shrink-0">亮度</span>
            <input
              type="range"
              min={-100}
              max={100}
              value={brightness}
              onChange={(e) => setBrightness(Number(e.target.value))}
              className="custom-slider flex-grow"
            />
            <span className="text-[10px] font-mono-status text-on-surface-variant w-6 text-right">
              {brightness}
            </span>
          </div>
          {/* 对比 */}
          <div className="flex items-center gap-3">
            <span className="text-[11px] text-on-surface-variant w-10 shrink-0">对比</span>
            <input
              type="range"
              min={-100}
              max={100}
              value={contrast}
              onChange={(e) => setContrast(Number(e.target.value))}
              className="custom-slider flex-grow"
            />
            <span className="text-[10px] font-mono-status text-on-surface-variant w-6 text-right">
              {contrast}
            </span>
          </div>
          {/* 饱和 */}
          <div className="flex items-center gap-3">
            <span className="text-[11px] text-on-surface-variant w-10 shrink-0">饱和</span>
            <input
              type="range"
              min={-100}
              max={100}
              value={saturation}
              onChange={(e) => setSaturation(Number(e.target.value))}
              className="custom-slider flex-grow"
            />
            <span className="text-[10px] font-mono-status text-on-surface-variant w-6 text-right">
              {saturation}
            </span>
          </div>
          {/* 锐化 */}
          <div className="flex items-center gap-3">
            <span className="text-[11px] text-on-surface-variant w-10 shrink-0">锐化</span>
            <input
              type="range"
              min={0}
              max={100}
              value={sharpness}
              onChange={(e) => setSharpness(Number(e.target.value))}
              className="custom-slider flex-grow"
            />
            <span className="text-[10px] font-mono-status text-on-surface-variant w-6 text-right">
              {sharpness}
            </span>
          </div>
        </div>
      </div>

      {/* 4. CTA */}
      <div className="pt-4">
        <button
          type="button"
          onClick={handleExport}
          disabled={imageFiles.length === 0 || isProcessing}
          className={`btn-apple btn-apple-primary rounded-[980px] w-full py-3.5 font-semibold text-body-sm flex items-center justify-center gap-2 no-scale ${
            imageFiles.length === 0 || isProcessing
              ? 'opacity-50 cursor-not-allowed'
              : ''
          }`}
        >
          <Zap size={18} />
          {isProcessing ? '处理中...' : '开始极速导出'}
        </button>
      </div>
    </div>
  );
}
