import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  invokeDirectExtract,
  buildDirectExtractArgs,
  type DirectExtractParams,
} from '../../../src/modules/audio/streamExtractor';
import { invoke } from '@tauri-apps/api/core';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('M2 AudioStreamExtractor', () => {
  describe('buildDirectExtractArgs (pure)', () => {
    it('builds args with -c:a copy', () => {
      const args = buildDirectExtractArgs('/in.mp4', '/out.m4a');
      expect(args).toContain('-y');
      expect(args).toContain('-i');
      expect(args).toContain('/in.mp4');
      expect(args).toContain('-vn');
      expect(args).toContain('-c:a');
      expect(args).toContain('copy');
      expect(args).toContain('/out.m4a');
    });

    it('never includes bitrate args', () => {
      const args = buildDirectExtractArgs('/in.mp4', '/out.mp3');
      expect(args).not.toContain('-b:a');
      expect(args).not.toContain('libmp3lame');
    });

    it('preserves output extension', () => {
      const args = buildDirectExtractArgs('/in.mp4', '/out.flac');
      expect(args[args.length - 1]).toBe('/out.flac');
    });
  });

  describe('invokeDirectExtract (Tauri bridge)', () => {
    beforeEach(() => {
      vi.clearAllMocks();
      vi.mocked(invoke).mockResolvedValue({
        output_path: '/out.m4a',
        original_size: 1000,
        output_size: 1000,
        saved_bytes: 0,
        processing_time_secs: 0.5,
      });
    });

    it('invokes extract_audio with direct mode', async () => {
      const params: DirectExtractParams = {
        inputPath: '/in.mp4',
        outputPath: '/out.m4a',
      };
      const result = await invokeDirectExtract(params);

      expect(invoke).toHaveBeenCalledWith('extract_audio', {
        inputPath: '/in.mp4',
        outputPath: '/out.m4a',
        format: 'm4a',
        bitrateKbps: undefined,
        mode: 'direct',
      });
      expect(result.outputPath).toBe('/out.m4a');
    });

    it('propagates backend errors', async () => {
      vi.mocked(invoke).mockRejectedValueOnce(new Error('codec 不兼容'));
      await expect(
        invokeDirectExtract({ inputPath: '/in.mp4', outputPath: '/out.mp3' })
      ).rejects.toThrow('codec 不兼容');
    });
  });
});
