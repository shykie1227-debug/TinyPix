import { describe, it, expect, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useTimelineDrag } from '../../src/hooks/useTimelineDrag';

function createMockTrack(rect: { left: number; width: number }) {
  const el = document.createElement('div');
  el.getBoundingClientRect = () =>
    ({
      left: rect.left,
      top: 0,
      width: rect.width,
      height: 20,
      right: rect.left + rect.width,
      bottom: 20,
      x: rect.left,
      y: 0,
      toJSON() {
        return this;
      },
    }) as DOMRect;
  return el;
}

describe('useTimelineDrag', () => {
  it('初始状态 dragging 为 null', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange: vi.fn(),
        onSeek: vi.fn(),
        trackRef,
      })
    );

    expect(result.current.dragging).toBeNull();
  });

  it('点击 start 触发拖拽并设置 dragging 状态', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange: vi.fn(),
        onSeek: vi.fn(),
        trackRef,
      })
    );

    const mockMouseEvent = {
      preventDefault: vi.fn(),
      stopPropagation: vi.fn(),
      clientX: 50,
    } as unknown as React.MouseEvent;

    act(() => {
      result.current.handleMouseDown(mockMouseEvent, 'start');
    });

    expect(result.current.dragging).toBe('start');
    expect(mockMouseEvent.preventDefault).toHaveBeenCalled();
    expect(mockMouseEvent.stopPropagation).toHaveBeenCalled();
  });

  it('拖拽中更新 trimStart 值', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const onTrimStartChange = vi.fn();
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange,
        onTrimEndChange: vi.fn(),
        onSeek: vi.fn(),
        trackRef,
      })
    );

    act(() => {
      result.current.handleMouseDown(
        { preventDefault: vi.fn(), stopPropagation: vi.fn(), clientX: 0 } as unknown as React.MouseEvent,
        'start'
      );
    });

    act(() => {
      document.dispatchEvent(new MouseEvent('mousemove', { clientX: 100 }));
    });

    expect(onTrimStartChange).toHaveBeenCalled();
  });

  it('拖拽结束清除 dragging 状态', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange: vi.fn(),
        onSeek: vi.fn(),
        trackRef,
      })
    );

    act(() => {
      result.current.handleMouseDown(
        { preventDefault: vi.fn(), stopPropagation: vi.fn(), clientX: 0 } as unknown as React.MouseEvent,
        'end'
      );
    });

    expect(result.current.dragging).toBe('end');

    act(() => {
      document.dispatchEvent(new MouseEvent('mouseup'));
    });

    expect(result.current.dragging).toBeNull();
  });

  it('start 拖拽时 trimStart 受边界限制（不超过 trimEnd - 0.1）', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const onTrimStartChange = vi.fn();
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 30,
        onTrimStartChange,
        onTrimEndChange: vi.fn(),
        onSeek: vi.fn(),
        trackRef,
      })
    );

    act(() => {
      result.current.handleMouseDown(
        { preventDefault: vi.fn(), stopPropagation: vi.fn(), clientX: 0 } as unknown as React.MouseEvent,
        'start'
      );
    });

    act(() => {
      document.dispatchEvent(new MouseEvent('mousemove', { clientX: 200 }));
    });

    expect(onTrimStartChange).toHaveBeenCalled();
    const lastCall = onTrimStartChange.mock.calls[onTrimStartChange.mock.calls.length - 1][0];
    expect(lastCall).toBeLessThanOrEqual(29.9);
  });

  it('end 拖拽时 trimEnd 受边界限制（不小于 trimStart + 0.1）', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const onTrimEndChange = vi.fn();
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 30,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange,
        onSeek: vi.fn(),
        trackRef,
      })
    );

    act(() => {
      result.current.handleMouseDown(
        { preventDefault: vi.fn(), stopPropagation: vi.fn(), clientX: 0 } as unknown as React.MouseEvent,
        'end'
      );
    });

    act(() => {
      document.dispatchEvent(new MouseEvent('mousemove', { clientX: 0 }));
    });

    expect(onTrimEndChange).toHaveBeenCalled();
    const lastCall = onTrimEndChange.mock.calls[onTrimEndChange.mock.calls.length - 1][0];
    expect(lastCall).toBeGreaterThanOrEqual(30.1);
  });

  it('playhead 拖拽时调用 onSeek 且受 0-duration 边界限制', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const onSeek = vi.fn();
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange: vi.fn(),
        onSeek,
        trackRef,
      })
    );

    act(() => {
      result.current.handleMouseDown(
        { preventDefault: vi.fn(), stopPropagation: vi.fn(), clientX: 0 } as unknown as React.MouseEvent,
        'playhead'
      );
    });

    act(() => {
      document.dispatchEvent(new MouseEvent('mousemove', { clientX: 100 }));
    });

    expect(onSeek).toHaveBeenCalled();
    const lastCall = onSeek.mock.calls[onSeek.mock.calls.length - 1][0];
    expect(lastCall).toBeGreaterThanOrEqual(0);
    expect(lastCall).toBeLessThanOrEqual(60);
  });

  it('点击轨道时调用 onSeek', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const onSeek = vi.fn();
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange: vi.fn(),
        onSeek,
        trackRef,
      })
    );

    act(() => {
      result.current.handleTrackClick({
        target: trackRef.current,
        clientX: 100,
      } as unknown as React.MouseEvent);
    });

    expect(onSeek).toHaveBeenCalled();
    const calledValue = onSeek.mock.calls[0][0];
    expect(calledValue).toBeGreaterThanOrEqual(0);
    expect(calledValue).toBeLessThanOrEqual(60);
  });

  it('点击非轨道目标时不调用 onSeek', () => {
    const trackRef = { current: createMockTrack({ left: 0, width: 200 }) };
    const onSeek = vi.fn();
    const { result } = renderHook(() =>
      useTimelineDrag({
        duration: 60,
        trimStart: 0,
        trimEnd: 60,
        onTrimStartChange: vi.fn(),
        onTrimEndChange: vi.fn(),
        onSeek,
        trackRef,
      })
    );

    act(() => {
      result.current.handleTrackClick({
        target: document.createElement('span'),
        clientX: 100,
      } as unknown as React.MouseEvent);
    });

    expect(onSeek).not.toHaveBeenCalled();
  });
});
