/**
 * M2 — AudioStreamExtractor
 *
 * 职责边界：
 * - ✅ 直接拷贝音频流（-c:a copy），不重编码
 * - ✅ 支持任意输入容器，输出目标容器
 * - ✅ 推送进度事件
 * - ❌ 不做格式转换（不调 -c:a libmp3lame 等）
 * - ❌ 不变更码率
 *
 * 单一职责：从视频容器中无损提取音频流。
 */

import type { AudioResult } from './formatConverter';

export interface DirectExtractParams {
  inputPath: string;
  outputPath: string;
  onProgress?: (percent: number) => void;
}

export interface DirectExtractContext {
  inputPath: string;
  outputPath: string;
  app?: unknown;
  totalSecs?: number;
}

/** 纯函数：组装 -c:a copy 的 ffmpeg 参数（不实际执行） */
export const buildDirectExtractArgs = (inputPath: string, outputPath: string): string[] => {
  return ['-y', '-i', inputPath, '-vn', '-c:a', 'copy', outputPath];
};

/** Tauri 桥接：调用后端 extract_audio（Tauri command），direct 模式 */
export const invokeDirectExtract = async (params: DirectExtractParams): Promise<AudioResult> => {
  const { invoke } = await import('@tauri-apps/api/core');
  const ext = params.outputPath.split('.').pop() || 'm4a';
  const raw = await invoke<{
    output_path: string;
    original_size: number;
    output_size: number;
    saved_bytes: number;
    processing_time_secs: number;
  }>('extract_audio', {
    inputPath: params.inputPath,
    outputPath: params.outputPath,
    format: ext,
    bitrateKbps: undefined,
    mode: 'direct',
  });
  return {
    outputPath: raw.output_path,
    originalSize: raw.original_size,
    outputSize: raw.output_size,
    savedBytes: raw.saved_bytes,
    processingTimeSecs: raw.processing_time_secs,
  };
};

/** 抽象接口：可注入 mock 实现做单测 */
export interface IAudioStreamExtractor {
  extract(ctx: DirectExtractContext): Promise<AudioResult>;
}
