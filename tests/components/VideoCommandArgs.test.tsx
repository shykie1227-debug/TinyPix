import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore, type FileItem } from '../../src/stores/appStore';
import AudioExtractor from '../../src/components/video/AudioExtractor';
import VideoConverter from '../../src/components/video/VideoConverter';
import VideoTrimmer from '../../src/components/video/VideoTrimmer';
import GifMaker from '../../src/components/video/GifMaker';

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
    vi.mocked(invoke).mockResolvedValue({ duration_secs: 120 });
  });

  it('passes all edit parameters to edit_and_export_video', async () => {
    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: video.path });
    });

    // Change some parameters from defaults
    // Speed slider is at position 1.0x default, Volume at 100%, brightness at 0, contrast at 0
    // Click the export button
    await userEvent.click(screen.getByRole('button', { name: '开始渲染导出' }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'edit_and_export_video',
        expect.objectContaining({
          inputPath: video.path,
          outputPath: expect.stringMatching(/_edited\.mp4$/),
          startSecs: 0,
          endSecs: 120,
          speed: 1,
          volume: 1,
          brightness: 0,
          contrast: 0,
          format: 'mp4',
        })
      );
    });
  });

  it('passes outputPath and format to standalone audio extraction', async () => {
    render(<AudioExtractor />);

    await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'extract_audio',
        expect.objectContaining({
          inputPath: video.path,
          outputPath: expect.stringMatching(/_audio\.mp3$/),
          format: 'mp3',
          bitrateKbps: undefined,
          mode: 'direct',
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
    await userEvent.click(screen.getByRole('button', { name: '开始渲染导出' }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'edit_and_export_video',
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
    await userEvent.click(screen.getByRole('button', { name: '开始渲染导出' }));

    await waitFor(() => {
      expect(invoke).toHaveBeenLastCalledWith('open_folder', {
        path: 'C:\\TinyPix Output',
      });
    });
  });

  it('passes multiple videos to merge_videos from the trim workspace', async () => {
    useAppStore.setState({
      files: [
        video,
        {
          ...video,
          id: 'video-2',
          path: 'C:\\Users\\huashu\\Videos\\part2.mp4',
          name: 'part2.mp4',
        },
      ],
    });

    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: video.path });
    });
    await userEvent.click(screen.getByRole('button', { name: '合并导出' }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'merge_videos',
        expect.objectContaining({
          inputPaths: [
            'C:\\Users\\huashu\\Videos\\demo.mp4',
            'C:\\Users\\huashu\\Videos\\part2.mp4',
          ],
          outputPath: expect.stringMatching(/_merged\.mp4$/),
        })
      );
    });
  });

  it('passes all 10 parameters to format conversion', async () => {
    render(<VideoConverter />);

    await userEvent.click(screen.getByRole('button', { name: '立即导出' }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'convert_video_format',
        expect.objectContaining({
          inputPath: video.path,
          outputPath: expect.stringMatching(/_converted\.mp4$/),
          targetFormat: 'mp4',
          quality: 23,
          videoCodec: 'h264',
          resolutionWidth: 1920,
          resolutionHeight: 1080,
          fps: 30,
          audioCodec: 'aac',
          audioBitrate: 192000,
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
          width: expect.any(Number),
          quality: 3,
          startSecs: 4.2,
          endSecs: 8.5,
        })
      );
    });
  });

});
