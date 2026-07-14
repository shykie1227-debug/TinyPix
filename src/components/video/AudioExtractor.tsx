import { useCallback, useEffect, useMemo, useState } from 'react';
import { Loader2, AudioWaveform, Play, Zap, Info } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore } from '../../stores/appStore';
import { useVideoProgress } from '../../hooks/useVideoProgress';
import { isVideoFormat } from '../../utils/mediaFormat';
import ToolOptionCard from '../common/ToolOptionCard';
import ChipButton from '../common/ChipButton';
import RadioOptionCard from '../common/RadioOptionCard';
import CustomSlider from '../common/CustomSlider';
import {
  AUDIO_FORMATS,
  findFormat,
  type AudioFormatLabel,
  type AudioResult,
} from '../../modules/audio/formatConverter';
import { generateOutputPath } from '../../modules/audio/outputManager';
import { invokeConvert } from '../../modules/audio/formatConverter';
import { invokeDirectExtract } from '../../modules/audio/streamExtractor';
import {
  AudioQualityOptimizer,
  type ExtractMode,
  type OptimizedConfig,
} from '../../modules/audio/qualityOptimizer';
import { useAudioSourceInfo } from '../../modules/audio/useAudioSourceInfo';
import { isCodecCompatibleWithFormat, type AudioFormat } from '../../modules/audio/inspector';

const WAVEFORM_BAR_COUNT = 18;
const FORMAT_OPTIONS = AUDIO_FORMATS.map((f) => ({ label: f.label, value: f.label }));
const EXTRACT_MODE_OPTIONS = [
  { label: '直接提取', desc: '速度最快，无损', value: 'direct' },
  { label: '音频重编码', desc: '可调节码率', value: 'reencode' },
];
const BITRATE_MARKS = [
  { value: 64, label: '64k' },
  { value: 128, label: '128k' },
  { value: 192, label: '192k' },
  { value: 256, label: '256k' },
  { value: 320, label: '320k' },
];

const formatDuration = (seconds: number): string => {
  if (!seconds || seconds <= 0) return '--:--';
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
};

const formatSizeMB = (mb: number): string => {
  if (!mb || mb <= 0) return '—';
  if (mb < 1) return `${(mb * 1024).toFixed(0)} KB`;
  return `${mb.toFixed(1)} MB`;
};

