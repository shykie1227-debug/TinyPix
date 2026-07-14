import { useCallback, useMemo, useState } from 'react';
import { ImageDown, X } from 'lucide-react';
import { useAppStore } from '../../stores/appStore';
import type { FileItem } from '../../stores/appStore';
import DropZone from '../layout/DropZone';
import MediaPreviewStage from '../preview/MediaPreviewStage';
import RotateFlipBar from './RotateFlipBar';
import ExportPanel from './ExportPanel';
import EditPanel from './EditPanel';

const EXPORT_FORMATS = new Set([
  'psd', 'pdf', 'ppt', 'pptx', 'eps', 'ai', 'svg', 'tiff', 'tif', 'bmp',
  'jpg', 'jpeg', 'png', 'webp', 'avif',
]);
const EDIT_FORMATS = new Set(['jpg', 'jpeg', 'png', 'webp', 'avif', 'bmp']);

interface ImageWorkbenchProps {
  onProcess?: () => void;
  estimateSizeBatch?: (items: Array<{ path: string; format: string; quality: number }>) => Promise<number>;
}

export default function ImageWorkbench({
  onProcess = () => {},
  estimateSizeBatch = () => Promise.resolve(0),
}: ImageWorkbenchProps) {
  const { files, addFiles, removeFile, options, setOptions, clearFiles } = useAppStore();
  const [isExportMode, setIsExportMode] = useState(true);
  const [rotation, setRotation] = useState(0);
  const [toast, setToast] = useState<string | null>(null);

  const currentFormats = isExportMode ? EXPORT_FORMATS : EDIT_FORMATS;
  const filteredFiles = useMemo(
    () => files.filter((file) => currentFormats.has(file.format.toLowerCase())),
    [files, currentFormats]
  );

  const handleFilesAdded = useCallback(
    (newFiles: FileItem[]) => {
      const compatibleFiles = newFiles.filter((file) =>
        currentFormats.has(file.format.toLowerCase())
      );
      if (compatibleFiles.length > 0) addFiles(compatibleFiles);
    },
    [addFiles, currentFormats]
  );

  const handleModeSwitch = useCallback(() => {
    const newMode = !isExportMode;
    setIsExportMode(newMode);

    const newFormats = newMode ? EXPORT_FORMATS : EDIT_FORMATS;
    const compatibleFiles = files.filter((file) => newFormats.has(file.format.toLowerCase()));
    const incompatibleCount = files.length - compatibleFiles.length;
    if (incompatibleCount === 0) return;

    clearFiles();
    if (compatibleFiles.length > 0) addFiles(compatibleFiles);
    setToast(`已移除 ${incompatibleCount} 个不兼容当前模式的文件`);
    window.setTimeout(() => setToast(null), 3000);
  }, [addFiles, clearFiles, files, isExportMode]);

  const handleRotationChange = useCallback(
    (degrees: number) => {
      setRotation(degrees);
      const normalized = ((degrees % 360) + 360) % 360;
      const rotateDegrees = ([0, 90, 180, 270].includes(normalized) ? normalized : 0) as 0 | 90 | 180 | 270;
      setOptions({ rotateDegrees });
    },
    [setOptions]
  );

  return (
    <div
      className="grid grid-cols-1 lg:grid-cols-12 gap-6 p-8 pb-16"
      role="region"
      aria-label="图片工具工作区"
    >
      <section className="lg:col-span-7 xl:col-span-8 min-w-0 space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <ImageDown size={22} className="text-secondary" />
            <div>
              <h2 className="text-xl font-semibold text-on-surface">图片导出</h2>
              <p className="text-xs text-on-surface-variant">
                {isExportMode ? '专业格式导出与批量转换' : '裁切、旋转、调色与压缩'}
              </p>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <span className="text-xs font-semibold text-on-surface-variant">
              {isExportMode ? '导出模式' : '编辑模式'}
            </span>
            <button
              type="button"
              onClick={handleModeSwitch}
              className={`relative inline-flex h-[31px] w-[52px] shrink-0 rounded-full border-2 border-transparent transition-colors duration-300 focus-visible:ring-2 focus-visible:ring-[#0071e3]/30 ${
                isExportMode ? 'bg-secondary-fixed' : 'bg-surface-container-high'
              }`}
              role="switch"
              aria-checked={isExportMode}
              aria-label="切换模式"
            >
              <span
                className={`pointer-events-none absolute left-[2px] top-[2px] h-[23px] w-[23px] rounded-full bg-white shadow transition-transform duration-300 ${
                  isExportMode ? 'translate-x-[21px]' : 'translate-x-0'
                }`}
              />
            </button>
          </div>
        </div>

        {filteredFiles.length === 0 ? (
          <DropZone
            onFilesAdded={handleFilesAdded}
            mediaType="image"
            title={isExportMode ? '拖拽文件到这里转换格式' : undefined}
            subtitle={isExportMode ? '支持 PSD, PDF, PPT, EPS, AI, SVG, TIFF, BMP 等格式' : undefined}
            acceptButton={isExportMode ? '选取文件' : undefined}
            formats={isExportMode ? ['PSD', 'PDF', 'PPT'] : undefined}
            extensions={isExportMode ? Array.from(EXPORT_FORMATS) : Array.from(EDIT_FORMATS)}
          />
        ) : (
          <>
            <MediaPreviewStage
              mode="image"
              title="图片预览"
              subtitle="本地预览已加载"
              files={filteredFiles}
              mediaType="image"
            />
            <div className="space-y-2">
              {filteredFiles.map((file) => (
                <div
                  key={file.id}
                  className="flex items-center gap-3 rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest px-4 py-3"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-on-surface">{file.name}</p>
                    <p className="mt-0.5 text-[11px] text-on-surface-variant">
                      {file.format} · {Math.round(file.originalSize / 1024)} KB
                    </p>
                  </div>
                  <span className="rounded-[980px] bg-secondary-fixed px-2 py-1 text-[10px] font-bold text-on-secondary-fixed">
                    {file.status === 'processing' ? '处理中' : file.status === 'completed' ? '已完成' : '就绪'}
                  </span>
                  <button
                    type="button"
                    onClick={() => removeFile(file.id)}
                    className="rounded-full p-2 text-on-surface-variant transition-opacity hover:opacity-60"
                    aria-label={`移除 ${file.name}`}
                  >
                    <X size={16} />
                  </button>
                </div>
              ))}
            </div>
          </>
        )}

        {!isExportMode && (
          <RotateFlipBar
            rotation={rotation}
            onRotationChange={handleRotationChange}
            onFlipH={() => setOptions({ flipH: !options.flipH })}
            onFlipV={() => setOptions({ flipV: !options.flipV })}
          />
        )}
      </section>

      <aside className="lg:col-span-5 xl:col-span-4 min-w-[320px]">
        <div className="sticky top-4 max-h-[calc(100vh-8rem)] overflow-y-auto">
          {isExportMode ? (
            <ExportPanel onProcess={onProcess} estimateSizeBatch={estimateSizeBatch} />
          ) : (
            <EditPanel />
          )}
        </div>
      </aside>

      {toast && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-16 left-1/2 z-50 -translate-x-1/2 rounded-[980px] bg-primary px-4 py-2 text-[12px] font-medium text-on-primary shadow-lg"
        >
          {toast}
        </div>
      )}
    </div>
  );
}
