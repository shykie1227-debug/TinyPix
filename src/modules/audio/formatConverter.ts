/**
 * M3 — AudioFormatConverter
 *
 * 职责边界：
 * - ✅ 将音频流转码为指定格式（5 种 codec 映射）
 * - ✅ 应用码率参数（仅 mp3/aac/m4a）
 * - ✅ 推送进度事件
 * - ❌ 不做直接提取（用 M2）
 * - ❌ 不做质量优化决策（接收 M4 推荐参数）
 *
 * 单一职责：将源音频转码为目标音频格式。
 */

export type AudioFormatLabel = 'MP3' | 'WAV' | 'AAC' | 'FLAC' | 'M4A';

export interface AudioFormat {
  label: AudioFormatLabel;
  codec: string;
  ext: string;
  lossless: boolean;
}

/** 5 种支持的音频格式定义 */
export const AUDIO_FORMATS: AudioFormat[] = [
  { label: 'MP3', codec: 'libmp3lame', ext: 'mp3', lossless: false },
  { label: 'WAV', codec: 'pcm_s16le', ext: 'wav', lossless: true },
  { label: 'AAC', codec: 'aac', ext: 'aac', lossless: false },
  { label: 'FLAC', codec: 'flac', ext: 'flac', lossless: true },
  { label: 'M4A', codec: 'aac', ext: 'm4a', lossless: false },
];

/** 按 label 查找 AudioFormat */
export const findFormat = (label: AudioFormatLabel): AudioFormat | undefined => {
  return AUDIO_FORMATS.find((f) => f.label === label);
};

export interface ConvertContext {
  inputPath: string;
  outputPath: string;
  onProgress?: (percent: number) => void;
}

export interface ConvertConfig {
  format: AudioFormat;
  bitrateKbps?: number;
}

export interface AudioResult {
  outputPath: string;
  originalSize: number;
  outputSize: number;
  savedBytes: number;
  processingTimeSecs: number;
}

/** 抽象接口：可注入 mock 实现做单测 */
export interface IAudioFormatConverter {
  convert(ctx: ConvertContext, config: ConvertConfig): Promise<AudioResult>;
}

/** 纯函数：bitrate 合法范围校验 */
export const clampBitrate = (kbps: number): number => {
  return Math.max(64, Math.min(320, kbps));
};

/** 纯函数：组装 ffmpeg 命令参数 */
export const buildFfmpegArgs = (ctx: ConvertContext, config: ConvertConfig): string[] => {
  const { inputPath, outputPath } = ctx;
  const { format, bitrateKbps } = config;
  const args: string[] = ['-y', '-i', inputPath, '-vn', '-c:a', format.codec];
  if (!format.lossless && bitrateKbps) {
    args.push('-b:a', `${bitrateKbps}k`);
  }
  args.push(outputPath);
  return args;
};

/** Tauri 桥接：调用后端 extract_audio（Tauri command），用于 reencode 模式 */
export const invokeConvert = async (
  params: {
    inputPath: string;
    outputPath: string;
    format: AudioFormat;
    bitrateKbps?: number;
  }
): Promise<AudioResult> => {
  const { invoke } = await import('@tauri-apps/api/core');
  const raw = await invoke<{
    output_path: string;
    original_size: number;
    output_size: number;
    saved_bytes: number;
    processing_time_secs: number;
  }>('extract_audio', {
    inputPath: params.inputPath,
    outputPath: params.outputPath,
    format: params.format.ext,
    bitrateKbps: params.bitrateKbps,
    mode: 'reencode',
  });
  return {
    outputPath: raw.output_path,
    originalSize: raw.original_size,
    outputSize: raw.output_size,
    savedBytes: raw.saved_bytes,
    processingTimeSecs: raw.processing_time_secs,
  };
};
