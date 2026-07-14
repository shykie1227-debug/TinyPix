import { useCallback, useState } from 'react';

type DragTarget = 'start' | 'end' | 'playhead' | null;

interface UseTimelineDragArgs {
  duration: number;
  trimStart: number;
  trimEnd: number;
  onTrimStartChange: (v: number) => void;
  onTrimEndChange: (v: number) => void;
  onSeek: (v: number) => void;
  trackRef: React.RefObject<HTMLDivElement | null>;
}

export interface UseTimelineDragResult {
  dragging: DragTarget;
  handleMouseDown: (e: React.MouseEvent, type: NonNullable<DragTarget>) => void;
  handleTrackClick: (e: React.MouseEvent) => void;
}

/**
 * 模块7：时间线拖拽控制模块（TimelineDragModule）
 * 起止标记、播放头的鼠标拖拽逻辑（含约束校验）
 */
export function useTimelineDrag({
  duration,
  trimStart,
  trimEnd,
  onTrimStartChange,
  onTrimEndChange,
  onSeek,
  trackRef,
}: UseTimelineDragArgs): UseTimelineDragResult {
  const [dragging, setDragging] = useState<DragTarget>(null);

  const getTimeFromX = useCallback(
    (clientX: number) => {
      if (!trackRef.current) return 0;
      const rect = trackRef.current.getBoundingClientRect();
      const percent = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
      return percent * duration;
    },
    [duration, trackRef]
  );

  const handleMouseDown = useCallback(
    (e: React.MouseEvent, type: NonNullable<DragTarget>) => {
      e.preventDefault();
      e.stopPropagation();
      setDragging(type);

      const handleMouseMove = (moveEvent: MouseEvent) => {
        const newTime = getTimeFromX(moveEvent.clientX);
        if (type === 'start') {
          onTrimStartChange(Math.min(Math.max(0, newTime), trimEnd - 0.1));
        } else if (type === 'end') {
          onTrimEndChange(Math.max(Math.min(duration, newTime), trimStart + 0.1));
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

  const handleTrackClick = useCallback(
    (e: React.MouseEvent) => {
      if (e.target !== trackRef.current) return;
      const newTime = getTimeFromX(e.clientX);
      onSeek(Math.max(0, Math.min(duration, newTime)));
    },
    [duration, onSeek, getTimeFromX, trackRef]
  );

  return { dragging, handleMouseDown, handleTrackClick };
}
