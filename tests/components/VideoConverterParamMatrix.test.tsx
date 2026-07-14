import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import { useAppStore, type FileItem } from '../../src/stores/appStore';
import VideoConverter from '../../src/components/video/VideoConverter';

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

const getVideoEncodingCard = () => {
  const cards = document.querySelectorAll('section.bg-surface-container-lowest');
  return Array.from(cards).find((card) => {
    const title = card.querySelector('label.font-label-caps');
    return title?.textContent === '视频编码';
  })!;
};

const getAudioSection = () => {
  const cards = document.querySelectorAll('section.bg-surface-container-lowest');
  return Array.from(cards).find((card) => {
    const title = card.querySelector('label.font-label-caps');
    return title?.textContent === '音频编码';
  })!;
};

const getFormatSection = () => {
  const cards = document.querySelectorAll('section.bg-surface-container-lowest');
  return Array.from(cards).find((card) => {
    const title = card.querySelector('label.font-label-caps');
    return title?.textContent === '目标格式';
  })!;
};

const FORMATS = ['MP4', 'MOV', 'AVI', 'MKV', 'WebM'] as const;
const VIDEO_CODECS = ['h264', 'h265', 'av1', 'vp9', 'prores'] as const;
const AUDIO_CODECS = ['aac', 'mp3', 'opus', 'flac', 'ac3'] as const;

const COMPATIBLE_VIDEO_CODECS: Record<(typeof FORMATS)[number], Array<(typeof VIDEO_CODECS)[number]>> = {
  MP4: ['h264', 'h265', 'av1'],
  MOV: ['h264', 'h265', 'prores'],
  AVI: ['h264'],
  MKV: ['h264', 'h265', 'av1', 'vp9'],
  WebM: ['av1', 'vp9'],
};

