import { useState, useCallback, useEffect, useMemo, useRef } from 'react';
import { CloudUpload } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { open } from '@tauri-apps/plugin-dialog';
import type { FileItem } from '../../stores/appStore';

interface DropZoneProps {
  onFilesAdded: (files: FileItem[]) => void;
  mediaType?: 'image' | 'video';
  title?: string;
  subtitle?: string;
  acceptButton?: string;
  formats?: readonly string[];
  extensions?: readonly string[];
}

const COPY = {
  image: {
    title: '拖拽图片文件到这里',
    subtitle: '支持 JPG/JPEG、PNG、WebP、AVIF、BMP、TIFF/TIF、PSD（单次最多 50 张）',
    formats: ['JPG', 'PNG', 'WebP', 'PSD'],
    acceptButton: '选择本地图片',
  },
  video: {
    title: '拖拽视频文件到这里',
    subtitle: '支持 MP4、MOV、MKV、AVI、WebM（最大 4GB / 文件）',
    formats: ['MP4', 'MOV', 'MKV', 'WebM'],
    acceptButton: '选择本地视频',
  },
} as const;

interface FileMetadata {
  file_name: string;
  extension: string;
  mime_type: string;
  size_bytes: number;
}

const IMAGE_EXTENSIONS = new Set(['jpg', 'jpeg', 'png', 'webp', 'bmp', 'tiff', 'tif', 'avif', 'psd']);
const VIDEO_EXTENSIONS = new Set(['mp4', 'mov', 'webm', 'avi', 'mkv']);
const MAX_VIDEO_BYTES = 4 * 1024 * 1024 * 1024;

const createId = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

const fileNameFromPath = (path: string) => path.split(/[\\/]/).pop() || path;
const extensionFromName = (name: string) => name.split('.').pop()?.toLowerCase() || '';
const supportedExtensions = (mediaType: 'image' | 'video', extensions?: readonly string[]) =>
  extensions
    ? new Set(extensions.map((extension) => extension.replace(/^\./, '').toLowerCase()))
    : mediaType === 'image'
      ? IMAGE_EXTENSIONS
      : VIDEO_EXTENSIONS;

async function createFileItem(path: string, allowedExtensions: ReadonlySet<string>, fallbackSize = 0): Promise<FileItem | null> {
  const fallbackName = fileNameFromPath(path);
  const fallbackExt = extensionFromName(fallbackName);
  if (!allowedExtensions.has(fallbackExt)) return null;

  try {
    const meta = await invoke<FileMetadata>('read_file_metadata', { path });
    const ext = (meta.extension || fallbackExt).toLowerCase();
    if (!allowedExtensions.has(ext)) return null;
    return {
      id: createId(),
      name: meta.file_name || fallbackName,
      format: ext.toUpperCase(),
      originalSize: meta.size_bytes || fallbackSize,
      status: 'pending',
      path,
    };
  } catch {
    return {
      id: createId(),
      name: fallbackName,
      format: fallbackExt.toUpperCase() || 'UNKNOWN',
      originalSize: fallbackSize,
      status: 'pending',
      path,
    };
  }
}

function isTauriEnvironment(): boolean {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;
}

