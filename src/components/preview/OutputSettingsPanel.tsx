import { FolderOpen, RotateCcw, X } from 'lucide-react';
import { open } from '@tauri-apps/plugin-dialog';
import { useAppStore } from '../../stores/appStore';

interface OutputSettingsPanelProps {
  onClose: () => void;
}

export default function OutputSettingsPanel({ onClose }: OutputSettingsPanelProps) {
  const { options, setOptions } = useAppStore();
  const autoOpen = Boolean(options.openAfterProcess);

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
      <div className="w-full max-w-lg bg-surface-container-lowest rounded-[18px] shadow-[0px_30px_90px_rgba(0,0,0,0.18)] border border-outline-variant/20 p-6">
        <div className="flex items-start justify-between gap-4 mb-6">
          <div>
            <h2 className="text-2xl font-bold text-on-surface">输出路径</h2>
            <p className="text-sm text-on-surface-variant mt-1">设置所有图片和视频处理后的保存位置</p>
          </div>
          <button onClick={onClose} className="p-2 rounded-xl hover:bg-surface-container-low" aria-label="关闭设置">
            <X size={18} />
          </button>
        </div>

        <div className="rounded-[18px] bg-surface-container-low p-4 mb-4">
          <p className="text-xs text-on-surface-variant mb-2">当前目录</p>
          <p className="text-sm font-semibold text-on-surface break-all">
            {options.outputDir || '未设置，默认保存到源文件同级目录'}
          </p>
        </div>

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

        <button
          type="button"
          onClick={onClose}
          className="w-full py-3 px-5 rounded-full bg-primary text-on-primary hover:opacity-80 active:opacity-70 transition-opacity text-sm font-semibold"
        >
          保存
        </button>
      </div>
    </div>
  );
}
