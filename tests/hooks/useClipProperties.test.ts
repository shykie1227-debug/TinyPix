import { describe, it, expect } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useClipProperties } from '../../src/hooks/useClipProperties';

describe('useClipProperties', () => {
  it('initializes with defaults and duration as trimEnd', () => {
    const { result } = renderHook(() => useClipProperties(120));
    expect(result.current.trimStart).toBe(0);
    expect(result.current.trimEnd).toBe(120);
    expect(result.current.speed).toBe(1);
    expect(result.current.volume).toBe(100);
    expect(result.current.brightness).toBe(0);
    expect(result.current.contrast).toBe(0);
    expect(result.current.exportFormat).toBe('mp4');
  });

  it('updates individual setters', () => {
    const { result } = renderHook(() => useClipProperties(120));
    act(() => result.current.setSpeed(2));
    expect(result.current.speed).toBe(2);
    act(() => result.current.setVolume(150));
    expect(result.current.volume).toBe(150);
  });

  it('setTrimRange updates both bounds', () => {
    const { result } = renderHook(() => useClipProperties(120));
    act(() => result.current.setTrimRange(10, 60));
    expect(result.current.trimStart).toBe(10);
    expect(result.current.trimEnd).toBe(60);
  });

  it('reset restores defaults but keeps duration', () => {
    const { result } = renderHook(() => useClipProperties(120));
    act(() => {
      result.current.setSpeed(4);
      result.current.setBrightness(50);
      result.current.setExportFormat('mkv');
      result.current.setTrimRange(5, 50);
    });
    act(() => result.current.reset());
    expect(result.current.speed).toBe(1);
    expect(result.current.brightness).toBe(0);
    expect(result.current.exportFormat).toBe('mp4');
    expect(result.current.trimStart).toBe(0);
    expect(result.current.trimEnd).toBe(120);
  });

  it('syncs trimEnd when metadata duration arrives after initial render', () => {
    let duration = 0;
    const { result, rerender } = renderHook(() => useClipProperties(duration));

    expect(result.current.trimEnd).toBe(0);

    duration = 120;
    rerender();

    expect(result.current.trimEnd).toBe(120);

    act(() => {
      result.current.setTrimRange(10, 40);
      result.current.reset();
    });

    expect(result.current.trimStart).toBe(0);
    expect(result.current.trimEnd).toBe(120);
  });
});
