import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Loader2, Wand2 } from 'lucide-react';
import { useAppStore } from '../../stores/appStore';
import { isVideoFormat } from '../../utils/mediaFormat';
import ChipButton from '../common/ChipButton';
import SegmentedControl from '../common/SegmentedControl';
import CustomSlider from '../common/CustomSlider';
import { withVideoSuffix } from '../../utils/videoOutput';
import { useMediaPreview } from '../../hooks/useMediaPreview';
import { useVideoProgress } from '../../hooks/useVideoProgress';

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

function formatGifTime(value: number): string {
  const safe = Math.max(0, value);
  const minutes = Math.floor(safe / 60);
  const seconds = safe - minutes * 60;
  return `${String(minutes).padStart(2, '0')}:${seconds.toFixed(2).padStart(5, '0')}`;
}

export default function GifMaker() {
  const { files, options, setProcessing, setProgress } = useAppStore();
  useVideoProgress(setProgress);
  const [gifSize, setGifSize] = useState<string>('original');
  const [fps, setFps] = useState<number>(15);
  const [quality, setQuality] = useState<number>(2);
  const [loopCount, setLoopCount] = useState<number>(0);
  const [startTime, setStartTime] = useState<string>('00:00.00');
  const [endTime, setEndTime] = useState<string>('');
  const [isProcessing, setIsProcessing] = useState(false);
  const [status, setStatus] = useState<string>('');
  const cancelRequested = useRef(false);

  const videos = useMemo(
    () => files.filter((f) => isVideoFormat(f.format)),
    [files]
  );
  const preview = useMediaPreview(videos[0]?.path, 'video');
  const initializedVideoRef = useRef('');

  useEffect(() => {
    const first = videos[0];
    const duration = preview.descriptor?.durationSecs;
    if (!first || !duration || duration <= 0 || initializedVideoRef.current === first.path) return;
    initializedVideoRef.current = first.path;
    setStartTime('00:00.00');
    setEndTime(formatGifTime(Math.min(duration, 60)));
  }, [preview.descriptor?.durationSecs, videos]);

  const getGifWidth = useCallback((): number | null => {
    switch (gifSize) {
      case '720':
        return 720;
      case '480':
        return 480;
      case '320':
        return 320;
      case 'original':
      default:
        return null;
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
    setProcessing(true);
    setProgress(0);
    cancelRequested.current = false;
    setStatus('生成 GIF 中...');

    let completed = 0;
    let failed = 0;
    for (const v of videos) {
      if (cancelRequested.current) break;
      try {
        const outPath = withVideoSuffix(v.path, '', 'gif', options.outputDir);

        const result = await invoke<{ output_path: string }>('create_gif', {
          inputPath: v.path,
          outputPath: outPath,
          fps,
          width: getGifWidth(),
          quality,
          startSecs: startSecs > 0 ? startSecs : undefined,
          endSecs: endSecs ?? undefined,
          loopCount,
        });
        const actualOutputPath = result?.output_path || outPath;
        completed += 1;
        setStatus(`完成: ${v.name} → ${actualOutputPath.split(/[\\/]/).pop()}`);
      } catch {
        failed += 1;
      }
    }
    setStatus(cancelRequested.current
      ? `已取消，完成 ${completed} 个`
      : failed > 0
        ? `完成 ${completed} 个，失败 ${failed} 个`
        : `全部完成，共 ${completed} 个`);
    try {
      if (completed > 0 && options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } finally {
      setIsProcessing(false);
      setProcessing(false);
      setProgress(100);
    }
  }, [videos, fps, getGifWidth, quality, loopCount, startTime, endTime, options.outputDir, options.openAfterProcess]);

  const cancel = useCallback(async () => {
    cancelRequested.current = true;
    setStatus('正在取消当前 GIF 任务…');
    await invoke('cancel_video_tasks');
  }, []);

  const disabled = !isProcessing && videos.length === 0;
  const selectedDuration = Math.max(
    0,
    (endTime ? parseTimeToSeconds(endTime) : preview.descriptor?.durationSecs ?? 0) - parseTimeToSeconds(startTime)
  );
  const previewWidth = getGifWidth() ?? preview.descriptor?.width ?? 0;
  const estimatedFrames = Math.round(selectedDuration * fps);

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
          <label className="flex flex-col gap-1 text-label-caps font-label-caps text-on-surface-variant">
            循环次数
            <select aria-label="循环次数" value={loopCount} onChange={(event) => setLoopCount(Number(event.target.value))} className="min-h-10 rounded-xl border-none bg-surface-container-low px-4 text-sm text-on-surface focus:outline-none focus:ring-2 focus:ring-secondary-fixed">
              <option value={0}>无限循环</option>
              <option value={1}>播放 1 次</option>
              <option value={3}>播放 3 次</option>
            </select>
          </label>
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
            <button type="button" onClick={() => setStartTime(formatGifTime(preview.playbackPosition))} className="min-h-10 self-start rounded-full bg-surface-container-high px-4 text-xs font-semibold text-on-surface">设为当前播放位置</button>
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
            <button type="button" onClick={() => setEndTime(formatGifTime(preview.playbackPosition))} className="min-h-10 self-start rounded-full bg-surface-container-high px-4 text-xs font-semibold text-on-surface">设为当前播放位置</button>
          </div>
        </div>
        <p className="mt-4 text-xs text-on-surface-variant" role="status">预计 {estimatedFrames} 帧 · {selectedDuration.toFixed(1)} 秒{previewWidth ? ` · ${previewWidth}px 宽` : ''}</p>
        {(previewWidth > 720 || fps > 20 || selectedDuration > 15 || estimatedFrames > 360) && (
          <p className="mt-3 rounded-xl bg-error/10 p-3 text-xs leading-5 text-error" role="status">当前尺寸、帧率或时长可能生成很大的 GIF；建议使用 720P、15 FPS 和 15 秒以内片段。</p>
        )}
      </section>

      <button
        type="button"
        onClick={isProcessing ? () => void cancel() : handleMakeGif}
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
            取消转换
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
