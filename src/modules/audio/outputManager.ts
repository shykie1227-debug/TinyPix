/**
 * M5 — OutputFileManager
 *
 * 职责边界：
 * - ✅ 生成输出路径（替换/拼接扩展名）
 * - ✅ 支持自定义 outputDir
 * - ✅ 跨平台分隔符兼容（Windows \ / macOS /）
 * - ✅ 路径合法性校验
 * - ❌ 不读取视频元信息
 * - ❌ 不调用 FFmpeg
 *
 * 单一职责：管理音频提取的输出文件路径与目录。
 */

const SEP_REGEX = /[\\/]+$/;

const basenameWithoutExtension = (path: string): string => {
  const name = path.split(/[\\/]/).pop() || path;
  return name.replace(/\.[^.]+$/, '');
};

const getExtensionFromPath = (path: string, fallback = ''): string => {
  const name = path.split(/[\\/]/).pop() || path;
  const match = name.match(/\.([^.]+)$/);
  return (match?.[1] || fallback).toLowerCase();
};

const normalizeExtension = (ext: string): string => {
  return ext.replace(/^\./, '').toLowerCase();
};

const normalizeDir = (dir: string): string => {
  return dir.replace(SEP_REGEX, '').trim();
};

/**
 * 纯函数：构建文件名
 * 规则：{basename}{suffix}.{extension}（扩展名小写、无前导点）
 */
export const buildFileName = (baseName: string, suffix: string, extension: string): string => {
  const ext = normalizeExtension(extension);
  return `${baseName}${suffix}.${ext}`;
};

/**
 * 纯函数：归一化输出目录
 * - undefined / '' → undefined
 * - 否则去除末尾分隔符
 */
export const ensureOutputDir = (outputDir?: string): string | undefined => {
  if (!outputDir || !outputDir.trim()) return undefined;
  return normalizeDir(outputDir);
};

/**
 * 纯函数：生成输出路径
 * - 有 outputDir：在 outputDir 下生成
 * - 无 outputDir：在输入文件同目录生成
 * - input 有扩展名：替换为新扩展名
 * - input 无扩展名：追加 suffix + .ext
 */
export const generateOutputPath = (
  inputPath: string,
  suffix: string,
  extension: string,
  outputDir?: string
): string => {
  const ext = normalizeExtension(extension);
  const dotExt = `.${ext}`;
  const basename = basenameWithoutExtension(inputPath);

  const dir = ensureOutputDir(outputDir);
  if (dir) {
    const sep = dir.includes('\\') && !dir.includes('/') ? '\\' : '/';
    return `${dir}${sep}${basename}${suffix}${dotExt}`;
  }

  if (/\.[^./\\]+$/.test(inputPath)) {
    return inputPath.replace(/\.[^./\\]+$/, `${suffix}${dotExt}`);
  }
  return `${inputPath}${suffix}${dotExt}`;
};

/**
 * OutputFileManager 类
 * 封装对单个输出路径的查询、校验操作。
 */
export class OutputFileManager {
  readonly path: string;

  constructor(path: string) {
    this.path = path;
  }

  /** 校验路径合法：非空、必须包含扩展名 */
  validate(): void {
    if (!this.path || !this.path.trim()) {
      throw new Error('输出路径不能为空');
    }
    if (!/\.[^./\\]+$/.test(this.path)) {
      throw new Error('输出路径必须包含扩展名');
    }
  }

  getDirectory(): string {
    const idx = Math.max(this.path.lastIndexOf('/'), this.path.lastIndexOf('\\'));
    return idx > 0 ? this.path.slice(0, idx) : '';
  }

  getBasename(): string {
    return this.path.split(/[\\/]/).pop() || this.path;
  }

  getExtension(): string {
    return getExtensionFromPath(this.path);
  }
}
