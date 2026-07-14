import { renderHook, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import { useAudioSourceInfo, type AudioSourceState } from '../../../src/modules/audio/useAudioSourceInfo';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

// 期望值（camelCase，inspect() 映射后应与此一致）
const MOCK_INFO = {
  codec: 'aac',
  sampleRate: 48000,
  channels: 2,
  durationSecs: 60,
  bitrateKbps: 128,
};

// 后端 AudioSourceInfo 结构体真实返回（snake_case，无 #[serde(rename_all = "camelCase")]）
const MOCK_RAW_SNAKE = {
  codec: 'aac',
  sample_rate: 48000,
  channels: 2,
  duration_secs: 60,
  bitrate_kbps: 128,
};

describe('useAudioSourceInfo Hook', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns idle state when inputPath is null', () => {
    const { result } = renderHook(() => useAudioSourceInfo(null));
    expect(result.current.state).toBe<AudioSourceState>('idle');
    expect(result.current.info).toBeNull();
  });

  it('loads source info when inputPath is provided', async () => {
    vi.mocked(invoke).mockResolvedValueOnce(MOCK_RAW_SNAKE);

    const { result } = renderHook(() => useAudioSourceInfo('/Users/test/movie.mp4'));

    expect(result.current.state).toBe<AudioSourceState>('loading');

    await waitFor(() => {
      expect(result.current.state).toBe<AudioSourceState>('ready');
    });
    expect(result.current.info).toEqual(MOCK_INFO);
    expect(invoke).toHaveBeenCalledWith('inspect_audio', { inputPath: '/Users/test/movie.mp4' });
  });

  it('transitions to error state on backend failure', async () => {
    vi.mocked(invoke).mockRejectedValueOnce(new Error('ffprobe 失败'));

    const { result } = renderHook(() => useAudioSourceInfo('/Users/test/movie.mp4'));

    await waitFor(() => {
      expect(result.current.state).toBe<AudioSourceState>('error');
    });
    expect(result.current.error).toBe('ffprobe 失败');
    expect(result.current.info).toBeNull();
  });

  it('falls back to unknown when backend returns unknown codec', async () => {
    vi.mocked(invoke).mockResolvedValueOnce({
      codec: 'unknown',
      sampleRate: 0,
      channels: 2,
      durationSecs: 0,
    });

    const { result } = renderHook(() => useAudioSourceInfo('/Users/test/movie.mp4'));

    await waitFor(() => {
      expect(result.current.state).toBe<AudioSourceState>('ready');
    });
    expect(result.current.info?.codec).toBe('unknown');
  });

  it('reloads when inputPath changes', async () => {
    vi.mocked(invoke).mockResolvedValueOnce(MOCK_RAW_SNAKE);
    const { result, rerender } = renderHook(({ path }) => useAudioSourceInfo(path), {
      initialProps: { path: '/Users/test/movie1.mp4' },
    });

    await waitFor(() => {
      expect(result.current.state).toBe<AudioSourceState>('ready');
    });

    vi.mocked(invoke).mockResolvedValueOnce({ ...MOCK_RAW_SNAKE, duration_secs: 120 });
    rerender({ path: '/Users/test/movie2.mp4' });

    await waitFor(() => {
      expect(result.current.info?.durationSecs).toBe(120);
    });
  });
});

describe('useAudioSourceInfo — 字段映射 (snake_case → camelCase)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('将后端 snake_case 字段映射为前端 camelCase 字段', async () => {
    // 模拟后端 AudioSourceInfo 结构体真实返回（无 #[serde(rename_all = "camelCase")]）
    vi.mocked(invoke).mockResolvedValueOnce({
      codec: 'aac',
      sample_rate: 48000,
      channels: 2,
      duration_secs: 120.5,
      bitrate_kbps: 128,
    } as unknown as Awaited<ReturnType<typeof invoke>>);

    const { result } = renderHook(() => useAudioSourceInfo('/tmp/test.mp4'));

    await waitFor(() => {
      expect(result.current.state).toBe<AudioSourceState>('ready');
    });

    expect(result.current.info?.sampleRate).toBe(48000);
    expect(result.current.info?.durationSecs).toBe(120.5);
    expect(result.current.info?.bitrateKbps).toBe(128);
  });
});
