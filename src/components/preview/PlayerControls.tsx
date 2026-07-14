import { Play, Pause, Volume2 } from 'lucide-react';

interface PlayerControlsProps {
  currentTime: number;
  duration: number;
  isPlaying: boolean;
  onPlayPause: () => void;
  onSeek: (time: number) => void;
  formatTime: (secs: number) => string;
}

export default function PlayerControls({
  currentTime,
  duration,
  isPlaying,
  onPlayPause,
  onSeek,
  formatTime,
}: PlayerControlsProps) {
  const safeDuration = Number.isFinite(duration) && duration > 0 ? duration : 0;
  const safeCurrentTime = Math.max(0, Math.min(safeDuration, currentTime));
  const progressPercent = safeDuration > 0 ? (safeCurrentTime / safeDuration) * 100 : 0;

  return (
    <div className="absolute bottom-0 left-0 right-0 bg-gradient-to-t from-black/70 via-black/30 to-transparent pt-10 pb-4 px-5">
      <div className="relative mb-3 h-4">
        <input
          type="range"
          min={0}
          max={safeDuration || 1}
          step={0.01}
          value={safeCurrentTime}
          disabled={safeDuration === 0}
          aria-label="视频播放进度"
          onChange={(event) => onSeek(Number(event.currentTarget.value))}
          className="absolute inset-0 z-10 h-4 w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
        />
        <div
          className="absolute left-0 right-0 top-1/2 h-1 -translate-y-1/2 rounded-full bg-white/20"
          aria-hidden="true"
        >
          <div
            className="h-full rounded-full bg-secondary-fixed"
            style={{ width: `${progressPercent}%` }}
          />
          <div
            className="absolute top-1/2 h-3 w-3 -translate-y-1/2 rounded-full bg-secondary-fixed shadow-md"
            style={{ left: `calc(${progressPercent}% - 6px)` }}
          />
        </div>
      </div>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={onPlayPause}
            aria-label={isPlaying ? '暂停视频' : '播放视频'}
            disabled={safeDuration === 0}
            className="w-10 h-10 rounded-full bg-white/20 backdrop-blur-sm flex items-center justify-center hover:bg-white/30 transition-colors"
          >
            {isPlaying ? (
              <Pause size={24} className="text-white" fill="currentColor" />
            ) : (
              <Play size={24} className="text-white" fill="currentColor" />
            )}
          </button>
          <button
            type="button"
            aria-label="音量"
            className="text-white/60 hover:text-white transition-colors"
          >
            <Volume2 size={20} />
          </button>
        </div>
        <div className="flex items-center gap-4">
          <span className="font-mono-status text-[12px] text-white/90 tracking-tight">
            {formatTime(safeCurrentTime)} / {formatTime(safeDuration)}
          </span>
        </div>
      </div>
    </div>
  );
}
