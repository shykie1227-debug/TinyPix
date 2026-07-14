import { describe, it, expect } from 'vitest';
import {
  OutputFileManager,
  generateOutputPath,
  ensureOutputDir,
  buildFileName,
} from '../../../src/modules/audio/outputManager';

describe('M5 OutputFileManager', () => {
  describe('buildFileName (pure)', () => {
    it('builds name with suffix and extension', () => {
      expect(buildFileName('movie', '_audio', 'mp3')).toBe('movie_audio.mp3');
    });

    it('lowercases extension', () => {
      expect(buildFileName('movie', '_audio', 'MP3')).toBe('movie_audio.mp3');
    });

    it('strips leading dot from extension', () => {
      expect(buildFileName('movie', '_audio', '.flac')).toBe('movie_audio.flac');
    });

    it('handles empty suffix', () => {
      expect(buildFileName('movie', '', 'mp3')).toBe('movie.mp3');
    });
  });

  describe('generateOutputPath (pure)', () => {
    it('replaces extension when no outputDir', () => {
      const result = generateOutputPath('/Users/test/movie.mp4', '_audio', 'mp3');
      expect(result).toBe('/Users/test/movie_audio.mp3');
    });

    it('appends suffix and extension when no input extension', () => {
      const result = generateOutputPath('/Users/test/movie', '_audio', 'mp3');
      expect(result).toBe('/Users/test/movie_audio.mp3');
    });

    it('uses outputDir when provided', () => {
      const result = generateOutputPath('/Users/test/movie.mp4', '_audio', 'mp3', '/Users/output');
      expect(result).toBe('/Users/output/movie_audio.mp3');
    });

    it('strips trailing slashes from outputDir', () => {
      const result = generateOutputPath('/Users/test/movie.mp4', '_audio', 'mp3', '/Users/output/');
      expect(result).toBe('/Users/output/movie_audio.mp3');
    });

    it('handles Windows-style paths', () => {
      const result = generateOutputPath('C:\\Videos\\movie.mp4', '_audio', 'mp3', 'D:\\Output');
      expect(result).toMatch(/D:.*Output.*movie_audio\.mp3$/);
    });

    it('preserves basename with spaces', () => {
      const result = generateOutputPath('/Users/test/my movie.mp4', '_audio', 'mp3');
      expect(result).toBe('/Users/test/my movie_audio.mp3');
    });
  });

  describe('ensureOutputDir (pure)', () => {
    it('returns undefined when outputDir is empty', () => {
      expect(ensureOutputDir(undefined)).toBeUndefined();
      expect(ensureOutputDir('')).toBeUndefined();
    });

    it('returns normalized outputDir when provided', () => {
      expect(ensureOutputDir('/Users/test/')).toBe('/Users/test');
      expect(ensureOutputDir('/Users/test///')).toBe('/Users/test');
      expect(ensureOutputDir('/Users/test')).toBe('/Users/test');
    });

    it('handles Windows trailing separator', () => {
      const result = ensureOutputDir('D:\\Output\\');
      expect(result).toMatch(/D:.*Output[^\\]*$/);
    });
  });

  describe('OutputFileManager (class)', () => {
    it('validates empty path', () => {
      const mgr = new OutputFileManager('');
      expect(() => mgr.validate()).toThrow();
    });

    it('validates path without extension', () => {
      const mgr = new OutputFileManager('/Users/test/movie');
      expect(() => mgr.validate()).toThrow(/扩展名/);
    });

    it('accepts valid path', () => {
      const mgr = new OutputFileManager('/Users/test/movie.mp3');
      expect(() => mgr.validate()).not.toThrow();
    });

    it('extracts directory from full path', () => {
      const mgr = new OutputFileManager('/Users/test/movie.mp3');
      expect(mgr.getDirectory()).toBe('/Users/test');
    });

    it('extracts filename from full path', () => {
      const mgr = new OutputFileManager('/Users/test/movie.mp3');
      expect(mgr.getBasename()).toBe('movie.mp3');
    });

    it('returns extension without dot', () => {
      const mgr = new OutputFileManager('/Users/test/movie.mp3');
      expect(mgr.getExtension()).toBe('mp3');
    });
  });
});
