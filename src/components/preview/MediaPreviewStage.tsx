import { useCallback, useEffect, useMemo, useState } from 'react';
import { convertFileSrc } from '@tauri-apps/api/core';
import { CloudUpload, Image as ImageIcon, Play, Scissors, Waves } from 'lucide-react';
import type { CropPercent, FileItem } from '../../stores/appStore';
import { useAppStore } from '../../stores/appStore';
import { formatBytes } from '../../utils/formatBytes';
import VideoPreviewStage from './VideoPreviewStage';
import ImagePreviewStage from './ImagePreviewStage';
import type { Crop } from 'react-image-crop';
import { useMediaPreview } from '../../hooks/useMediaPreview';
import { mapDisplayCropToSource, mapSourceCropToDisplay } from '../../utils/imagePreviewRenderer';
import { formatDuration } from '../../utils/timeFormat';

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
  x: 0,
  y: 0,
  width: 100,
  height: 100,
};

const cropFromOptions = (
  cropPercent: CropPercent | undefined,
  rotation: number,
  flipH: boolean,
  flipV: boolean
): Crop => ({
  unit: '%',
  ...(cropPercent
    ? mapSourceCropToDisplay(cropPercent, rotation, flipH, flipV)
    : DEFAULT_CROP),
});

export default function MediaPreviewStage({
  mode = 'upload',
  title,
  subtitle,
  files = [],
  actionLabel,
  onAction,
  mediaType = 'video',
  currentTime,
  totalTime,
  progressPercent,
  overlayStatus = '当前播放位置',
}: MediaPreviewStageProps) {
  const firstFile = files[0];
  const { options, setOptions } = useAppStore();
  const [crop, setCrop] = useState<Crop>(DEFAULT_CROP);
  const [videoPlaybackFailed, setVideoPlaybackFailed] = useState(false);
  const [imageError, setImageError] = useState(false);
  const preview = useMediaPreview(firstFile?.path, mediaType);
  const descriptor = preview.descriptor;
  const Icon = mode === 'timeline' ? Scissors : mode === 'waveform' ? Waves : mediaType === 'image' ? ImageIcon : CloudUpload;
  const assetUrl = useMemo(
    () => (descriptor?.playbackPath ? convertFileSrc(descriptor.playbackPath) : ''),
    [descriptor?.playbackPath]
  );
  const posterUrl = useMemo(
    () => (descriptor?.posterPath ? convertFileSrc(descriptor.posterPath) : ''),
    [descriptor?.posterPath]
  );
  const hasFile = Boolean(firstFile);
  const showPlayer = hasFile && mediaType === 'video' && (mode === 'preview' || mode === 'timeline' || mode === 'waveform' || mode === 'gif');
  const showImageEditor = hasFile && mediaType === 'image';
  const duration = descriptor?.durationSecs ?? 0;
  const actualProgress = duration > 0 ? (preview.playbackPosition / duration) * 100 : 0;

  useEffect(() => {
    if (descriptor?.state === 'ready' && descriptor.playbackPath) {
      setVideoPlaybackFailed(false);
    }
  }, [descriptor?.playbackPath, descriptor?.state]);

  const syncCropToStore = useCallback(
    (nextCrop: Crop) => {
      if (
        typeof nextCrop.x === 'number' &&
        typeof nextCrop.y === 'number' &&
        typeof nextCrop.width === 'number' &&
        typeof nextCrop.height === 'number'
      ) {
        const displayCrop = {
          x: nextCrop.x,
          y: nextCrop.y,
          width: nextCrop.width,
          height: nextCrop.height,
        };
        setOptions({ cropPercent: mapDisplayCropToSource(
          displayCrop,
          options.rotateDegrees,
          Boolean(options.flipH),
          Boolean(options.flipV)
        ) });
      }
    },
    [options.flipH, options.flipV, options.rotateDegrees, setOptions]
  );

  useEffect(() => {
    setCrop(cropFromOptions(
      options.cropPercent,
      options.rotateDegrees,
      Boolean(options.flipH),
      Boolean(options.flipV)
    ));
    setImageError(false);
    setVideoPlaybackFailed(false);
  }, [firstFile?.path, mediaType, options.cropPercent, options.flipH, options.flipV, options.rotateDegrees]);

  return (
    <div className="bg-surface-container-lowest rounded-[18px] border border-outline-variant/10 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] overflow-hidden">
      <div className={showPlayer || showImageEditor ? 'p-5' : 'min-h-[430px] p-10 flex flex-col items-center justify-center text-center border-2 border-dashed border-outline-variant/40 rounded-[18px] m-0'}>
        {showPlayer ? (
          <VideoPreviewStage
            file={firstFile}
            assetUrl={assetUrl}
            posterUrl={posterUrl}
            mode={mode as 'preview' | 'timeline' | 'waveform' | 'gif'}
            currentTime={currentTime ?? formatDuration(preview.playbackPosition)}
            totalTime={totalTime ?? formatDuration(duration)}
            progressPercent={progressPercent ?? actualProgress}
            overlayStatus={overlayStatus}
            videoPlaybackFailed={videoPlaybackFailed}
            videoPreviewFailed={descriptor?.state === 'error' || descriptor?.state === 'cancelled'}
            previewCancelled={descriptor?.state === 'cancelled'}
            videoPreviewLoading={descriptor?.state === 'probing' || descriptor?.state === 'generating'}
            previewProgress={preview.progress}
            previewError={descriptor?.error?.message}
            onRetryPreview={preview.retry}
            onCancelPreview={() => void preview.cancel()}
            onVideoPlaybackFailed={() => {
              setVideoPlaybackFailed(true);
              preview.forceProxy();
            }}
            onVideoPreviewFailed={() => setVideoPlaybackFailed(true)}
            initialTime={preview.playbackPosition}
            onPlaybackTime={preview.setPlaybackPosition}
            title={title}
            subtitle={subtitle}
          />
        ) : showImageEditor ? (
          <ImagePreviewStage
            file={firstFile}
            assetUrl={assetUrl}
            rotation={options.rotateDegrees}
            flipH={Boolean(options.flipH)}
            flipV={Boolean(options.flipV)}
            color={options.colorAdjust ?? { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 }}
            opacityPercent={options.opacityPercent}
            resizeTargetW={options.resizeTargetW}
            resizeTargetH={options.resizeTargetH}
            crop={crop}
            onCropChange={setCrop}
            onCropComplete={syncCropToStore}
            imageError={imageError || descriptor?.state === 'error'}
            loading={descriptor?.state === 'probing' || descriptor?.state === 'generating'}
            progress={preview.progress}
            errorMessage={descriptor?.error?.message}
            onRetry={preview.retry}
            onCancel={() => void preview.cancel()}
            onImageError={() => setImageError(true)}
            onImageLoad={() => {
              setCrop(cropFromOptions(
                options.cropPercent,
                options.rotateDegrees,
                Boolean(options.flipH),
                Boolean(options.flipV)
              ));
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
