import { useAppStore } from '../../stores/appStore';
import { open } from '@tauri-apps/plugin-dialog';
import { useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Images, Trash2 } from 'lucide-react';

interface FileMetadata {
  file_name: string;
  extension: string;
  size_bytes: number;
}

const createId = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

export default function HomePage() {
  const { files, clearFiles, addFiles } = useAppStore();
  const [version] = useState('3.5.1');

  const handleAdd = async () => {
    try {
      const selected = await open({
        multiple: true,
        filters: [
          {
            name: 'All Media',
            extensions: [
              'jpg', 'jpeg', 'png', 'webp', 'avif', 'bmp', 'tiff', 'tif', 'psd',
              'mp4', 'mov', 'avi', 'mkv', 'webm',
            ],
          },
        ],
      });
      if (!selected) return;
      const paths = Array.isArray(selected) ? selected : [selected];
      const items = await Promise.all(paths.map(async (p) => {
        const fallbackName = p.split('/').pop()?.split('\\').pop() || p;
        const fallbackExt = p.split('.').pop()?.toUpperCase() || 'UNKNOWN';
        try {
          const meta = await invoke<FileMetadata>('read_file_metadata', { path: p });
          return {
            id: createId(),
            name: meta.file_name || fallbackName,
            format: (meta.extension || fallbackExt).toUpperCase(),
            originalSize: meta.size_bytes || 0,
            status: 'pending' as const,
            path: p,
          };
        } catch {
          return {
            id: createId(),
            name: fallbackName,
            format: fallbackExt,
            originalSize: 0,
            status: 'pending' as const,
            path: p,
          };
        }
      }));
      addFiles(items);
    } catch {
      // cancelled
    }
  };

  const completed = files.filter((f) => f.status === 'completed').length;
  const saved = files
    .filter((f) => f.status === 'completed' && f.outputSize)
    .reduce((sum, f) => sum + (f.originalSize - (f.outputSize || 0)), 0);
  const savedMB = (saved / (1024 * 1024)).toFixed(1);

  return (
    <div className="space-y-6">
      {/* Title */}
      <div>
        <h2
          className="page-title-hero tracking-tight text-on-surface"
        >
          首页
        </h2>
        <p className="text-on-surface-variant mt-2 text-base">
          TinyPix Pro v{version} · 媒体处理中心
        </p>
      </div>

      {/* Quick stats */}
      <div className="grid grid-cols-3 gap-6">
        <div className="bg-surface-container-lowest rounded-[18px] p-6 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10">
          <p className="text-on-surface-variant text-xs font-semibold opacity-70 text-on-surface-variant">
            当前文件
          </p>
          <p className="text-on-surface mt-2 text-4xl font-semibold">
            {files.length}
          </p>
        </div>
        <div className="bg-surface-container-lowest rounded-[18px] p-6 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10">
          <p className="text-on-surface-variant text-xs font-semibold opacity-70 text-on-surface-variant">
            已完成
          </p>
          <p className="text-on-surface mt-2 text-4xl font-semibold">
            {completed}
          </p>
        </div>
        <div className="bg-surface-container-lowest rounded-[18px] p-6 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10">
          <p className="text-on-surface-variant text-xs font-semibold opacity-70 text-on-surface-variant">
            节省空间
          </p>
          <p className="text-on-surface mt-2 text-4xl font-semibold">
            {savedMB} <span className="text-base font-medium opacity-60">MB</span>
          </p>
        </div>
      </div>

      {/* Quick actions */}
      <div className="grid grid-cols-2 gap-6">
        <button
          onClick={handleAdd}
          className="bg-primary text-on-primary rounded-full p-8 text-left hover:opacity-80 active:opacity-70 transition-opacity"
        >
          <Images size={32} className="mb-3" />
          <p className="text-2xl font-semibold">
            添加文件
          </p>
          <p className="opacity-70 mt-1 text-sm">
            选择图片或视频开始处理
          </p>
        </button>
        <button
          onClick={() => { if (files.length > 0) clearFiles(); }}
          disabled={files.length === 0}
          className={`
            rounded-[18px] p-8 text-left transition-all
            ${files.length === 0
              ? 'bg-surface-container-low text-on-surface-variant cursor-not-allowed opacity-50'
              : 'bg-surface-container-lowest border border-outline-variant/20 text-on-surface hover:opacity-80 active:opacity-70'
            }
          `}
        >
          <Trash2 size={32} className="mb-3" />
          <p className="text-2xl font-semibold">
            清空列表
          </p>
          <p className="opacity-70 mt-1 text-sm">
            移除所有已添加的文件
          </p>
        </button>
      </div>
    </div>
  );
}
