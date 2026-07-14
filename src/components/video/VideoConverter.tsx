import { useState, useCallback } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore } from '../../stores/appStore';
import { Loader2, Trash2, Zap } from 'lucide-react';
import { withVideoSuffix } from '../../utils/videoOutput';
import { formatBytes } from '../../utils/formatBytes';
import { useVideoProgress } from '../../hooks/useVideoProgress';
import { isVideoFormat } from '../../utils/mediaFormat';
import ToolOptionCard from '../common/ToolOptionCard';

const VIDEO_FORMATS = ['MP4', 'MOV', 'AVI', 'MKV', 'WebM'] as const;
type VideoFormat = (typeof VIDEO_FORMATS)[number];

const VIDEO_CODEC_OPTIONS = [
  { value: 'h264', label: 'H.264 (AVC) — 高兼容' },
  { value: 'h265', label: 'H.265 (HEVC) — 高压缩' },
  { value: 'av1', label: 'AV1 — 最新一代' },
  { value: 'vp9', label: 'VP9 — Web 优化' },
  { value: 'prores', label: 'ProRes — 专业制作' },
] as const;

const RESOLUTIONS = ['原始', '1080p', '720p', '480p', '4K', '自定义'] as const;
type Resolution = (typeof RESOLUTIONS)[number];

const FPS_OPTIONS = ['原始', '30fps', '24fps', '60fps'] as const;

const BITRATE_OPTIONS = [128000, 192000, 256000, 320000] as const;

const RECOMMENDED_CODECS: Record<VideoFormat, { video: string; audio: string }> = {
  MP4: { video: 'h264', audio: 'aac' },
  MOV: { video: 'h264', audio: 'aac' },
  AVI: { video: 'h264', audio: 'mp3' },
  MKV: { video: 'h265', audio: 'aac' },
  WebM: { video: 'vp9', audio: 'opus' },
};

const COMPATIBLE_VIDEO_CODECS: Record<VideoFormat, string[]> = {
  MP4: ['h264', 'h265', 'av1'],
  MOV: ['h264', 'h265', 'prores'],
  AVI: ['h264'],
  MKV: ['h264', 'h265', 'av1', 'vp9'],
  WebM: ['av1', 'vp9'],
};

type QueueItemState = {
  status: 'waiting' | 'processing' | 'completed' | 'error';
  message?: string;
};

const RESOLUTION_MAP: Record<string, { width: number | null; height: number | null }> = {
  '原始': { width: null, height: null },
  '1080p': { width: 1920, height: 1080 },
  '720p': { width: 1280, height: 720 },
  '480p': { width: 854, height: 480 },
  '4K': { width: 3840, height: 2160 },
};

const labelClass = 'font-label-caps text-label-caps uppercase opacity-50 block mb-4';

