import { useEffect, useState } from 'react';
import ReactCrop, { type Crop } from 'react-image-crop';
import 'react-image-crop/dist/ReactCrop.css';
import { Image as ImageIcon } from 'lucide-react';
import type { FileItem } from '../../stores/appStore';

interface ImagePreviewStageProps {
  file?: FileItem;
  assetUrl: string;
  fallbackSrc?: string;
  cssFilter: string;
  combinedTransform: string;
  crop: Crop;
  onCropChange: (c: Crop) => void;
  onCropComplete: (c: Crop) => void;
  imageError: boolean;
  onImageError: () => void;
  onImageLoad: () => void;
}

export default function ImagePreviewStage({
  file,
  assetUrl,
  fallbackSrc,
  cssFilter,
  combinedTransform,
  crop,
  onCropChange,
  onCropComplete,
  imageError,
  onImageError,
  onImageLoad,
}: ImagePreviewStageProps) {
  const [activeSrc, setActiveSrc] = useState(assetUrl || fallbackSrc || '');

  useEffect(() => {
    setActiveSrc(assetUrl || fallbackSrc || '');
  }, [assetUrl, fallbackSrc]);

  const handleImageError = () => {
    if (fallbackSrc && activeSrc !== fallbackSrc) {
      setActiveSrc(fallbackSrc);
      return;
    }
    onImageError();
  };

  return (
    <div className="grid min-h-[430px] grid-rows-[1fr_auto] gap-4">
      <div className="flex min-h-[340px] items-center justify-center rounded-[18px] bg-[#f4f4f1] p-5">
        {imageError || !activeSrc ? (
          <div className="flex flex-col items-center justify-center text-center text-on-surface-variant">
            <ImageIcon size={28} className="mb-3 opacity-60" />
            <p className="text-sm font-semibold text-on-surface">图片预览加载失败</p>
            <p className="mt-1 text-xs">请确认文件仍在本地磁盘，导出功能仍可继续使用。</p>
          </div>
        ) : (
          <ReactCrop
            crop={crop}
            onChange={(_, percentCrop) => {
              onCropChange(percentCrop);
              onCropComplete(percentCrop);
            }}
            keepSelection
          >
            <img
              data-testid="image-crop-preview"
              src={activeSrc}
              alt={file?.name || '图片预览'}
              className="block max-h-[420px] max-w-full object-contain transition-transform"
              style={{
                transform: combinedTransform,
                filter: cssFilter,
              }}
              onLoad={onImageLoad}
              onError={handleImageError}
            />
          </ReactCrop>
        )}
      </div>
      <div className="grid grid-cols-3 gap-3">
        {['裁切与旋转', '质量压缩', '格式转换'].map((item) => (
          <span key={item} className="rounded-[12px] bg-surface-container-low px-4 py-3 text-center text-xs font-bold text-on-surface">
            {item}
          </span>
        ))}
      </div>
    </div>
  );
}
