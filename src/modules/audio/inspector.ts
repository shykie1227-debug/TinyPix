/**
 * M1 — AudioFileInspector
 *
 * 职责边界：
 * - ✅ 调用 ffprobe 解析视频文件的音频元信息
 * - ✅ 识别源音频编码、采样率、声道数、时长
 * - ✅ 判断当前所选输出格式与源编码的"直接提取兼容性"
 * - ❌ 不做提取/转码
 * - ❌ 不写文件
 *
 * 单一职责：解析并识别输入视频的音频流信息。
 */

import type { AudioFormat, AudioFormatLabel } from './formatConverter';
import { findFormat } from './formatConverter';

export type { AudioFormat, AudioFormatLabel } from './formatConverter';

export type AudioCodec = 'aac' | 'mp3' | 'opus' | 'vorbis' | 'flac' | 'pcm' | 'unknown';

export interface AudioSourceInfo {
  codec: AudioCodec;
  sampleRate: number;
  channels: 1 | 2;
  durationSecs: number;
  bitrateKbps?: number;
  compatibleFormats?: AudioFormatLabel[];
}

const CODEC_COMPATIBILITY: Record<AudioCodec, AudioFormatLabel[]> = {
  aac: ['M4A', 'AAC'],
  mp3: ['MP3'],
  opus: [],
  vorbis: [],
  flac: ['FLAC'],
  pcm: ['WAV'],
  unknown: [],
};

/** 纯函数：判断源 codec 与目标格式的兼容关系（直接提取场景） */
export const isCodecCompatibleWithFormat = (codec: AudioCodec, targetLabel: AudioFormatLabel): boolean => {
  const compatible = CODEC_COMPATIBILITY[codec] || [];
  return compatible.includes(targetLabel);
};

/** 纯函数：根据完整源信息判断直接提取兼容性 */
export const isDirectExtractCompatible = (source: AudioSourceInfo, target: AudioFormat): boolean => {
  return isCodecCompatibleWithFormat(source.codec, target.label);
};

/** 纯函数：解析 ffprobe JSON 输出 */
export const parseFfprobeOutput = (jsonStr: string): AudioSourceInfo => {
  try {
    const data = JSON.parse(jsonStr);
    const stream = data?.streams?.[0];
    if (!stream) {
      return { codec: 'unknown', sampleRate: 0, channels: 2, durationSecs: 0 };
    }
    const codec = (stream.codec_name as AudioCodec) || 'unknown';
    return {
      codec,
      sampleRate: parseInt(stream.sample_rate || '0', 10),
      channels: stream.channels === 1 ? 1 : 2,
      durationSecs: parseFloat(stream.duration || '0'),
      bitrateKbps: stream.bit_rate ? Math.round(parseInt(stream.bit_rate, 10) / 1000) : undefined,
    };
  } catch {
    return { codec: 'unknown', sampleRate: 0, channels: 2, durationSecs: 0 };
  }
};

export class AudioFileInspector {
  /**
   * 通过 invoke('ffprobe_audio', { path }) 获取 JSON 字符串
   * 实际实现由 Rust 端承担（src-tauri/src/domain/audio/inspector.rs）
   */
  async inspect(inputPath: string): Promise<AudioSourceInfo> {
    const { invoke } = await import('@tauri-apps/api/core');
    const raw = await invoke<unknown>('inspect_audio', { inputPath });
    // 后端 inspect_audio 返回 AudioSourceInfo 结构体（snake_case），需映射为前端 camelCase
    if (typeof raw === 'string') {
      return parseFfprobeOutput(raw);
    }
    const data = raw as {
      codec?: string;
      sample_rate?: number;
      channels?: number;
      duration_secs?: number;
      bitrate_kbps?: number;
    };
    return {
      codec: (data.codec as AudioCodec) || 'unknown',
      sampleRate: data.sample_rate ?? 0,
      channels: data.channels === 1 ? 1 : 2,
      durationSecs: data.duration_secs ?? 0,
      bitrateKbps: data.bitrate_kbps,
    };
  }

  isDirectExtractCompatible(source: AudioSourceInfo, target: AudioFormat): boolean {
    return isDirectExtractCompatible(source, target);
  }

  getCompatibleFormats(source: AudioSourceInfo): AudioFormat[] {
    const labels = CODEC_COMPATIBILITY[source.codec] || [];
    return labels.map((label) => findFormat(label)).filter((f): f is AudioFormat => Boolean(f));
  }
}
