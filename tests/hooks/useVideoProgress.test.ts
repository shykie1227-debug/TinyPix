import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useVideoProgress } from '../../src/hooks/useVideoProgress';
import { listen } from '@tauri-apps/api/event';

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(() => Promise.resolve(vi.fn())),
}));

describe('useVideoProgress', () => {
  let tauriMock: unknown;

  beforeEach(() => {
    vi.clearAllMocks();
    tauriMock = (window as Record<string, unknown>).__TAURI_INTERNALS__;
    (window as Record<string, unknown>).__TAURI_INTERNALS__ = {};
  });

  afterEach(() => {
    if (tauriMock !== undefined) {
      (window as Record<string, unknown>).__TAURI_INTERNALS__ = tauriMock;
    } else {
      delete (window as Record<string, unknown>).__TAURI_INTERNALS__;
    }
  });

  it('Tauri 不可用时不会注册监听', () => {
    delete (window as Record<string, unknown>).__TAURI_INTERNALS__;
    renderHook(() => useVideoProgress());
    expect(listen).not.toHaveBeenCalled();
  });

  it('Tauri 可用时注册 video-progress 事件监听', async () => {
    renderHook(() => useVideoProgress());
    await vi.waitFor(() => {
      expect(listen).toHaveBeenCalledWith('video-progress', expect.any(Function));
    });
  });

  it('收到进度事件时调用 onProgress 回调', async () => {
    const onProgress = vi.fn();
    let eventHandler: ((event: { payload: { progress_pct: number } }) => void) | null = null;

    vi.mocked(listen).mockImplementation(async (_event, handler) => {
      eventHandler = handler as (event: { payload: { progress_pct: number } }) => void;
      return vi.fn();
    });

    renderHook(() => useVideoProgress(onProgress));

    await vi.waitFor(() => expect(eventHandler).not.toBeNull());

    act(() => {
      eventHandler!({ payload: { progress_pct: 45 } });
    });

    expect(onProgress).toHaveBeenCalledWith(45);

    act(() => {
      eventHandler!({ payload: { progress_pct: 100 } });
    });

    expect(onProgress).toHaveBeenCalledWith(100);
  });

  it('组件卸载时取消监听', async () => {
    const unlisten = vi.fn();
    vi.mocked(listen).mockResolvedValue(unlisten);

    const { unmount } = renderHook(() => useVideoProgress());

    await vi.waitFor(() => expect(listen).toHaveBeenCalled());

    unmount();
    expect(unlisten).toHaveBeenCalled();
  });

  it('listen 抛出异常时不崩溃', async () => {
    vi.mocked(listen).mockRejectedValue(new Error('not available'));

    expect(() => renderHook(() => useVideoProgress())).not.toThrow();
    await vi.waitFor(() => expect(listen).toHaveBeenCalled());
  });
});
