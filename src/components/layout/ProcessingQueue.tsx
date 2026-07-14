import { convertFileSrc } from '@tauri-apps/api/core';
import { useState } from 'react';
import { Loader2, X } from 'lucide-react';
import type { FileItem } from '../../stores/appStore';

interface ProcessingQueueProps {
  files: FileItem[];
  onRemove?: (id: string) => void;
}

const fmt = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const isImageFile = (format: string) =>
  /^(jpg|jpeg|png|gif|webp|bmp|tiff|avif|ico)$/i.test(format);

function QueueItem({ file, onRemove }: { file: FileItem; onRemove?: (id: string) => void }) {
  const [imgError, setImgError] = useState(false);

  const isProcessing = file.status === 'processing';
  const isCompleted = file.status === 'completed';
  const isError = file.status === 'error';

  const showThumbnail = isImageFile(file.format) && file.path && !imgError;
  const thumbnailSrc = showThumbnail ? convertFileSrc(file.path) : null;

  const savedPct = file.originalSize > 0
    ? Math.round((1 - (file.outputSize ?? 0) / file.originalSize) * 100)
    : 0;

  return (
    <div
      className={`
        flex items-center gap-6 p-4 rounded-2xl transition-all group
        ${isError ? 'bg-error-container/30' : 'bg-surface-container-low hover:bg-white'}
      `}
    >
      {/* Thumbnail */}
      <div className="w-16 h-16 rounded-xl bg-surface-container-highest flex-shrink-0 flex items-center justify-center overflow-hidden">
        {thumbnailSrc ? (
          <img src={thumbnailSrc} alt={file.name} className="w-full h-full object-cover" onError={() => setImgError(true)} />
        ) : (
          <span className="text-[10px] text-on-surface-variant uppercase">{file.format}</span>
        )}
      </div>

      {/* Info */}
      <div className="flex-grow min-w-0">
        <div className="flex justify-between items-start mb-2">
          <p className="font-bold text-on-surface truncate" style={{ fontFamily: "'Manrope', sans-serif", fontSize: '16px', lineHeight: '24px', fontWeight: 700 }}>
            {file.name}
          </p>
          {isProcessing && <p className="shrink-0 ml-4 text-secondary" style={{ fontFamily: "'Manrope', sans-serif", fontSize: '12px', fontWeight: 600 }}>处理中</p>}
          {isCompleted && file.outputSize && <p className="shrink-0 ml-4 text-secondary" style={{ fontFamily: "'Geist', monospace", fontSize: '12px' }}>↓ {fmt(file.outputSize)} (节省 {savedPct}%)</p>}
          {isError && <p className="shrink-0 ml-4 text-error" style={{ fontFamily: "'Manrope', sans-serif", fontSize: '12px', fontWeight: 600 }}>出错</p>}
          {!isProcessing && !isCompleted && !isError && <p className="shrink-0 ml-4 text-on-surface-variant" style={{ fontFamily: "'Manrope', sans-serif", fontSize: '12px' }}>待处理</p>}
        </div>
        <div className="w-full h-1.5 bg-surface-container-highest rounded-full overflow-hidden">
          <div
            className={`h-full rounded-full transition-all duration-1000 ${isProcessing ? 'bg-secondary-fixed' : isCompleted ? 'bg-secondary' : 'bg-surface-container-high'}`}
            style={{ width: isProcessing ? '60%' : isCompleted ? '100%' : '0%' }}
          />
        </div>
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2 shrink-0">
        {!isProcessing && (
          <button
            onClick={() => onRemove?.(file.id)}
            className="w-10 h-10 rounded-full flex items-center justify-center hover:bg-error/10 text-error opacity-0 group-hover:opacity-100 transition-all"
          >
            <X size={18} />
          </button>
        )}
        {isProcessing && <Loader2 size={18} className="text-secondary animate-spin" />}
      </div>
    </div>
  );
}

export default function ProcessingQueue({ files, onRemove }: ProcessingQueueProps) {
  const queueFiles = files.filter((f) => f.status !== 'pending');

  return (
    <div className="bg-surface-container-lowest rounded-[18px] p-8 shadow-[0px_10px_30px_rgba(0,0,0,0.04)]">
      <div className="flex justify-between items-center mb-6">
        <h4 className="text-on-surface" style={{ fontFamily: "'Hanken Grotesk', sans-serif", fontSize: '24px', lineHeight: '32px', fontWeight: 600 }}>处理队列</h4>
        <span className="text-on-surface-variant uppercase" style={{ fontFamily: "'Geist', monospace", fontSize: '11px', lineHeight: '14px', fontWeight: 500, opacity: 0.5 }}>{queueFiles.length} 个文件在队列中</span>
      </div>
      <div className="space-y-4">
        {queueFiles.map((file) => (
          <QueueItem key={file.id} file={file} onRemove={onRemove} />
        ))}
      </div>
    </div>
  );
}
