import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { convertFileSrc } from '@tauri-apps/api/core';
import { useAppStore } from '../../stores/appStore';
import { Loader2, Play, Rocket, Sliders, Settings } from 'lucide-react';
import { withVideoSuffix } from '../../utils/videoOutput';
import { formatDuration } from '../../utils/timeFormat';
import { useVideoProgress } from '../../hooks/useVideoProgress';
import { useClipProperties } from '../../hooks/useClipProperties';
import { useCssFilterPreview } from '../../hooks/useCssFilterPreview';
import { isVideoFormat } from '../../utils/mediaFormat';
import { submitEditExport } from '../../modules/editExportBridge';
import TrimTimeline from '../preview/TrimTimeline';
import SliderControl from '../common/SliderControl';
import ExportFormatSelector from '../image/ExportFormatSelector';
import VideoPlayer from '../layout/VideoPlayer';
import PlayerControls from '../preview/PlayerControls';
import DropZone from '../layout/DropZone';
import type { FileItem } from '../../stores/appStore';

/**
 * 模块15：视频剪辑编排模块（VideoTrimmerOrchestrator）
 * 纯编排：组合 14 个子模块，自身 < 200 行
 */
export default function VideoTrimmer() {
  const { files, options, addFiles } = useAppStore();

  // 视频元数据
  const [duration, setDuration] = useState<number>(0);
  const [currentTime, setCurrentTime] = useState<number>(0);
  const [isPlaying, setIsPlaying] = useState<boolean>(false);
  const [videoPreviewPath, setVideoPreviewPath] = useState<string>('');
  const [videoPlaybackFailed, setVideoPlaybackFailed] = useState<boolean>(false);
  const [isProcessing, setIsProcessing] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState('');

  // 模块11 状态
  const props = useClipProperties(duration);
  const {
    trimStart, trimEnd, speed, volume, brightness, contrast, exportFormat,
    setTrimEnd, setSpeed, setVolume, setBrightness, setContrast, setExportFormat, reset: resetProps,
  } = props;

  useVideoProgress(setProgress);

  const videoFiles = files.filter((f) => isVideoFormat(f.format));
  const firstVideo = videoFiles[0];
  const assetUrl = useMemo(
    () => (firstVideo?.path ? convertFileSrc(firstVideo.path) : ''),
    [firstVideo?.path]
  );
  const posterUrl = useMemo(
    () => (typeof videoPreviewPath === 'string' && videoPreviewPath ? convertFileSrc(videoPreviewPath) : undefined),
    [videoPreviewPath]
  );

  const handleFilesAdded = useCallback(
    (newFiles: FileItem[]) => {
      addFiles(newFiles);
    },
    [addFiles]
  );

  // 模块2：探测元数据
  useEffect(() => {
    if (!firstVideo) return;
    setVideoPlaybackFailed(false);
    setVideoPreviewPath('');
    setCurrentTime(0);
    let cancelled = false;
    invoke<{ duration_secs: number }>('get_video_info', { path: firstVideo.path })
      .then((info) => {
        if (!cancelled) {
          setDuration(info.duration_secs);
          setTrimEnd(info.duration_secs);
        }
      })
      .catch(() => { if (!cancelled) setError('无法获取视频信息'); });
    return () => { cancelled = true; };
  }, [firstVideo?.path, setTrimEnd]);

  // 模块3：预览图
  useEffect(() => {
    if (!firstVideo?.path) return;
    invoke<string>('create_video_preview', { inputPath: firstVideo.path })
      .then((path) => setVideoPreviewPath(typeof path === 'string' ? path : ''))
      .catch(() => setVideoPreviewPath(''));
  }, [firstVideo?.path]);

  // 模块10：CSS 预览
  const previewFilter = useCssFilterPreview(brightness, contrast);

  // 播放控制
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const handlePlayPause = useCallback(() => {
    const v = videoRef.current;
    if (!v) return;
    if (v.paused) {
      v.play().catch(() => setVideoPlaybackFailed(true));
    }
    else v.pause();
  }, []);
  const handleSeek = useCallback((time: number) => {
    const clamped = Math.max(0, Math.min(duration || 0, time));
    const v = videoRef.current;
    if (v) v.currentTime = clamped;
    setCurrentTime(clamped);
  }, [duration]);

  // 裁切约束
  const handleTrimStartChange = useCallback(
    (val: number) => props.setTrimStart(Math.min(val, trimEnd - 0.1)),
    [trimEnd, props]
  );
  const handleTrimEndChange = useCallback(
    (val: number) => setTrimEnd(Math.max(val, trimStart + 0.1)),
    [trimStart, setTrimEnd]
  );

  const handleReset = useCallback(() => {
    resetProps();
    handleSeek(0);
    setIsPlaying(false);
    setError('');
  }, [resetProps, handleSeek]);

  // 模块12：调用一站式导出
  const handleExport = useCallback(async () => {
    if (!firstVideo || isProcessing) return;
    if (trimEnd - trimStart <= 0.1) {
      setError('请选择有效的裁切范围');
      return;
    }
    setIsProcessing(true);
    setProgress(0);
    setError('');
    try {
      await submitEditExport({
        inputPath: firstVideo.path,
        outputPath: withVideoSuffix(firstVideo.path, '_edited', exportFormat, options.outputDir),
        startSecs: trimStart,
        endSecs: trimEnd,
        speed, volume: volume / 100, brightness, contrast, targetFormat: exportFormat,
      });
      setProgress(100);
      if (options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setIsProcessing(false);
    }
  }, [firstVideo, isProcessing, trimStart, trimEnd, speed, volume, brightness, contrast, exportFormat, options.outputDir, options.openAfterProcess]);

  const handleMergeExport = useCallback(async () => {
    if (!firstVideo || videoFiles.length < 2 || isProcessing) return;
    setIsProcessing(true);
    setProgress(0);
    setError('');
    try {
      await invoke('merge_videos', {
        inputPaths: videoFiles.map((file) => file.path),
        outputPath: withVideoSuffix(firstVideo.path, '_merged', exportFormat, options.outputDir),
      });
      setProgress(100);
      if (options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setIsProcessing(false);
    }
  }, [firstVideo, videoFiles, isProcessing, exportFormat, options.outputDir, options.openAfterProcess]);

  const canExport = Boolean(firstVideo && !isProcessing && trimEnd - trimStart > 0.1);
  const canMerge = videoFiles.length > 1 && !isProcessing;

  if (!firstVideo) {
    return (
      <div
        className="grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(320px,384px)] gap-6"
        role="region"
        aria-label="视频剪辑工作区"
      >
        <div className="min-w-0">
          <DropZone onFilesAdded={handleFilesAdded} mediaType="video" />
        </div>
        <div className="min-w-[320px] flex flex-col gap-4">
          <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 p-5">
            <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4 flex items-center gap-2">
              <Sliders size={18} className="text-secondary" />
              片段属性
            </label>
            <p className="text-sm text-on-surface-variant leading-6">
              添加视频后可选择入点、出点、播放速度、音量、亮度和对比度。
            </p>
          </div>
          <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 p-5">
            <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4 flex items-center gap-2">
              <Settings size={18} className="text-secondary" />
              导出设置
            </label>
            <ExportFormatSelector value={exportFormat} onChange={setExportFormat} disabled />
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(320px,384px)] gap-6"
      role="region"
      aria-label="视频剪辑工作区"
    >
      {/* 左侧：预览 + 时间线 */}
      <div className="flex-1 flex flex-col gap-6 min-w-0">
        <div
          className="aspect-video rounded-[18px] bg-[#111] relative overflow-hidden"
          data-testid="video-preview-poster"
        >
          {firstVideo ? (
            <>
              <VideoPlayer
                src={assetUrl}
                poster={posterUrl}
                filter={previewFilter}
                onTimeUpdate={setCurrentTime}
                onDurationChange={(nextDuration) => {
                  setDuration(nextDuration);
                  setTrimEnd(nextDuration);
                }}
                onPlayStateChange={setIsPlaying}
                onError={() => setVideoPlaybackFailed(true)}
                onReady={(node) => { videoRef.current = node; }}
              />
              {videoPlaybackFailed && posterUrl && (
                <img
                  src={posterUrl}
                  alt={`${firstVideo.name} 预览图`}
                  className="absolute inset-0 h-full w-full object-contain bg-[#111]"
                />
              )}
              {videoPlaybackFailed && (
                <div className="absolute inset-0 flex flex-col items-center justify-center bg-black/55 text-white/90 pointer-events-none">
                  <Play size={48} className="mb-4 text-secondary-fixed" fill="currentColor" />
                  <p className="text-sm">内嵌播放器暂不支持此编码</p>
                  <p className="text-xs mt-1 text-white/70">FFmpeg 本地处理仍可继续</p>
                </div>
              )}
            </>
          ) : (
            <div className="absolute inset-0 flex flex-col items-center justify-center text-white/40">
              <Play size={48} className="mb-4" fill="currentColor" />
              <p className="text-sm">请添加视频文件</p>
            </div>
          )}
          <PlayerControls
            currentTime={currentTime}
            duration={duration}
            isPlaying={isPlaying}
            onPlayPause={handlePlayPause}
            onSeek={handleSeek}
            formatTime={formatDuration}
          />
        </div>

        <TrimTimeline
          duration={duration}
          trimStart={trimStart}
          trimEnd={trimEnd}
          currentTime={currentTime}
          onTrimStartChange={handleTrimStartChange}
          onTrimEndChange={handleTrimEndChange}
          onSeek={handleSeek}
        />
      </div>

      {/* 右侧：参数面板 */}
      <div className="min-w-[320px] flex flex-col gap-4">
        <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 p-5">
          <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4 flex items-center gap-2">
            <Sliders size={18} className="text-secondary" />
            片段属性
          </label>
          <div className="space-y-4">
            <SliderControl label="播放速度" value={speed} min={0.25} max={4} step={0.25}
              onChange={setSpeed} format={(v) => `${v.toFixed(2)}x`} />
            <SliderControl label="音量" value={volume} min={0} max={200} step={5}
              onChange={setVolume} format={(v) => `${v}%`} />
            <SliderControl label="亮度" value={brightness} min={-100} max={100} step={5}
              onChange={setBrightness} format={(v) => `${v}`} />
            <SliderControl label="对比度" value={contrast} min={-100} max={100} step={5}
              onChange={setContrast} format={(v) => `${v}`} />
          </div>
        </div>

        <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 p-5">
          <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4 flex items-center gap-2">
            <Settings size={18} className="text-secondary" />
            导出设置
          </label>
          <ExportFormatSelector value={exportFormat} onChange={setExportFormat} disabled={isProcessing} />
        </div>

        {error && <p className="text-error text-xs text-center">{error}</p>}

        {isProcessing && (
          <div>
            <div className="w-full bg-surface-container-high rounded-full h-2">
              <div className="bg-secondary-fixed h-2 rounded-full transition-all duration-300" style={{ width: `${progress}%` }} />
            </div>
            <p className="text-on-surface-variant mt-1.5 text-center text-xs">渲染中 {progress}%</p>
          </div>
        )}

        <button
          onClick={handleExport}
          disabled={!canExport}
          className={
            canExport
              ? 'w-full py-3.5 bg-primary text-on-primary rounded-[980px] hover:opacity-80 active:opacity-70 transition-opacity text-sm font-semibold flex items-center justify-center gap-2'
              : 'w-full py-3.5 bg-surface-container-high text-on-surface-variant rounded-[980px] cursor-not-allowed text-sm font-semibold flex items-center justify-center gap-2'
          }
        >
          {isProcessing ? (<><Loader2 size={16} className="animate-spin" />渲染中...</>)
            : (<><Rocket size={16} />开始渲染导出</>)}
        </button>

        {videoFiles.length > 1 && (
          <button
            type="button"
            onClick={handleMergeExport}
            disabled={!canMerge}
            className={
              canMerge
                ? 'w-full py-3.5 bg-surface-container-low text-on-surface rounded-[980px] hover:bg-surface-container-high active:opacity-70 transition-colors text-sm font-semibold flex items-center justify-center gap-2'
                : 'w-full py-3.5 bg-surface-container-high text-on-surface-variant rounded-[980px] cursor-not-allowed text-sm font-semibold flex items-center justify-center gap-2'
            }
          >
            合并导出
          </button>
        )}

        <button onClick={handleReset}
          className="w-full py-2.5 bg-surface-container-low hover:bg-surface-container-high text-on-surface rounded-[12px] transition-colors text-xs font-semibold">
          重置
        </button>
      </div>
    </div>
  );
}
