import { describe, it, expect } from 'vitest';
import {
  AudioQualityOptimizer,
  estimateSizeMB,
  validateConfig,
  getDefaultBitrate,
} from '../../../src/modules/audio/qualityOptimizer';
import { AUDIO_FORMATS } from '../../../src/modules/audio/formatConverter';

describe('M4 AudioQualityOptimizer', () => {
  describe('getDefaultBitrate (pure)', () => {
    it('returns 192 for MP3', () => {
      expect(getDefaultBitrate('MP3')).toBe(192);
    });

    it('returns 192 for AAC', () => {
      expect(getDefaultBitrate('AAC')).toBe(192);
    });

    it('returns 192 for M4A', () => {
      expect(getDefaultBitrate('M4A')).toBe(192);
    });

    it('returns 0 for WAV (lossless, ignore bitrate)', () => {
      expect(getDefaultBitrate('WAV')).toBe(0);
    });

    it('returns 0 for FLAC (lossless, ignore bitrate)', () => {
      expect(getDefaultBitrate('FLAC')).toBe(0);
    });
  });

  describe('estimateSizeMB (pure)', () => {
    it('estimates lossy size: bitrate * duration / 8 / 1024 / 1024', () => {
      // 192kbps * 60s / 8 / 1024 / 1024 = 1.373 MB
      const result = estimateSizeMB(AUDIO_FORMATS[0], 192, 60);
      expect(result).toBeCloseTo(1.37, 1);
    });

    it('estimates WAV size as 16bit/48kHz/2ch PCM', () => {
      // 48000 * 2 * 2 * 60 / 1024 / 1024 = 10.99 MB
      const result = estimateSizeMB(AUDIO_FORMATS[1], 0, 60);
      expect(result).toBeCloseTo(10.99, 1);
    });

    it('estimates FLAC size with 0.55 compression ratio', () => {
      // 48000 * 2 * 2 * 60 * 0.55 / 1024 / 1024 = 6.04 MB
      const result = estimateSizeMB(AUDIO_FORMATS[3], 0, 60);
      expect(result).toBeCloseTo(6.04, 1);
    });

    it('returns 0 for zero duration', () => {
      expect(estimateSizeMB(AUDIO_FORMATS[0], 192, 0)).toBe(0);
    });
  });

  describe('validateConfig (pure)', () => {
    it('rejects bitrate < 64 for lossy', () => {
      const errors = validateConfig(AUDIO_FORMATS[0], 'reencode', 32);
      expect(errors.length).toBeGreaterThan(0);
      expect(errors[0]).toMatch(/码率/);
    });

    it('rejects bitrate > 320 for lossy', () => {
      const errors = validateConfig(AUDIO_FORMATS[0], 'reencode', 500);
      expect(errors.length).toBeGreaterThan(0);
    });

    it('accepts valid bitrate range for lossy', () => {
      expect(validateConfig(AUDIO_FORMATS[0], 'reencode', 128)).toEqual([]);
      expect(validateConfig(AUDIO_FORMATS[0], 'reencode', 64)).toEqual([]);
      expect(validateConfig(AUDIO_FORMATS[0], 'reencode', 320)).toEqual([]);
    });

    it('skips bitrate validation for lossless', () => {
      expect(validateConfig(AUDIO_FORMATS[1], 'reencode', 0)).toEqual([]);
      expect(validateConfig(AUDIO_FORMATS[3], 'reencode', 0)).toEqual([]);
    });

    it('accepts missing bitrate for direct mode', () => {
      expect(validateConfig(AUDIO_FORMATS[0], 'direct', undefined)).toEqual([]);
    });

    it('warns when bitrate provided for direct mode', () => {
      const errors = validateConfig(AUDIO_FORMATS[0], 'direct', 128);
      expect(errors.length).toBeGreaterThan(0);
      expect(errors[0]).toMatch(/直接提取/);
    });

    it('rejects unknown mode', () => {
      const errors = validateConfig(AUDIO_FORMATS[0], 'invalid' as any, 128);
      expect(errors.length).toBeGreaterThan(0);
    });
  });

  describe('AudioQualityOptimizer.optimize (class)', () => {
    it('returns recommended bitrate matching user selection', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[0], 'reencode', undefined, 60, 256);
      expect(result.recommendedBitrate).toBe(256);
    });

    it('uses default bitrate when userBitrate not provided', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[0], 'reencode', undefined, 60, undefined);
      expect(result.recommendedBitrate).toBe(192);
    });

    it('uses 0 for lossless formats', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[1], 'reencode', undefined, 60, 320);
      expect(result.recommendedBitrate).toBe(0);
    });

    it('emits warning when user bitrate exceeds 320', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[0], 'reencode', undefined, 60, 500);
      expect(result.warnings.some((w) => w.includes('码率'))).toBe(true);
    });

    it('emits warning when lossless format with non-zero bitrate', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[1], 'reencode', undefined, 60, 192);
      expect(result.warnings.some((w) => w.includes('无损'))).toBe(true);
    });

    it('emits warning when direct mode with bitrate', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[0], 'direct', undefined, 60, 128);
      expect(result.warnings.some((w) => w.includes('直接'))).toBe(true);
    });

    it('includes estimated size in result', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[0], 'reencode', undefined, 60, 192);
      expect(result.estimatedSizeMB).toBeGreaterThan(0);
    });

    it('handles missing duration gracefully', () => {
      const opt = new AudioQualityOptimizer();
      const result = opt.optimize(AUDIO_FORMATS[0], 'reencode', undefined, undefined, 192);
      expect(result.estimatedSizeMB).toBe(0);
    });
  });
});
