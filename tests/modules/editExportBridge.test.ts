import { describe, it, expect, vi } from 'vitest';
import { submitEditExport } from '../../src/modules/editExportBridge';

vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
}));

import { invoke } from '@tauri-apps/api/core';

const baseParams = {
  inputPath: '/a/clip.mp4',
  outputPath: '/a/clip_edited.mp4',
  startSecs: 0,
  endSecs: 60,
  speed: 1,
  volume: 1,
  brightness: 0,
  contrast: 0,
  targetFormat: 'mp4' as const,
};

describe('submitEditExport', () => {
  it('invokes the backend command with mapped parameters', async () => {
    (invoke as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({});
    await submitEditExport(baseParams);
    expect(invoke).toHaveBeenCalledWith('edit_and_export_video', {
      inputPath: '/a/clip.mp4',
      outputPath: '/a/clip_edited.mp4',
      startSecs: 0,
      endSecs: 60,
      speed: 1,
      volume: 1,
      brightness: 0,
      contrast: 0,
      format: 'mp4',
    });
  });

  it('normalizes UI brightness and contrast to backend [-1, 1] range', async () => {
    (invoke as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({});
    await submitEditExport({
      ...baseParams,
      brightness: 50,
      contrast: -25,
      targetFormat: 'mkv',
    });
    expect(invoke).toHaveBeenCalledWith(
      'edit_and_export_video',
      expect.objectContaining({
        brightness: 0.5,
        contrast: -0.25,
        format: 'mkv',
      })
    );
  });

  it('throws when start >= end', async () => {
    await expect(
      submitEditExport({ ...baseParams, startSecs: 60, endSecs: 30 })
    ).rejects.toThrow('起始时间必须小于结束时间');
  });

  it('throws when speed is out of range', async () => {
    await expect(
      submitEditExport({ ...baseParams, speed: 0 })
    ).rejects.toThrow(/速度/);
    await expect(
      submitEditExport({ ...baseParams, speed: 200 })
    ).rejects.toThrow(/速度/);
  });

  it('throws when brightness or contrast exceed [-100, 100]', async () => {
    await expect(
      submitEditExport({ ...baseParams, brightness: 150 })
    ).rejects.toThrow(/亮度/);
    await expect(
      submitEditExport({ ...baseParams, contrast: -150 })
    ).rejects.toThrow(/对比度/);
  });

  it('throws when volume is out of range', async () => {
    await expect(
      submitEditExport({ ...baseParams, volume: -1 })
    ).rejects.toThrow(/音量/);
  });
});
