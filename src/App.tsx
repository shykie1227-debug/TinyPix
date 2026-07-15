import { lazy, Suspense, useMemo, useState, useCallback, useEffect } from 'react';
import { RotateCcw } from 'lucide-react';
import Sidebar from './components/layout/Sidebar';
import StatusBar from './components/layout/StatusBar';
import DropZone from './components/layout/DropZone';
import ImageWorkbench from './components/image/ImageWorkbench';
import ProcessingQueue from './components/layout/ProcessingQueue';
import FileListItem from './components/layout/FileListItem';
const VideoOutput = lazy(() => import('./components/video/VideoOutput'));
const GifMaker = lazy(() => import('./components/video/GifMaker'));
const VideoTrimmer = lazy(() => import('./components/video/VideoTrimmer'));
import HomePage from './components/layout/HomePage';
import OutputSettingsPanel from './components/preview/OutputSettingsPanel';
import MediaPreviewStage from './components/preview/MediaPreviewStage';
import { useAppStore } from './stores/appStore';
import type { FileItem } from './stores/appStore';
import { useImageProcessor } from './hooks/useImageProcessor';
import { isVideoFormat, isImageFormat } from './utils/mediaFormat';
import { calculateAppZoom, calculateInitialWindowSize } from './utils/appZoom';

type Nav = 'home' | 'workspace';
type WorkspaceTab = 'image' | 'video' | 'gif' | 'trim';

const TAB_META: Record<WorkspaceTab, { label: string; description: string; mediaType: 'image' | 'video' }> = {
  image: {
    label: '图片处理',
    description: '编辑、压缩、格式转换与隐私清理',
    mediaType: 'image',
  },
  video: {
    label: '视频输出',
    description: '压缩、格式转换与音频提取',
    mediaType: 'video',
  },
  gif: {
    label: 'GIF 制作',
    description: '从视频片段生成高质量 GIF',
    mediaType: 'video',
  },
  trim: {
    label: '视频剪辑',
    description: '选择入点出点，支持无损优先与精确输出',
    mediaType: 'video',
  },
};

function TopNavBar({
  activeTab,
  onTabChange,
  onReset,
  showReset,
}: {
  activeTab: WorkspaceTab;
  onTabChange: (tab: WorkspaceTab) => void;
  onReset: () => void;
  showReset: boolean;
}) {
  const topTabs: Array<{ id: WorkspaceTab; label: string }> = [
    { id: 'image', label: '图片工具' },
    { id: 'video', label: '视频工具' },
  ];
  const activeTopTab = TAB_META[activeTab].mediaType === 'video' ? 'video' : 'image';

  return (
    <header className="flex justify-between items-center w-full px-8 py-4 max-w-full bg-surface-bright border-b border-outline-variant/20">
      <div className="flex items-center gap-8">
        <div className="flex items-center gap-6">
          {topTabs.map((tab) => {
            const active = activeTopTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => onTabChange(tab.id)}
                className={
                  active
                    ? 'text-on-surface font-bold border-b-2 border-primary pb-1 text-sm font-label-caps text-label-caps'
                    : 'text-on-surface-variant hover:text-on-surface transition-colors text-sm font-label-caps text-label-caps'
                }
              >
                {tab.label}
              </button>
            );
          })}
        </div>
      </div>
      {showReset ? (
        <button
          type="button"
          onClick={onReset}
          className="inline-flex items-center gap-2 rounded-[980px] border border-primary px-5 py-2.5 text-sm font-semibold text-on-surface transition-opacity hover:opacity-80 active:opacity-70"
          aria-label="重置"
        >
          <RotateCcw size={16} />
          重置
        </button>
      ) : (
        <div aria-hidden="true" />
      )}
    </header>
  );
}

