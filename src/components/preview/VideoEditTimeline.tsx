import type { VideoEditSegment } from '../../modules/video/editProject';
import { formatTime } from '../../utils/timeFormat';

interface VideoEditTimelineProps {
  duration: number;
  currentTime: number;
  segments: VideoEditSegment[];
  selectedSegmentId?: string;
  zoom: number;
  filmstripUrl?: string;
  waveformUrl?: string;
  inPoint: number;
  outPoint: number;
  onSeek: (time: number) => void;
  onInPointChange: (time: number) => void;
  onOutPointChange: (time: number) => void;
  onSelectSegment: (id: string) => void;
}

export default function VideoEditTimeline({
  duration,
  currentTime,
  segments,
  selectedSegmentId,
  zoom,
  filmstripUrl,
  waveformUrl,
  inPoint,
  outPoint,
  onSeek,
  onInPointChange,
  onOutPointChange,
  onSelectSegment,
}: VideoEditTimelineProps) {
  const safeDuration = duration > 0 ? duration : 1;
  const playheadPercent = Math.max(0, Math.min(100, (currentTime / safeDuration) * 100));

  return (
    <section className="overflow-hidden rounded-[18px] border border-outline-variant/15 bg-surface-container-lowest" aria-label="单轨时间线" role="region">
      <div className="flex items-center justify-between border-b border-outline-variant/10 px-4 py-2 text-[10px] text-on-surface-variant">
        <span>{formatTime(0)}</span>
        <span>单轨 · 非破坏编辑 · {Math.round(zoom * 100)}%</span>
        <span>{formatTime(duration)}</span>
      </div>
      <div className="overflow-x-auto px-4 py-4">
        <div className="relative min-w-full" style={{ width: `${Math.max(1, zoom) * 100}%` }}>
          <div className="relative h-24 overflow-hidden rounded-xl bg-[#202020]">
            {filmstripUrl && (
              <img src={filmstripUrl} alt="视频缩略图条" className="absolute inset-0 h-14 w-full object-cover opacity-80" />
            )}
            {waveformUrl && (
              <img src={waveformUrl} alt="真实音频波形" className="absolute inset-x-0 bottom-0 h-10 w-full object-fill opacity-70" />
            )}
            <div className="absolute inset-0">
              {segments.map((segment, index) => {
                const left = (segment.startSecs / safeDuration) * 100;
                const width = ((segment.endSecs - segment.startSecs) / safeDuration) * 100;
                const selected = selectedSegmentId === segment.id;
                return (
                  <button
                    type="button"
                    key={segment.id}
                    aria-label={`片段 ${index + 1} ${segment.included ? '保留' : '已排除'}`}
                    onClick={(event) => {
                      event.stopPropagation();
                      onSelectSegment(segment.id);
                      onSeek(segment.startSecs);
                    }}
                    className={`absolute inset-y-0 border-2 text-[10px] font-semibold transition-colors ${
                      segment.included
                        ? 'border-secondary-fixed/70 bg-secondary-fixed/10 text-white'
                        : 'border-error/70 bg-black/70 text-error line-through'
                    } ${selected ? 'ring-2 ring-white ring-inset' : ''}`}
                    style={{ left: `${left}%`, width: `${width}%` }}
                  >
                    <span className="absolute bottom-1 left-1 rounded bg-black/60 px-1.5 py-0.5">
                      {segment.included ? `片段 ${index + 1}` : '已排除'}
                    </span>
                  </button>
                );
              })}
            </div>
            <div className="pointer-events-none absolute inset-y-0 z-20 w-0.5 bg-secondary-fixed" style={{ left: `${playheadPercent}%` }}>
              <span className="absolute -left-1.5 top-0 h-0 w-0 border-x-[7px] border-t-[9px] border-x-transparent border-t-secondary-fixed" />
            </div>
          </div>
          <div className="mt-3 grid grid-cols-3 gap-2">
            <input type="range" min={0} max={Math.max(0, outPoint - 0.01)} step={0.01} value={inPoint} aria-label="拖动入点边界" onChange={(event) => onInPointChange(Number(event.currentTarget.value))} className="accent-secondary-fixed" />
            <input type="range" min={0} max={safeDuration} step={0.01} value={Math.min(currentTime, safeDuration)} aria-label="时间线播放头" onChange={(event) => onSeek(Number(event.currentTarget.value))} className="accent-secondary-fixed" />
            <input type="range" min={Math.min(safeDuration, inPoint + 0.01)} max={safeDuration} step={0.01} value={outPoint} aria-label="拖动出点边界" onChange={(event) => onOutPointChange(Number(event.currentTarget.value))} className="accent-secondary-fixed" />
          </div>
        </div>
      </div>
    </section>
  );
}
