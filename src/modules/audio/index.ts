/**
 * audio 模块统一导出
 *
 * 5 个模块化单元：
 * - M1 AudioFileInspector: 音频文件解析与格式识别
 * - M2 AudioStreamExtractor: 音频流提取与处理
 * - M3 AudioFormatConverter: 音频格式转换
 * - M4 AudioQualityOptimizer: 音频质量优化
 * - M5 OutputFileManager: 输出文件管理与存储
 */

export * from './inspector';
export * from './streamExtractor';
export * from './formatConverter';
export * from './qualityOptimizer';
export * from './outputManager';
