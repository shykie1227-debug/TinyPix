import { Play, Scissors, Waves } from 'lucide-react';
import type { FileItem } from '../../stores/appStore';
import { formatBytes } from '../../utils/formatBytes';

interface VideoPreviewStageProps {
  file?: FileItem;
  assetUrl: string;
  posterUrl: string;
  mode: 'preview' | 'timeline' | 'waveform' | 'gif';
  currentTime?: string;
  totalTime?: string;
  progressPercent?: number;
  overlayStatus?: string;
  videoPlaybackFailed: boolean;
  videoPreviewFailed: boolean;
  videoPreviewLoading: boolean;
  onVideoPlaybackFailed: () => void;
  onVideoPreviewFailed: () => void;
  title?: string;
  subtitle?: string;
}

export default function VideoPreviewStage({
  file,
  assetUrl,
  posterUrl,
  mode,
  currentTime = '00:04.2',
  totalTime = '00:15.0',
  progressPercent = 28,
  overlayStatus = '正在选取片段',
  videoPlaybackFailed,
  videoPreviewFailed,
  videoPreviewLoading,
  onVideoPlaybackFailed,
  onVideoPreviewFailed,
  title,
  subtitle,
}: VideoPreviewStageProps) {
  const Icon = mode === 'timeline' ? Scissors : mode === 'waveform' ? Waves : Play;

  if (mode === 'waveform') {
    return (
      <div className="rounded-[16px] bg-[#111111] min-h-[330px] overflow-hidden relative flex items-center justify-center">
        <div
          data-testid="video-preview-poster"
          className="relative z-10 flex min-h-[330px] w-full items-center justify-center overflow-hidden text-white"
        >
          <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,#2a2a2a_0%,#121212_72%)]" />
          <div className="w-full max-w-xl relative z-10">
            <div className="h-36 rounded-[18px] bg-surface-container-low flex items-end justify-center gap-2 px-8 py-6 mb-8">
              {Array.from({ length: 24 }).map((_, index) => (
                <span
                  key={index}
                  className="w-2 rounded-full bg-secondary-fixed"
                  style={{ height: `${20 + ((index * 17) % 72)}%`, opacity: index % 4 === 0 ? 1 : 0.55 }}
                />
              ))}
            </div>
            <div className="flex flex-col items-center">
              <Icon size={36} className="text-on-surface mb-4" />
              <h3 className="text-on-surface text-2xl font-bold">{title}</h3>
              <p className="text-on-surface-variant text-sm mt-2">{subtitle}</p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="rounded-[16px] bg-[#111111] min-h-[330px] overflow-hidden relative flex items-center justify-center">
      <div
        data-testid="video-preview-poster"
        className="relative z-10 flex min-h-[330px] w-full items-center justify-center overflow-hidden text-white"
      >
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_center,#2a2a2a_0%,#121212_72%)]" />

        {!videoPlaybackFailed && (
          <video
            data-testid="video-preview-player"
            key={assetUrl}
            className="absolute inset-0 z-10 h-full w-full bg-black object-contain"
            src={assetUrl}
            poster={posterUrl || undefined}
            controls
            preload="metadata"
            playsInline
            onError={onVideoPlaybackFailed}
          />
        )}

        {videoPlaybackFailed && posterUrl && (
          <img
            src={posterUrl}
            alt={`${file?.name || '视频'} 预览图`}
            className="absolute inset-0 z-10 h-full w-full object-contain opacity-90"
            onError={onVideoPreviewFailed}
          />
        )}

        {videoPreviewLoading && (
          <div className="relative z-20 flex flex-col items-center justify-center p-8 text-center">
            <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-white/10 backdrop-blur">
              <div className="h-6 w-6 animate-spin rounded-full border-2 border-white/30 border-t-secondary-fixed" />
            </div>
            <p className="text-base font-bold">正在生成预览...</p>
            <p className="mt-2 max-w-sm text-xs leading-5 text-white/75">
              正在提取视频帧，请稍候
            </p>
          </div>
        )}

        {videoPlaybackFailed && !videoPreviewLoading && (
          <div className="relative z-20 flex flex-col items-center justify-center p-8 text-center">
            {!posterUrl && (
              <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-white/10 backdrop-blur">
                <Play size={26} className="text-secondary-fixed" fill="currentColor" />
              </div>
            )}
            <p className="text-base font-bold">内嵌播放器暂不支持此编码</p>
            <p className="mt-2 max-w-sm text-xs leading-5 text-white/75">
              {videoPreviewFailed
                ? '当前视频无法生成缩略图，但 FFmpeg 本地处理仍可继续。'
                : posterUrl
                  ? 'FFmpeg 本地处理仍可继续'
                  : '正在生成缩略图...'}
            </p>
            {!posterUrl && file && (
              <div className="mt-4 rounded-xl bg-white/5 px-4 py-3 text-left">
                <p className="text-xs font-medium text-white/90 truncate max-w-[240px]">{file.name}</p>
                <p className="mt-1 text-[10px] text-white/60">
                  {file.format.toUpperCase()} · {formatBytes(file.originalSize)}
                </p>
              </div>
            )}
          </div>
        )}

        {!videoPlaybackFailed && !videoPreviewLoading && (
          <div className="pointer-events-none absolute inset-0 z-20 bg-gradient-to-b from-black/10 via-transparent to-black/25" />
        )}
      </div>
      {mode === 'timeline' && (
        <div className="absolute left-5 right-5 bottom-5 z-20 rounded-[12px] bg-black/70 p-3 backdrop-blur">
          <div className="h-1.5 rounded-full bg-white/20 overflow-hidden">
            <div className="h-full w-2/5 bg-secondary-fixed rounded-full" />
          </div>
          <div className="flex justify-between text-white/70 text-[10px] mt-2 font-mono">
            <span>00:00</span>
            <span>拖动时间点后导出</span>
          </div>
        </div>
      )}
      {mode === 'gif' && (
        <div className="absolute bottom-6 left-6 right-6 z-20 bg-white/10 backdrop-blur-md rounded-[18px] p-4 border border-white/20">
          <div className="flex items-center justify-between font-mono-status text-[10px] text-white mb-2">
            <span>{currentTime} / {totalTime}</span>
            <span className="text-secondary-fixed">{overlayStatus}</span>
          </div>
          <div className="w-full h-1 bg-white/20 rounded-full overflow-hidden">
            <div
              className="h-full bg-secondary-fixed rounded-full transition-all duration-300"
              style={{ width: `${progressPercent}%` }}
            />
          </div>
        </div>
      )}
    </div>
  );
}
