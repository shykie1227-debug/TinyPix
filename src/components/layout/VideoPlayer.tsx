import { useRef, useCallback } from 'react';

interface VideoPlayerProps {
  src?: string;
  poster?: string;
  filter?: string;
  onTimeUpdate?: (currentTime: number) => void;
  onDurationChange?: (duration: number) => void;
  onPlayStateChange?: (isPlaying: boolean) => void;
  onError?: () => void;
  onReady?: (video: HTMLVideoElement) => void;
}

export type VideoPlayerHandle = {
  seek: (time: number) => void;
  play: () => void;
  pause: () => void;
};

/**
 * 模块4：视频预览播放模块（VideoPlayerModule）
 * HTML5 <video> 元素 + 错误回退 + CSS filter 应用
 */
export default function VideoPlayer({
  src,
  poster,
  filter,
  onTimeUpdate,
  onDurationChange,
  onPlayStateChange,
  onError,
  onReady,
}: VideoPlayerProps) {
  const videoRef = useRef<HTMLVideoElement>(null);

  const setRef = useCallback(
    (node: HTMLVideoElement | null) => {
      videoRef.current = node;
      if (node && onReady) onReady(node);
    },
    [onReady]
  );

  return (
    <video
      ref={setRef}
      key={src}
      data-testid="video-preview-player"
      className="absolute inset-0 w-full h-full object-contain"
      src={src || undefined}
      poster={poster}
      preload="metadata"
      playsInline
      onLoadedMetadata={(e) => {
        const nextDuration = e.currentTarget.duration;
        if (Number.isFinite(nextDuration) && nextDuration > 0) {
          onDurationChange?.(nextDuration);
        }
      }}
      onTimeUpdate={(e) => onTimeUpdate?.(e.currentTarget.currentTime)}
      onPlay={() => onPlayStateChange?.(true)}
      onPause={() => onPlayStateChange?.(false)}
      onError={() => onError?.()}
      style={filter ? { filter } : undefined}
    />
  );
}
