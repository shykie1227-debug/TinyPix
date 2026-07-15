import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore, type FileItem } from '../../src/stores/appStore';
import GifMaker from '../../src/components/video/GifMaker';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

const video: FileItem = {
  id: 'video-1',
  path: 'C:\\Users\\huashu\\Videos\\demo.mp4',
  name: 'demo.mp4',
  format: 'MP4',
  originalSize: 1024 * 1024,
  status: 'pending',
};

const useDefaultInvokeMock = () => {
  vi.mocked(invoke).mockImplementation(async (command: string, args?: Record<string, unknown>) => {
    if (command === 'prepare_media_preview') {
      return {
        state: 'ready', kind: 'direct-video', playbackPath: video.path,
        durationSecs: 30, width: 1920, height: 1080, fps: 30,
        hasAudio: true, isProxy: false, taskId: args?.taskId,
      };
    }
    return {};
  });
};

const setVideoFile = () => {
  useAppStore.setState({
    files: [video],
    isProcessing: false,
    progress: 0,
    totalSaved: 0,
  });
};

const clearFiles = () => {
  useAppStore.getState().clearFiles();
};

describe('GifMaker component', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useDefaultInvokeMock();
    clearFiles();
    useAppStore.setState((state) => ({
      options: { ...state.options, outputDir: undefined },
    }));
  });

  describe('GIF size chips', () => {
    it('renders 4 size options and selects 原始尺寸 by default', () => {
      render(<GifMaker />);

      const chips = screen.getAllByRole('button', { name: /原始尺寸|720P|480P|320P/ });
      expect(chips).toHaveLength(4);

      const original = screen.getByRole('button', { name: '原始尺寸' });
      expect(original.className).toContain('bg-primary');
    });

    it('switches selected chip exclusively on click', async () => {
      render(<GifMaker />);

      const chip720 = screen.getByRole('button', { name: '720P' });
      await userEvent.click(chip720);

      expect(chip720.className).toContain('bg-primary');

      const original = screen.getByRole('button', { name: '原始尺寸' });
      expect(original.className).not.toContain('bg-primary');
    });
  });

  describe('FPS segmented control', () => {
    it('renders 3 FPS options and selects 15 by default', () => {
      render(<GifMaker />);

      const fpsButtons = screen.getAllByRole('tab', { name: /^10$|^15$|^24$/ });
      expect(fpsButtons).toHaveLength(3);

      const fps15 = fpsButtons.find((btn) => btn.textContent === '15');
      expect(fps15).toHaveAttribute('aria-selected', 'true');
    });

    it('switches FPS selection on click', async () => {
      render(<GifMaker />);

      const fps24 = screen.getByRole('tab', { name: '24' });
      await userEvent.click(fps24);

      expect(fps24).toHaveAttribute('aria-selected', 'true');
    });
  });

  describe('quality slider', () => {
    it('defaults to quality 2 with badge showing 中 (Medium)', () => {
      render(<GifMaker />);

      const badge = screen.getByText('中 (Medium)');
      expect(badge).toBeInTheDocument();

      const slider = screen.getByRole('slider') as HTMLInputElement;
      expect(slider.value).toBe('2');
    });

    it('updates badge text when slider value changes', async () => {
      render(<GifMaker />);

      const slider = screen.getByRole('slider') as HTMLInputElement;

      fireEvent.change(slider, { target: { value: '2' } });
      expect(screen.getByText('中 (Medium)')).toBeInTheDocument();

      fireEvent.change(slider, { target: { value: '1' } });
      expect(screen.getByText('低 (Low)')).toBeInTheDocument();
    });
  });

  describe('time range inputs', () => {
    it('renders start and end time inputs with default values', () => {
      render(<GifMaker />);

      const startInput = screen.getByLabelText('开始时间') as HTMLInputElement;
      const endInput = screen.getByLabelText('结束时间') as HTMLInputElement;

      expect(startInput.value).toBe('00:00.00');
      expect(endInput.value).toBe('');
    });
  });

  describe('CTA button', () => {
    it('is disabled when no video files are present', () => {
      render(<GifMaker />);

      const cta = screen.getByRole('button', { name: /开始转换/ });
      expect(cta).toBeDisabled();
    });

    it('is enabled when video files are present', async () => {
      setVideoFile();
      render(<GifMaker />);

      const cta = screen.getByRole('button', { name: /开始转换/ });
      expect(cta).not.toBeDisabled();
      await waitFor(() => expect(screen.getByLabelText('结束时间')).toHaveValue('00:30.00'));
    });

    it('calls create_gif with correct parameters on click', async () => {
      setVideoFile();
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
      await userEvent.selectOptions(screen.getByLabelText('循环次数'), '3');

      const cta = screen.getByRole('button', { name: /开始转换/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'create_gif',
          expect.objectContaining({
            inputPath: video.path,
            outputPath: expect.stringMatching(/\.gif$/),
            fps: 15,
            width: null,
            quality: 3,
            startSecs: 4.2,
            endSecs: 8.5,
            loopCount: 3,
          })
        );
      });
    });

    it('uses a Windows output directory without mixing separators', async () => {
      setVideoFile();
      useAppStore.setState((state) => ({
        options: { ...state.options, outputDir: 'C:\\TinyPix Output' },
      }));
      vi.mocked(invoke).mockResolvedValue({});
      render(<GifMaker />);

      await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'create_gif',
          expect.objectContaining({
            outputPath: 'C:\\TinyPix Output\\demo.gif',
          })
        );
      });
    });

    it('opens the configured output directory after GIF conversion when enabled', async () => {
      setVideoFile();
      useAppStore.setState((state) => ({
        options: { ...state.options, outputDir: 'C:\\TinyPix Output', openAfterProcess: true },
      }));
      vi.mocked(invoke).mockResolvedValue({});
      render(<GifMaker />);

      await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenLastCalledWith('open_folder', {
          path: 'C:\\TinyPix Output',
        });
      });
    });
  });
});


