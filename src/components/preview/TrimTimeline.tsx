import { useCallback, useRef, useState } from 'react';
import { formatTime } from '../../utils/timeFormat';

interface TrimTimelineProps {
  duration: number;
  trimStart: number;
  trimEnd: number;
  currentTime: number;
  onTrimStartChange: (v: number) => void;
  onTrimEndChange: (v: number) => void;
  onSeek: (v: number) => void;
}

// Generate time ruler marks based on duration
const generateTimeMarks = (duration: number): string[] => {
  const marks: string[] = [];
  let interval = 30; // 30 second intervals

  if (duration > 3600) {
    interval = 300; // 5 minutes for long videos
  } else if (duration > 1800) {
    interval = 60; // 1 minute
  }

  for (let t = 0; t <= duration; t += interval) {
    marks.push(formatTime(t));
  }

  // Always add the final time
  if (marks[marks.length - 1] !== formatTime(duration)) {
    marks.push(formatTime(duration));
  }

  return marks;
};

export default function TrimTimeline({
  duration,
  trimStart,
  trimEnd,
  currentTime,
  onTrimStartChange,
  onTrimEndChange,
  onSeek,
}: TrimTimelineProps) {
  const trackRef = useRef<HTMLDivElement>(null);
  const [dragging, setDragging] = useState<'start' | 'end' | 'playhead' | null>(null);

  const timeMarks = generateTimeMarks(duration);

  // Calculate position as percentage
  const getPercent = useCallback(
    (time: number) => (duration > 0 ? (time / duration) * 100 : 0),
    [duration]
  );

  // Calculate time from mouse position
  const getTimeFromX = useCallback(
    (clientX: number) => {
      if (!trackRef.current) return 0;
      const rect = trackRef.current.getBoundingClientRect();
      const percent = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
      return percent * duration;
    },
    [duration]
  );

  const handleMouseDown = useCallback(
    (e: React.MouseEvent, type: 'start' | 'end' | 'playhead') => {
      e.preventDefault();
      e.stopPropagation();
      setDragging(type);

      const handleMouseMove = (moveEvent: MouseEvent) => {
        const newTime = getTimeFromX(moveEvent.clientX);

        if (type === 'start') {
          const clamped = Math.min(newTime, trimEnd - 0.1);
          onTrimStartChange(Math.max(0, clamped));
        } else if (type === 'end') {
          const clamped = Math.max(newTime, trimStart + 0.1);
          onTrimEndChange(Math.min(duration, clamped));
        } else if (type === 'playhead') {
          onSeek(Math.max(0, Math.min(duration, newTime)));
        }
      };

      const handleMouseUp = () => {
        setDragging(null);
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
      };

      document.addEventListener('mousemove', handleMouseMove);
      document.addEventListener('mouseup', handleMouseUp);
    },
    [duration, trimStart, trimEnd, onTrimStartChange, onTrimEndChange, onSeek, getTimeFromX]
  );

  // Handle track click to seek
  const handleTrackClick = useCallback(
    (e: React.MouseEvent) => {
      if (duration <= 0) return;
      const newTime = getTimeFromX(e.clientX);
      onSeek(Math.max(0, Math.min(duration, newTime)));
    },
    [duration, onSeek, getTimeFromX]
  );

  const startPercent = getPercent(trimStart);
  const endPercent = getPercent(trimEnd);
  const playheadPercent = getPercent(currentTime);

  return (
    <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 overflow-hidden">
      {/* Time Ruler */}
      <div className="relative px-5 pt-3 pb-2 border-b border-outline-variant/10">
        <div className="flex items-end justify-between text-[10px] font-mono-status text-on-surface-variant/50 select-none">
          {timeMarks.map((mark, i) => (
            <span key={i}>{mark}</span>
          ))}
        </div>
      </div>

      {/* Track Area */}
      <div className="relative px-5 py-6" ref={trackRef} onClick={handleTrackClick}>
        {/* Playhead (vertical lime line) */}
        <div
          className="absolute top-0 bottom-0 z-30 pointer-events-none"
          style={{ left: `${playheadPercent}%` }}
        >
          <div className="absolute -top-1 left-1/2 -translate-x-1/2 w-0 h-0 border-l-[6px] border-r-[6px] border-t-[8px] border-l-transparent border-r-transparent border-t-secondary-fixed" />
          <div className="w-0.5 h-full bg-secondary-fixed" />
        </div>

        {/* Video Track */}
        <div className="relative h-16 rounded-[8px] bg-surface-container overflow-hidden">
          {/* Full video bar */}
          <div className="absolute inset-0 bg-surface-container-high/50" />

          {/* Selected region highlight */}
          <div
            className="absolute top-0 bottom-0 bg-secondary-fixed/30 border-y-2 border-secondary-fixed"
            style={{
              left: `${startPercent}%`,
              width: `${endPercent - startPercent}%`,
            }}
          />

          {/* Waveform decoration (simplified as uniform bars) */}
          <div className="absolute inset-0 flex items-center gap-px px-2 opacity-20">
            {Array.from({ length: 60 }).map((_, i) => (
              <div
                key={i}
                className="flex-1 bg-on-surface rounded-full"
                style={{ height: `${30 + ((i * 17) % 70)}%` }}
              />
            ))}
          </div>

          {/* Start Handle */}
          <div
            className={`absolute top-0 bottom-0 z-20 cursor-ew-resize ${dragging === 'start' ? 'opacity-100' : 'opacity-90 hover:opacity-100'}`}
            style={{ left: `${startPercent}%`, transform: 'translateX(-100%)' }}
            onMouseDown={(e) => handleMouseDown(e, 'start')}
          >
            <div className="absolute top-1/2 -translate-y-1/2 left-0 w-4 h-10 bg-secondary-fixed rounded-md flex items-center justify-center">
              <div className="w-1 h-4 bg-on-secondary-fixed rounded-full" />
            </div>
            <div className="absolute top-1/2 -translate-y-1/2 left-0 w-1 h-10 bg-secondary-fixed rounded-full -ml-0.5" />
            {/* Triangle handle */}
            <div
              className="absolute -top-2 left-1/2 -translate-x-1/2 w-0 h-0 border-l-[6px] border-r-[6px] border-t-[8px] border-l-transparent border-r-transparent border-t-secondary-fixed"
              style={{ transform: 'translateX(-50%)' }}
            />
          </div>

          {/* End Handle */}
          <div
            className={`absolute top-0 bottom-0 z-20 cursor-ew-resize ${dragging === 'end' ? 'opacity-100' : 'opacity-90 hover:opacity-100'}`}
            style={{ left: `${endPercent}%` }}
            onMouseDown={(e) => handleMouseDown(e, 'end')}
          >
            <div className="absolute top-1/2 -translate-y-1/2 left-0 w-4 h-10 bg-secondary-fixed rounded-md flex items-center justify-center">
              <div className="w-1 h-4 bg-on-secondary-fixed rounded-full" />
            </div>
            <div className="absolute top-1/2 -translate-y-1/2 left-0 w-1 h-10 bg-secondary-fixed rounded-full -ml-0.5" />
            {/* Triangle handle */}
            <div
              className="absolute -top-2 left-1/2 -translate-x-1/2 w-0 h-0 border-l-[6px] border-r-[6px] border-t-[8px] border-l-transparent border-r-transparent border-t-secondary-fixed"
              style={{ transform: 'translateX(-50%)' }}
            />
          </div>
        </div>

        {/* Labels */}
        <div className="flex justify-between mt-3 text-[10px] font-mono-status text-on-surface-variant/60">
          <span>入点: {formatTime(trimStart)}</span>
          <span>选中: {formatTime(trimEnd - trimStart)}</span>
          <span>出点: {formatTime(trimEnd)}</span>
        </div>
      </div>
    </div>
  );
}