function WorkspaceContent({
  activeTab,
}: {
  activeTab: WorkspaceTab;
}) {
  const { files, addFiles, removeFile } = useAppStore();

  const handleFilesAdded = useCallback(
    (newFiles: FileItem[]) => {
      addFiles(newFiles);
    },
    [addFiles]
  );

  const handleRemoveFile = useCallback(
    (id: string) => {
      removeFile(id);
    },
    [removeFile]
  );

  const meta = TAB_META[activeTab];
  const relevantFiles = useMemo(
    () => files.filter((f) => (meta.mediaType === 'image' ? isImageFormat(f.format) : isVideoFormat(f.format))),
    [files, meta.mediaType]
  );
  const queueFiles = files.filter((f) => f.status !== 'pending');
  const previewMode =
    meta.mediaType === 'image'
      ? 'image'
      : activeTab === 'trim'
      ? 'timeline'
      : activeTab === 'gif'
      ? 'gif'
      : 'preview';

  return (
    <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
      {activeTab === 'image' ? (
        /* 图片工具：全屏三栏布局 */
        <ImageWorkbench />
      ) : (
        /* 视频工具：Bento Grid 布局 */
        <div className="tinypix-video-workspace p-8 space-y-6">
          <h2 className="sr-only">{meta.label}</h2>

          {activeTab === 'trim' ? (
            <Suspense fallback={<div className="p-4 text-on-surface-variant text-sm">加载中...</div>}>
              <VideoTrimmer />
            </Suspense>
          ) : (
            /* Bento Grid — 响应式 */
            <div className="grid grid-cols-12 gap-6">
              {/* Left: DropZone + file list */}
              <div className="col-span-7 min-w-0 space-y-4 xl:col-span-8">
                {relevantFiles.length > 0 ? (
                  <MediaPreviewStage
                    mode={previewMode}
                    title={meta.label}
                    subtitle="文件已加入队列，可以在右侧调整参数后导出"
                    files={relevantFiles}
                    mediaType={meta.mediaType}
                  />
                ) : (
                  <DropZone onFilesAdded={handleFilesAdded} mediaType={meta.mediaType} />
                )}
                {relevantFiles.length > 0 && (
                  <div className="space-y-2">
                    {relevantFiles.map((file) => (
                      <FileListItem key={file.id} file={file} />
                    ))}
                  </div>
                )}
              </div>

              {/* Right: function-specific control panel */}
              <div className="col-span-5 min-w-0 space-y-6 xl:col-span-4">
                <Suspense fallback={<div className="p-4 text-on-surface-variant text-sm">加载中...</div>}>
                  {activeTab === 'video' && <VideoOutput />}
                  {activeTab === 'gif' && <GifMaker />}
                </Suspense>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Processing Queue (仅视频工具显示) */}
      {queueFiles.length > 0 && activeTab !== 'image' && (
        <div className="mb-6 px-8">
          <ProcessingQueue files={files} onRemove={handleRemoveFile} />
        </div>
      )}
    </div>
  );
}

function App() {
  const [activeNav, setActiveNav] = useState<Nav>('workspace');
  const [activeTab, setActiveTab] = useState<WorkspaceTab>('video');
  const [showOutputSettings, setShowOutputSettings] = useState(false);
  const [workspaceResetNonce, setWorkspaceResetNonce] = useState(0);
  const { files, options, isProcessing, clearFiles, resetWorkspaceOptions } = useAppStore();
  const { startProcess } = useImageProcessor();
  const imagePendingCount = files.filter((file) => isImageFormat(file.format) && file.status === 'pending').length;

  useEffect(() => {
    let disposed = false;
    const unlisteners: Array<() => void> = [];
    let resizeFrame = 0;
    const disableBrowserZoom = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && ['+', '=', '-', '0'].includes(event.key)) {
        event.preventDefault();
      }
    };
    window.addEventListener('keydown', disableBrowserZoom, { capture: true });

    const initializeDesktopViewport = async () => {
      try {
        const [{ getCurrentWindow, LogicalSize }, { getCurrentWebview }] = await Promise.all([
          import('@tauri-apps/api/window'),
          import('@tauri-apps/api/webview'),
        ]);
        if (disposed) return;
        const appWindow = getCurrentWindow();
        const webview = getCurrentWebview();
        const initial = calculateInitialWindowSize(window.screen.availWidth, window.screen.availHeight);
        await appWindow.setSize(new LogicalSize(initial.width, initial.height));
        await appWindow.center();

        const applyZoom = async (physicalSize?: { width: number; height: number }, knownScale?: number) => {
          const size = physicalSize ?? await appWindow.innerSize();
          const scale = knownScale ?? await appWindow.scaleFactor();
          await webview.setZoom(calculateAppZoom(size.width / scale, size.height / scale));
        };
        await applyZoom();
        unlisteners.push(await appWindow.onResized(({ payload }) => {
          cancelAnimationFrame(resizeFrame);
          resizeFrame = requestAnimationFrame(() => { void applyZoom(payload); });
        }));
        unlisteners.push(await appWindow.onScaleChanged(({ payload }) => {
          cancelAnimationFrame(resizeFrame);
          resizeFrame = requestAnimationFrame(() => { void applyZoom(payload.size, payload.scaleFactor); });
        }));
      } catch {
        // Browser-only tests and development previews do not expose the desktop window bridge.
      }
    };
    void initializeDesktopViewport();
    return () => {
      disposed = true;
      cancelAnimationFrame(resizeFrame);
      unlisteners.forEach((unlisten) => unlisten());
      window.removeEventListener('keydown', disableBrowserZoom, { capture: true });
    };
  }, []);

  const handleStartBatch = useCallback(() => {
    if (imagePendingCount === 0 || isProcessing) return;
    setActiveNav('workspace');
    setActiveTab('image');
    startProcess(options);
  }, [imagePendingCount, isProcessing, options, startProcess]);

  const handleResetWorkspace = useCallback(() => {
    clearFiles();
    resetWorkspaceOptions();
    setWorkspaceResetNonce((value) => value + 1);
  }, [clearFiles, resetWorkspaceOptions]);

  return (
    <div
      className="h-screen min-h-0 bg-surface-bright flex select-none overflow-hidden"
    >
      <Sidebar
        activeNav={activeNav}
        onNavChange={setActiveNav}
        activeTab={activeTab}
        onTabChange={setActiveTab}
        onOpenSettings={() => setShowOutputSettings(true)}
      />

      <main className="grid h-full min-w-0 flex-grow grid-rows-[auto_minmax(0,1fr)_48px] overflow-hidden">
        <TopNavBar
          activeTab={activeTab}
          onTabChange={(tab) => {
            setActiveNav('workspace');
            setActiveTab(tab);
          }}
          onReset={handleResetWorkspace}
          showReset={activeNav === 'workspace'}
        />

        {activeNav === 'home' && (
          <div className="flex-grow overflow-y-auto p-8">
            <HomePage />
          </div>
        )}
        {activeNav === 'workspace' && (
          <WorkspaceContent key={`${activeTab}-${workspaceResetNonce}`} activeTab={activeTab} />
        )}

        <StatusBar />
      </main>

      {/* History Panel Modal */}
      {showOutputSettings && <OutputSettingsPanel onClose={() => setShowOutputSettings(false)} />}
    </div>
  );
}

export default App;
