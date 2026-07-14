import { describe, it, expect } from 'vitest';
import { vi, beforeEach } from 'vitest';
import {
  AUDIO_FORMATS,
  buildFfmpegArgs,
  clampBitrate,
  findFormat,
  invokeConvert,
} from '../../../src/modules/audio/formatConverter';
import { invoke } from '@tauri-apps/api/core';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('M3 AudioFormatConverter', () => {
  describe('AUDIO_FORMATS constant', () => {
    it('exposes all 5 formats', () => {
      expect(AUDIO_FORMATS.length).toBe(5);
    });

    it('each format has required fields', () => {
      AUDIO_FORMATS.forEach((f) => {
        expect(f.label).toBeDefined();
        expect(f.codec).toBeDefined();
        expect(f.ext).toBeDefined();
        expect(typeof f.lossless).toBe('boolean');
      });
    });

    it('MP3 and M4A are lossy', () => {
      expect(AUDIO_FORMATS.find((f) => f.label === 'MP3')?.lossless).toBe(false);
      expect(AUDIO_FORMATS.find((f) => f.label === 'M4A')?.lossless).toBe(false);
      expect(AUDIO_FORMATS.find((f) => f.label === 'AAC')?.lossless).toBe(false);
    });

    it('WAV and FLAC are lossless', () => {
      expect(AUDIO_FORMATS.find((f) => f.label === 'WAV')?.lossless).toBe(true);
      expect(AUDIO_FORMATS.find((f) => f.label === 'FLAC')?.lossless).toBe(true);
    });
  });

  describe('findFormat (pure)', () => {
    it('finds MP3 by label', () => {
      expect(findFormat('MP3')?.codec).toBe('libmp3lame');
    });

    it('returns undefined for invalid label', () => {
      expect(findFormat('INVALID' as any)).toBeUndefined();
    });
  });

  describe('clampBitrate (pure)', () => {
    it('clamps low values to 64', () => {
      expect(clampBitrate(32)).toBe(64);
      expect(clampBitrate(0)).toBe(64);
    });

    it('clamps high values to 320', () => {
      expect(clampBitrate(500)).toBe(320);
      expect(clampBitrate(1000)).toBe(320);
    });

    it('keeps in-range values', () => {
      expect(clampBitrate(128)).toBe(128);
      expect(clampBitrate(64)).toBe(64);
      expect(clampBitrate(320)).toBe(320);
    });
  });

  describe('buildFfmpegArgs (pure)', () => {
    it('builds lossy args with bitrate', () => {
      const args = buildFfmpegArgs(
        { inputPath: '/in.mp4', outputPath: '/out.mp3' },
        { format: AUDIO_FORMATS[0], bitrateKbps: 192 }
      );
      expect(args).toContain('-c:a');
      expect(args).toContain('libmp3lame');
      expect(args).toContain('-b:a');
      expect(args).toContain('192k');
      expect(args).toContain('/in.mp4');
      expect(args).toContain('/out.mp3');
    });

    it('omits bitrate for lossless WAV', () => {
      const args = buildFfmpegArgs(
        { inputPath: '/in.mp4', outputPath: '/out.wav' },
        { format: AUDIO_FORMATS[1], bitrateKbps: 192 }
      );
      expect(args).toContain('pcm_s16le');
      expect(args).not.toContain('-b:a');
    });

    it('omits bitrate for lossless FLAC', () => {
      const args = buildFfmpegArgs(
        { inputPath: '/in.mp4', outputPath: '/out.flac' },
        { format: AUDIO_FORMATS[3] }
      );
      expect(args).toContain('flac');
      expect(args).not.toContain('-b:a');
    });

    it('always includes -y and -vn', () => {
      const args = buildFfmpegArgs(
        { inputPath: '/in.mp4', outputPath: '/out.mp3' },
        { format: AUDIO_FORMATS[0], bitrateKbps: 128 }
      );
      expect(args).toContain('-y');
      expect(args).toContain('-vn');
    });
  });

  describe('invokeConvert (Tauri bridge)', () => {
    beforeEach(() => {
      vi.clearAllMocks();
      vi.mocked(invoke).mockResolvedValue({
        output_path: '/out.mp3',
        original_size: 1000,
        output_size: 100,
        saved_bytes: 900,
        processing_time_secs: 1,
      });
    });

    it('invokes extract_audio with reencode mode', async () => {
      const result = await invokeConvert({
        inputPath: '/in.mp4',
        outputPath: '/out.mp3',
        format: AUDIO_FORMATS[0],
        bitrateKbps: 192,
      });

      expect(invoke).toHaveBeenCalledWith('extract_audio', {
        inputPath: '/in.mp4',
        outputPath: '/out.mp3',
        format: 'mp3',
        bitrateKbps: 192,
        mode: 'reencode',
      });
      expect(result.outputPath).toBe('/out.mp3');
    });

    it('omits bitrateKbps when not provided', async () => {
      await invokeConvert({
        inputPath: '/in.mp4',
        outputPath: '/out.mp3',
        format: AUDIO_FORMATS[0],
      });

      const callArgs = vi.mocked(invoke).mock.calls[0][1] as any;
      expect(callArgs.bitrateKbps).toBeUndefined();
    });
  });
});
