import { useMemo, useRef, useState } from 'react';
import { ChevronDown, Download, FolderOpen, Square } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { open } from '@tauri-apps/plugin-dialog';
import ToolOptionCard from '../common/ToolOptionCard';
import RadioOptionCard from '../common/RadioOptionCard';
import { useAppStore } from '../../stores/appStore';
import { isVideoFormat } from '../../utils/mediaFormat';
import { getVideoOutputPath } from '../../utils/videoOutput';
import {
  OUTPUT_FORMATS,
  QUALITY_PRESETS,
  getDefaultSettings,
  getOutputExtension,
  getOutputKind,
  getRecommendedCodecs,
  type OutputFormat,
  type VideoOutputFormat,
  type VideoQualityPreset,
} from '../../modules/video/outputProfiles';
import { useVideoProgress } from '../../hooks/useVideoProgress';

const RESOLUTIONS = [
  { label: '保持原尺寸', width: null, height: null },
  { label: '1080P', width: 1920, height: 1080 },
  { label: '720P', width: 1280, height: 720 },
] as const;

export default function VideoOutput() {
  const { files, options, isProcessing, setProcessing, setProgress, updateFile } = useAppStore();
  useVideoProgress(setProgress);
  const [settings, setSettings] = useState(getDefaultSettings);
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [status, setStatus] = useState('选择输出格式后即可开始');
  const cancelRequested = useRef(false);
  const videos = useMemo(() => files.filter((file) => isVideoFormat(file.format)), [files]);
  const queuedVideos = useMemo(
    () => videos.filter((file) => file.status === 'pending' || file.status === 'error'),
    [videos]
  );
  const kind = getOutputKind(settings.format);

  const setFormat = (format: OutputFormat) => {
    setSettings((current) => ({ ...current, format }));
  };

  const pickOutputDirectory = async () => {
    const selected = await open({ directory: true });
    if (selected) useAppStore.getState().setOptions({ outputDir: String(selected) });
  };

  const run = async () => {
    if (isProcessing || queuedVideos.length === 0) {
      setStatus(videos.length === 0 ? '请先添加至少一个视频文件' : status);
      return;
    }

    setProcessing(true);
    setProgress(0);
    cancelRequested.current = false;
    let completed = 0;
    let failed = 0;
    const extension = getOutputExtension(settings.format);

    for (const file of queuedVideos) {
      if (cancelRequested.current) break;
      updateFile(file.id, { status: 'processing', error: undefined });
      const outputPath = getVideoOutputPath(file.path, extension, kind, options.outputDir);
      try {
        let actualOutputPath = outputPath;
        if (kind === 'audio') {
          const result = await invoke<{ output_path: string }>('extract_audio', {
            inputPath: file.path,
            outputPath,
            format: extension,
            bitrateKbps: Math.round(settings.audioBitrate / 1000),
            mode: settings.audioMode === 'direct' ? 'direct' : 'reencode',
          });
          actualOutputPath = result.output_path;
        } else {
          const codecs = getRecommendedCodecs(settings.format as VideoOutputFormat);
          const result = await invoke<{ output_path: string }>('convert_video_format', {
            inputPath: file.path,
            outputPath,
            targetFormat: extension,
            quality: settings.quality,
            videoCodec: codecs.video,
            resolutionWidth: settings.resolutionWidth,
            resolutionHeight: settings.resolutionHeight,
            fps: settings.fps,
            audioCodec: codecs.audio,
            audioBitrate: settings.audioBitrate,
          });
          actualOutputPath = result.output_path;
        }
        completed += 1;
        updateFile(file.id, { status: 'completed', outputPath: actualOutputPath });
      } catch (error) {
        if (cancelRequested.current) break;
        failed += 1;
        updateFile(file.id, { status: 'error', error: String(error) });
      }
      setStatus(`正在处理 ${completed + failed}/${queuedVideos.length}`);
    }

    setProcessing(false);
    setProgress(100);
    setStatus(cancelRequested.current ? `已取消，完成 ${completed} 个` : (failed ? `完成 ${completed} 个，失败 ${failed} 个` : `全部完成，共 ${completed} 个`));
    if (completed > 0 && options.openAfterProcess && options.outputDir?.trim()) {
      await invoke('open_folder', { path: options.outputDir });
    }
  };

  const cancel = async () => {
    cancelRequested.current = true;
    setStatus('正在取消当前任务…');
    await invoke('cancel_video_tasks');
  };

  return (
    <div className="flex min-w-0 flex-col gap-4" aria-label="视频输出参数">
      <ToolOptionCard title="视频输出" subtitle="压缩、格式转换和音频提取统一在这里完成">
        <div className="space-y-5">
          <fieldset>
            <legend className="mb-3 text-xs font-semibold text-on-surface-variant">输出格式</legend>
            <div className="grid grid-cols-3 gap-2">
              {OUTPUT_FORMATS.map((item) => (
                <button
                  type="button"
                  key={item.label}
                  aria-label={`${item.label} · ${item.title}`}
                  aria-pressed={settings.format === item.label}
                  onClick={() => setFormat(item.label)}
                  className={`min-h-11 min-w-0 rounded-xl border px-2 py-2 text-left transition-colors ${
                    settings.format === item.label
                      ? 'border-primary bg-primary text-on-primary'
                      : 'border-outline-variant/30 bg-surface-container-low text-on-surface hover:bg-surface-container-high'
                  }`}
                >
                  <span className="block truncate text-sm font-bold">{item.label}</span>
                  <span className="block truncate text-[10px] opacity-70">{item.title}</span>
                </button>
              ))}
            </div>
          </fieldset>

          {kind === 'video' && (
            <fieldset>
              <legend className="mb-3 text-xs font-semibold text-on-surface-variant">用途预设</legend>
              <RadioOptionCard
                value={settings.qualityPreset}
                onChange={(value) => {
                  const preset = value as VideoQualityPreset;
                  setSettings((current) => ({
                    ...current,
                    qualityPreset: preset,
                    quality: QUALITY_PRESETS[preset].quality,
                  }));
                }}
                options={Object.entries(QUALITY_PRESETS).map(([value, item]) => ({
                  value,
                  label: item.label,
                  desc: item.hint,
                  badge: value === 'balanced' ? '推荐' : undefined,
                }))}
              />
            </fieldset>
          )}

          <div className="grid grid-cols-2 gap-2">
            <button type="button" onClick={pickOutputDirectory} className="min-h-11 rounded-full bg-surface-container px-3 text-sm font-semibold text-on-surface hover:opacity-80">
              <span className="inline-flex items-center gap-2"><FolderOpen size={16} />输出目录</span>
            </button>
            <button
              type="button"
              aria-expanded={advancedOpen}
              onClick={() => setAdvancedOpen((value) => !value)}
              className="min-h-11 rounded-full bg-surface-container px-3 text-sm font-semibold text-on-surface hover:opacity-80"
            >
              <span className="inline-flex items-center gap-2">高级设置<ChevronDown size={16} className={advancedOpen ? 'rotate-180' : ''} /></span>
            </button>
          </div>

          {advancedOpen && (
            <div className="space-y-4 rounded-xl bg-surface-container-low p-3">
              {kind === 'video' ? (
                <fieldset>
                  <legend className="mb-2 text-xs font-semibold">输出尺寸</legend>
                  <div className="grid grid-cols-3 gap-2">
                    {RESOLUTIONS.map((resolution) => (
                      <button
                        type="button"
                        key={resolution.label}
                        onClick={() => setSettings((current) => ({ ...current, resolutionWidth: resolution.width, resolutionHeight: resolution.height }))}
                        className={`min-h-10 rounded-lg px-2 text-xs font-semibold ${settings.resolutionWidth === resolution.width ? 'bg-secondary-container text-on-secondary-container' : 'bg-surface-container-lowest text-on-surface'}`}
                      >
                        {resolution.label}
                      </button>
                    ))}
                  </div>
                </fieldset>
              ) : (
                <p className="text-xs text-on-surface-variant">音频将使用稳定的软件编码器导出，避免依赖显卡驱动。</p>
              )}
            </div>
          )}

          <button
            type="button"
            disabled={!isProcessing && queuedVideos.length === 0}
            onClick={isProcessing ? () => void cancel() : run}
            className="flex min-h-11 w-full items-center justify-center gap-2 rounded-full bg-primary px-5 text-sm font-semibold text-on-primary transition-opacity hover:opacity-80 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isProcessing ? <Square size={17} /> : <Download size={17} />}
            {isProcessing ? '取消输出' : queuedVideos.some((file) => file.status === 'error') ? '重试失败项' : '开始输出'}
          </button>
          <p role="status" aria-live="polite" className="break-words text-center text-xs text-on-surface-variant">{status}</p>
        </div>
      </ToolOptionCard>
    </div>
  );
}
