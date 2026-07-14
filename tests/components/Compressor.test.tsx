import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import { open } from '@tauri-apps/plugin-dialog';
import Compressor from '../../src/components/video/Compressor';
import { useAppStore, type FileItem } from '../../src/stores/appStore';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => ({ listen: vi.fn(async () => vi.fn()) }));
vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));

const videoFile: FileItem = {
  id: 'video-1',
  path: '/Users/test/demo.mp4',
  name: 'demo.mp4',
  format: 'MP4',
  originalSize: 1024 * 1024 * 100,
  status: 'pending',
};

const setVideoInStore = () => {
  useAppStore.setState({
    files: [videoFile],
    isProcessing: false,
    progress: 0,
    totalSaved: 0,
  });
};

describe('Compressor (embedded mode)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.getState().clearFiles();
    useAppStore.setState((state) => ({
      options: { ...state.options, outputDir: undefined },
    }));
    vi.mocked(invoke).mockResolvedValue({
      output_path: '/Users/test/demo_compressed.mp4',
      original_size: 1024 * 1024 * 100,
      output_size: 1024 * 1024 * 50,
      saved_bytes: 1024 * 1024 * 50,
      processing_time_secs: 10,
    });
  });

  describe('compression level radio options', () => {
    it('renders three compression level options', () => {
      render(<Compressor embedded />);

      expect(screen.getByText('轻度压缩')).toBeInTheDocument();
      expect(screen.getByText('标准压缩')).toBeInTheDocument();
      expect(screen.getByText('极限压缩')).toBeInTheDocument();
    });

    it('has 标准压缩 selected by default with 推荐 badge', () => {
      render(<Compressor embedded />);

      const standardOption = screen.getByText('标准压缩').closest('[role="radio"]');
      expect(standardOption).toHaveClass('border-secondary-fixed');
      expect(screen.getByText('推荐')).toBeInTheDocument();
    });

    it('switches selection when clicking a different preset', async () => {
      render(<Compressor embedded />);

      const lightOption = screen.getByText('轻度压缩').closest('[role="radio"]')!;
      await userEvent.click(lightOption);

      expect(lightOption).toHaveClass('border-secondary-fixed');
      const standardOption = screen.getByText('标准压缩').closest('[role="radio"]')!;
      expect(standardOption).not.toHaveClass('border-secondary-fixed');
    });
  });

  describe('resolution chip buttons', () => {
    it('renders five resolution options', () => {
      render(<Compressor embedded />);

      expect(screen.getByText('原始尺寸')).toBeInTheDocument();
      expect(screen.getByText('4K (2160P)')).toBeInTheDocument();
      expect(screen.getByText('1080P')).toBeInTheDocument();
      expect(screen.getByText('720P')).toBeInTheDocument();
      expect(screen.getByText('480P (适合移动端)')).toBeInTheDocument();
    });

    it('has 原始尺寸 selected by default', () => {
      render(<Compressor embedded />);

      const originalChip = screen.getByText('原始尺寸');
      expect(originalChip.closest('button')).toHaveClass('bg-primary');
    });

    it('switches selection when clicking a different resolution', async () => {
      render(<Compressor embedded />);

      const chip1080p = screen.getByText('1080P');
      await userEvent.click(chip1080p);

      expect(chip1080p.closest('button')).toHaveClass('bg-primary');
      const originalChip = screen.getByText('原始尺寸');
      expect(originalChip.closest('button')).not.toHaveClass('bg-primary');
    });

    it('480P spans two columns', () => {
      render(<Compressor embedded />);

      const chip480p = screen.getByText('480P (适合移动端)').closest('button')!;
      expect(chip480p.className).toMatch(/col-span-2/);
    });
  });

  describe('CTA button', () => {
    it('is disabled when no video files', () => {
      render(<Compressor embedded />);

      const cta = screen.getByRole('button', { name: /开始极速压缩/ });
      expect(cta).toBeDisabled();
    });

    it('is enabled when video files exist', () => {
      setVideoInStore();
      render(<Compressor embedded />);

      const cta = screen.getByRole('button', { name: /开始极速压缩/ });
      expect(cta).not.toBeDisabled();
    });

    it('shows Loader2 and 压缩中 when processing', async () => {
      setVideoInStore();
      vi.mocked(invoke).mockImplementation(
        () => new Promise((resolve) => setTimeout(() => resolve({}), 1000))
      );
      render(<Compressor embedded />);

      const cta = screen.getByRole('button', { name: /开始极速压缩/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /压缩中\.\.\./ })).toBeInTheDocument();
      });
    });
  });

  describe('compress_video command invocation', () => {
    it('calls compress_video with correct default parameters', async () => {
      setVideoInStore();
      render(<Compressor embedded />);

      const cta = screen.getByRole('button', { name: /开始极速压缩/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'compress_video',
          expect.objectContaining({
            inputPath: videoFile.path,
            outputPath: expect.stringMatching(/_compressed\.mp4$/),
            preset: 'standard',
            crf: 26,
            scale: null,
          })
        );
      });
    });

    it('passes correct crf when 轻度压缩 is selected', async () => {
      setVideoInStore();
      render(<Compressor embedded />);

      await userEvent.click(screen.getByText('轻度压缩').closest('[role="radio"]')!);
      await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'compress_video',
          expect.objectContaining({
            preset: 'light',
            crf: 20,
          })
        );
      });
    });

    it('passes correct scale when 1080P is selected', async () => {
      setVideoInStore();
      render(<Compressor embedded />);

      await userEvent.click(screen.getByText('1080P'));
      await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'compress_video',
          expect.objectContaining({
            scale: '1920:1080',
          })
        );
      });
    });

    it('passes correct scale when 480P is selected', async () => {
      setVideoInStore();
      render(<Compressor embedded />);

      await userEvent.click(screen.getByText('480P (适合移动端)'));
      await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'compress_video',
          expect.objectContaining({
            scale: '854:480',
          })
        );
      });
    });

    it('uses the configured output directory', async () => {
      setVideoInStore();
      useAppStore.setState((state) => ({
        options: { ...state.options, outputDir: 'C:\\TinyPix Output' },
      }));
      render(<Compressor embedded />);

      await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'compress_video',
          expect.objectContaining({
            outputPath: 'C:\\TinyPix Output\\demo_compressed.mp4',
          })
        );
      });
    });

    it('opens the configured output directory after compression when enabled', async () => {
      setVideoInStore();
      useAppStore.setState((state) => ({
        options: { ...state.options, outputDir: 'C:\\TinyPix Output', openAfterProcess: true },
      }));
      render(<Compressor embedded />);

      await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenLastCalledWith('open_folder', {
          path: 'C:\\TinyPix Output',
        });
      });
    });
  });

  describe('card styling', () => {
    it('uses correct card container styling', () => {
      render(<Compressor embedded />);

      const cards = document.querySelectorAll('section.bg-surface-container-lowest');
      expect(cards.length).toBeGreaterThanOrEqual(2);
      cards.forEach((card) => {
        expect(card.className).toMatch(/rounded-\[18px\]/);
        expect(card.className).toMatch(/border-outline-variant/);
      });
    });

    it('section titles use label-caps uppercase style', () => {
      render(<Compressor embedded />);

      const titles = screen.getAllByText(/压缩等级|输出分辨率/);
      titles.forEach((title) => {
        expect(title.tagName).toBe('LABEL');
        expect(title.className).toMatch(/opacity-50/);
      });
    });
  });
});

