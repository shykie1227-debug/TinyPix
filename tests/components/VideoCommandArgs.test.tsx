import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore, type FileItem } from '../../src/stores/appStore';
import VideoTrimmer from '../../src/components/video/VideoTrimmer';
import GifMaker from '../../src/components/video/GifMaker';

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(async () => vi.fn()),
}));

vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
  convertFileSrc: vi.fn((path: string) => path),
}));

const video: FileItem = {
  id: 'video-1',
  path: 'C:\\Users\\huashu\\Videos\\demo.mp4',
  name: 'demo.mp4',
  format: 'MP4',
  originalSize: 1024 * 1024,
  status: 'pending',
};

const setVideoFile = () => {
  useAppStore.setState({
    files: [video],
    isProcessing: false,
    progress: 0,
    totalSaved: 0,
  });
};

describe('video command arguments', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.getState().clearFiles();
    useAppStore.setState((state) => ({
      options: { ...state.options, outputDir: undefined },
    }));
    setVideoFile();
    vi.mocked(invoke).mockImplementation(async (command: string, args?: { taskId?: string }) => {
      if (command === 'get_video_info') return { duration_secs: 120, fps: 30 };
      if (command === 'prepare_media_preview') return {
        state: 'ready',
        kind: 'proxy-video',
        playbackPath: 'C:\\Users\\huashu\\AppData\\Local\\TinyPix\\previews\\demo.mp4',
        posterPath: 'C:\\Users\\huashu\\AppData\\Local\\TinyPix\\previews\\demo.jpg',
        durationSecs: 120,
        fps: 30,
        hasAudio: true,
        isProxy: true,
        taskId: args?.taskId,
      };
      if (command === 'generate_timeline_assets') return {};
      if (command === 'export_video_edit') return { outputPath: 'C:\\Users\\huashu\\Videos\\demo_edited.mp4' };
      return {};
    });
  });

  it('passes the selected range to lossless trim by default', async () => {
    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: video.path });
    });

    // Change some parameters from defaults
    // Speed slider is at position 1.0x default, Volume at 100%, brightness at 0, contrast at 0
    // Click the export button
    const exportButton = await screen.findByRole('button', { name: '合并导出' });
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    await userEvent.click(exportButton);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'export_video_edit',
        expect.objectContaining({
          inputPath: video.path,
          outputPath: expect.stringMatching(/_edited\.mp4$/),
          mode: 'lossless',
          segments: [expect.objectContaining({ startSecs: 0, endSecs: 120, included: true })],
        })
      );
    });
  });


  it('uses the configured output directory for edited video', async () => {
    useAppStore.setState((state) => ({
      options: { ...state.options, outputDir: 'C:\\TinyPix Output' },
    }));
    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: video.path });
    });
    const exportButton = await screen.findByRole('button', { name: '合并导出' });
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    await userEvent.click(exportButton);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'export_video_edit',
        expect.objectContaining({
          outputPath: 'C:\\TinyPix Output\\demo_edited.mp4',
        })
      );
    });
  });

  it('opens the configured output directory after edited video export when enabled', async () => {
    useAppStore.setState((state) => ({
      options: { ...state.options, outputDir: 'C:\\TinyPix Output', openAfterProcess: true },
    }));
    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: video.path });
    });
    const exportButton = await screen.findByRole('button', { name: '合并导出' });
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    await userEvent.click(exportButton);

    await waitFor(() => {
      expect(invoke).toHaveBeenLastCalledWith('open_folder', {
        path: 'C:\\TinyPix Output',
      });
    });
  });

  it('switches to CPU re-encode when precise boundary is selected', async () => {
    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: video.path });
    });
    await userEvent.click(screen.getByRole('checkbox', { name: /精确边界/ }));
    const exportButton = await screen.findByRole('button', { name: '合并导出' });
    await waitFor(() => expect(exportButton).not.toBeDisabled());
    await userEvent.click(exportButton);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'export_video_edit',
        expect.objectContaining({
          mode: 'precise',
        })
      );
    });
  });


  it('passes quality, startSecs, endSecs to create_gif', async () => {
    vi.mocked(invoke).mockResolvedValue({
      output_path: 'C:\\Users\\huashu\\Videos\\demo.gif',
      original_size: 1024 * 1024,
      output_size: 512 * 1024,
      saved_bytes: 512 * 1024,
      processing_time_secs: 2.5,
    });

    render(<GifMaker />);

    const startInput = screen.getByLabelText('开始时间') as HTMLInputElement;
    const endInput = screen.getByLabelText('结束时间') as HTMLInputElement;
    await userEvent.clear(startInput);
    await userEvent.type(startInput, '00:04.20');
    await userEvent.clear(endInput);
    await userEvent.type(endInput, '00:08.50');

    const slider = screen.getByRole('slider') as HTMLInputElement;
    fireEvent.change(slider, { target: { value: '3' } });

    await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'create_gif',
        expect.objectContaining({
          inputPath: video.path,
          outputPath: expect.stringMatching(/\.gif$/),
          fps: 15,
          width: null,
          loopCount: 0,
          quality: 3,
          startSecs: 4.2,
          endSecs: 8.5,
        })
      );
    });
  });

});
