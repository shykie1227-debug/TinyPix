/**
 * M4 — AudioQualityOptimizer
 *
 * 职责边界：
 * - ✅ 根据格式 + 模式推荐码率
 * - ✅ 估算输出文件大小
 * - ✅ 验证参数合法性（码率范围、格式与 bitrate 兼容性）
 * - ✅ 返回 warning
 * - ❌ 不做提取/转码
 * - ❌ 不写文件
 *
 * 单一职责：为音频提取/转码提供质量参数推荐与合法性校验。
 */

import type { AudioFormat, AudioFormatLabel } from './formatConverter';
import { findFormat } from './formatConverter';

export type ExtractMode = 'direct' | 'reencode';

export interface OptimizedConfig {
  recommendedBitrate: number;
  estimatedSizeMB: number;
  warnings: string[];
}

const BITRATE_MIN = 64;
const BITRATE_MAX = 320;
const LOSSY_DEFAULT_BITRATE = 192;
const WAV_SAMPLE_RATE = 48000;
const WAV_BYTES_PER_SAMPLE = 2;
const WAV_CHANNELS = 2;
const FLAC_COMPRESSION_RATIO = 0.55;

/** 纯函数：获取格式的默认码率（lossless 返回 0） */
export const getDefaultBitrate = (label: AudioFormatLabel): number => {
  const format = findFormat(label);
  if (!format) return 0;
  return format.lossless ? 0 : LOSSY_DEFAULT_BITRATE;
};

/** 纯函数：估算输出文件大小（MB） */
export const estimateSizeMB = (format: AudioFormat, bitrateKbps: number, durationSecs: number): number => {
  if (!durationSecs || durationSecs <= 0) return 0;
  if (format.lossless) {
    const rawBytes = WAV_SAMPLE_RATE * WAV_BYTES_PER_SAMPLE * WAV_CHANNELS * durationSecs;
    const sizeBytes = format.label === 'FLAC' ? rawBytes * FLAC_COMPRESSION_RATIO : rawBytes;
    return sizeBytes / 1024 / 1024;
  }
  return (bitrateKbps * 1000 * durationSecs) / 8 / 1024 / 1024;
};

/** 纯函数：校验配置合法性，返回错误列表（空数组表示合法） */
export const validateConfig = (format: AudioFormat, mode: ExtractMode, userBitrate?: number): string[] => {
  const errors: string[] = [];

  if (mode !== 'direct' && mode !== 'reencode') {
    errors.push(`未知的提取模式: ${mode}`);
    return errors;
  }

  if (mode === 'direct' && userBitrate !== undefined) {
    errors.push('直接提取模式将忽略码率参数');
  }

  if (mode === 'reencode' && !format.lossless) {
    if (userBitrate === undefined) {
      errors.push(`${format.label} 格式在重编码模式下必须指定码率`);
    } else if (userBitrate < BITRATE_MIN || userBitrate > BITRATE_MAX) {
      errors.push(`码率必须在 ${BITRATE_MIN}-${BITRATE_MAX} kbps 之间`);
    }
  }

  return errors;
};

export class AudioQualityOptimizer {
  optimize(
    format: AudioFormat,
    mode: ExtractMode,
    _sourceBitrate?: number,
    durationSecs?: number,
    userBitrate?: number
  ): OptimizedConfig {
    const warnings: string[] = [];

    const recommendedBitrate = this.recommendBitrate(format, mode, userBitrate, warnings);
    const estimatedSizeMB = estimateSizeMB(format, recommendedBitrate, durationSecs || 0);

    return { recommendedBitrate, estimatedSizeMB, warnings };
  }

  private recommendBitrate(
    format: AudioFormat,
    mode: ExtractMode,
    userBitrate: number | undefined,
    warnings: string[]
  ): number {
    if (format.lossless) {
      if (userBitrate && userBitrate > 0) {
        warnings.push(`${format.label} 是无损格式，码率将被忽略`);
      }
      return 0;
    }

    if (mode === 'direct') {
      if (userBitrate !== undefined) {
        warnings.push('直接提取模式将忽略码率参数');
      }
      return 0;
    }

    if (userBitrate === undefined) {
      return LOSSY_DEFAULT_BITRATE;
    }

    if (userBitrate < BITRATE_MIN) {
      warnings.push(`码率 ${userBitrate} 低于最小值 ${BITRATE_MIN}，将提升到 ${BITRATE_MIN}`);
      return BITRATE_MIN;
    }
    if (userBitrate > BITRATE_MAX) {
      warnings.push(`码率 ${userBitrate} 高于最大值 ${BITRATE_MAX}，将限制为 ${BITRATE_MAX}`);
      return BITRATE_MAX;
    }
    return userBitrate;
  }
}