// ============================================================
// 补充测试：handlePick / 批量失败不中断 / 参数验证 / 独立模式
// 后端 compress_video 签名 (src-tauri/src/commands/video_commands.rs):
//   compress_video(app, input_path: String, output_path: String,
//                  preset: String, crf: Option<u8>, scale: Option<String>)
// 前端 invoke 通过 Tauri 自动 camelCase -> snake_case 映射。
// ============================================================

const videoFile2: FileItem = {
  id: 'video-2',
  path: '/Users/test/clip2.mov',
  name: 'clip2.mov',
  format: 'MOV',
  originalSize: 1024 * 1024 * 50,
  status: 'pending',
};

const resetStoreForExtra = () => {
  useAppStore.getState().clearFiles();
  useAppStore.setState((state) => ({
    options: { ...state.options, outputDir: undefined, openAfterProcess: false },
  }));
};

describe('Compressor (standalone mode)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStoreForExtra();
    vi.mocked(invoke).mockResolvedValue({
      output_path: '/out.mp4',
      original_size: 100,
      output_size: 50,
      saved_bytes: 50,
      processing_time_secs: 1,
    });
    vi.mocked(open).mockResolvedValue(null);
  });

  it('renders the page title and 选择视频 / 开始压缩 buttons', () => {
    render(<Compressor />);
    expect(screen.getByText('视频压缩')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '选择视频' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '开始压缩' })).toBeInTheDocument();
  });

  it('disables 开始压缩 when no video files', () => {
    render(<Compressor />);
    expect(screen.getByRole('button', { name: '开始压缩' })).toBeDisabled();
  });

  it('invokes compress_video when 开始压缩 clicked in standalone mode', async () => {
    setVideoInStore();
    render(<Compressor />);
    await userEvent.click(screen.getByRole('button', { name: '开始压缩' }));
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('compress_video', expect.objectContaining({
        inputPath: videoFile.path,
      }));
    });
  });
});