describe('GifMaker time range validation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useDefaultInvokeMock();
    clearFiles();
    useAppStore.setState((state) => ({
      options: { ...state.options, outputDir: undefined, openAfterProcess: false },
    }));
  });

  it('shows validation error and skips create_gif when start time is greater than end time', async () => {
    setVideoFile();
    render(<GifMaker />);

    const startInput = screen.getByLabelText('开始时间') as HTMLInputElement;
    const endInput = screen.getByLabelText('结束时间') as HTMLInputElement;
    await userEvent.clear(startInput);
    await userEvent.type(startInput, '00:10.00');
    await userEvent.clear(endInput);
    await userEvent.type(endInput, '00:05.00');

    await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

    expect(await screen.findByText('开始时间必须小于结束时间')).toBeInTheDocument();
    expect(invoke).not.toHaveBeenCalled();
  });

  it('rejects zero-length range when start equals end', async () => {
    setVideoFile();
    render(<GifMaker />);

    const startInput = screen.getByLabelText('开始时间') as HTMLInputElement;
    const endInput = screen.getByLabelText('结束时间') as HTMLInputElement;
    await userEvent.clear(startInput);
    await userEvent.type(startInput, '00:05.00');
    await userEvent.clear(endInput);
    await userEvent.type(endInput, '00:05.00');

    await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

    expect(await screen.findByText('开始时间必须小于结束时间')).toBeInTheDocument();
    expect(invoke).not.toHaveBeenCalled();
  });

  it('proceeds with create_gif and shows completion status for a valid range', async () => {
    setVideoFile();
    vi.mocked(invoke).mockResolvedValue({});

    render(<GifMaker />);

    const startInput = screen.getByLabelText('开始时间') as HTMLInputElement;
    const endInput = screen.getByLabelText('结束时间') as HTMLInputElement;
    await userEvent.clear(startInput);
    await userEvent.type(startInput, '01:00.00');
    await userEvent.clear(endInput);
    await userEvent.type(endInput, '02:30.00');

    await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

    expect(await screen.findByText('全部完成，共 1 个')).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith(
      'create_gif',
      expect.objectContaining({
        startSecs: 60,
        endSecs: 150,
      })
    );
  });

  it('shows failure status when create_gif rejects', async () => {
    setVideoFile();
    vi.mocked(invoke).mockRejectedValueOnce(new Error('ffmpeg error'));

    render(<GifMaker />);

    await userEvent.click(screen.getByRole('button', { name: /开始转换/ }));

    expect(await screen.findByText(/失败/)).toBeInTheDocument();
  });
});
