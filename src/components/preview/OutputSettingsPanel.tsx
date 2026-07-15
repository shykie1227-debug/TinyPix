import { useCallback, useEffect, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { FolderOpen, RotateCcw, Trash2, X } from 'lucide-react';
import { open } from '@tauri-apps/plugin-dialog';
import { useAppStore } from '../../stores/appStore';
import thirdPartyNotices from '../../../THIRD_PARTY_NOTICES?raw';
import type { MediaEngineStatus } from '../../types/media';
import { clearMediaPreviewMemoryCache } from '../../hooks/useMediaPreview';

interface OutputSettingsPanelProps {
  onClose: () => void;
}

export default function OutputSettingsPanel({ onClose }: OutputSettingsPanelProps) {
  const { options, setOptions } = useAppStore();
  const autoOpen = Boolean(options.openAfterProcess);
  const [engine, setEngine] = useState<MediaEngineStatus | null>(null);
  const [engineBusy, setEngineBusy] = useState(false);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [previewStatus, setPreviewStatus] = useState('');

  const refreshEngine = useCallback(async () => {
    try {
      setEngine(await invoke<MediaEngineStatus>('get_media_engine_status'));
    } catch {
      setEngine({ ready: false, ffmpegPath: '', ffprobePath: '', version: '', sha256: '', cacheDirectory: '', error: '暂时无法读取媒体引擎状态，请在桌面应用中重试。' });
    }
  }, []);

  useEffect(() => { void refreshEngine(); }, [refreshEngine]);

  const handleClearEngine = async () => {
    setEngineBusy(true);
    try {
      await invoke('clear_media_engine_cache');
      await refreshEngine();
    } finally {
      setEngineBusy(false);
    }
  };

  const handleClearPreviews = async () => {
    setPreviewBusy(true);
    setPreviewStatus('');
    try {
      await invoke('clear_preview_cache');
      clearMediaPreviewMemoryCache();
      setPreviewStatus('预览缓存已清理');
    } catch {
      setPreviewStatus('预览缓存清理失败，请关闭正在使用预览的任务后重试。');
    } finally {
      setPreviewBusy(false);
    }
  };

  const handlePickDir = async () => {
    try {
      const selected = await open({ directory: true });
      if (selected) {
        setOptions({ outputDir: String(selected) });
      }
    } catch {
      // User cancelled the native dialog.
    }
  };

  return (
    <div className="fixed inset-0 z-[80] bg-black/20 backdrop-blur-sm flex items-center justify-center p-6" role="dialog" aria-modal="true">
      <div className="flex h-[calc(100vh-3rem)] max-h-[720px] w-full max-w-2xl flex-col overflow-hidden rounded-[18px] border border-outline-variant/20 bg-surface-container-lowest shadow-[0px_30px_90px_rgba(0,0,0,0.18)]">
        <div className="mb-6 flex shrink-0 items-start justify-between gap-4 px-6 pt-6">
          <div>
            <h2 className="text-2xl font-bold text-on-surface">设置</h2>
            <p className="text-sm text-on-surface-variant mt-1">输出、媒体引擎与开源许可</p>
          </div>
          <button onClick={onClose} className="p-2 rounded-xl hover:bg-surface-container-low" aria-label="关闭设置">
            <X size={18} />
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-6">

        <div className="rounded-[18px] bg-surface-container-low p-4 mb-4">
          <p className="text-xs text-on-surface-variant mb-2">当前目录</p>
          <p className="text-sm font-semibold text-on-surface break-all">
            {options.outputDir || '未设置，默认保存到源文件同级目录'}
          </p>
        </div>

        <section className="mb-4 rounded-[18px] border border-outline-variant/15 bg-surface-container-low p-4" aria-labelledby="engine-title">
          <div className="flex min-w-0 items-start justify-between gap-4">
            <div className="min-w-0">
              <h3 id="engine-title" className="text-sm font-semibold text-on-surface">内置媒体引擎</h3>
              <p className={`mt-1 text-xs ${engine?.ready ? 'text-on-surface-variant' : 'text-error'}`} role="status">
                {engine?.ready ? engine.version : (engine?.error || '正在校验 FFmpeg/FFprobe…')}
              </p>
              {engine?.sha256 && <p className="mt-2 break-all font-mono-status text-[10px] text-on-surface-variant">SHA-256: {engine.sha256}</p>}
              <p className="mt-2 text-xs leading-5 text-on-surface-variant">运行时不联网；首次启动仅将已校验引擎释放到本地缓存。</p>
            </div>
            <button
              type="button"
              aria-label="清理媒体引擎缓存"
              disabled={engineBusy}
              onClick={() => void handleClearEngine()}
              className="flex min-h-10 shrink-0 items-center gap-2 rounded-full bg-surface-container-high px-3 text-xs font-semibold text-on-surface disabled:opacity-50"
            >
              <Trash2 size={15} />清理缓存
            </button>
          </div>
        </section>

        <section className="mb-4 rounded-[18px] border border-outline-variant/15 bg-surface-container-low p-4" aria-labelledby="preview-cache-title">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0">
              <h3 id="preview-cache-title" className="text-sm font-semibold text-on-surface">本地预览缓存</h3>
              <p className="mt-1 text-xs leading-5 text-on-surface-variant">包含图片 PNG 预览、视频兼容代理、缩略图和波形；清理后会在下次使用时自动重建。</p>
              {previewStatus && <p className="mt-2 text-xs text-on-surface-variant" role="status">{previewStatus}</p>}
            </div>
            <button
              type="button"
              aria-label="清理预览缓存"
              disabled={previewBusy}
              onClick={() => void handleClearPreviews()}
              className="flex min-h-10 shrink-0 items-center gap-2 rounded-full bg-surface-container-high px-3 text-xs font-semibold text-on-surface disabled:opacity-50"
            >
              <Trash2 size={15} />清理预览
            </button>
          </div>
        </section>

        <details className="mb-4 rounded-[18px] border border-outline-variant/15 bg-surface-container-low p-4">
          <summary className="min-h-10 cursor-pointer text-sm font-semibold text-on-surface">开源许可</summary>
          <div className="space-y-2 pt-3 text-xs leading-5 text-on-surface-variant">
            <p>TinyPix：MIT License。</p>
            <p>FFmpeg：GPLv3，由 Gyan.dev 提供 Windows 静态构建；作为独立进程使用，未链接进 TinyPix。</p>
            <pre className="max-h-48 overflow-auto whitespace-pre-wrap break-words rounded-xl bg-surface-container-lowest p-3 font-mono-status text-[10px] leading-4">{thirdPartyNotices}</pre>
          </div>
        </details>

        <div className="grid grid-cols-2 gap-3 mb-4">
          <button
            onClick={handlePickDir}
            className="py-3 px-4 rounded-full bg-primary text-on-primary hover:opacity-80 active:opacity-70 transition-opacity flex items-center justify-center gap-2 text-sm font-semibold"
          >
            <FolderOpen size={16} />
            选择目录
          </button>
          <button
            onClick={() => setOptions({ outputDir: undefined })}
            className="py-3 px-4 rounded-[980px] bg-surface-container text-on-surface hover:opacity-80 active:opacity-70 transition-opacity flex items-center justify-center gap-2 text-sm font-semibold"
          >
            <RotateCcw size={16} />
            跟随源文件
          </button>
        </div>

        <div className="rounded-[18px] bg-surface-container-low p-4 mb-4">
          <button
            type="button"
            role="switch"
            aria-checked={autoOpen}
            aria-label="处理完成后自动打开文件夹"
            onClick={() => setOptions({ openAfterProcess: !autoOpen })}
            className="w-full flex items-center justify-between gap-4 text-left"
          >
            <span>
              <span className="block text-sm font-semibold text-on-surface">处理完成后自动打开文件夹</span>
              <span className="block text-xs text-on-surface-variant mt-1">导出完成时自动打开输出目录</span>
            </span>
            <span
              className={`relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition-colors ${
                autoOpen ? 'bg-secondary-container' : 'bg-surface-container-highest'
              }`}
            >
              <span
                className={`inline-block h-5 w-5 rounded-full bg-surface-bright shadow-sm transition-transform ${
                  autoOpen ? 'translate-x-6' : 'translate-x-1'
                }`}
              />
            </span>
          </button>
        </div>

        </div>

        <div className="shrink-0 border-t border-outline-variant/15 bg-surface-container-lowest px-6 py-4">
          <button
            type="button"
            onClick={onClose}
            className="w-full py-3 px-5 rounded-full bg-primary text-on-primary hover:opacity-80 active:opacity-70 transition-opacity text-sm font-semibold"
          >
            保存
          </button>
        </div>
      </div>
    </div>
  );
}
