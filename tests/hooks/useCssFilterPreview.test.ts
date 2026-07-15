import { describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useCssFilterPreview } from '../../src/hooks/useCssFilterPreview';

describe('useCssFilterPreview', () => {
  it.each([
    [0, 0, 'brightness(1.00) contrast(1.00)'],
    [100, 0, 'brightness(2.00) contrast(1.00)'],
    [-100, 0, 'brightness(0.00) contrast(1.00)'],
    [0, -50, 'brightness(1.00) contrast(0.50)'],
    [20, -30, 'brightness(1.20) contrast(0.70)'],
  ] as const)('maps brightness %s and contrast %s', (brightness, contrast, expected) => {
    const { result } = renderHook(() => useCssFilterPreview(brightness, contrast));
    expect(result.current).toBe(expected);
  });

  it.each([
    [{ saturation: 50 }, 'saturate(1.50)'],
    [{ flipH: true }, 'scaleX(-1) scaleY(1)'],
    [{ flipV: true }, 'scaleX(1) scaleY(-1)'],
    [{ flipH: true, flipV: true }, 'scaleX(-1) scaleY(-1)'],
    [{}, 'none'],
    [{ flipH: true, saturation: 100 }, 'scaleX(-1) scaleY(1) saturate(2.00)'],
  ] as const)('maps preview options', (options, expected) => {
    const { result } = renderHook(() => useCssFilterPreview(options));
    expect(result.current).toBe(expected);
  });
});
