import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useCssFilterPreview } from '../../src/hooks/useCssFilterPreview';

describe('useCssFilterPreview', () => {
  it('returns identity for 0/0', () => {
    const { result } = renderHook(() => useCssFilterPreview(0, 0));
    expect(result.current).toBe('brightness(1.00) contrast(1.00)');
  });

  it('boosts brightness by factor', () => {
    const { result } = renderHook(() => useCssFilterPreview(100, 0));
    expect(result.current).toBe('brightness(2.00) contrast(1.00)');
  });

  it('dims brightness by factor', () => {
    const { result } = renderHook(() => useCssFilterPreview(-100, 0));
    expect(result.current).toBe('brightness(0.00) contrast(1.00)');
  });

  it('handles negative contrast', () => {
    const { result } = renderHook(() => useCssFilterPreview(0, -50));
    expect(result.current).toBe('brightness(1.00) contrast(0.50)');
  });

  it('combines both', () => {
    const { result } = renderHook(() => useCssFilterPreview(20, -30));
    expect(result.current).toBe('brightness(1.20) contrast(0.70)');
  });
});

describe('useCssFilterPreview (new signature: options object)', () => {
  it('includes saturate() when saturation is non-zero', () => {
    const { result } = renderHook(() => useCssFilterPreview({ saturation: 50 }));
    expect(result.current).toBe('saturate(1.50)');
  });

  it('produces scaleX(-1) scaleY(1) when flipH is true', () => {
    const { result } = renderHook(() => useCssFilterPreview({ flipH: true }));
    expect(result.current).toBe('scaleX(-1) scaleY(1)');
  });

  it('produces scaleX(1) scaleY(-1) when flipV is true', () => {
    const { result } = renderHook(() => useCssFilterPreview({ flipV: true }));
    expect(result.current).toBe('scaleX(1) scaleY(-1)');
  });

  it('produces scaleX(-1) scaleY(-1) when both flipH and flipV are true', () => {
    const { result } = renderHook(() => useCssFilterPreview({ flipH: true, flipV: true }));
    expect(result.current).toBe('scaleX(-1) scaleY(-1)');
  });

  it('returns "none" when all params are default/none', () => {
    const { result } = renderHook(() => useCssFilterPreview({}));
    expect(result.current).toBe('none');
  });

  it('combines transforms and filters together (flipH + saturation)', () => {
    const { result } = renderHook(() => useCssFilterPreview({ flipH: true, saturation: 100 }));
    expect(result.current).toBe('scaleX(-1) scaleY(1) saturate(2.00)');
  });
});
