import { useAppStore } from '../../stores/appStore';
import { isVideoFormat } from '../../utils/mediaFormat';
import { formatBytes } from '../../utils/formatBytes';
import { Cpu } from 'lucide-react';

const PRESET_RATIO: Record<string, number> = {
  light: 0.8,
  standard: 0.5,
  extreme: 0.2,
};

export default function StatusBar() {
  const { isProcessing, progress, totalSaved, files, videoPreset } = useAppStore();

  const videoFiles = files.filter((f) => isVideoFormat(f.format));
  const pendingVideos = videoFiles.filter((f) => f.status === 'pending');
  const completedFiles = files.filter((f) => f.status === 'completed' && f.outputSize);
  const totalOriginal = completedFiles.reduce((sum, f) => sum + f.originalSize, 0);
  const totalOutput = completedFiles.reduce((sum, f) => sum + (f.outputSize || 0), 0);
  const ratio = totalOriginal > 0 && totalOutput > 0
    ? `${(totalOriginal / totalOutput).toFixed(1)}:1`
    : null;
  const saved = totalOriginal > 0 && totalOutput > 0 ? totalOriginal - totalOutput : 0;

  const pendingTotalSize = pendingVideos.reduce((sum, f) => sum + f.originalSize, 0);
  const estimatedRatio = PRESET_RATIO[videoPreset] ?? 0.5;
  const estimatedOutput = pendingTotalSize * estimatedRatio;
  const estimatedSaved = pendingTotalSize - estimatedOutput;
  const estimatedPct = Math.round((1 - estimatedRatio) * 100);

  const showEstimate = pendingVideos.length > 0 && !isProcessing;
  const showCompleted = completedFiles.length > 0;

  return (
    <footer
      className="fixed bottom-0 left-64 right-0 h-12 z-50 flex items-center justify-between px-8 bg-surface-container-highest border-t border-outline-variant/30 font-mono-status text-[11px] uppercase tracking-wider"
    >
      {/* Left: GPU status + original → estimated output */}
      <div className="flex items-center gap-8">
        <span className="flex items-center gap-2 text-on-surface-variant">
          <Cpu size={14} />
          GPU 加速: 已开启
        </span>
        {(showEstimate || showCompleted) && (
          <>
            <span className="text-on-surface-variant">
              原始大小: {formatBytes(showEstimate ? pendingTotalSize : totalOriginal)}
            </span>
            <span className="opacity-30 text-on-surface-variant">→</span>
            <span className="text-secondary font-bold">
              预计输出: {formatBytes(showEstimate ? estimatedOutput : totalOutput)}
            </span>
          </>
        )}
        {isProcessing && (
          <span className="text-on-surface-variant">
            处理进度: {progress}%
          </span>
        )}
      </div>

      {/* Right: compression ratio, saved space, engine status */}
      <div className="flex items-center gap-8">
        {(showEstimate || showCompleted) && (
          <>
            <span className="text-on-surface-variant">
              压缩比: {showEstimate ? `~${estimatedPct}%` : ratio}
            </span>
            <span className="text-on-surface-variant">
              节省空间: {formatBytes(showEstimate ? estimatedSaved : saved)}
            </span>
          </>
        )}
        <span className="flex items-center gap-2 text-primary font-bold">
          <span className="relative flex h-2 w-2">
            {isProcessing && (
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-secondary-fixed opacity-75" />
            )}
            <span className="relative inline-flex rounded-full h-2 w-2 bg-secondary-fixed" />
          </span>
          引擎状态: {isProcessing ? '处理中' : '稳定'}
        </span>
      </div>
    </footer>
  );
}