describe('Compressor handlePick', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStoreForExtra();
    vi.mocked(invoke).mockResolvedValue({
      output_path: '/out.mp4',
      original_size: 100,
      output_size: 50,
      saved_bytes: 50,
      processing_time_secs: 1,
    });
  });

  it('does not add files when dialog is cancelled', async () => {
    vi.mocked(open).mockResolvedValue(null);
    render(<Compressor />);
    await userEvent.click(screen.getByRole('button', { name: '选择视频' }));
    expect(invoke).not.toHaveBeenCalledWith('read_file_metadata', expect.anything());
    expect(useAppStore.getState().files).toHaveLength(0);
  });

  it('adds files when dialog returns paths', async () => {
    vi.mocked(open).mockResolvedValue(['/a.mp4', '/b.mov']);
    vi.mocked(invoke).mockImplementation((cmd: string) => {
      if (cmd === 'read_file_metadata') {
        return Promise.resolve({
          file_name: 'a.mp4',
          extension: 'mp4',
          size_bytes: 1024,
        });
      }
      return Promise.resolve({});
    });
    render(<Compressor />);
    await userEvent.click(screen.getByRole('button', { name: '选择视频' }));
    await waitFor(() => {
      expect(useAppStore.getState().files).toHaveLength(2);
    });
    expect(invoke).toHaveBeenCalledWith('read_file_metadata', { path: '/a.mp4' });
    expect(invoke).toHaveBeenCalledWith('read_file_metadata', { path: '/b.mov' });
  });
});

describe('Compressor batch compression failure handling', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStoreForExtra();
    useAppStore.setState({ files: [videoFile, videoFile2] });
    vi.mocked(invoke).mockResolvedValue({
      output_path: '/out.mp4',
      original_size: 100,
      output_size: 50,
      saved_bytes: 50,
      processing_time_secs: 1,
    });
  });

  it('continues processing subsequent files when one fails', async () => {
    vi.mocked(invoke)
      .mockRejectedValueOnce(new Error('compress failed'))
      .mockResolvedValueOnce({
        output_path: '/out.mov',
        original_size: 50,
        output_size: 25,
        saved_bytes: 25,
        processing_time_secs: 1,
      });
    render(<Compressor embedded />);
    await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledTimes(2);
    });
    expect(invoke).toHaveBeenNthCalledWith(1, 'compress_video', expect.objectContaining({
      inputPath: videoFile.path,
    }));
    expect(invoke).toHaveBeenNthCalledWith(2, 'compress_video', expect.objectContaining({
      inputPath: videoFile2.path,
    }));
    await waitFor(() => {
      expect(screen.getByText(/完成: 1\/2/)).toBeInTheDocument();
    });
  });
});

describe('compress_video parameter validation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    resetStoreForExtra();
    vi.mocked(invoke).mockResolvedValue({
      output_path: '/out.mp4',
      original_size: 100,
      output_size: 50,
      saved_bytes: 50,
      processing_time_secs: 1,
    });
  });

  it('passes crf 34 when 极限压缩 selected', async () => {
    setVideoInStore();
    render(<Compressor embedded />);
    await userEvent.click(screen.getByText('极限压缩').closest('[role="radio"]')!);
    await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('compress_video', expect.objectContaining({
        preset: 'extreme',
        crf: 34,
      }));
    });
  });

  it('passes scale 3840:2160 when 4K selected', async () => {
    setVideoInStore();
    render(<Compressor embedded />);
    await userEvent.click(screen.getByText('4K (2160P)'));
    await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('compress_video', expect.objectContaining({
        scale: '3840:2160',
      }));
    });
  });

  it('passes scale 1280:720 when 720P selected', async () => {
    setVideoInStore();
    render(<Compressor embedded />);
    await userEvent.click(screen.getByText('720P'));
    await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('compress_video', expect.objectContaining({
        scale: '1280:720',
      }));
    });
  });

  it('passes exactly the five backend parameters with correct types', async () => {
    setVideoInStore();
    render(<Compressor embedded />);
    await userEvent.click(screen.getByRole('button', { name: /开始极速压缩/ }));
    await waitFor(() => {
      const calls = vi.mocked(invoke).mock.calls.filter(
        ([cmd]) => cmd === 'compress_video'
      );
      expect(calls.length).toBeGreaterThan(0);
      const params = calls[0][1] as Record<string, unknown>;
      expect(Object.keys(params).sort()).toEqual(
        ['crf', 'inputPath', 'outputPath', 'preset', 'scale']
      );
      expect(typeof params.inputPath).toBe('string');
      expect(typeof params.outputPath).toBe('string');
      expect(typeof params.preset).toBe('string');
      expect(typeof params.crf).toBe('number');
      expect(params.scale).toBeNull();
    });
  });
});
