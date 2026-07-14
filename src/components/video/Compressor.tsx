import { useState, useCallback, useEffect } from 'react';
import { open } from '@tauri-apps/plugin-dialog';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore, type FileItem } from '../../stores/appStore';
import { Loader2, Zap } from 'lucide-react';
import ToolOptionCard from '../common/ToolOptionCard';
import RadioOptionCard from '../common/RadioOptionCard';
import ChipButton from '../common/ChipButton';
import { useVideoProgress } from '../../hooks/useVideoProgress';
import { isVideoFormat } from '../../utils/mediaFormat';
import { withVideoSuffix } from '../../utils/videoOutput';

const PRESETS = [
  { id: 'light' as const, label: '轻度压缩', hint: '减少体积约 20%', crf: 20 },
  { id: 'standard' as const, label: '标准压缩', hint: '减少体积约 50%', crf: 26, badge: '推荐' },
  { id: 'extreme' as const, label: '极限压缩', hint: '减少体积约 80%', crf: 34 },
];

const RESOLUTION_OPTIONS = [
  { label: '原始尺寸', value: '原始尺寸' },
  { label: '4K (2160P)', value: '4K (2160P)' },
  { label: '1080P', value: '1080P' },
  { label: '720P', value: '720P' },
  { label: '480P (适合移动端)', value: '480P (适合移动端)', colSpan: 2 },
];

type Preset = typeof PRESETS[number]['id'];

interface FileMetadata {
  file_name: string;
  extension: string;
  size_bytes: number;
}

const createId = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

interface CompressorProps {
  embedded?: boolean;
}