describe('VideoConverter 125 组合参数验证', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.getState().clearFiles();
    setVideoFile();
    vi.mocked(invoke).mockResolvedValue({});
  });

  /**
   * 完整 125 组合参数验证：
   * 5 格式 × 5 视频编码器 × 5 音频编码器 = 125
   * 仅验证 invoke 参数对象的形状，不执行实际 FFmpeg
   */
  describe('完整 125 组合参数', () => {
    for (const format of FORMATS) {
      for (const vCodec of VIDEO_CODECS) {
        for (const aCodec of AUDIO_CODECS) {
          const compatible = COMPATIBLE_VIDEO_CODECS[format].includes(vCodec);
          const testName = compatible
            ? `${format} + ${vCodec} + ${aCodec} → 10 字段齐全`
            : `${format} + ${vCodec} + ${aCodec} → 不兼容时禁止导出`;
          it(testName, async () => {
            render(<VideoConverter />);

            // 1. 切换目标格式
            const formatCard = getFormatSection();
            const formatBtn = Array.from(formatCard.querySelectorAll('button')).find(
              (b) => b.textContent === format
            );
            if (formatBtn) await userEvent.click(formatBtn);

            // 2. 切换视频编码器
            const videoCard = getVideoEncodingCard();
            const videoSelect = videoCard.querySelector('select#video-codec-select') as HTMLSelectElement;
            if (videoSelect) {
              await userEvent.selectOptions(videoSelect, vCodec);
            }

            if (!compatible) {
              expect(videoSelect.querySelector(`option[value="${vCodec}"]`)).toBeDisabled();
              expect(invoke).not.toHaveBeenCalled();
              return;
            }

            // 3. 切换音频编码器（WebM 强制 Opus，需特殊处理）
            const audioCard = getAudioSection();
            const audioSelect = audioCard.querySelector('select#audio-codec-select') as HTMLSelectElement;
            if (audioSelect && !audioSelect.disabled) {
              await userEvent.selectOptions(audioSelect, aCodec);
            }

            // 4. 点击导出
            await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

            // 5. 验证 invoke 参数
            await waitFor(() => {
              expect(invoke).toHaveBeenCalledWith(
                'convert_video_format',
                expect.objectContaining({
                  inputPath: video.path,
                  outputPath: expect.stringMatching(/^C:\\.*_converted\./),
                  targetFormat: format.toLowerCase(),
                  quality: 23,
                  videoCodec: vCodec,
                  resolutionWidth: 1920,
                  resolutionHeight: 1080,
                  fps: 30,
                  audioCodec: format === 'WebM' ? 'opus' : aCodec,
                  audioBitrate: 192000,
                })
              );
            });
          });
        }
      }
    }
  }, 60000); // 125 测试需要较长超时

  /**
   * 推荐组合回归保护：5 个 format-codec 联动矩阵
   * 这是用户最常用的 5 种组合
   */
  describe('5 推荐组合回归保护', () => {
    const recommendations = [
      { fmt: 'MP4', v: 'h264', a: 'aac' },
      { fmt: 'MOV', v: 'h264', a: 'aac' },
      { fmt: 'AVI', v: 'h264', a: 'mp3' },
      { fmt: 'MKV', v: 'h265', a: 'aac' },
      { fmt: 'WebM', v: 'vp9', a: 'opus' },
    ];

    recommendations.forEach(({ fmt, v, a }) => {
      it(`${fmt} 推荐组合 → ${v} + ${a}`, async () => {
        render(<VideoConverter />);

        const formatCard = getFormatSection();
        const formatBtn = Array.from(formatCard.querySelectorAll('button')).find(
          (b) => b.textContent === fmt
        );
        if (formatBtn) await userEvent.click(formatBtn);

        await waitFor(() => {
          expect(invoke).not.toHaveBeenCalled();
        });

        await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

        await waitFor(() => {
          expect(invoke).toHaveBeenCalledWith(
            'convert_video_format',
            expect.objectContaining({
              targetFormat: fmt.toLowerCase(),
              videoCodec: v,
              audioCodec: a,
            })
          );
        });
      });
    });
  });

  /**
   * 边界值测试
   */
  describe('参数边界值', () => {
    it('custom 分辨率偶数 → 通过', async () => {
      render(<VideoConverter />);

      const videoCard = getVideoEncodingCard();
      const customBtn = Array.from(videoCard.querySelectorAll('button')).find(
        (b) => b.textContent === '自定义'
      );
      if (customBtn) await userEvent.click(customBtn);

      const widthInput = screen.getByPlaceholderText('1920') as HTMLInputElement;
      const heightInput = screen.getByPlaceholderText('1080') as HTMLInputElement;
      await userEvent.clear(widthInput);
      await userEvent.type(widthInput, '1280');
      await userEvent.clear(heightInput);
      await userEvent.type(heightInput, '720');

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            resolutionWidth: 1280,
            resolutionHeight: 720,
          })
        );
      });
    });

    it('custom 分辨率最小边界 64x64', async () => {
      render(<VideoConverter />);

      const videoCard = getVideoEncodingCard();
      const customBtn = Array.from(videoCard.querySelectorAll('button')).find(
        (b) => b.textContent === '自定义'
      );
      if (customBtn) await userEvent.click(customBtn);

      const widthInput = screen.getByPlaceholderText('1920') as HTMLInputElement;
      const heightInput = screen.getByPlaceholderText('1080') as HTMLInputElement;
      await userEvent.clear(widthInput);
      await userEvent.type(widthInput, '64');
      await userEvent.clear(heightInput);
      await userEvent.type(heightInput, '64');

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            resolutionWidth: 64,
            resolutionHeight: 64,
          })
        );
      });
    });

    it('custom 分辨率最大边界 7680x4320', async () => {
      render(<VideoConverter />);

      const videoCard = getVideoEncodingCard();
      const customBtn = Array.from(videoCard.querySelectorAll('button')).find(
        (b) => b.textContent === '自定义'
      );
      if (customBtn) await userEvent.click(customBtn);

      const widthInput = screen.getByPlaceholderText('1920') as HTMLInputElement;
      const heightInput = screen.getByPlaceholderText('1080') as HTMLInputElement;
      await userEvent.clear(widthInput);
      await userEvent.type(widthInput, '7680');
      await userEvent.clear(heightInput);
      await userEvent.type(heightInput, '4320');

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            resolutionWidth: 7680,
            resolutionHeight: 4320,
          })
        );
      });
    });

    it('RF 画质最小值 0', async () => {
      render(<VideoConverter />);

      const slider = screen.getByLabelText('画质控制') as HTMLInputElement;
      fireEvent.change(slider, { target: { value: '0' } });

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({ quality: 0 })
        );
      });
    });

    it('RF 画质最大值 51', async () => {
      render(<VideoConverter />);

      const slider = screen.getByLabelText('画质控制') as HTMLInputElement;
      fireEvent.change(slider, { target: { value: '51' } });

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({ quality: 51 })
        );
      });
    });

    it('比特率最小值 128000', async () => {
      render(<VideoConverter />);

      const audioCard = getAudioSection();
      const brBtns = Array.from(audioCard.querySelectorAll('button'));
      const btn128 = brBtns.find((b) => b.textContent === '128k');
      if (btn128) await userEvent.click(btn128);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({ audioBitrate: 128000 })
        );
      });
    });

    it('比特率最大值 320000', async () => {
      render(<VideoConverter />);

      const audioCard = getAudioSection();
      const brBtns = Array.from(audioCard.querySelectorAll('button'));
      const btn320 = brBtns.find((b) => b.textContent === '320k');
      if (btn320) await userEvent.click(btn320);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({ audioBitrate: 320000 })
        );
      });
    });
  });

  /**
   * 联动约束测试
   */
  describe('联动约束', () => {
    it('切换 ProRes → RF 滑块禁用', async () => {
      render(<VideoConverter />);

      const formatCard = getFormatSection();
      const movBtn = Array.from(formatCard.querySelectorAll('button')).find(
        (b) => b.textContent === 'MOV'
      );
      if (movBtn) await userEvent.click(movBtn);

      const videoCard = getVideoEncodingCard();
      const videoSelect = videoCard.querySelector('select#video-codec-select') as HTMLSelectElement;
      await userEvent.selectOptions(videoSelect, 'prores');

      const slider = screen.getByLabelText('画质控制') as HTMLInputElement;
      expect(slider).toBeDisabled();
    });

    it('切换 FLAC → 所有比特率 chip 禁用', async () => {
      render(<VideoConverter />);

      const audioCard = getAudioSection();
      const audioSelect = audioCard.querySelector('select#audio-codec-select') as HTMLSelectElement;
      await userEvent.selectOptions(audioSelect, 'flac');

      const brBtns = Array.from(audioCard.querySelectorAll('button'));
      brBtns.forEach((btn) => {
        if (btn.textContent?.match(/^\d+k$/)) {
          expect(btn).toBeDisabled();
        }
      });
    });

    it('切换 WebM → 音频下拉禁用（强制 Opus）', async () => {
      render(<VideoConverter />);

      const formatCard = getFormatSection();
      const webmBtn = Array.from(formatCard.querySelectorAll('button')).find(
        (b) => b.textContent === 'WebM'
      );
      if (webmBtn) await userEvent.click(webmBtn);

      const audioCard = getAudioSection();
      const audioSelect = audioCard.querySelector('select#audio-codec-select') as HTMLSelectElement;
      expect(audioSelect).toBeDisabled();
      expect(audioSelect.value).toBe('opus');
    });

    it('切换 WebM → 音频自动锁定 Opus 即使后端切其他也会被强制', async () => {
      render(<VideoConverter />);

      const formatCard = getFormatSection();
      const webmBtn = Array.from(formatCard.querySelectorAll('button')).find(
        (b) => b.textContent === 'WebM'
      );
      if (webmBtn) await userEvent.click(webmBtn);

      await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

      await waitFor(() => {
        expect(invoke).toHaveBeenCalledWith(
          'convert_video_format',
          expect.objectContaining({
            targetFormat: 'webm',
            audioCodec: 'opus',
          })
        );
      });
    });
  });

  /**
   * 帧率可选值测试
   */
  describe('帧率可选值', () => {
    const fpsTests = [
      { chip: '30fps', expected: 30 },
      { chip: '24fps', expected: 24 },
      { chip: '60fps', expected: 60 },
      { chip: '原始', expected: null },
    ];

    fpsTests.forEach(({ chip, expected }) => {
      it(`帧率 ${chip} → fps=${expected}`, async () => {
        render(<VideoConverter />);

        const videoCard = getVideoEncodingCard();
        const fpsLabel = Array.from(videoCard.querySelectorAll('label')).find(
          (l) => l.textContent === '帧率'
        );
        const fpsContainer = fpsLabel?.nextElementSibling;
        if (fpsContainer) {
          const btn = Array.from(fpsContainer.querySelectorAll('button')).find(
            (b) => b.textContent === chip
          );
          if (btn) await userEvent.click(btn);
        }

        await userEvent.click(screen.getByRole('button', { name: /立即导出/ }));

        await waitFor(() => {
          expect(invoke).toHaveBeenCalledWith(
            'convert_video_format',
            expect.objectContaining({ fps: expected })
          );
        });
      });
    });
  });
});