export default function AudioExtractor() {
  const { files, options } = useAppStore();
  const [audioFormat, setAudioFormat] = useState<AudioFormatLabel>('MP3');
  const [extractMode, setExtractMode] = useState<ExtractMode>('direct');
  const [bitrate, setBitrate] = useState(192);
  const [isExtracting, setIsExtracting] = useState(false);
  const [status, setStatus] = useState('');
  const [videoProgress, setVideoProgress] = useState(0);
  const [optimization, setOptimization] = useState<OptimizedConfig | null>(null);
  const [waveformHeights, setWaveformHeights] = useState<number[]>(
    () => Array.from({ length: WAVEFORM_BAR_COUNT }, () => Math.floor(Math.random() * 80) + 10)
  );
  useVideoProgress(setVideoProgress);

  const videos = useMemo(() => files.filter((file) => isVideoFormat(file.format)), [files]);
  const firstVideo = videos[0];
  const isDirectMode = extractMode === 'direct';

  // M1: 加载源音频信息（用于智能提示与预计大小）
  const source = useAudioSourceInfo(firstVideo?.path ?? null);

  useEffect(() => {
    const interval = setInterval(() => {
      setWaveformHeights(
        Array.from({ length: WAVEFORM_BAR_COUNT }, () => Math.floor(Math.random() * 80) + 10)
      );
    }, 150);
    return () => clearInterval(interval);
  }, []);

  // M4: 实时计算优化参数（预计大小、警告）
  useEffect(() => {
    const optimizer = new AudioQualityOptimizer();
    const format = findFormat(audioFormat);
    if (!format) return;
    const result = optimizer.optimize(
      format,
      extractMode,
      source.info?.bitrateKbps,
      source.info?.durationSecs,
      isDirectMode ? undefined : bitrate
    );
    setOptimization(result);
  }, [audioFormat, extractMode, bitrate, isDirectMode, source.info]);

  // 兼容性判断（基于 M1 + 当前 format）
  const compatibility = useMemo<{ compatible: boolean; reason: string }>(() => {
    if (!source.info || source.info.codec === 'unknown') {
      return { compatible: true, reason: '' };
    }
    const compatible = isCodecCompatibleWithFormat(source.info.codec, audioFormat as AudioFormatLabel);
    if (compatible) {
      return { compatible: true, reason: '可走直接提取，零损失' };
    }
    return {
      compatible: false,
      reason: `源编码 ${source.info.codec} 与 ${audioFormat} 不兼容，将自动重编码`,
    };
  }, [source.info, audioFormat]);

  const handleExtractAudio = useCallback(async () => {
    if (videos.length === 0 || isExtracting) return;
    const format = findFormat(audioFormat);
    if (!format) return;

    setIsExtracting(true);
    setStatus('正在提取音频...');
    try {
      let successCount = 0;
      for (const video of videos) {
        const outputPath = generateOutputPath(
          video.path,
          '_audio',
          format.ext,
          options.outputDir
        );

        let result: AudioResult;
        if (extractMode === 'direct') {
          result = await invokeDirectExtract({
            inputPath: video.path,
            outputPath,
          });
        } else {
          result = await invokeConvert({
            inputPath: video.path,
            outputPath,
            format,
            bitrateKbps: bitrate,
          });
        }
        successCount += 1;
        setStatus(`音频提取中 ${successCount}/${videos.length}`);
      }
      setStatus(`音频提取完成 ${successCount}/${videos.length}`);
      if (successCount > 0 && options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    } catch (error) {
      setStatus(`提取失败: ${error}`);
    } finally {
      setIsExtracting(false);
    }
  }, [videos, audioFormat, extractMode, bitrate, isExtracting, options.outputDir, options.openAfterProcess]);

  const barColors = useMemo(() => {
    const colors = ['bg-secondary-container', 'bg-secondary', 'bg-secondary-fixed'];
    return Array.from({ length: WAVEFORM_BAR_COUNT }, (_, i) => colors[i % 3]);
  }, []);

  // M1 → 源文件信息（时长/编码/采样率/码率）
  const sourceInfoItems = useMemo(() => {
    if (!source.info) return [];
    const { codec, sampleRate, channels, bitrateKbps } = source.info;
    const items: { label: string; value: string }[] = [];
    items.push({ label: '编码', value: codec && codec !== 'unknown' ? codec.toUpperCase() : '—' });
    items.push({ label: '时长', value: formatDuration(source.info.durationSecs) });
    if (sampleRate > 0) items.push({ label: '采样率', value: `${(sampleRate / 1000).toFixed(1)} kHz` });
    if (channels) items.push({ label: '声道', value: channels === 1 ? '单声道' : '立体声' });
    if (bitrateKbps) items.push({ label: '码率', value: `${bitrateKbps} kbps` });
    return items;
  }, [source.info]);

  return (
    <div className="flex flex-col gap-6 sticky top-4 max-h-[calc(100vh-8rem)] overflow-y-auto custom-scrollbar">
      <div className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 p-4">
        <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4 flex items-center gap-2">
          <AudioWaveform size={18} className="text-secondary" />
          音频预览
        </label>
        <div className="flex items-end justify-between gap-[2px] overflow-hidden px-1 h-12 mb-2">
          {waveformHeights.map((height, index) => (
            <span
              key={index}
              className={`waveform-bar rounded-full w-1 ${barColors[index]}`}
              style={{ height: `${height}%` }}
            />
          ))}
        </div>
        <div className="flex items-center justify-between">
          <span className="font-mono-status opacity-60 text-[12px]">
            00:00 / {formatDuration(source.info?.durationSecs || 0)}
          </span>
          <button
            type="button"
            className="w-10 h-10 rounded-full bg-primary text-on-primary flex items-center justify-center hover:opacity-80 active:opacity-70 transition-opacity"
            aria-label="播放"
          >
            <Play size={18} fill="currentColor" />
          </button>
        </div>
      </div>

      {sourceInfoItems.length > 0 && (
        <div
          data-testid="file-info-card"
          className="bg-surface-container-lowest rounded-[18px] shadow-[0px_10px_30px_rgba(0,0,0,0.04)] border border-outline-variant/10 p-4"
        >
          <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-3 flex items-center gap-2">
            <Info size={14} className="text-secondary" />
            文件信息
          </label>
          <div className="grid grid-cols-2 gap-2">
            {sourceInfoItems.map((item) => (
              <div
                key={item.label}
                data-testid={`file-info-${item.label}`}
                className="flex items-center justify-between rounded-xl bg-surface-container-low px-3 py-1.5"
              >
                <span className="text-on-surface-variant text-xs">{item.label}</span>
                <span className="text-secondary font-bold text-xs">{item.value}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      <ToolOptionCard>
        <div className="space-y-6">
          <div>
            <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4">输出格式</label>
            <ChipButton
              options={FORMAT_OPTIONS}
              value={audioFormat}
              onChange={(v) => setAudioFormat(v as AudioFormatLabel)}
              gridCols={3}
            />
          </div>

          <div>
            <label className="font-label-caps text-label-caps uppercase opacity-50 block mb-4">提取方式</label>
            <RadioOptionCard
              options={EXTRACT_MODE_OPTIONS}
              value={extractMode}
              onChange={(v) => setExtractMode(v as ExtractMode)}
            />
            {source.info && source.info.codec !== 'unknown' && (
              <div
                className={
                  compatibility.compatible
                    ? 'mt-2 text-xs flex items-center gap-1.5 text-secondary'
                    : 'mt-2 text-xs flex items-center gap-1.5 text-tertiary'
                }
              >
                <Info size={12} />
                <span>{compatibility.reason}</span>
              </div>
            )}
          </div>

          <div>
            <div className="flex items-center justify-between mb-4">
              <label className="font-label-caps text-label-caps uppercase opacity-50 block">输出码率</label>
              <span className="text-secondary font-bold text-body-sm">
                {isDirectMode ? '自动' : `${bitrate} kbps`}
              </span>
            </div>
            <CustomSlider
              min={64}
              max={320}
              step={64}
              value={bitrate}
              onChange={setBitrate}
              marks={BITRATE_MARKS}
              disabled={isDirectMode}
            />
          </div>

          {optimization && optimization.estimatedSizeMB > 0 && (
            <div className="flex items-center justify-between rounded-xl bg-surface-container-low px-3 py-2">
              <span className="text-on-surface-variant text-xs">预计输出</span>
              <span className="text-secondary font-bold text-sm">
                {formatSizeMB(optimization.estimatedSizeMB)}
              </span>
            </div>
          )}

          {optimization && optimization.warnings.length > 0 && (
            <div className="text-xs text-on-surface-variant bg-tertiary-container/40 rounded-xl p-3">
              {optimization.warnings.map((w, i) => (
                <p key={i}>• {w}</p>
              ))}
            </div>
          )}
        </div>
      </ToolOptionCard>

      <button
        type="button"
        onClick={handleExtractAudio}
        disabled={!firstVideo || isExtracting}
        className={
          !firstVideo || isExtracting
            ? 'w-full py-3.5 rounded-full bg-surface-container-highest text-on-surface-variant cursor-not-allowed text-body-sm font-semibold flex items-center justify-center gap-2'
            : 'w-full py-3.5 rounded-full bg-primary text-on-primary hover:opacity-80 active:opacity-70 transition-opacity text-body-sm font-semibold flex items-center justify-center gap-2'
        }
      >
        {isExtracting ? (
          <span className="flex items-center justify-center gap-2">
            <Loader2 size={18} className="animate-spin" />
            提取中...
          </span>
        ) : (
          <>
            <Zap size={18} />
            开始提取音频
          </>
        )}
      </button>

      {isExtracting && videoProgress > 0 && (
        <div>
          <div className="w-full bg-surface-container-high rounded-full h-2">
            <div
              className="bg-secondary-fixed h-2 rounded-full transition-all duration-300"
              style={{ width: `${videoProgress}%` }}
            />
          </div>
          <p className="text-on-surface-variant mt-1.5 text-center text-xs">
            提取中 {Math.round(videoProgress)}%
          </p>
        </div>
      )}

      {status && (
        <p className="text-center text-xs text-on-surface-variant">{status}</p>
      )}

      {videos.length > 1 && (
        <div className="bg-tertiary-container/60 rounded-xl px-4 py-2.5 text-on-tertiary-container text-xs">
          将按当前参数批量提取 {videos.length} 个视频的音频
        </div>
      )}
    </div>
  );
}
