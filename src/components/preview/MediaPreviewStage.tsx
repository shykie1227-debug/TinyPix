import { useCallback, useEffect, useMemo, useState } from 'react';
import { convertFileSrc, invoke } from '@tauri-apps/api/core';
import { CloudUpload, Image as ImageIcon, Play, Scissors, Waves } from 'lucide-react';
import type { FileItem } from '../../stores/appStore';
import { useAppStore } from '../../stores/appStore';
import { useCssFilterPreview } from '../../hooks/useCssFilterPreview';
import { formatBytes } from '../../utils/formatBytes';
import VideoPreviewStage from './VideoPreviewStage';
import ImagePreviewStage from './ImagePreviewStage';
import type { Crop } from 'react-image-crop';

interface MediaPreviewStageProps {
  mode?: 'upload' | 'preview' | 'timeline' | 'waveform' | 'image' | 'gif';
  title: string;
  subtitle: string;
  files?: FileItem[];
  actionLabel?: string;
  onAction?: () => void;
  mediaType?: 'image' | 'video';
  currentTime?: string;
  totalTime?: string;
  progressPercent?: number;
  overlayStatus?: string;
}

const DEFAULT_CROP: Crop = {
  unit: '%',
  x: 10,
  y: 10,
  width: 80,
  height: 80,
};

