import { useMemo } from 'react';

interface FilterOptions {
  brightness?: number;
  contrast?: number;
  saturation?: number;
  flipH?: boolean;
  flipV?: boolean;
}

export function useCssFilterPreview(options: FilterOptions | number = {}, contrast?: number): string {
  return useMemo(() => {
    if (typeof options === 'number') {
      const brightnessFactor = (1 + options / 100).toFixed(2);
      const contrastFactor = (1 + (contrast ?? 0) / 100).toFixed(2);
      return `brightness(${brightnessFactor}) contrast(${contrastFactor})`;
    }

    const transforms: string[] = [];
    if (options.flipH || options.flipV) {
      transforms.push(`scaleX(${options.flipH ? -1 : 1}) scaleY(${options.flipV ? -1 : 1})`);
    }
    const filters: string[] = [];
    if (options.brightness) filters.push(`brightness(${(1 + options.brightness / 100).toFixed(2)})`);
    if (options.contrast) filters.push(`contrast(${(1 + options.contrast / 100).toFixed(2)})`);
    if (options.saturation) filters.push(`saturate(${(1 + options.saturation / 100).toFixed(2)})`);
    return [...transforms, ...filters].join(' ') || 'none';
  }, [options, contrast]);
}
