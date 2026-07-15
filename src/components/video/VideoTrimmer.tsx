import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { convertFileSrc, invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { open } from '@tauri-apps/plugin-dialog';
import {
  ChevronsLeft,
  ChevronsRight,
  Film,
  Loader2,
  Maximize2,
  Pause,
  Play,
  Plus,
  Redo2,
  RotateCcw,
  Scissors,
  Square,
  StepBack,
  StepForward,
  Trash2,
  Undo2,
  ZoomIn,
  ZoomOut,
} from 'lucide-react';
import { useAppStore, type FileItem } from '../../stores/appStore';
import { isVideoFormat } from '../../utils/mediaFormat';
import { withVideoSuffix } from '../../utils/videoOutput';
import { formatDuration } from '../../utils/timeFormat';
import VideoPlayer from '../layout/VideoPlayer';
import PlayerControls from '../preview/PlayerControls';
import DropZone from '../layout/DropZone';
import VideoEditTimeline from '../preview/VideoEditTimeline';
import { useMediaPreview } from '../../hooks/useMediaPreview';
import {
  commitEdit,
  createEditHistory,
  createVideoEditProject,
  getIncludedRanges,
  redoEdit,
  setSegmentIncluded,
  splitAtPlayhead,
  stepFrame,
  trimToInPoint,
  trimToOutPoint,
  undoEdit,
  type VideoEditHistory,
  type VideoEditProject,
} from '../../modules/video/editProject';

interface TimelineAssets {
  filmstripPath?: string;
  waveformPath?: string;
}

const outputFormatFor = (file: FileItem, precise: boolean) => {
  if (precise) return 'mp4';
  const format = file.format.toLowerCase();
  return ['mp4', 'mov', 'mkv'].includes(format) ? format : 'mp4';
};

const taskId = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `edit-${Date.now()}-${Math.random().toString(36).slice(2)}`;

const ToolButton = ({
  label,
  onClick,
  disabled = false,
  children,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  children: React.ReactNode;
}) => (
  <button
    type="button"
    aria-label={label}
    title={label}
    disabled={disabled}
    onClick={onClick}
    className="flex min-h-10 min-w-10 shrink-0 items-center justify-center rounded-lg bg-surface-container-high px-2 text-xs font-semibold text-on-surface hover:bg-surface-container-highest disabled:cursor-not-allowed disabled:opacity-35"
  >
    {children}
  </button>
);

export default function VideoTrimmer() {
  const { files, options, addFiles, removeFile } = useAppStore();
  const videos = files.filter((file) => isVideoFormat(file.format));
  const firstVideo = videos[0];
  const [duration, setDuration] = useState(0);
  const [fps, setFps] = useState(30);
  const [currentTime, setCurrentTime] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [history, setHistory] = useState<VideoEditHistory | null>(null);
  const [selectedSegmentId, setSelectedSegmentId] = useState('segment-1');
  const [zoom, setZoom] = useState(1);
  const [assets, setAssets] = useState<TimelineAssets | null>(null);
  const [assetsLoading, setAssetsLoading] = useState(false);
  const [isProcessing, setIsProcessing] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const playbackStopRef = useRef<number | null>(null);
  const activeTaskIdRef = useRef('');
  const preview = useMediaPreview(firstVideo?.path, 'video');
  const project = history?.present ?? null;

  useEffect(() => {
    let cleanup: (() => void) | undefined;
    let active = true;
    void listen<{ taskId: string; percent: number }>('media-task-progress', (event) => {
      if (active && event.payload.taskId === activeTaskIdRef.current) {
        setProgress(Math.max(0, Math.min(100, event.payload.percent)));
      }
    }).then((unlisten) => {
      if (active) cleanup = unlisten; else unlisten();
    }).catch(() => {});
    return () => { active = false; cleanup?.(); };
  }, []);

  const assetUrl = useMemo(
    () => preview.descriptor?.playbackPath ? convertFileSrc(preview.descriptor.playbackPath) : '',
    [preview.descriptor?.playbackPath]
  );
  const filmstripUrl = assets?.filmstripPath ? convertFileSrc(assets.filmstripPath) : undefined;
  const waveformUrl = assets?.waveformPath ? convertFileSrc(assets.waveformPath) : undefined;

  useEffect(() => {
    if (!firstVideo) return;
    let cancelled = false;
    setError('');
    setCurrentTime(0);
    setHistory(null);
    setAssets(null);
    invoke<{ duration_secs: number; fps?: number }>('get_video_info', { path: firstVideo.path })
      .then((info) => {
        if (cancelled) return;
        setDuration(info.duration_secs);
        if (info.fps && info.fps > 0) setFps(info.fps);
      })
      .catch(() => { if (!cancelled) setError('无法读取视频时长，请确认媒体引擎可用。'); });
    return () => { cancelled = true; };
  }, [firstVideo?.path]);

  useEffect(() => {
    const previewDuration = preview.descriptor?.durationSecs;
    if (previewDuration && previewDuration > 0) setDuration(previewDuration);
    if (preview.descriptor?.fps && preview.descriptor.fps > 0) setFps(preview.descriptor.fps);
  }, [preview.descriptor?.durationSecs, preview.descriptor?.fps]);

  useEffect(() => {
    if (!firstVideo || duration <= 0) return;
    const format = outputFormatFor(firstVideo, false);
    const outputPath = withVideoSuffix(firstVideo.path, '_edited', format, options.outputDir);
    setHistory(createEditHistory(createVideoEditProject({
      inputPath: firstVideo.path,
      durationSecs: duration,
      fps,
      outputPath,
    })));
    setSelectedSegmentId('segment-1');
  }, [duration, firstVideo?.path, fps, options.outputDir]);

  useEffect(() => {
    if (!firstVideo || duration <= 0 || preview.descriptor?.state !== 'ready') return;
    let cancelled = false;
    setAssetsLoading(true);
    invoke<TimelineAssets>('generate_timeline_assets', {
      inputPath: firstVideo.path,
      durationSecs: duration,
      fps,
      hasAudio: Boolean(preview.descriptor.hasAudio),
      taskId: taskId(),
    }).then((value) => {
      if (!cancelled) setAssets(value);
    }).catch(() => {
      if (!cancelled) setAssets(null);
    }).finally(() => {
      if (!cancelled) setAssetsLoading(false);
    });
    return () => { cancelled = true; };
  }, [duration, firstVideo?.path, fps, preview.descriptor?.hasAudio, preview.descriptor?.state]);

  useEffect(() => {
    if (videos.length <= 1) return;
    const replacement = videos[videos.length - 1];
    const shouldReplace = window.confirm('视频剪辑一次只编辑一个源视频。是否用新视频替换当前视频？');
    videos.forEach((video) => {
      if ((shouldReplace && video.id !== replacement.id) || (!shouldReplace && video.id === replacement.id)) {
        removeFile(video.id);
      }
    });
  }, [removeFile, videos.length]);

  const handleSeek = useCallback((time: number) => {
    const next = Math.max(0, Math.min(duration, time));
    if (videoRef.current) videoRef.current.currentTime = next;
    setCurrentTime(next);
    preview.setPlaybackPosition(next);
  }, [duration, preview]);

  const handlePlayPause = useCallback(() => {
    const video = videoRef.current;
    if (!video) return;
    playbackStopRef.current = null;
    if (video.paused) void video.play(); else video.pause();
  }, []);

  const applyEdit = useCallback((transform: (value: VideoEditProject) => VideoEditProject) => {
    setHistory((current) => {
      if (!current) return current;
      const next = transform(current.present);
      return next === current.present ? current : commitEdit(current, next);
    });
  }, []);

  const activeSegment = project?.segments.find((segment) => segment.id === selectedSegmentId)
    ?? project?.segments.find((segment) => currentTime >= segment.startSecs && currentTime <= segment.endSecs)
    ?? project?.segments[0];

  const splitCurrent = useCallback(() => {
    if (!project) return;
    const next = splitAtPlayhead(project, currentTime);
    if (next === project) return;
    const selected = next.segments.find((segment) => Math.abs(segment.startSecs - currentTime) < 1e-5);
    if (selected) setSelectedSegmentId(selected.id);
    setHistory((current) => current ? commitEdit(current, next) : current);
  }, [currentTime, project]);

  const toggleSelected = useCallback(() => {
    if (!activeSegment) return;
    applyEdit((value) => setSegmentIncluded(value, activeSegment.id, !activeSegment.included));
  }, [activeSegment, applyEdit]);

  const playSelected = useCallback(() => {
    if (!activeSegment || !videoRef.current) return;
    handleSeek(activeSegment.startSecs);
    playbackStopRef.current = activeSegment.endSecs;
    void videoRef.current.play();
  }, [activeSegment, handleSeek]);

  const frameStep = useCallback((delta: number) => {
    handleSeek(stepFrame(currentTime, delta, fps, duration));
  }, [currentTime, duration, fps, handleSeek]);

  const setIn = useCallback(() => applyEdit((value) => trimToInPoint(value, currentTime)), [applyEdit, currentTime]);
  const setOut = useCallback(() => applyEdit((value) => trimToOutPoint(value, currentTime)), [applyEdit, currentTime]);
  const doUndo = useCallback(() => setHistory((current) => current ? undoEdit(current) : current), []);
  const doRedo = useCallback(() => setHistory((current) => current ? redoEdit(current) : current), []);

  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
        event.preventDefault();
        if (event.shiftKey) doRedo(); else doUndo();
      } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') {
        event.preventDefault(); doRedo();
      } else if (event.key.toLowerCase() === 's') splitCurrent();
      else if (event.key.toLowerCase() === 'i') setIn();
      else if (event.key.toLowerCase() === 'o') setOut();
      else if (event.key === 'Delete' || event.key === 'Backspace') toggleSelected();
      else if (event.key === 'ArrowLeft') frameStep(-1);
      else if (event.key === 'ArrowRight') frameStep(1);
      else if (event.key === ' ') { event.preventDefault(); handlePlayPause(); }
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [doRedo, doUndo, frameStep, handlePlayPause, setIn, setOut, splitCurrent, toggleSelected]);

  const handleExport = async () => {
    if (!firstVideo || !project || isProcessing || getIncludedRanges(project).length === 0) return;
    const mode = project.mode;
    if (mode === 'lossless' && !['mp4', 'mov', 'mkv'].includes(firstVideo.format.toLowerCase())) {
      setError('AVI 和 WebM 不能直接封装为 H.264/AAC 无损片段，请启用精确模式输出 MP4。');
      return;
    }
    setIsProcessing(true);
    setProgress(5);
    setError('');
    setSuccess('');
    const outputPath = withVideoSuffix(
      firstVideo.path,
      '_edited',
      outputFormatFor(firstVideo, mode === 'precise'),
      options.outputDir
    );
    const exportTaskId = taskId();
    activeTaskIdRef.current = exportTaskId;
    try {
      const result = await invoke<{ outputPath: string; output_path?: string }>('export_video_edit', {
        inputPath: project.inputPath,
        durationSecs: project.durationSecs,
        fps: project.fps,
        hasAudio: Boolean(preview.descriptor?.hasAudio),
        segments: project.segments,
        mode,
        outputPath,
        taskId: exportTaskId,
      });
      setProgress(100);
      const actualPath = result.outputPath || result.output_path || outputPath;
      setSuccess(`已导出：${actualPath}`);
      if (options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } catch (reason) {
      setError(`剪辑失败：${String(reason)}`);
    } finally {
      activeTaskIdRef.current = '';
      setIsProcessing(false);
    }
  };

  const cancelExport = async () => {
    setError('正在取消剪辑任务…');
    await invoke('cancel_video_tasks');
  };

  const handleAddSource = useCallback(async () => {
    try {
      const selected = await open({
        multiple: false,
        filters: [{ name: 'Videos', extensions: ['mp4', 'mov', 'mkv', 'avi', 'webm'] }],
      });
      if (!selected || Array.isArray(selected)) return;
      const inputPath = String(selected);
      const fallbackName = inputPath.split(/[\\/]/).pop() || inputPath;
      const extension = fallbackName.split('.').pop()?.toUpperCase() || 'VIDEO';
      let originalSize = 0;
      try {
        const metadata = await invoke<{ size_bytes?: number }>('read_file_metadata', { path: inputPath });
        originalSize = metadata.size_bytes || 0;
      } catch {
        // The editor can still open the source when metadata probing is unavailable.
      }
      addFiles([{
        id: taskId(),
        path: inputPath,
        name: fallbackName,
        format: extension,
        originalSize,
        status: 'pending',
      }]);
    } catch {
      // User cancelled the picker or the desktop dialog was unavailable.
    }
  }, [addFiles]);

  if (!firstVideo) {
    return (
      <div className="grid min-w-0 grid-cols-[minmax(0,1fr)_clamp(280px,30vw,340px)] gap-5" role="region" aria-label="视频剪辑工作区">
        <DropZone onFilesAdded={addFiles} mediaType="video" />
        <aside className="min-w-0 rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest p-5">
          <h2 className="flex items-center gap-2 text-sm font-semibold text-on-surface"><Scissors size={18} />片段设置</h2>
          <p className="mt-3 text-sm leading-6 text-on-surface-variant">添加一个视频后可分割、删除片段、逐帧定位并合并导出。</p>
        </aside>
      </div>
    );
  }

  const includedRanges = project ? getIncludedRanges(project) : [];
  const inPoint = includedRanges[0]?.startSecs ?? 0;
  const outPoint = includedRanges[includedRanges.length - 1]?.endSecs ?? duration;
  const canExport = !isProcessing && includedRanges.length > 0;

  return (
    <div className="grid min-w-0 grid-cols-[minmax(150px,190px)_minmax(0,1fr)] gap-4" role="region" aria-label="视频剪辑工作区">
      <aside className="min-w-0 rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest p-3" role="region" aria-label="项目素材">
        <div className="flex items-center justify-between gap-2 px-1">
          <h2 className="flex min-w-0 items-center gap-2 text-xs font-semibold text-on-surface"><Film size={15} />素材</h2>
          <button type="button" onClick={() => void handleAddSource()} className="flex min-h-8 min-w-8 items-center justify-center rounded-lg bg-surface-container-low text-on-surface hover:bg-surface-container-high" aria-label="添加素材" title="添加素材"><Plus size={15} /></button>
        </div>
        <p className="mt-4 px-1 text-[10px] font-semibold uppercase tracking-[0.12em] text-on-surface-variant">源视频</p>
        <button type="button" className="mt-2 w-full overflow-hidden rounded-xl border-2 border-secondary-fixed bg-surface-container-low p-2 text-left" aria-label={`选择 ${firstVideo.name}`} onClick={() => handleSeek(0)}>
          <div className="flex aspect-video items-center justify-center rounded-lg bg-[#202020] text-secondary-fixed"><Film size={24} /></div>
          <span className="mt-2 block truncate text-xs font-semibold text-on-surface">{firstVideo.name}</span>
          <span className="mt-1 block text-[10px] text-on-surface-variant">{firstVideo.format} · 单轨</span>
        </button>
        <p className="mt-4 px-1 text-[10px] leading-4 text-on-surface-variant">当前剪辑只使用一个源视频，导出时不会修改原文件。</p>
      </aside>
      <div className="grid min-w-0 grid-cols-[minmax(0,1fr)_clamp(280px,30vw,340px)] gap-5">
      <section className="min-w-0 space-y-4 overflow-hidden">
        <div className="relative aspect-video max-h-[44vh] overflow-hidden rounded-[18px] bg-[#111]" data-testid="video-preview-poster" role="region" aria-label="视频预览">
          <VideoPlayer
            src={assetUrl}
            poster={preview.descriptor?.posterPath ? convertFileSrc(preview.descriptor.posterPath) : undefined}
            onTimeUpdate={(value) => {
              setCurrentTime(value);
              preview.setPlaybackPosition(value);
              if (playbackStopRef.current !== null && value >= playbackStopRef.current) {
                videoRef.current?.pause();
                playbackStopRef.current = null;
              }
            }}
            onDurationChange={setDuration}
            onPlayStateChange={setIsPlaying}
            onError={preview.forceProxy}
            onReady={(node) => {
              videoRef.current = node;
              if (preview.playbackPosition > 0) node.currentTime = preview.playbackPosition;
            }}
          />
          {(preview.descriptor?.state === 'probing' || preview.descriptor?.state === 'generating') && (
            <div className="absolute inset-0 z-20 flex flex-col items-center justify-center bg-black/70 text-white" role="status">
              <Loader2 size={26} className="mb-3 animate-spin text-secondary-fixed" />
              <p className="text-sm font-semibold">正在准备可播放预览 {Math.round(preview.progress)}%</p>
              <button type="button" onClick={() => void preview.cancel()} className="mt-3 min-h-10 rounded-full bg-white/10 px-5 text-xs font-semibold">取消预览</button>
            </div>
          )}
          {(preview.descriptor?.state === 'error' || preview.descriptor?.state === 'cancelled') && (
            <div className="absolute inset-0 z-20 flex flex-col items-center justify-center bg-black/75 px-8 text-center text-white" role="alert">
              <p className="text-sm font-semibold">{preview.descriptor.error?.message || '视频预览失败'}</p>
              <button type="button" onClick={preview.retry} className="mt-3 min-h-10 rounded-full bg-secondary-fixed px-5 text-xs font-semibold text-on-secondary-fixed">重试预览</button>
            </div>
          )}
          <PlayerControls currentTime={currentTime} duration={duration} isPlaying={isPlaying} onPlayPause={handlePlayPause} onSeek={handleSeek} formatTime={formatDuration} />
        </div>

        <div className="flex max-w-full gap-2 overflow-x-auto rounded-[14px] bg-surface-container-lowest p-2" role="toolbar" aria-label="视频剪辑工具栏">
          <ToolButton label="到开头" onClick={() => handleSeek(0)}><ChevronsLeft size={17} /></ToolButton>
          <ToolButton label="上一帧" onClick={() => frameStep(-1)}><StepBack size={17} /></ToolButton>
          <ToolButton label="播放/暂停" onClick={handlePlayPause}>{isPlaying ? <Pause size={17} /> : <Play size={17} />}</ToolButton>
          <ToolButton label="下一帧" onClick={() => frameStep(1)}><StepForward size={17} /></ToolButton>
          <ToolButton label="到结尾" onClick={() => handleSeek(duration)}><ChevronsRight size={17} /></ToolButton>
          <ToolButton label="设置入点" onClick={setIn}><span>I</span></ToolButton>
          <ToolButton label="设置出点" onClick={setOut}><span>O</span></ToolButton>
          <ToolButton label="分割片段" onClick={splitCurrent}><Scissors size={17} /><span className="ml-1">S</span></ToolButton>
          <ToolButton label={activeSegment?.included === false ? '恢复选中片段' : '删除选中片段'} onClick={toggleSelected}><Trash2 size={17} /></ToolButton>
          <ToolButton label="撤销" disabled={!history?.past.length} onClick={doUndo}><Undo2 size={17} /></ToolButton>
          <ToolButton label="重做" disabled={!history?.future.length} onClick={doRedo}><Redo2 size={17} /></ToolButton>
          <ToolButton label="播放所选" onClick={playSelected}><Play size={17} /></ToolButton>
          <ToolButton label="缩小时间线" onClick={() => setZoom((value) => Math.max(1, value / 1.5))}><ZoomOut size={17} /></ToolButton>
          <ToolButton label="放大时间线" onClick={() => setZoom((value) => Math.min(8, value * 1.5))}><ZoomIn size={17} /></ToolButton>
          <ToolButton label="适配全片" onClick={() => setZoom(1)}><Maximize2 size={17} /></ToolButton>
        </div>

        {project && (
          <VideoEditTimeline
            duration={duration}
            currentTime={currentTime}
            segments={project.segments}
            selectedSegmentId={activeSegment?.id}
            zoom={zoom}
            filmstripUrl={filmstripUrl}
            waveformUrl={waveformUrl}
            inPoint={inPoint}
            outPoint={outPoint}
            onSeek={handleSeek}
            onInPointChange={(value) => { handleSeek(value); applyEdit((current) => trimToInPoint(current, value)); }}
            onOutPointChange={(value) => { handleSeek(value); applyEdit((current) => trimToOutPoint(current, value)); }}
            onSelectSegment={setSelectedSegmentId}
          />
        )}
        {assetsLoading && <p className="text-center text-xs text-on-surface-variant" role="status">正在生成真实缩略图和音频波形…</p>}
      </section>

      <aside className="min-w-0 space-y-4 overflow-y-auto pr-1">
        <section className="rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest p-5">
          <h2 className="text-sm font-semibold text-on-surface">片段属性</h2>
          <div className="mt-4 grid grid-cols-2 gap-3">
            <label className="text-xs text-on-surface-variant">入点（秒）
              <input aria-label="入点（秒）" className="mt-1 min-h-10 w-full rounded-lg bg-surface-container-low px-3 text-sm text-on-surface" type="number" min={0} max={outPoint} step={1 / fps} value={Number(inPoint.toFixed(3))} onChange={(event) => { handleSeek(Number(event.target.value)); applyEdit((value) => trimToInPoint(value, Number(event.target.value))); }} />
            </label>
            <label className="text-xs text-on-surface-variant">出点（秒）
              <input aria-label="出点（秒）" className="mt-1 min-h-10 w-full rounded-lg bg-surface-container-low px-3 text-sm text-on-surface" type="number" min={inPoint} max={duration} step={1 / fps} value={Number(outPoint.toFixed(3))} onChange={(event) => { handleSeek(Number(event.target.value)); applyEdit((value) => trimToOutPoint(value, Number(event.target.value))); }} />
            </label>
          </div>
          {activeSegment && (
            <p className="mt-3 rounded-xl bg-surface-container-low p-3 text-xs text-on-surface-variant">
              当前片段：{formatDuration(activeSegment.startSecs)} – {formatDuration(activeSegment.endSecs)} · {activeSegment.included ? '保留' : '已排除'}
            </p>
          )}
        </section>

        <section className="rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest p-5">
          <h2 className="text-sm font-semibold text-on-surface">导出设置</h2>
          <label className="mt-4 flex min-h-11 items-start gap-3 text-sm text-on-surface">
            <input className="mt-1" type="checkbox" checked={project?.mode === 'precise'} onChange={(event) => setHistory((current) => current ? { ...current, present: { ...current.present, mode: event.target.checked ? 'precise' : 'lossless' } } : current)} />
            <span><span className="block font-semibold">精确边界</span><span className="mt-1 block text-xs leading-5 text-on-surface-variant">关闭时逐段无损复制后合并，边界可能有关键帧误差；开启后统一 CPU 重编码为 MP4/H.264/AAC。</span></span>
          </label>
        </section>

        {(error || success) && <p className={`break-all rounded-xl p-3 text-xs leading-5 ${error ? 'bg-error/10 text-error' : 'bg-secondary-container text-on-secondary-container'}`} role="status">{error || success}</p>}
        {isProcessing && <div role="status" aria-live="polite"><div className="h-2 overflow-hidden rounded-full bg-surface-container-high"><div className="h-full bg-secondary-fixed" style={{ width: `${progress}%` }} /></div><p className="mt-2 text-center text-xs text-on-surface-variant">剪辑中 {progress}%</p></div>}

        <button type="button" onClick={isProcessing ? () => void cancelExport() : () => void handleExport()} disabled={!isProcessing && !canExport} className="flex min-h-11 w-full items-center justify-center gap-2 rounded-full bg-primary px-4 text-sm font-semibold text-on-primary disabled:cursor-not-allowed disabled:opacity-50">
          {isProcessing ? <Square size={17} /> : <Scissors size={17} />}{isProcessing ? '取消剪辑' : '合并导出'}
        </button>
        <div className="grid grid-cols-2 gap-2">
          <button type="button" onClick={() => { if (firstVideo && duration > 0) { const format = outputFormatFor(firstVideo, false); setHistory(createEditHistory(createVideoEditProject({ inputPath: firstVideo.path, durationSecs: duration, fps, outputPath: withVideoSuffix(firstVideo.path, '_edited', format, options.outputDir) }))); setSelectedSegmentId('segment-1'); handleSeek(0); } setError(''); setSuccess(''); }} className="flex min-h-10 items-center justify-center gap-2 rounded-full bg-surface-container-low text-xs font-semibold text-on-surface"><RotateCcw size={15} />重置项目</button>
          <button type="button" onClick={() => removeFile(firstVideo.id)} className="flex min-h-10 items-center justify-center gap-2 rounded-full bg-surface-container-low text-xs font-semibold text-on-surface"><Trash2 size={15} />移除视频</button>
        </div>
      </aside>
      </div>
    </div>
  );
}
