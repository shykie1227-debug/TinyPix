import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import VideoConverter from '../../src/components/video/VideoConverter';
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

describe('VideoConverter', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.getState().clearFiles();
    vi.mocked(invoke).mockResolvedValue({
      output_path: '/Users/test/demo_converted.mp4',
      original_size: 1024 * 1024 * 100,
      output_size: 1024 * 1024 * 50,
      saved_bytes: 1024 * 1024 * 50,
      processing_time_secs: 10,
    });
  });

  describe('card structure and styling', () => {
    it('renders three separate white cards (target format, video encoding, audio encoding)', () => {
      render(<VideoConverter />);

      const cards = document.querySelectorAll('section.bg-surface-container-lowest');
      expect(cards.length).toBeGreaterThanOrEqual(3);
    });

    it('cards have correct rounded corners and border', () => {
      render(<VideoConverter />);

      const cards = document.querySelectorAll('section.bg-surface-container-lowest');
      cards.forEach((card) => {
        expect(card.className).toMatch(/rounded-\[18px\]/);
        expect(card.className).toMatch(/border-outline-variant/);
      });
    });

    it('section titles use label-caps uppercase style', () => {
      render(<VideoConverter />);

      const cards = document.querySelectorAll('section.bg-surface-container-lowest');
      const mainCards = Array.from(cards).slice(-3);
      expect(mainCards.length).toBe(3);
      mainCards.forEach((card) => {
        const title = card.querySelector('label.font-label-caps');
        expect(title).not.toBeNull();
        expect(title?.className).toMatch(/opacity-50/);
      });
    });
  });

  describe('target format chips', () => {
    it('renders five format options', () => {
      render(<VideoConverter />);

      expect(screen.getByText('MP4')).toBeInTheDocument();
      expect(screen.getByText('MOV')).toBeInTheDocument();
      expect(screen.getByText('AVI')).toBeInTheDocument();
      expect(screen.getByText('MKV')).toBeInTheDocument();
      expect(screen.getByText('WebM')).toBeInTheDocument();
    });

    it('has MP4 selected by default', () => {
      render(<VideoConverter />);

      const mp4Div = screen.getByText('MP4');
      expect(mp4Div.className).toMatch(/bg-primary/);
    });

    it('WebM spans two columns', () => {
      render(<VideoConverter />);

      const webmBtn = screen.getByText('WebM').closest('button')!;
      expect(webmBtn.style.gridColumn).toBe('span 2');
    });
  });

  describe('format-codec联动', () => {
    it('MP4 defaults to H.264 + AAC', () => {
      setVideoInStore();
      render(<VideoConverter />);

      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      expect(videoSelect.value).toBe('h264');
      expect(audioSelect.value).toBe('aac');
    });

    it('switching to WebM sets VP9 + Opus', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('WebM'));

      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      expect(videoSelect.value).toBe('vp9');
      expect(audioSelect.value).toBe('opus');
    });

    it('switching to MKV sets H.265 + AAC', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('MKV'));

      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      expect(videoSelect.value).toBe('h265');
      expect(audioSelect.value).toBe('aac');
    });

    it('switching to AVI sets H.264 + MP3', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('AVI'));

      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      expect(videoSelect.value).toBe('h264');
      expect(audioSelect.value).toBe('mp3');
    });

    it('switching to MOV sets H.264 + AAC', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('MOV'));

      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      expect(videoSelect.value).toBe('h264');
      expect(audioSelect.value).toBe('aac');
    });

    it('disables incompatible video codecs before export when WebM is selected', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('WebM'));

      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      expect(videoSelect.querySelector('option[value="h264"]')).toBeDisabled();
      expect(videoSelect.querySelector('option[value="h265"]')).toBeDisabled();
      expect(videoSelect.querySelector('option[value="av1"]')).not.toBeDisabled();
      expect(videoSelect.querySelector('option[value="vp9"]')).not.toBeDisabled();
    });
  });

  describe('processing queue', () => {
    it('shows every queued file with source format, target format, status, and delete action', async () => {
      setVideosInStore();
      render(<VideoConverter />);

      expect(screen.getByText('处理队列')).toBeInTheDocument();
      expect(screen.getAllByText('demo.mp4').length).toBeGreaterThanOrEqual(1);
      expect(screen.getByText('clip.mov')).toBeInTheDocument();
      expect(screen.getByText('源 MP4')).toBeInTheDocument();
      expect(screen.getByText('源 MOV')).toBeInTheDocument();
      expect(screen.getAllByText('目标 MP4')).toHaveLength(2);
      expect(screen.getAllByText('等待中')).toHaveLength(2);

      await userEvent.click(screen.getByRole('button', { name: '删除 clip.mov' }));

      expect(screen.queryByText('clip.mov')).not.toBeInTheDocument();
      expect(useAppStore.getState().files.map((file) => file.id)).toEqual(['video-1']);
    });

    it('keeps successful files and continues the batch when one conversion fails', async () => {
      setVideosInStore();
      vi.mocked(invoke)
        .mockRejectedValueOnce('codec failed')
        .mockResolvedValueOnce({
          output_path: '/Users/test/clip_converted.mp4',
          original_size: 1024 * 1024 * 80,
          output_size: 1024 * 1024 * 40,
          saved_bytes: 1024 * 1024 * 40,
          processing_time_secs: 8,
        });
      render(<VideoConverter />);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledTimes(2);
      });
      expect(screen.getByText('demo.mp4 转换失败：codec failed')).toBeInTheDocument();
      expect(screen.getByText('完成')).toBeInTheDocument();
      expect(screen.getByText('格式转换完成 1/2，失败 1')).toBeInTheDocument();
    });
  });

  describe('ProRes encoder disables RF slider', () => {
    it('RF slider is enabled for H.264', () => {
      setVideoInStore();
      render(<VideoConverter />);

      const slider = screen.getByRole('slider', { name: /画质控制/ });
      expect(slider).not.toBeDisabled();
    });

    it('RF slider is disabled for ProRes', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('MOV'));
      const videoSelect = screen.getByLabelText('编码器') as HTMLSelectElement;
      await userEvent.selectOptions(videoSelect, 'prores');

      const slider = screen.getByRole('slider', { name: /画质控制/ });
      expect(slider).toBeDisabled();
    });
  });

  const getAudioSection = () => {
    const cards = document.querySelectorAll('section.bg-surface-container-lowest');
    return Array.from(cards).find((card) => {
      const title = card.querySelector('label.font-label-caps');
      return title?.textContent === '音频编码';
    })!;
  };

  const getBitrateButtons = () => {
    const audioSection = getAudioSection();
    const bitrateLabel = Array.from(audioSection.querySelectorAll('label')).find(
      (l) => l.textContent === '比特率'
    );
    const bitrateContainer = bitrateLabel?.nextElementSibling;
    return bitrateContainer ? bitrateContainer.querySelectorAll('button') : [];
  };

  describe('FLAC audio codec disables bitrate chips', () => {
    it('bitrate chips are enabled for AAC', () => {
      setVideoInStore();
      render(<VideoConverter />);

      const bitrateBtns = getBitrateButtons();
      expect(bitrateBtns.length).toBe(4);
      expect(bitrateBtns[1]).not.toBeDisabled();
    });

    it('bitrate chips are disabled for FLAC', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      await userEvent.selectOptions(audioSelect, 'flac');

      const bitrateBtns = getBitrateButtons();
      bitrateBtns.forEach((btn) => {
        expect(btn).toBeDisabled();
      });
    });
  });

  describe('WebM forces Opus audio', () => {
    it('audio codec selector is disabled when format is WebM', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('WebM'));

      const audioSelect = screen.getByLabelText('音频编码器') as HTMLSelectElement;
      expect(audioSelect).toBeDisabled();
      expect(audioSelect.value).toBe('opus');
    });
  });

  const getVideoEncodingCard = () => {
    const cards = document.querySelectorAll('section.bg-surface-container-lowest');
    return Array.from(cards).find((card) => {
      const title = card.querySelector('label.font-label-caps');
      return title?.textContent === '视频编码';
    })!;
  };

  const getResolutionSection = () => getVideoEncodingCard();

  describe('resolution controls', () => {
    it('renders six resolution options', () => {
      render(<VideoConverter />);

      const resSection = getResolutionSection();
      expect(resSection.textContent).toContain('原始');
      expect(resSection.textContent).toContain('1080p');
      expect(resSection.textContent).toContain('720p');
      expect(resSection.textContent).toContain('480p');
      expect(resSection.textContent).toContain('4K');
      expect(resSection.textContent).toContain('自定义');
    });

    it('custom resolution expands width/height inputs when 自定义 is selected', async () => {
      render(<VideoConverter />);

      expect(screen.queryByPlaceholderText('1920')).not.toBeInTheDocument();
      expect(screen.queryByPlaceholderText('1080')).not.toBeInTheDocument();

      const resSection = getResolutionSection();
      const customBtn = Array.from(resSection.querySelectorAll('button')).find(
        (btn) => btn.textContent === '自定义'
      );
      if (customBtn) await userEvent.click(customBtn);

      expect(screen.getByPlaceholderText('1920')).toBeInTheDocument();
      expect(screen.getByPlaceholderText('1080')).toBeInTheDocument();
    });

    it('custom resolution with odd values shows error and disables export', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      const resSection = getResolutionSection();
      const customBtn = Array.from(resSection.querySelectorAll('button')).find(
        (btn) => btn.textContent === '自定义'
      );
      if (customBtn) await userEvent.click(customBtn);

      const widthInput = screen.getByPlaceholderText('1920');
      await userEvent.clear(widthInput);
      await userEvent.type(widthInput, '1921');

      const cta = screen.getByRole('button', { name: /立即导出/ });
      expect(cta).toBeDisabled();
    });

    it('custom resolution out of range disables export', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      const resSection = getResolutionSection();
      const customBtn = Array.from(resSection.querySelectorAll('button')).find(
        (btn) => btn.textContent === '自定义'
      );
      if (customBtn) await userEvent.click(customBtn);

      const widthInput = screen.getByPlaceholderText('1920');
      await userEvent.clear(widthInput);
      await userEvent.type(widthInput, '10000');

      const cta = screen.getByRole('button', { name: /立即导出/ });
      expect(cta).toBeDisabled();
    });
  });

  const getFpsButtons = () => {
    const resSection = getResolutionSection();
    const fpsLabel = Array.from(resSection.querySelectorAll('label')).find(
      (l) => l.textContent === '帧率'
    );
    const fpsContainer = fpsLabel?.nextElementSibling;
    return fpsContainer ? fpsContainer.querySelectorAll('button') : [];
  };

  describe('framerate controls', () => {
    it('renders four framerate options', () => {
      render(<VideoConverter />);

      const fpsBtns = getFpsButtons();
      expect(fpsBtns.length).toBe(4);
      expect(fpsBtns[0].textContent).toBe('原始');
      expect(fpsBtns[1].textContent).toBe('30fps');
      expect(fpsBtns[2].textContent).toBe('24fps');
      expect(fpsBtns[3].textContent).toBe('60fps');
    });

    it('has 30fps selected by default', () => {
      render(<VideoConverter />);

      const fpsBtns = getFpsButtons();
      expect(fpsBtns[1].className).toMatch(/bg-primary/);
    });
  });

  describe('CTA button', () => {
    it('is disabled when no video files', () => {
      render(<VideoConverter />);

      const cta = screen.getByRole('button', { name: /立即导出/ });
      expect(cta).toBeDisabled();
    });

    it('is enabled when video files exist', () => {
      setVideoInStore();
      render(<VideoConverter />);

      const cta = screen.getByRole('button', { name: /立即导出/ });
      expect(cta).not.toBeDisabled();
    });

    it('shows Loader2 and 转换中 when processing', async () => {
      setVideoInStore();
      vi.mocked(invoke).mockImplementation(
        () => new Promise((resolve) => setTimeout(() => resolve({}), 1000))
      );
      render(<VideoConverter />);

      const cta = screen.getByRole('button', { name: /立即导出/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /转换中\.\.\./ })).toBeInTheDocument();
      });
    });
  });

  describe('convert_video_format command invocation', () => {
    it('passes all 10 parameters with default values', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      const cta = screen.getByRole('button', { name: /立即导出/ });
      await userEvent.click(cta);

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            inputPath: videoFile.path,
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

    it('passes correct parameters when WebM is selected', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByText('WebM'));
      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            targetFormat: 'webm',
            videoCodec: 'vp9',
            audioCodec: 'opus',
          })
        );
      });
    });

    it('passes null fps when 原始 framerate is selected', async () => {
      setVideoInStore();
      render(<VideoConverter />);

      const fpsBtns = getFpsButtons();
      await userEvent.click(fpsBtns[0]);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            fps: null,
          })
        );
      });
    });

    it('converts every queued video and uses the configured output directory', async () => {
      setVideosInStore();
      render(<VideoConverter />);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledTimes(2);
      });
      expect(invoke).toHaveBeenNthCalledWith(
        1,
        'convert_video_format',
        expect.objectContaining({
          inputPath: videoFile.path,
          outputPath: 'D:\\TinyPixOut\\demo_converted.mp4',
        })
      );
      expect(invoke).toHaveBeenNthCalledWith(
        2,
        'convert_video_format',
        expect.objectContaining({
          inputPath: secondVideoFile.path,
          outputPath: 'D:\\TinyPixOut\\clip_converted.mp4',
        })
      );
      expect(screen.queryByText(/当前仅处理第一个视频文件/)).not.toBeInTheDocument();
    });

    it('opens the configured output directory after conversion when enabled', async () => {
      setVideoInStore();
      useAppStore.setState((state) => ({
        options: { ...state.options, outputDir: 'D:\\TinyPixOut', openAfterProcess: true },
      }));
      render(<VideoConverter />);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenLastCalledWith('open_folder', {
          path: 'D:\\TinyPixOut',
        });
      });
    });
  });
});
