import { useCallback, useEffect, useMemo, useState } from 'react';
import { ImageDown, X } from 'lucide-react';
import { useAppStore, type FileItem } from '../../stores/appStore';
import { IMAGE_INPUT_EXTENSIONS } from '../../utils/imageCapabilities';
import { isImageFormat } from '../../utils/mediaFormat';
import DropZone from '../layout/DropZone';
import MediaPreviewStage from '../preview/MediaPreviewStage';
import ImageControlsPanel from './ImageControlsPanel';

interface ImageWorkbenchProps {
  onProcess?: () => void;
  estimateSizeBatch?: (items: Array<{ path: string; format: string; quality: number }>) => Promise<number>;
}

export default function ImageWorkbench(_props: ImageWorkbenchProps) {
  const { files, addFiles, removeFile } = useAppStore();
  const imageFiles = useMemo(() => files.filter((file) => isImageFormat(file.format)), [files]);
  const [selectedImageId, setSelectedImageId] = useState<string>();
  const selectedImage = imageFiles.find((file) => file.id === selectedImageId) ?? imageFiles[0];
  const previewFiles = useMemo(
    () => selectedImage
      ? [selectedImage, ...imageFiles.filter((file) => file.id !== selectedImage.id)]
      : [],
    [imageFiles, selectedImage]
  );

  useEffect(() => {
    if (!selectedImageId || !imageFiles.some((file) => file.id === selectedImageId)) {
      setSelectedImageId(imageFiles[0]?.id);
    }
  }, [imageFiles, selectedImageId]);

  const handleFilesAdded = useCallback((newFiles: FileItem[]) => {
    addFiles(newFiles.filter((file) => isImageFormat(file.format)));
  }, [addFiles]);

  return (
    <div className="tinypix-image-workbench grid min-w-0 grid-cols-[minmax(0,1fr)_clamp(280px,30vw,340px)] gap-5 p-5" role="region" aria-label="图片工具工作区">
      <section className="min-w-0 space-y-4">
        <div className="flex min-w-0 items-center gap-3">
          <ImageDown size={22} className="shrink-0 text-secondary" />
          <div className="min-w-0">
            <h2 className="truncate text-xl font-semibold text-on-surface">图片处理</h2>
            <p className="truncate text-xs text-on-surface-variant">裁切、旋转、尺寸、色彩、格式与隐私清理一次完成</p>
          </div>
        </div>

        {imageFiles.length === 0 ? (
          <DropZone
            onFilesAdded={handleFilesAdded}
            mediaType="image"
            title="拖拽图片到这里"
            subtitle="支持 JPG、PNG、WebP、AVIF、BMP、TIFF 和 PSD"
            acceptButton="选择本地图片"
            formats={['JPG', 'PNG', 'WebP', 'PSD']}
            extensions={[...IMAGE_INPUT_EXTENSIONS]}
          />
        ) : (
          <>
            <MediaPreviewStage mode="image" title="图片预览" subtitle="裁切和调整会在这里实时预览" files={previewFiles} mediaType="image" />
            <div className="space-y-2">
              {imageFiles.map((file) => (
                <div key={file.id} className="flex min-w-0 items-center gap-3 rounded-[14px] border border-outline-variant/10 bg-surface-container-lowest px-4 py-3">
                  <button
                    type="button"
                    aria-label={`预览 ${file.name}`}
                    aria-pressed={selectedImage?.id === file.id}
                    onClick={() => setSelectedImageId(file.id)}
                    className="min-w-0 flex-1 text-left"
                  >
                    <p className="truncate text-sm font-semibold text-on-surface">{file.name}</p>
                    <p className="mt-0.5 truncate text-[11px] text-on-surface-variant">{file.format} · {Math.round(file.originalSize / 1024)} KB</p>
                    {file.error && <p className="mt-1 break-words text-[11px] text-error">{file.error}</p>}
                  </button>
                  <span className="shrink-0 rounded-full bg-secondary-fixed px-2 py-1 text-[10px] font-bold text-on-secondary-fixed">
                    {selectedImage?.id === file.id ? '当前预览' : file.status === 'processing' ? '处理中' : file.status === 'completed' ? '已完成' : file.status === 'error' ? '失败' : '就绪'}
                  </span>
                  <button type="button" onClick={() => removeFile(file.id)} className="min-h-10 min-w-10 rounded-full p-2 text-on-surface-variant hover:bg-surface-container-low" aria-label={`移除 ${file.name}`}>
                    <X size={16} />
                  </button>
                </div>
              ))}
            </div>
          </>
        )}
      </section>

      <aside className="min-w-0 overflow-y-auto pr-1">
        <ImageControlsPanel />
      </aside>
    </div>
  );
}
