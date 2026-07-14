import { useState } from 'react';
import { CheckCircle2, XCircle, Loader2, Circle } from 'lucide-react';
import { convertFileSrc } from '@tauri-apps/api/core';
import { useAppStore } from '../../stores/appStore';
import type { FileItem } from '../../stores/appStore';

interface FileListItemProps {
  file: FileItem;
}

const formatFileSize = (bytes: number): string => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const isImageFile = (format: string) =>
  /^(jpg|jpeg|png|gif|webp|bmp|tiff|avif|ico)$/i.test(format);

export default function FileListItem({ file }: FileListItemProps) {
  const [imgError, setImgError] = useState(false);
  const { progress } = useAppStore();

  const StatusIcon = () => {
    switch (file.status) {
      case 'pending':
        return <Circle size={16} className="text-on-surface-variant" />;
      case 'processing':
        return <Loader2 size={16} className="text-secondary animate-spin" />;
      case 'completed':
        return <CheckCircle2 size={16} className="text-secondary" />;
      case 'error':
        return <XCircle size={16} className="text-error" />;
      default:
        return null;
    }
  };

  const showThumbnail = isImageFile(file.format) && file.path && !imgError;
  const thumbnailSrc = showThumbnail ? convertFileSrc(file.path) : null;

  return (
    <div className="flex items-center gap-6 p-4 bg-surface-container-low rounded-2xl group hover:bg-surface-container-lowest transition-all border border-transparent hover:border-secondary-fixed/30">
      {/* Thumbnail */}
      <div className="w-16 h-16 rounded-xl bg-surface-container-highest flex-shrink-0 flex items-center justify-center overflow-hidden">
        {thumbnailSrc ? (
          <img
            src={thumbnailSrc}
            alt={file.name}
            className="w-full h-full object-cover"
            onError={() => setImgError(true)}
          />
        ) : (
          <span className="text-[10px] text-on-surface-variant uppercase font-medium">{file.format}</span>
        )}
      </div>

      {/* File Info */}
      <div className="flex-grow min-w-0">
        <div className="flex justify-between items-start mb-2">
          <p className="font-semibold text-on-surface truncate text-base">
            {file.name}
          </p>
          <p className="font-medium text-[12px] shrink-0 font-mono-status">
            {file.status === 'processing' && (
              <span className="text-secondary">{progress}% 处理中</span>
            )}
            {file.status === 'pending' && (
              <span className="text-on-surface-variant">待处理</span>
            )}
            {file.status === 'completed' && (
              <span className="text-secondary">已完成</span>
            )}
            {file.status === 'error' && (
              <span className="text-error">出错</span>
            )}
          </p>
        </div>
        {/* Progress Bar (Demo UI style) */}
        <div className="w-full h-1.5 bg-surface-container-highest rounded-full overflow-hidden">
          <div
            className="h-full progress-lime transition-all duration-1000"
            style={{ width: file.status === 'processing' ? `${progress}%` : file.status === 'completed' ? '100%' : '0%' }}
          />
        </div>
      </div>

      {/* File Meta */}
      <div className="flex items-center gap-2 shrink-0">
        <span className="text-[11px] text-on-surface-variant font-mono-status">
          {formatFileSize(file.originalSize)}
        </span>
        {file.status === 'completed' && file.outputSize && (
          <span className="text-[11px] text-secondary font-medium font-mono-status">
            ↓ {formatFileSize(file.outputSize)}
          </span>
        )}
      </div>

      {/* Status Icon */}
      <div className="shrink-0">
        <StatusIcon />
      </div>
    </div>
  );
}