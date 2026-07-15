import { describe, expect, it } from 'vitest';
import {
  OUTPUT_FORMATS,
  getDefaultSettings,
  getOutputKind,
  getRecommendedCodecs,
} from '../../../src/modules/video/outputProfiles';

describe('video output profiles', () => {
  it('uses a compatible MP4 preset by default', () => {
    expect(getDefaultSettings()).toEqual({
      format: 'MP4',
      qualityPreset: 'balanced',
      quality: 26,
      resolutionWidth: null,
      resolutionHeight: null,
      fps: null,
      audioBitrate: 192000,
      audioMode: 'auto',
    });
  });

  it('groups five video formats and four audio formats', () => {
    expect(OUTPUT_FORMATS.filter((item) => item.kind === 'video')).toHaveLength(5);
    expect(OUTPUT_FORMATS.filter((item) => item.kind === 'audio')).toHaveLength(4);
    expect(getOutputKind('WebM')).toBe('video');
    expect(getOutputKind('FLAC')).toBe('audio');
  });

  it('uses CPU-only compatible codecs for every container', () => {
    expect(getRecommendedCodecs('MP4')).toEqual({ video: 'h264', audio: 'aac' });
    expect(getRecommendedCodecs('MOV')).toEqual({ video: 'h264', audio: 'aac' });
    expect(getRecommendedCodecs('MKV')).toEqual({ video: 'h264', audio: 'aac' });
    expect(getRecommendedCodecs('AVI')).toEqual({ video: 'mpeg4', audio: 'mp3' });
    expect(getRecommendedCodecs('WebM')).toEqual({ video: 'vp9', audio: 'opus' });
  });
});
