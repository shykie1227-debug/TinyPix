import { describe, it, expect } from 'vitest';
import { withVideoSuffix, getExtension } from '../../src/utils/videoOutput';

describe('videoOutput', () => {
  describe('getExtension', () => {
    it('extracts extension from standard path', () => {
      expect(getExtension('/Users/abc/clip.mp4')).toBe('mp4');
    });
    it('handles Windows path', () => {
      expect(getExtension('C:\\Users\\abc\\clip.mov')).toBe('mov');
    });
    it('falls back when no extension', () => {
      expect(getExtension('/abc/clip', 'mp4')).toBe('mp4');
    });
  });

  describe('withVideoSuffix', () => {
    it('appends suffix to filename', () => {
      expect(withVideoSuffix('/a/clip.mp4', '_edited')).toBe('/a/clip_edited.mp4');
    });
    it('overrides extension when provided', () => {
      expect(withVideoSuffix('/a/clip.mp4', '_edited', 'mov')).toBe('/a/clip_edited.mov');
    });
    it('uses outputDir when provided', () => {
      expect(withVideoSuffix('/a/clip.mp4', '_edited', 'mp4', '/out')).toBe('/out/clip_edited.mp4');
    });
    it('handles path without extension', () => {
      expect(withVideoSuffix('/a/clip', '_edited', 'mp4')).toBe('/a/clip_edited.mp4');
    });
    it('strips leading dot in extension param', () => {
      expect(withVideoSuffix('/a/clip.mp4', '_x', '.mkv')).toBe('/a/clip_x.mkv');
    });
  });
});
