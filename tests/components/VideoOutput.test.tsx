import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import VideoOutput from '../../src/components/video/VideoOutput';
import { useAppStore } from '../../src/stores/appStore';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));

describe('VideoOutput', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.setState({ files: [], isProcessing: false, progress: 0 });
  });

  it('shows the nine supported video and audio targets with one primary action', () => {
    render(<VideoOutput />);

    for (const label of ['MP4', 'MOV', 'MKV', 'AVI', 'WebM', 'MP3', 'WAV', 'AAC', 'FLAC']) {
      expect(screen.getByRole('button', { name: new RegExp(label, 'i') })).toBeInTheDocument();
    }
    expect(screen.getAllByRole('button', { name: '开始输出' })).toHaveLength(1);
  });

  it('converts every queued video with the compatible MP4 defaults', async () => {
    vi.mocked(invoke).mockResolvedValue({ output_path: '/tmp/out.mp4' });
    useAppStore.getState().addFiles([
      { id: '1', path: '/tmp/a.mov', name: 'a.mov', format: 'MOV', originalSize: 10, status: 'pending' },
      { id: '2', path: '/tmp/b.mkv', name: 'b.mkv', format: 'MKV', originalSize: 10, status: 'pending' },
    ]);

    render(<VideoOutput />);
    await userEvent.click(screen.getByRole('button', { name: '开始输出' }));

    await waitFor(() => expect(invoke).toHaveBeenCalledTimes(2));
    expect(invoke).toHaveBeenNthCalledWith(1, 'convert_video_format', expect.objectContaining({
      inputPath: '/tmp/a.mov',
      outputPath: '/tmp/a_output.mp4',
      targetFormat: 'mp4',
      videoCodec: 'h264',
      audioCodec: 'aac',
    }));
    expect(invoke).toHaveBeenNthCalledWith(2, 'convert_video_format', expect.objectContaining({
      inputPath: '/tmp/b.mkv',
      outputPath: '/tmp/b_output.mp4',
    }));
  });

  it('uses the audio command for audio-only targets', async () => {
    vi.mocked(invoke).mockResolvedValue({ output_path: '/tmp/a_audio.mp3' });
    useAppStore.getState().addFiles([
      { id: '1', path: '/tmp/a.mov', name: 'a.mov', format: 'MOV', originalSize: 10, status: 'pending' },
    ]);

    render(<VideoOutput />);
    await userEvent.click(screen.getByRole('button', { name: /MP3/ }));
    await userEvent.click(screen.getByRole('button', { name: '开始输出' }));

    await waitFor(() => expect(invoke).toHaveBeenCalledWith('extract_audio', expect.objectContaining({
      inputPath: '/tmp/a.mov',
      outputPath: '/tmp/a_audio.mp3',
      format: 'mp3',
    })));
  });

  it('continues the queue after one file fails and reports partial completion', async () => {
    vi.mocked(invoke)
      .mockRejectedValueOnce(new Error('bad input'))
      .mockResolvedValueOnce({ output_path: '/tmp/b_output.mp4' });
    useAppStore.getState().addFiles([
      { id: '1', path: '/tmp/a.mov', name: 'a.mov', format: 'MOV', originalSize: 10, status: 'pending' },
      { id: '2', path: '/tmp/b.mov', name: 'b.mov', format: 'MOV', originalSize: 10, status: 'pending' },
    ]);

    render(<VideoOutput />);
    await userEvent.click(screen.getByRole('button', { name: '开始输出' }));

    expect(await screen.findByText('完成 1 个，失败 1 个')).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledTimes(2);
    expect(useAppStore.getState().files.find((file) => file.id === '1')?.status).toBe('error');
    expect(useAppStore.getState().files.find((file) => file.id === '2')?.status).toBe('completed');
  });

  it('exposes cancellation while a video task is running', async () => {
    let resolveTask!: (value: unknown) => void;
    vi.mocked(invoke).mockImplementation((command) => {
      if (command === 'cancel_video_tasks') return Promise.resolve();
      return new Promise((resolve) => { resolveTask = resolve; });
    });
    useAppStore.getState().addFiles([
      { id: '1', path: '/tmp/a.mov', name: 'a.mov', format: 'MOV', originalSize: 10, status: 'pending' },
    ]);
    render(<VideoOutput />);
    await userEvent.click(screen.getByRole('button', { name: '开始输出' }));
    await userEvent.click(await screen.findByRole('button', { name: '取消输出' }));
    expect(invoke).toHaveBeenCalledWith('cancel_video_tasks');
    await act(async () => { resolveTask({}); });
    await waitFor(() => expect(screen.getByText(/已取消/)).toBeInTheDocument());
  });
});