export default function MediaPreviewStage({
  mode = 'upload',
  title,
  subtitle,
  files = [],
  actionLabel,
  onAction,
  mediaType = 'video',
  currentTime = '00:04.2',
  totalTime = '00:15.0',
  progressPercent = 28,
  overlayStatus = '正在选取片段',
}: MediaPreviewStageProps) {
  const firstFile = files[0];
  const { options, setOptions } = useAppStore();
  const [crop, setCrop] = useState<Crop>(DEFAULT_CROP);
  const [videoPreviewPath, setVideoPreviewPath] = useState('');
  const [videoPreviewFailed, setVideoPreviewFailed] = useState(false);
  const [videoPreviewLoading, setVideoPreviewLoading] = useState(false);
  const [videoPlaybackFailed, setVideoPlaybackFailed] = useState(false);
  const [imageError, setImageError] = useState(false);
  const Icon = mode === 'timeline' ? Scissors : mode === 'waveform' ? Waves : mediaType === 'image' ? ImageIcon : CloudUpload;
  const assetUrl = useMemo(() => (firstFile?.path ? convertFileSrc(firstFile.path) : ''), [firstFile?.path]);
  const posterUrl = useMemo(() => (videoPreviewPath ? convertFileSrc(videoPreviewPath) : ''), [videoPreviewPath]);
  const hasFile = Boolean(firstFile);
  const showPlayer = hasFile && mediaType === 'video' && (mode === 'preview' || mode === 'timeline' || mode === 'waveform' || mode === 'gif');
  const showImageEditor = hasFile && mediaType === 'image';

  // CSS filter 预览 (brightness/contrast/saturation + flipH/flipV)
  const cssFilter = useCssFilterPreview({
    brightness: options.colorAdjust?.brightness ?? 0,
    contrast: options.colorAdjust?.contrast ?? 0,
    saturation: options.colorAdjust?.saturation ?? 0,
  });

  // 旋转角度转 transform
  const rotateTransform = `rotate(${options.rotateDegrees}deg)`;
  const flipTransform = [
    options.flipH ? 'scaleX(-1)' : '',
    options.flipV ? 'scaleY(-1)' : '',
  ].filter(Boolean).join(' ');
  // 合并 transform: 旋转 + 镜像。滤镜单独进入 filter，避免浏览器丢弃整段样式。
  const combinedTransform = [rotateTransform, flipTransform].filter(Boolean).join(' ');

  const syncCropToStore = useCallback(
    (nextCrop: Crop) => {
      if (
        typeof nextCrop.x === 'number' &&
        typeof nextCrop.y === 'number' &&
        typeof nextCrop.width === 'number' &&
        typeof nextCrop.height === 'number'
      ) {
        setOptions({
          cropPercent: {
            x: Math.round(nextCrop.x),
            y: Math.round(nextCrop.y),
            width: Math.round(nextCrop.width),
            height: Math.round(nextCrop.height),
          },
        });
      }
    },
    [setOptions]
  );

  const createVideoPoster = useCallback(async () => {
    if (!firstFile?.path) return;
    setVideoPreviewLoading(true);
    setVideoPreviewFailed(false);
    setVideoPreviewPath('');
    try {
      const previewPath = await invoke<string>('create_video_preview', {
        inputPath: firstFile.path,
      });
      setVideoPreviewPath(previewPath);
    } catch {
      setVideoPreviewFailed(true);
    } finally {
      setVideoPreviewLoading(false);
    }
  }, [firstFile?.path]);

  useEffect(() => {
    setCrop(DEFAULT_CROP);
    setImageError(false);
    setVideoPreviewPath('');
    setVideoPreviewFailed(false);
    setVideoPlaybackFailed(false);
    setVideoPreviewLoading(false);

    if (firstFile?.path && mediaType === 'video') {
      void createVideoPoster();
    }
    if (mediaType === 'image') {
      syncCropToStore(DEFAULT_CROP);
    }
  }, [firstFile?.path, mediaType, createVideoPoster, syncCropToStore]);

  return (
    <div className="bg-surface-container-lowest rounded-[18px] border border-outline-variant/10 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] overflow-hidden">
      <div className={showPlayer || showImageEditor ? 'p-5' : 'min-h-[430px] p-10 flex flex-col items-center justify-center text-center border-2 border-dashed border-outline-variant/40 rounded-[18px] m-0'}>
        {showPlayer ? (
          <VideoPreviewStage
            file={firstFile}
            assetUrl={assetUrl}
            posterUrl={posterUrl}
            mode={mode as 'preview' | 'timeline' | 'waveform' | 'gif'}
            currentTime={currentTime}
            totalTime={totalTime}
            progressPercent={progressPercent}
            overlayStatus={overlayStatus}
            videoPlaybackFailed={videoPlaybackFailed}
            videoPreviewFailed={videoPreviewFailed}
            videoPreviewLoading={videoPreviewLoading}
            onVideoPlaybackFailed={() => setVideoPlaybackFailed(true)}
            onVideoPreviewFailed={() => setVideoPreviewFailed(true)}
            title={title}
            subtitle={subtitle}
          />
        ) : showImageEditor ? (
          <ImagePreviewStage
            file={firstFile}
            assetUrl={assetUrl}
            fallbackSrc={firstFile?.path}
            cssFilter={cssFilter}
            combinedTransform={combinedTransform}
            crop={crop}
            onCropChange={setCrop}
            onCropComplete={syncCropToStore}
            imageError={imageError}
            onImageError={() => setImageError(true)}
            onImageLoad={() => {
              setCrop(DEFAULT_CROP);
              syncCropToStore(DEFAULT_CROP);
            }}
          />
        ) : (
          <>
            <div className="w-20 h-20 rounded-[18px] bg-surface-container-low flex items-center justify-center mb-6">
              <Icon size={38} className="text-on-surface" />
            </div>
            <h3 className="text-on-surface text-2xl font-bold">{title}</h3>
            <p className="text-on-surface-variant text-sm mt-2">{subtitle}</p>
            <div className="flex gap-3 mt-8">
              {(mediaType === 'video' ? ['MP4', 'MOV', 'MKV'] : ['JPG', 'PNG', 'WebP']).map((fmt) => (
                <span key={fmt} className="px-4 py-2 bg-surface-container-low rounded-lg flex items-center gap-2 text-[11px] font-semibold">
                  <span className="w-1.5 h-1.5 rounded-full bg-secondary-fixed" />
                  {fmt}
                </span>
              ))}
            </div>
            {actionLabel && (
              <button onClick={onAction} className="mt-10 px-8 py-4 bg-primary text-on-primary rounded-full hover:opacity-80 active:opacity-70 transition-opacity text-sm font-semibold">
                {actionLabel}
              </button>
            )}
          </>
        )}
      </div>

      {firstFile && (
        <div className="mx-5 mb-5 rounded-[18px] bg-surface-container-lowest border border-outline-variant/10 p-4 flex items-center gap-4">
          <div className="h-14 w-20 rounded-[12px] bg-[#202020] flex items-center justify-center text-secondary-fixed">
            {mediaType === 'video' ? <Play size={18} fill="currentColor" /> : <ImageIcon size={18} />}
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-bold text-on-surface truncate">{firstFile.name}</p>
            <p className="text-xs text-on-surface-variant mt-1">{firstFile.format} · {formatBytes(firstFile.originalSize)}</p>
          </div>
          <span className="text-[11px] rounded-full bg-secondary-fixed text-on-secondary-fixed px-2 py-1 font-bold">
            就绪
          </span>
        </div>
      )}
    </div>
  );
}
