import { useEffect, useRef, useState } from 'react';
import ReactCrop, { type Crop } from 'react-image-crop';
import 'react-image-crop/dist/ReactCrop.css';
import { Image as ImageIcon } from 'lucide-react';
import type { ColorAdjust, FileItem } from '../../stores/appStore';
import { applySharpen, buildCanvasFilter, previewGeometry } from '../../utils/imagePreviewRenderer';

interface ImagePreviewStageProps {
  file?: FileItem;
  assetUrl: string;
  rotation: number;
  flipH: boolean;
  flipV: boolean;
  color: ColorAdjust;
  opacityPercent: number;
  resizeTargetW?: number;
  resizeTargetH?: number;
  crop: Crop;
  onCropChange: (c: Crop) => void;
  onCropComplete: (c: Crop) => void;
  imageError: boolean;
  loading?: boolean;
  progress?: number;
  errorMessage?: string;
  onRetry?: () => void;
  onCancel?: () => void;
  onImageError: () => void;
  onImageLoad: () => void;
}

export default function ImagePreviewStage({
  file,
  assetUrl,
  rotation,
  flipH,
  flipV,
  color,
  opacityPercent,
  resizeTargetW,
  resizeTargetH,
  crop,
  onCropChange,
  onCropComplete,
  imageError,
  loading = false,
  progress = 0,
  errorMessage,
  onRetry,
  onCancel,
  onImageError,
  onImageLoad,
}: ImagePreviewStageProps) {
  const [activeSrc, setActiveSrc] = useState(assetUrl || '');
  const [sourceImage, setSourceImage] = useState<HTMLImageElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    setActiveSrc(assetUrl || '');
    setSourceImage(null);
  }, [assetUrl]);

  useEffect(() => {
    if (!activeSrc) return;
    let active = true;
    const image = new Image();
    image.onload = () => {
      if (!active) return;
      setSourceImage(image);
      onImageLoad();
    };
    image.onerror = () => {
      if (active) handleImageError();
    };
    image.src = activeSrc;
    return () => {
      active = false;
      image.onload = null;
      image.onerror = null;
    };
  }, [activeSrc]);

  useEffect(() => {
    if (!sourceImage || !canvasRef.current) return;
    const frame = requestAnimationFrame(() => {
      const canvas = canvasRef.current;
      if (!canvas) return;
      const sourceWidth = sourceImage.naturalWidth || sourceImage.width;
      const sourceHeight = sourceImage.naturalHeight || sourceImage.height;
      if (!sourceWidth || !sourceHeight) return;

      const rotated = previewGeometry(sourceWidth, sourceHeight, rotation);
      const offscreen = document.createElement('canvas');
      offscreen.width = rotated.width;
      offscreen.height = rotated.height;
      const offscreenContext = offscreen.getContext('2d');
      if (!offscreenContext) return;
      offscreenContext.save();
      offscreenContext.translate(rotated.width / 2, rotated.height / 2);
      offscreenContext.rotate((rotation * Math.PI) / 180);
      offscreenContext.scale(flipH ? -1 : 1, flipV ? -1 : 1);
      offscreenContext.drawImage(sourceImage, -sourceWidth / 2, -sourceHeight / 2, sourceWidth, sourceHeight);
      offscreenContext.restore();

      const target = previewGeometry(
        sourceWidth,
        sourceHeight,
        rotation,
        resizeTargetW,
        resizeTargetH
      );
      const limit = Math.min(1, 1600 / Math.max(target.width, target.height));
      canvas.width = Math.max(1, Math.round(target.width * limit));
      canvas.height = Math.max(1, Math.round(target.height * limit));
      const context = canvas.getContext('2d');
      if (!context) return;
      context.clearRect(0, 0, canvas.width, canvas.height);
      context.save();
      context.filter = buildCanvasFilter(color);
      context.globalAlpha = Math.max(0, Math.min(100, opacityPercent)) / 100;
      context.drawImage(offscreen, 0, 0, canvas.width, canvas.height);
      context.restore();

      if (color.sharpness > 0) {
        const imageData = context.getImageData(0, 0, canvas.width, canvas.height);
        imageData.data.set(applySharpen(imageData.data, canvas.width, canvas.height, color.sharpness));
        context.putImageData(imageData, 0, 0);
      }
    });
    return () => cancelAnimationFrame(frame);
  }, [color, flipH, flipV, opacityPercent, resizeTargetH, resizeTargetW, rotation, sourceImage]);

  const handleImageError = () => {
    onImageError();
  };

  return (
    <div className="grid min-h-[430px] grid-rows-[1fr_auto] gap-4">
      <div className="flex min-h-[340px] items-center justify-center rounded-[18px] bg-[#f4f4f1] p-5">
        {loading ? (
          <div className="flex flex-col items-center justify-center text-center text-on-surface-variant" role="status">
            <span className="mb-3 h-7 w-7 animate-spin rounded-full border-2 border-outline-variant border-t-secondary-fixed" />
            <p className="text-sm font-semibold text-on-surface">正在生成图片预览 {Math.round(progress)}%</p>
            {onCancel && <button type="button" onClick={onCancel} className="mt-3 min-h-10 rounded-full bg-surface-container-high px-5 text-xs font-semibold text-on-surface">取消预览</button>}
          </div>
        ) : imageError || !activeSrc ? (
          <div className="flex flex-col items-center justify-center text-center text-on-surface-variant">
            <ImageIcon size={28} className="mb-3 opacity-60" />
            <p className="text-sm font-semibold text-on-surface">图片预览加载失败</p>
            <p className="mt-1 text-xs">{errorMessage || '请确认文件仍在本地磁盘，导出功能仍可继续使用。'}</p>
            {onRetry && <button type="button" onClick={onRetry} className="mt-3 min-h-10 rounded-full bg-primary px-5 text-xs font-semibold text-on-primary">重试预览</button>}
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
            <canvas
              ref={canvasRef}
              data-testid="image-crop-preview"
              className="block max-h-[420px] max-w-full object-contain transition-transform"
              role="img"
              aria-label={file?.name || '图片预览'}
              data-preview-rotation={rotation}
              data-preview-flip-h={flipH}
              data-preview-flip-v={flipV}
              data-preview-brightness={color.brightness}
              data-preview-contrast={color.contrast}
              data-preview-saturation={color.saturation}
              data-preview-sharpness={color.sharpness}
              data-preview-opacity={opacityPercent}
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