export default function VideoConverter() {
  const { files, options, removeFile } = useAppStore();

  const [targetFormat, setTargetFormat] = useState<VideoFormat>('MP4');
  const [videoCodec, setVideoCodec] = useState<string>('h264');
  const [rfValue, setRfValue] = useState<number>(23);
  const [resolution, setResolution] = useState<Resolution>('1080p');
  const [customWidth, setCustomWidth] = useState<number>(1920);
  const [customHeight, setCustomHeight] = useState<number>(1080);
  const [fps, setFps] = useState<number | null>(30);
  const [audioCodec, setAudioCodec] = useState<string>('aac');
  const [bitrate, setBitrate] = useState<number>(192000);

  const [isConverting, setIsConverting] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const [customResError, setCustomResError] = useState(false);
  const [queueState, setQueueState] = useState<Record<string, QueueItemState>>({});
  useVideoProgress(setProgress);

  const videoFiles = files.filter((f) => isVideoFormat(f.format));
  const firstVideo = videoFiles[0];

  const handleFormatChange = useCallback((fmt: VideoFormat) => {
    setTargetFormat(fmt);
    const recommended = RECOMMENDED_CODECS[fmt];
    setVideoCodec(recommended.video);
    setAudioCodec(recommended.audio);
  }, []);

  const handleResolutionChange = useCallback((res: Resolution) => {
    setResolution(res);
  }, []);

  const handleFpsChange = useCallback((fpsStr: string) => {
    if (fpsStr === '原始') {
      setFps(null);
    } else {
      setFps(parseInt(fpsStr));
    }
  }, []);

  const getResolutionValues = useCallback((): { width: number | null; height: number | null } => {
    if (resolution === '自定义') {
      return { width: customWidth, height: customHeight };
    }
    return RESOLUTION_MAP[resolution] || { width: null, height: null };
  }, [resolution, customWidth, customHeight]);

  const validateCustomResolution = useCallback((w: number, h: number): boolean => {
    if (w < 64 || w > 7680 || h < 64 || h > 7680) return false;
    if (w % 2 !== 0 || h % 2 !== 0) return false;
    return true;
  }, []);

  const handleCustomWidthChange = useCallback((val: number) => {
    setCustomWidth(val);
    setCustomResError(!validateCustomResolution(val, customHeight));
  }, [customHeight, validateCustomResolution]);

  const handleCustomHeightChange = useCallback((val: number) => {
    setCustomHeight(val);
    setCustomResError(!validateCustomResolution(customWidth, val));
  }, [customWidth, validateCustomResolution]);

  const isCurrentVideoCodecCompatible = COMPATIBLE_VIDEO_CODECS[targetFormat].includes(videoCodec);

  const isExportDisabled = useCallback((): boolean => {
    if (!firstVideo || isConverting) return true;
    if (resolution === '自定义' && customResError) return true;
    if (!isCurrentVideoCodecCompatible) return true;
    return false;
  }, [firstVideo, isConverting, resolution, customResError, isCurrentVideoCodecCompatible]);

  const handleConvert = useCallback(async () => {
    if (
      videoFiles.length === 0 ||
      isConverting ||
      (resolution === '自定义' && customResError) ||
      !isCurrentVideoCodecCompatible
    ) return;

    setIsConverting(true);
    setProgress(0);
    setError('');
    setStatus('正在转换格式...');
    setQueueState(
      Object.fromEntries(videoFiles.map((video) => [video.id, { status: 'waiting' as const }]))
    );

    try {
      const res = getResolutionValues();
      let successCount = 0;
      let failureCount = 0;
      for (const video of videoFiles) {
        setQueueState((prev) => ({
          ...prev,
          [video.id]: { status: 'processing', message: '转换中' },
        }));
        try {
          await invoke('convert_video_format', {
            inputPath: video.path,
            outputPath: withVideoSuffix(
              video.path,
              '_converted',
              targetFormat.toLowerCase(),
              options.outputDir
            ),
            targetFormat: targetFormat.toLowerCase(),
            quality: rfValue,
            videoCodec: videoCodec,
            resolutionWidth: res.width,
            resolutionHeight: res.height,
            fps: fps,
            audioCodec: audioCodec,
            audioBitrate: bitrate,
          });
          successCount += 1;
          setQueueState((prev) => ({
            ...prev,
            [video.id]: { status: 'completed', message: '完成' },
          }));
        } catch (itemError) {
          failureCount += 1;
          const message = String(itemError);
          setQueueState((prev) => ({
            ...prev,
            [video.id]: { status: 'error', message: `${video.name} 转换失败：${message}` },
          }));
        }
        setStatus(`格式转换中 ${successCount}/${videoFiles.length}`);
        setProgress(Math.round(((successCount + failureCount) / videoFiles.length) * 100));
      }
      setProgress(100);
      setStatus(
        failureCount > 0
          ? `格式转换完成 ${successCount}/${videoFiles.length}，失败 ${failureCount}`
          : `格式转换完成 ${successCount}/${videoFiles.length}`
      );
      if (failureCount > 0) {
        setError('部分文件转换失败，请查看处理队列');
      }
      if (successCount > 0 && options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setIsConverting(false);
    }
  }, [videoFiles, isConverting, targetFormat, rfValue, videoCodec, fps, audioCodec, bitrate, getResolutionValues, customResError, options.outputDir, options.openAfterProcess, isCurrentVideoCodecCompatible]);

  const isProRes = videoCodec === 'prores';
  const isFlac = audioCodec === 'flac';
  const isWebM = targetFormat === 'WebM';

  return (
    <div className="flex flex-col gap-4 sticky top-4 max-h-[calc(100vh-8rem)] overflow-y-auto custom-scrollbar pr-1">
      {/* Video Info Card */}
      {videoFiles.length > 0 && (
        <ToolOptionCard>
          <p className="font-label-caps text-label-caps uppercase opacity-50 mb-3">当前视频</p>
          <p className="text-on-surface text-sm truncate font-medium">{firstVideo.name}</p>
          <p className="text-on-surface-variant text-xs mt-1">
            大小: {formatBytes(firstVideo.originalSize)}
          </p>
        </ToolOptionCard>
      )}

      {/* Multi-file notice */}
      {videoFiles.length > 1 && (
        <div className="bg-tertiary-container/60 rounded-xl px-4 py-2.5 text-on-tertiary-container text-xs">
          将按当前参数批量处理 {videoFiles.length} 个视频
        </div>
      )}

      {videoFiles.length > 0 && (
        <ToolOptionCard>
          <div className="flex items-center justify-between mb-4">
            <label className="font-label-caps text-label-caps uppercase opacity-50">处理队列</label>
            <span className="text-[11px] text-on-surface-variant opacity-60">
              {videoFiles.length}/50
            </span>
          </div>
          <div className="flex flex-col gap-2">
            {videoFiles.map((video) => {
              const item = queueState[video.id];
              const itemStatus = item?.status ?? 'waiting';
              const statusLabel =
                itemStatus === 'processing'
                  ? '转换中'
                  : itemStatus === 'completed'
                    ? '完成'
                    : itemStatus === 'error'
                      ? '失败'
                      : '等待中';
              return (
                <div
                  key={video.id}
                  className="rounded-[14px] bg-surface-container-low px-3 py-2.5 border border-outline-variant/10"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="text-xs font-semibold text-on-surface truncate">{video.name}</p>
                      <div className="mt-1 flex flex-wrap gap-1.5 text-[10px] text-on-surface-variant">
                        <span className="rounded-full bg-surface-container-high px-2 py-0.5">
                          源 {video.format}
                        </span>
                        <span className="rounded-full bg-primary-container text-on-primary-container px-2 py-0.5">
                          目标 {targetFormat}
                        </span>
                        <span
                          className={
                            itemStatus === 'error'
                              ? 'rounded-full bg-error/10 text-error px-2 py-0.5'
                              : 'rounded-full bg-surface-container-high px-2 py-0.5'
                          }
                        >
                          {statusLabel}
                        </span>
                      </div>
                    </div>
                    <button
                      type="button"
                      aria-label={`删除 ${video.name}`}
                      onClick={() => removeFile(video.id)}
                      disabled={isConverting}
                      className="shrink-0 rounded-full p-1.5 text-on-surface-variant hover:bg-surface-container-high disabled:opacity-40 disabled:cursor-not-allowed"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                  {item?.status === 'error' && item.message && (
                    <p className="mt-2 text-[11px] text-error">{item.message}</p>
                  )}
                </div>
              );
            })}
          </div>
        </ToolOptionCard>
      )}

      <ToolOptionCard>
        <label className={labelClass}>目标格式</label>
        <div className="grid grid-cols-2 gap-2">
          {VIDEO_FORMATS.map((fmt) => (
            <button
              key={fmt}
              onClick={() => handleFormatChange(fmt)}
              className={
                fmt === 'WebM' ? 'col-span-2 ' : ''
              }
              style={{ gridColumn: fmt === 'WebM' ? 'span 2' : undefined }}
            >
              <div
                className={
                  targetFormat === fmt
                    ? 'py-2.5 rounded-[980px] bg-primary text-on-primary font-bold transition-all text-xs w-full'
                    : 'py-2.5 rounded-[980px] bg-surface-container hover:bg-surface-container-high text-on-surface font-bold transition-all text-xs w-full'
                }
              >
                {fmt}
              </div>
            </button>
          ))}
        </div>
      </ToolOptionCard>

      <ToolOptionCard>
        <label className={labelClass}>视频编码</label>

        {/* Encoder Select */}
        <div className="flex flex-col gap-1 mb-5">
          <label htmlFor="video-codec-select" className="text-xs text-on-surface-variant opacity-60">编码器</label>
          <select
            id="video-codec-select"
            value={videoCodec}
            onChange={(e) => setVideoCodec(e.target.value)}
            className="bg-surface-container border border-outline-variant/30 rounded-[12px] px-4 py-2.5 text-sm text-on-surface appearance-none cursor-pointer focus:ring-2 focus:ring-primary/30"
          >
            {VIDEO_CODEC_OPTIONS.map((codec) => (
              <option
                key={codec.value}
                value={codec.value}
                disabled={!COMPATIBLE_VIDEO_CODECS[targetFormat].includes(codec.value)}
              >
                {codec.label}
              </option>
            ))}
          </select>
        </div>

        {/* RF Slider */}
        <div className="flex flex-col gap-1 mb-2">
          <div className="flex justify-between items-center">
            <label className="text-xs text-on-surface-variant opacity-60">画质控制</label>
            <span className="bg-secondary-container text-on-secondary-container px-2 py-0.5 rounded-full text-[11px] font-bold font-mono-status">
              RF {rfValue}
            </span>
          </div>
        </div>
        <input
          type="range"
          min="0"
          max="51"
          value={rfValue}
          onChange={(e) => setRfValue(Number(e.target.value))}
          disabled={isProRes}
          aria-label="画质控制"
          className={`w-full custom-slider cursor-pointer mb-2 ${isProRes ? 'opacity-40 cursor-not-allowed' : ''}`}
        />
        <div className="flex justify-between font-mono-status text-[10px] opacity-40 mb-5">
          <span>更小文件</span>
          <span>更高画质</span>
        </div>

        {/* Resolution */}
        <div className="flex flex-col gap-1 mb-5">
          <label className="text-xs text-on-surface-variant opacity-60">分辨率</label>
          <div className="grid grid-cols-3 gap-2">
            {RESOLUTIONS.map((res) => (
              <button
                key={res}
                onClick={() => handleResolutionChange(res)}
                className={
                  resolution === res
                    ? 'py-2 px-2 bg-primary text-on-primary rounded-[12px] text-xs font-bold transition-colors'
                    : 'py-2 px-2 bg-surface-container hover:bg-surface-container-high rounded-[12px] text-xs font-bold transition-colors'
                }
              >
                {res}
              </button>
            ))}
          </div>
          {resolution === '自定义' && (
            <div className="grid grid-cols-2 gap-2 mt-2">
              <input
                type="number"
                min="64"
                max="7680"
                value={customWidth}
                onChange={(e) => handleCustomWidthChange(Number(e.target.value))}
                placeholder="1920"
                className={`bg-surface-container-low border rounded-[12px] px-4 py-2.5 text-sm focus:ring-1 focus:ring-secondary-fixed outline-none ${customResError ? 'border-error' : 'border-outline-variant/20'}`}
              />
              <input
                type="number"
                min="64"
                max="7680"
                value={customHeight}
                onChange={(e) => handleCustomHeightChange(Number(e.target.value))}
                placeholder="1080"
                className={`bg-surface-container-low border rounded-[12px] px-4 py-2.5 text-sm focus:ring-1 focus:ring-secondary-fixed outline-none ${customResError ? 'border-error' : 'border-outline-variant/20'}`}
              />
            </div>
          )}
        </div>

        {/* Framerate */}
        <div className="flex flex-col gap-1">
          <label className="text-xs text-on-surface-variant opacity-60">帧率</label>
          <div className="grid grid-cols-4 gap-2">
            {FPS_OPTIONS.map((fpsStr) => (
              <button
                key={fpsStr}
                onClick={() => handleFpsChange(fpsStr)}
                className={
                  (fps === null && fpsStr === '原始') || fps === parseInt(fpsStr)
                    ? 'py-2 bg-primary text-on-primary rounded-[12px] text-xs font-bold transition-colors'
                    : 'py-2 bg-surface-container hover:bg-surface-container-high rounded-[12px] text-xs font-bold transition-colors'
                }
              >
                {fpsStr}
              </button>
            ))}
          </div>
        </div>
      </ToolOptionCard>

      <ToolOptionCard>
        <label className={labelClass}>音频编码</label>

        {/* Audio Encoder Select */}
        <div className="flex flex-col gap-1 mb-4">
          <label htmlFor="audio-codec-select" className="text-xs text-on-surface-variant opacity-60">音频编码器</label>
          <select
            id="audio-codec-select"
            value={audioCodec}
            onChange={(e) => setAudioCodec(e.target.value)}
            disabled={isWebM}
            className={`bg-surface-container border border-outline-variant/30 rounded-[12px] px-4 py-2.5 text-sm text-on-surface appearance-none cursor-pointer focus:ring-2 focus:ring-primary/30 ${isWebM ? 'opacity-50 cursor-not-allowed' : ''}`}
          >
            <option value="aac">AAC — 高兼容</option>
            <option value="mp3">MP3 — 通用</option>
            <option value="opus">Opus — 高品质</option>
            <option value="flac">FLAC — 无损</option>
            <option value="ac3">AC3 — 环绕声</option>
          </select>
        </div>

        {/* Bitrate */}
        <div className="flex flex-col gap-1">
          <label className="text-xs text-on-surface-variant opacity-60">比特率</label>
          <div className="grid grid-cols-4 gap-2">
            {BITRATE_OPTIONS.map((br) => (
              <button
                key={br}
                onClick={() => !isFlac && setBitrate(br)}
                disabled={isFlac}
                className={
                  bitrate === br
                    ? 'py-2 bg-primary text-on-primary rounded-[12px] text-xs font-bold transition-colors'
                    : `py-2 bg-surface-container rounded-[12px] text-xs font-bold transition-colors ${isFlac ? 'opacity-40 cursor-not-allowed' : 'hover:bg-surface-container-high'}`
                }
              >
                {br / 1000}k
              </button>
            ))}
          </div>
        </div>
      </ToolOptionCard>

      {/* Error */}
      {error && <p className="text-error text-xs text-center">{error}</p>}

      {/* Progress */}
      {(isConverting || progress > 0) && (
        <div>
          <div className="w-full bg-surface-container-high rounded-full h-2">
            <div
              className="bg-secondary-fixed h-2 rounded-full transition-all duration-300"
              style={{ width: `${progress}%` }}
            />
          </div>
          <p className="text-on-surface-variant mt-1.5 text-center text-xs">
            {status || `${progress}%`}
          </p>
        </div>
      )}

      {/* CTA Button */}
      <button
        onClick={handleConvert}
        disabled={isExportDisabled()}
        className={
          isExportDisabled()
            ? 'w-full py-3 rounded-[980px] bg-surface-container-high text-on-surface-variant cursor-not-allowed text-sm font-semibold flex items-center justify-center gap-2'
            : 'w-full py-3 rounded-[980px] bg-primary text-on-primary hover:opacity-80 active:opacity-70 transition-opacity text-sm font-semibold flex items-center justify-center gap-2'
        }
      >
        {isConverting ? (
          <span className="flex items-center justify-center gap-2">
            <Loader2 size={18} className="animate-spin" />
            转换中...
          </span>
        ) : (
          <>
            <Zap size={20} />
            立即导出
          </>
        )}
      </button>
    </div>
  );
}
