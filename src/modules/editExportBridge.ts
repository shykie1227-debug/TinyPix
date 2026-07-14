import { invoke } from '@tauri-apps/api/core';

export interface EditExportParams {
  inputPath: string;
  outputPath: string;
  startSecs: number;
  endSecs: number;
  speed: number;
  volume: number;
  brightness: number;
  contrast: number;
  targetFormat: 'mp4' | 'mov' | 'mkv';
}

export interface VideoResult {
  output_path: string;
  original_size: number;
  output_size: number;
  saved_bytes: number;
  processing_time_secs: number;
}

/**
 * 模块12：一站式编辑命令桥接模块（EditExportBridgeModule）
 * 封装 invoke('edit_and_export_video', ...) 的参数映射和错误处理
 */
export async function submitEditExport(params: EditExportParams): Promise<VideoResult> {
  if (params.startSecs < 0) {
    throw new Error('起始时间不能为负数');
  }
  if (params.startSecs >= params.endSecs) {
    throw new Error('起始时间必须小于结束时间');
  }
  if (params.speed <= 0 || params.speed > 100) {
    throw new Error(`不支持的速度倍率: ${params.speed}`);
  }
  if (params.volume < 0 || params.volume > 10) {
    throw new Error(`不支持的音量倍率: ${params.volume}`);
  }
  if (params.brightness < -100 || params.brightness > 100) {
    throw new Error(`不支持的亮度值: ${params.brightness}`);
  }
  if (params.contrast < -100 || params.contrast > 100) {
    throw new Error(`不支持的对比度值: ${params.contrast}`);
  }

  const normalizeFilterValue = (value: number) => Number((value / 100).toFixed(4));

  return await invoke<VideoResult>('edit_and_export_video', {
    inputPath: params.inputPath,
    outputPath: params.outputPath,
    startSecs: params.startSecs,
    endSecs: params.endSecs,
    speed: params.speed,
    volume: params.volume,
    brightness: normalizeFilterValue(params.brightness),
    contrast: normalizeFilterValue(params.contrast),
    format: params.targetFormat,
  });
}
