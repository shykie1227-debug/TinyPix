import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import AudioExtractor from '../../src/components/video/AudioExtractor';
import { useAppStore, type FileItem } from '../../src/stores/appStore';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => ({ listen: vi.fn(async () => vi.fn()) }));

const videoFile: FileItem = {
  id: 'video-1',
  path: '/Users/test/demo.mp4',
  name: 'demo.mp4',
  format: 'MP4',
  originalSize: 1024 * 1024 * 100,
  status: 'pending',
};

const secondVideoFile: FileItem = {
  id: 'video-2',
  path: '/Users/test/clip.mov',
  name: 'clip.mov',
  format: 'MOV',
  originalSize: 1024 * 1024 * 80,
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

const setVideosInStore = () => {
  useAppStore.setState({
    files: [videoFile, secondVideoFile],
    options: {
      ...useAppStore.getState().options,
      outputDir: 'D:\\TinyPixOut',
    },
    isProcessing: false,
    progress: 0,
    totalSaved: 0,
  });
};

describe('AudioExtractor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.getState().clearFiles();
    vi.mocked(invoke).mockImplementation(async (cmd: string) => {
      if (cmd === 'inspect_audio') {
        return new Promise(() => {});
      }
      return {
        output_path: '/Users/test/demo_audio.mp3',
        original_size: 1024 * 1024 * 100,
        output_size: 1024 * 1024 * 5,
        saved_bytes: 1024 * 1024 * 95,
        processing_time_secs: 5,
      };
    });
  });

  describe('file info card', () => {
    it('renders file info card when M1 source info is available', async () => {
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return {
            codec: 'aac',
            sample_rate: 44100,
            channels: 2,
            duration_secs: 120,
            bitrate_kbps: 128,
          };
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      // M1 inspect_audio 在 beforeEach 中返回有效数据，等待卡片渲染
      await waitFor(() => {
        expect(screen.getByTestId('file-info-card')).toBeInTheDocument();
      });
    });

    it('shows codec, duration, sample rate, channels, bitrate from M1', async () => {
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return {
            codec: 'aac',
            sample_rate: 44100,
            channels: 2,
            duration_secs: 120,
            bitrate_kbps: 128,
          };
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await waitFor(() => {
        expect(screen.getByTestId('file-info-card')).toBeInTheDocument();
      });
      // Mock 数据: aac / 44.1 kHz / 2ch / 120s / 128 kbps
      expect(screen.getByTestId('file-info-编码')).toHaveTextContent('AAC');
      expect(screen.getByTestId('file-info-时长')).toHaveTextContent('02:00');
      expect(screen.getByTestId('file-info-采样率')).toHaveTextContent('44.1 kHz');
      expect(screen.getByTestId('file-info-声道')).toHaveTextContent('立体声');
      expect(screen.getByTestId('file-info-码率')).toHaveTextContent('128 kbps');
    });

    it('hides file info card when no source file is selected', () => {
      render(<AudioExtractor />);

      expect(screen.queryByTestId('file-info-card')).not.toBeInTheDocument();
    });
  });

  describe('audio preview card', () => {
    it('renders audio preview card at the top with graphic_eq icon', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const previewCard = screen.getByText(/音频预览/);
      expect(previewCard).toBeInTheDocument();
    });

    it('renders 18 waveform bars', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const waveformBars = document.querySelectorAll('.waveform-bar');
      expect(waveformBars.length).toBe(18);
    });

    it('renders play button with play_arrow icon', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const playBtn = screen.getByRole('button', { name: /play_arrow|播放/ });
      expect(playBtn).toBeInTheDocument();
    });
  });

  describe('output format chips', () => {
    it('renders all five format options', () => {
      render(<AudioExtractor />);

      expect(screen.getByText('MP3')).toBeInTheDocument();
      expect(screen.getByText('WAV')).toBeInTheDocument();
      expect(screen.getByText('AAC')).toBeInTheDocument();
      expect(screen.getByText('FLAC')).toBeInTheDocument();
      expect(screen.getByText('M4A')).toBeInTheDocument();
    });

    it('has MP3 selected by default with primary background', () => {
      render(<AudioExtractor />);

      const mp3Btn = screen.getByText('MP3').closest('button');
      expect(mp3Btn).toHaveClass('bg-primary');
      expect(mp3Btn).toHaveClass('text-on-primary');
    });

    it('switches selected format when clicking another chip', async () => {
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText('WAV'));

      const wavBtn = screen.getByText('WAV').closest('button');
      expect(wavBtn).toHaveClass('bg-primary');

      const mp3Btn = screen.getByText('MP3').closest('button');
      expect(mp3Btn).not.toHaveClass('bg-primary');
    });

    it('selected chip has bg-primary style', async () => {
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText('FLAC'));

      const flacBtn = screen.getByText('FLAC').closest('button');
      expect(flacBtn).toHaveClass('bg-primary');
      expect(flacBtn).toHaveClass('text-on-primary');
    });
  });

  describe('extraction mode radio', () => {
    it('renders two extraction mode options', () => {
      render(<AudioExtractor />);

      expect(screen.getByText(/直接提取/)).toBeInTheDocument();
      expect(screen.getByText(/音频重编码/)).toBeInTheDocument();
    });

    it('has direct extraction selected by default', () => {
      render(<AudioExtractor />);

      const directOption = screen.getByText(/直接提取/).closest('[role="radio"], button');
      expect(directOption?.className).toMatch(/border-secondary-fixed/);
    });

    it('switches to reencode mode when clicked', async () => {
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText(/音频重编码/));

      const reencodeOption = screen.getByText(/音频重编码/).closest('[role="radio"], button');
      expect(reencodeOption?.className).toMatch(/border-secondary-fixed/);
    });
  });

  describe('bitrate slider and mode联动', () => {
    it('bitrate slider has min=64 max=320 step=64', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const sliders = screen.getAllByRole('slider');
      const bitrateSlider = sliders[0] as HTMLInputElement;
      expect(bitrateSlider.min).toBe('64');
      expect(bitrateSlider.max).toBe('320');
      expect(bitrateSlider.step).toBe('64');
    });

    it('slider is disabled when direct extraction mode is selected', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const sliders = screen.getAllByRole('slider');
      const bitrateSlider = sliders[0];
      expect(bitrateSlider).toBeDisabled();
    });

    it('slider is enabled when reencode mode is selected', async () => {
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText(/音频重编码/));

      const sliders = screen.getAllByRole('slider');
      const bitrateSlider = sliders[0];
      expect(bitrateSlider).not.toBeDisabled();
    });

    it('shows correct bitrate value badge in secondary color', async () => {
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText(/音频重编码/));

      const badge = screen.getByText(/192.*kbps|192.*K/);
      expect(badge.className).toMatch(/text-secondary/);
    });

    it('has five tick marks: 64k, 128k, 192k, 256k, 320k', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      expect(screen.getByText(/64k/i)).toBeInTheDocument();
      expect(screen.getByText(/128k/i)).toBeInTheDocument();
      expect(screen.getByText(/192k/i)).toBeInTheDocument();
      expect(screen.getByText(/256k/i)).toBeInTheDocument();
      expect(screen.getByText(/320k/i)).toBeInTheDocument();
    });
  });

  describe('CTA button', () => {
    it('is disabled when no video files', () => {
      render(<AudioExtractor />);

      const cta = screen.getByRole('button', { name: /开始提取音频/ });
      expect(cta).toBeDisabled();
    });

    it('is enabled when video files exist', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const cta = screen.getByRole('button', { name: /开始提取音频/ });
      expect(cta).not.toBeDisabled();
    });

    it('has primary black background style', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const cta = screen.getByRole('button', { name: /开始提取音频/ });
      expect(cta.className).toMatch(/bg-primary/);
      expect(cta.className).toMatch(/text-on-primary/);
    });

    it('has zap icon from lucide-react', () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const cta = screen.getByRole('button', { name: /开始提取音频/ });
      const zapIcon = cta.querySelector('svg');
      expect(zapIcon).toBeInTheDocument();
    });
  });

  describe('extract_audio command invocation', () => {
    it('passes correct parameters for direct extraction mode (bitrateKbps = undefined)', async () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const cta = screen.getByRole('button', { name: /开始提取音频/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'extract_audio',
          expect.objectContaining({
            inputPath: videoFile.path,
            format: 'mp3',
            bitrateKbps: undefined,
            mode: 'direct',
          })
        );
      });
    });

    it('passes bitrateKbps value for reencode mode', async () => {
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText(/音频重编码/));
      await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'extract_audio',
          expect.objectContaining({
            bitrateKbps: 192,
            mode: 'reencode',
          })
        );
      });
    });

    it('passes lowercase format for WAV', async () => {
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText('WAV'));
      await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'extract_audio',
          expect.objectContaining({
            format: 'wav',
          })
        );
      });
    });

    it('output path uses _audio suffix', async () => {
      setVideoInStore();
      render(<AudioExtractor />);

      const cta = screen.getByRole('button', { name: /开始提取音频/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'extract_audio',
          expect.objectContaining({
            outputPath: expect.stringMatching(/_audio\.mp3$/),
          })
        );
      });
    });

    it('extracts audio from every queued video and uses the configured output directory', async () => {
      setVideosInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

      // 调用序列：1× inspect_audio (M1) + 2× extract_audio (M2/M3)
      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'extract_audio',
          expect.objectContaining({
            inputPath: secondVideoFile.path,
            outputPath: expect.stringMatching(/D:[\\/]TinyPixOut[\\/]clip_audio\.mp3$/),
          })
        );
      });
      expect(invoke).toHaveBeenCalledWith(
        'inspect_audio',
        expect.objectContaining({ inputPath: videoFile.path })
      );
      expect(invoke).toHaveBeenCalledWith(
        'extract_audio',
        expect.objectContaining({
          inputPath: videoFile.path,
          outputPath: expect.stringMatching(/D:[\\/]TinyPixOut[\\/]demo_audio\.mp3$/),
        })
      );
      expect(invoke).toHaveBeenCalledWith(
        'extract_audio',
        expect.objectContaining({
          inputPath: secondVideoFile.path,
          outputPath: expect.stringMatching(/D:[\\/]TinyPixOut[\\/]clip_audio\.mp3$/),
        })
      );
      expect(screen.queryByText(/当前仅处理第一个视频文件/)).not.toBeInTheDocument();
    });

    it('opens the configured output directory after audio extraction when enabled', async () => {
      setVideoInStore();
      useAppStore.setState((state) => ({
        options: { ...state.options, outputDir: 'D:\\TinyPixOut', openAfterProcess: true },
      }));
      render(<AudioExtractor />);

      await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenLastCalledWith('open_folder', {
          path: 'D:\\TinyPixOut',
        });
      });
    });
  });

  describe('compatibility warning and error state', () => {
    it('shows incompatible warning when source codec does not match selected format', async () => {
      // aac codec 仅与 M4A/AAC 兼容，默认 MP3 格式应触发不兼容警告
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return {
            codec: 'aac',
            sample_rate: 44100,
            channels: 2,
            duration_secs: 120,
            bitrate_kbps: 128,
          };
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await waitFor(() => {
        expect(screen.getByText(/源编码 aac 与 MP3 不兼容/)).toBeInTheDocument();
      });
      expect(screen.getByText(/将自动重编码/)).toBeInTheDocument();
    });

    it('styles incompatible warning with tertiary color', async () => {
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return {
            codec: 'aac',
            sample_rate: 44100,
            channels: 2,
            duration_secs: 120,
            bitrate_kbps: 128,
          };
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await waitFor(() => {
        expect(screen.getByText(/源编码 aac 与 MP3 不兼容/)).toBeInTheDocument();
      });
      const warning = screen.getByText(/源编码 aac 与 MP3 不兼容/).closest('div');
      expect(warning?.className).toMatch(/text-tertiary/);
    });

    it('shows compatible hint when source codec matches selected format', async () => {
      // aac codec + AAC 格式 = 兼容（aac → ['M4A', 'AAC']）
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return {
            codec: 'aac',
            sample_rate: 44100,
            channels: 2,
            duration_secs: 120,
            bitrate_kbps: 128,
          };
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByText('AAC'));

      await waitFor(() => {
        expect(screen.getByText(/可走直接提取，零损失/)).toBeInTheDocument();
      });
      expect(screen.queryByText(/不兼容/)).not.toBeInTheDocument();
    });

    it('does not show any compatibility warning when codec is unknown', async () => {
      // codec 为 unknown 时，组件应跳过兼容性判断（不渲染警告 div）
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return {
            codec: 'unknown',
            sample_rate: 0,
            channels: 2,
            duration_secs: 0,
          };
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await waitFor(() => {
        expect(screen.queryByText(/不兼容/)).not.toBeInTheDocument();
      });
      expect(screen.queryByText(/可走直接提取/)).not.toBeInTheDocument();
    });

    it('shows error status when extract_audio invoke fails', async () => {
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return new Promise(() => {});
        }
        if (cmd === 'extract_audio') {
          throw new Error('ffmpeg failed');
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

      await waitFor(() => {
        expect(screen.getByText(/提取失败/)).toBeInTheDocument();
      });
      expect(screen.getByText(/ffmpeg failed/)).toBeInTheDocument();
    });

    it('clears extracting state after error so CTA can be re-triggered', async () => {
      vi.mocked(invoke).mockImplementation(async (cmd: string) => {
        if (cmd === 'inspect_audio') {
          return new Promise(() => {});
        }
        if (cmd === 'extract_audio') {
          throw new Error('ffmpeg failed');
        }
        return {};
      });
      setVideoInStore();
      render(<AudioExtractor />);

      await userEvent.click(screen.getByRole('button', { name: /开始提取音频/ }));

      await waitFor(() => {
        expect(screen.getByText(/提取失败/)).toBeInTheDocument();
      });
      // finally 块应重置 isExtracting，按钮应不再显示"提取中"
      expect(screen.queryByText(/提取中\.\.\./)).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: /开始提取音频/ })).not.toBeDisabled();
    });
  });

});