export default function DropZone({
  onFilesAdded,
  mediaType = 'image',
  title,
  subtitle,
  acceptButton,
  formats,
  extensions,
}: DropZoneProps) {
  const [isDragOver, setIsDragOver] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const defaultCopy = COPY[mediaType];
  const copy = {
    title: title || defaultCopy.title,
    subtitle: subtitle || defaultCopy.subtitle,
    formats: formats || defaultCopy.formats,
    acceptButton: acceptButton || defaultCopy.acceptButton,
  };
  const allowedExtensions = useMemo(
    () => supportedExtensions(mediaType, extensions),
    [extensions, mediaType]
  );
  const unlistenRef = useRef<(() => void) | null>(null);

  const handleFiles = useCallback(
    async (paths: string[], sizes?: Map<string, number>) => {
      setError(null);
      if (mediaType === 'video') {
        const hasOversizedVideo = paths.some((path) => {
          const size = sizes?.get(path) || 0;
          return size > MAX_VIDEO_BYTES;
        });
        if (hasOversizedVideo) {
          setError('文件超过 4GB 限制');
          return;
        }
      }

      const items = await Promise.all(
        paths.map((path) => {
          const size = sizes?.get(path) || 0;
          return createFileItem(path, allowedExtensions, size);
        })
      );
      const mediaFiles = items.filter((item): item is FileItem => item !== null);
      if (mediaFiles.length > 0) {
        onFilesAdded(mediaFiles);
      } else {
        setError(mediaType === 'video' ? '不支持的视频格式' : '不支持的图片格式');
      }
    },
    [allowedExtensions, mediaType, onFilesAdded]
  );

  useEffect(() => {
    if (!isTauriEnvironment()) return;

    let mounted = true;

    const setupTauriDragDrop = async () => {
      try {
        const { getCurrentWebview } = await import('@tauri-apps/api/webview');
        const unlisten = await getCurrentWebview().onDragDropEvent((event) => {
          if (!mounted) return;

          const payload = event.payload;

          if (payload.type === 'enter' || payload.type === 'over') {
            setIsDragOver(true);
          } else if (payload.type === 'leave') {
            setIsDragOver(false);
          } else if (payload.type === 'drop') {
            setIsDragOver(false);
            if (payload.paths && payload.paths.length > 0) {
              handleFiles(payload.paths);
            }
          }
        });

        if (mounted) {
          unlistenRef.current = unlisten;
        } else {
          unlisten();
        }
      } catch (error) {
        console.warn('Failed to setup Tauri drag drop event listener:', error);
      }
    };

    setupTauriDragDrop();

    return () => {
      mounted = false;
      if (unlistenRef.current) {
        unlistenRef.current();
        unlistenRef.current = null;
      }
    };
  }, [handleFiles]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setIsDragOver(false);

      const droppedFiles = Array.from(e.dataTransfer.files);
      if (droppedFiles.length === 0) return;

      const sizes = new Map<string, number>();
      const paths = droppedFiles.map((file) => {
        const path = (file as File & { path?: string }).path || file.name;
        sizes.set(path, file.size);
        return path;
      });
      handleFiles(paths, sizes);
    },
    [handleFiles]
  );

  const handleBrowse = useCallback(async () => {
    try {
      const selected = await open({
        multiple: true,
        filters: [{
          name: mediaType === 'image' ? 'Images' : 'Videos',
          extensions: Array.from(allowedExtensions),
        }],
      });

      if (selected) {
        const fileList = Array.isArray(selected) ? selected : [selected];
        const items = await Promise.all(
          fileList.map((file) => createFileItem(String(file), allowedExtensions))
        );
        const mediaFiles = items.filter((item): item is FileItem => item !== null);

        onFilesAdded(mediaFiles);
      }
    } catch {
      // cancelled or error
    }
  }, [allowedExtensions, mediaType, onFilesAdded]);

  return (
    <div
      className={`tinypix-drop-zone bg-surface-container-lowest rounded-[18px] p-10 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] flex flex-col items-center justify-center border-2 border-dashed border-outline-variant/40 hover:border-secondary-fixed transition-colors group min-h-[440px] ${isDragOver ? 'drop-zone-active' : ''}`}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      {/* Icon */}
      <div className="tinypix-drop-zone-icon w-20 h-20 rounded-[18px] bg-surface-container-low flex items-center justify-center mb-6 group-hover:opacity-80 transition-opacity duration-180">
        <CloudUpload size={40} className="text-on-surface" />
      </div>

      {/* Title */}
      <h3
        className="text-on-surface mb-2 font-semibold text-2xl leading-8"
      >
        {copy.title}
      </h3>

      {/* Subtitle */}
      <p
        className="text-on-surface-variant text-center text-sm leading-5 opacity-70"
      >
        {copy.subtitle}
      </p>
      {error && (
        <p className="mt-4 text-error text-sm font-semibold" role="alert">
          {error}
        </p>
      )}

      {/* Format chips */}
      <div className="tinypix-drop-zone-formats flex gap-3 mt-8">
        {copy.formats.map((fmt) => (
          <div
            key={fmt}
            className="px-4 py-2 bg-surface-container-low rounded-lg flex items-center gap-2 font-mono-status text-mono-status"
          >
            <span className="w-1.5 h-1.5 rounded-full bg-secondary-fixed" />
            {fmt}
          </div>
        ))}
      </div>

      {/* Browse button */}
      <button
        onClick={handleBrowse}
        className="tinypix-drop-zone-browse mt-10 min-h-11 px-8 py-3 bg-primary text-on-primary rounded-full hover:opacity-80 active:opacity-70 transition-opacity font-label-caps text-label-caps"
      >
        {copy.acceptButton}
      </button>
    </div>
  );
}