export default function Compressor({ embedded = false }: CompressorProps = {}) {
  const { addFiles, files, options, setVideoPreset } = useAppStore();
  const [preset, setPreset] = useState<Preset>('standard');
  const [resolution, setResolution] = useState<string>('原始尺寸');
  const [isProcessing, setIsProcessing] = useState(false);
  const [status, setStatus] = useState('');
  const [videoProgress, setVideoProgress] = useState(0);
  useVideoProgress(setVideoProgress);

  useEffect(() => {
    setVideoPreset(preset);
  }, [preset, setVideoPreset]);

  const handlePick = useCallback(async () => {
    try {
      const selected = await open({
        multiple: true,
        filters: [{ name: 'Videos', extensions: ['mp4', 'mov', 'avi', 'mkv', 'webm'] }],
      });
      if (!selected) return;
      const paths = Array.isArray(selected) ? selected : [selected];
      const items: FileItem[] = await Promise.all(paths.map(async (p) => {
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
  }, [addFiles]);

  const crf = PRESETS.find((p) => p.id === preset)?.crf ?? 23;

  const resolutionScale = (() => {
    switch (resolution) {
      case '4K (2160P)': return '3840:2160';
      case '1080P': return '1920:1080';
      case '720P': return '1280:720';
      case '480P (适合移动端)': return '854:480';
      default: return null;
    }
  })();

  const handleCompress = useCallback(async () => {
    const videos = files.filter((f) => isVideoFormat(f.format));
    if (videos.length === 0) {
      setStatus('请先添加视频文件');
      return;
    }

    setIsProcessing(true);
    setStatus('压缩中...');

    let success = 0;
    try {
      for (const v of videos) {
        const outPath = withVideoSuffix(v.path, '_compressed', undefined, options.outputDir);
        try {
          await invoke('compress_video', {
            inputPath: v.path,
            outputPath: outPath,
            preset,
            crf,
            scale: resolutionScale,
          });
          success++;
          setStatus(`进度: ${success}/${videos.length}`);
        } catch (e) {
          console.error(`Failed: ${v.name}`, e);
        }
      }
      setStatus(`完成: ${success}/${videos.length} 个文件`);
      if (success > 0 && options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } finally {
      setIsProcessing(false);
    }
  }, [files, preset, crf, resolutionScale, options.outputDir, options.openAfterProcess]);

  const videos = files.filter((f) => isVideoFormat(f.format));

  // 嵌入模式:不显示大标题和"选择视频"按钮(Workspace 已有 DropZone)
  if (embedded) {
    const radioOptions = PRESETS.map((p) => ({
      label: p.label,
      desc: p.hint,
      value: p.id,
      badge: p.badge,
    }));

    return (
      <div className="space-y-6 sticky top-4 max-h-[calc(100vh-8rem)] overflow-y-auto">
        {/* 压缩等级卡片 */}
        <ToolOptionCard>
          <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4">压缩等级</label>
          <RadioOptionCard
            options={radioOptions}
            value={preset}
            onChange={(v) => setPreset(v as Preset)}
          />
        </ToolOptionCard>

        {/* 输出分辨率卡片 */}
        <ToolOptionCard>
          <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4">输出分辨率</label>
          <ChipButton
            options={RESOLUTION_OPTIONS}
            value={resolution}
            onChange={setResolution}
            gridCols={2}
          />
        </ToolOptionCard>

        {/* CTA 按钮 */}
        <button
          onClick={handleCompress}
          disabled={isProcessing || videos.length === 0}
          className={`
            w-full py-3.5 rounded-[980px] flex items-center justify-center gap-2 text-body-sm font-semibold transition-opacity
            ${isProcessing || videos.length === 0
              ? 'bg-surface-container-high text-on-surface-variant cursor-not-allowed'
              : 'bg-primary text-on-primary hover:opacity-80 active:opacity-70'
            }
          `}
        >
          {isProcessing && <Loader2 size={16} className="animate-spin" />}
          {!isProcessing && <Zap size={18} />}
          {isProcessing ? '压缩中...' : '开始极速压缩'}
        </button>

        {status && (
          <p className="text-on-surface-variant text-center font-mono-status text-[11px]">
            {isProcessing ? `${status} ${videoProgress > 0 ? `(${Math.round(videoProgress)}%)` : ''}` : status}
          </p>
        )}
      </div>
    );
  }

  // 独立页面模式(保留向后兼容,但实际 App.tsx 不再渲染)
  return (
    <div className="space-y-6">
      <div>
        <h2
          className="tracking-tight text-on-surface font-bold"
          style={{ fontSize: '48px', lineHeight: '56px' }}
        >
          视频压缩
        </h2>
        <p className="text-on-surface-variant mt-2 text-base">
          批量压缩视频文件 · FFmpeg 后端
        </p>
      </div>

      <div className="grid grid-cols-12 gap-6">
        <div className="col-span-4 space-y-6">
          <div className="bg-surface-container-lowest rounded-[18px] p-6 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10">
            <label
              className="block mb-4 text-on-surface-variant text-xs font-semibold tracking-[0.02em]"
            >
              压缩级别
            </label>
            <div className="grid grid-cols-2 gap-2">
              {PRESETS.map((p) => (
                <button
                  key={p.id}
                  onClick={() => setPreset(p.id)}
                  className={`
                    py-2.5 px-3 rounded-xl text-left transition-all
                    ${preset === p.id
                      ? 'bg-secondary-container text-on-secondary-container border-2 border-secondary-fixed'
                      : 'bg-surface-container-low hover:bg-surface-container text-on-surface'
                    }
                  `}
                  style={{ fontSize: '13px', fontWeight: 600 }}
                >
                  <div>{p.label}</div>
                  <div className="opacity-60 text-[10px] mt-0.5 font-mono">
                    CRF {p.crf}
                  </div>
                </button>
              ))}
            </div>
          </div>

          <div className="bg-surface-container-lowest rounded-[18px] p-6 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10">
            <label
              className="block mb-3 text-on-surface-variant text-xs font-semibold tracking-[0.02em]"
            >
              当前 CRF
            </label>
            <p
              className="text-on-surface font-bold text-[32px]"
            >
              {crf}
            </p>
            <p
              className="text-on-surface-variant mt-1 text-[11px]"
            >
              数值越低 → 质量越好 / 体积越大
            </p>
          </div>
        </div>

        <div className="col-span-8">
          <div className="bg-surface-container-lowest rounded-[18px] p-10 shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 text-center">
            <Zap size={56} className="text-on-surface-variant mb-4 mx-auto" />
            <p
              className="text-on-surface mb-2 font-bold text-xl"
            >
              已添加 {videos.length} 个视频
            </p>
            <p
              className="text-on-surface-variant mb-6 text-sm"
            >
              {files.length} 个文件在队列中
            </p>

            <div className="flex gap-3 justify-center">
              <button
                onClick={handlePick}
                className="px-6 py-3 bg-surface-container-low text-on-surface rounded-xl hover:bg-surface-container transition-all text-[13px] font-semibold"
              >
                选择视频
              </button>
              <button
                onClick={handleCompress}
                disabled={isProcessing || videos.length === 0}
                className={`
                  px-6 py-3 rounded-xl transition-all flex items-center gap-2
                  ${isProcessing || videos.length === 0
                    ? 'bg-surface-container-high text-on-surface-variant cursor-not-allowed'
                    : 'bg-primary text-on-primary hover:opacity-80 active:opacity-70'
                  }
                `}
                style={{ fontSize: '13px', fontWeight: 600 }}
              >
                {isProcessing && <Loader2 size={14} className="animate-spin" />}
                {isProcessing ? '压缩中...' : '开始压缩'}
              </button>
            </div>
            {status && (
              <p
                className="text-on-surface-variant mt-4 font-mono text-[11px]"
              >
                {isProcessing ? `${status} ${videoProgress > 0 ? `(${Math.round(videoProgress)}%)` : ''}` : status}
              </p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
