import { useMemo } from 'react';

interface FilterOptions {
  brightness?: number;
  contrast?: number;
  saturation?: number;
  flipH?: boolean;
  flipV?: boolean;
}

/**
 * 模块10：CSS 滤镜预览模块（CssFilterPreviewModule）
 * 将 brightness/contrast/saturation 数值（-100~100）转换为 CSS filter 字符串
 * 公式: factor = 1 + value/100
 * 同时支持 flipH/flipV 的镜像变换
 *
 * 向后兼容: 旧签名 useCssFilterPreview(brightness, contrast) 始终输出两个滤镜
 */
export function useCssFilterPreview(options: FilterOptions | number = {}, contrast?: number): string {
  return useMemo(() => {
    // 检测旧签名 (brightness, contrast) - 旧行为是始终输出两个滤镜
    if (typeof options === 'number') {
      const b = options;
      const c = contrast ?? 0;
      const bFactor = (1 + b / 100).toFixed(2);
      const cFactor = (1 + c / 100).toFixed(2);
      return `brightness(${bFactor}) contrast(${cFactor})`;
    }

    // 新签名: 智能输出非零滤镜
    const b = options.brightness ?? 0;
    const c = options.contrast ?? 0;
    const s = options.saturation ?? 0;
    const fh = options.flipH ?? false;
    const fv = options.flipV ?? false;

    const transforms: string[] = [];
    if (fh || fv) {
      const sx = fh ? -1 : 1;
      const sy = fv ? -1 : 1;
      transforms.push(`scaleX(${sx}) scaleY(${sy})`);
    }

    const filters: string[] = [];
    if (b !== 0) filters.push(`brightness(${(1 + b / 100).toFixed(2)})`);
    if (c !== 0) filters.push(`contrast(${(1 + c / 100).toFixed(2)})`);
    if (s !== 0) filters.push(`saturate(${(1 + s / 100).toFixed(2)})`);

    return [...transforms, ...filters].join(' ') || 'none';
  }, [options, contrast]);
}
