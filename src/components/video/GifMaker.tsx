import { useCallback, useMemo, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Loader2, Wand2 } from 'lucide-react';
import { useAppStore } from '../../stores/appStore';
import { isVideoFormat } from '../../utils/mediaFormat';
import ChipButton from '../common/ChipButton';
import SegmentedControl from '../common/SegmentedControl';
import CustomSlider from '../common/CustomSlider';
import { withVideoSuffix } from '../../utils/videoOutput';

const GIF_SIZE_OPTIONS = [
  { label: '原始尺寸', value: 'original' },
  { label: '720P', value: '720' },
  { label: '480P', value: '480' },
  { label: '320P', value: '320' },
];

const FPS_OPTIONS = [
  { label: '10', value: '10' },
  { label: '15', value: '15' },
  { label: '24', value: '24' },
];

const QUALITY_LABELS: Record<number, string> = {
  1: '低 (Low)',
  2: '中 (Medium)',
  3: '高 (High)',
};

function parseTimeToSeconds(timeStr: string): number {
  const parts = timeStr.split(':');
  if (parts.length !== 2) return 0;
  const minutes = parseInt(parts[0], 10) || 0;
  const seconds = parseFloat(parts[1]) || 0;
  return minutes * 60 + seconds;
}

export default function GifMaker() {
  const { files, options } = useAppStore();
  const [gifSize, setGifSize] = useState<string>('original');
  const [fps, setFps] = useState<number>(15);
  const [quality, setQuality] = useState<number>(2);
  const [startTime, setStartTime] = useState<string>('00:00.00');
  const [endTime, setEndTime] = useState<string>('');
  const [isProcessing, setIsProcessing] = useState(false);
  const [status, setStatus] = useState<string>('');

  const videos = useMemo(
    () => files.filter((f) => isVideoFormat(f.format)),
    [files]
  );

  const getGifWidth = useCallback((): number => {
    switch (gifSize) {
      case '720':
        return 720;
      case '480':
        return 480;
      case '320':
        return 320;
      case 'original':
      default:
        return 1920;
    }
  }, [gifSize]);

  const handleMakeGif = useCallback(async () => {
    if (videos.length === 0) {
      setStatus('请先添加视频文件');
      return;
    }

    const startSecs = parseTimeToSeconds(startTime);
    const endSecs = endTime ? parseTimeToSeconds(endTime) : null;

    if (endSecs !== null && startSecs >= endSecs) {
      setStatus('开始时间必须小于结束时间');
      return;
    }

    setIsProcessing(true);
    setStatus('生成 GIF 中...');

    try {
      for (const v of videos) {
        const outPath = withVideoSuffix(v.path, '', 'gif', options.outputDir);

        await invoke('create_gif', {
          inputPath: v.path,
          outputPath: outPath,
          fps,
          width: getGifWidth(),
          quality,
          startSecs: startSecs > 0 ? startSecs : undefined,
          endSecs: endSecs ?? undefined,
        });
        setStatus(`完成: ${v.name} → ${outPath.split('/').pop()}`);
      }
      if (options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } catch (e) {
      setStatus(`失败: ${e}`);
    } finally {
      setIsProcessing(false);
    }
  }, [videos, fps, getGifWidth, quality, startTime, endTime, options.outputDir, options.openAfterProcess]);

  const disabled = isProcessing || videos.length === 0;

  return (
    <div className="flex flex-col gap-6">
      <section className="bg-surface-container-lowest rounded-[18px] p-6 shadow-sm border border-outline-variant/10 flex-grow">
        <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4">
          GIF 尺寸
        </label>
        <div className="mb-8">
          <ChipButton
            options={GIF_SIZE_OPTIONS}
            value={gifSize}
            onChange={setGifSize}
            gridCols={2}
          />
        </div>

        <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4">
          帧率 (FPS)
        </label>
        <div className="mb-8">
          <SegmentedControl
            options={FPS_OPTIONS}
            value={fps.toString()}
            onChange={(v) => setFps(Number(v))}
          />
        </div>

        <div className="space-y-4">
          <div className="flex justify-between items-center">
            <label className="font-label-caps text-label-caps uppercase opacity-50">
              输出质量
            </label>
            <span className="bg-secondary-container text-on-secondary-container px-2 py-0.5 rounded text-[10px] font-bold">
              {QUALITY_LABELS[quality]}
            </span>
          </div>
          <CustomSlider
            min={1}
            max={3}
            step={1}
            value={quality}
            onChange={setQuality}
          />
        </div>

        <div className="mt-8 space-y-4 border-t border-outline-variant/10 pt-6">
          <div className="flex flex-col gap-1">
            <label htmlFor="gif-start-time" className="text-label-caps font-label-caps text-on-surface-variant">
              开始时间
            </label>
            <input
              id="gif-start-time"
              type="text"
              value={startTime}
              onChange={(e) => setStartTime(e.target.value)}
              placeholder="00:00.00"
              className="bg-surface-container-low border-none rounded-xl px-4 py-2 font-mono-status text-on-surface focus:outline-none focus:ring-2 focus:ring-secondary-fixed"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor="gif-end-time" className="text-label-caps font-label-caps text-on-surface-variant">
              结束时间
            </label>
            <input
              id="gif-end-time"
              type="text"
              value={endTime}
              onChange={(e) => setEndTime(e.target.value)}
              placeholder="视频总时长"
              className="bg-surface-container-low border-none rounded-xl px-4 py-2 font-mono-status text-on-surface focus:outline-none focus:ring-2 focus:ring-secondary-fixed"
            />
          </div>
        </div>
      </section>

      <button
        type="button"
        onClick={handleMakeGif}
        disabled={disabled}
        className={`
          btn-apple btn-apple-primary rounded-[980px] w-full py-3.5 font-semibold text-body-sm flex items-center justify-center gap-2 no-scale
          ${disabled
            ? 'bg-surface-container-high text-on-surface-variant cursor-not-allowed opacity-100'
            : ''
          }
        `}
      >
        {isProcessing ? (
          <>
            <Loader2 size={18} className="animate-spin" />
            转换中...
          </>
        ) : (
          <>
            <Wand2 size={18} />
            开始转换
          </>
        )}
      </button>

      {status && (
        <p className="text-on-surface-variant text-center font-mono-status text-[11px]">
          {status}
        </p>
      )}
    </div>
  );
}
