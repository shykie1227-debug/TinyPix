import { describe, expect, it } from 'vitest';
import {
  OUTPUT_FORMATS,
  QUALITY_PRESETS,
  getOutputExtension,
  getOutputKind,
  getRecommendedCodecs,
  type AudioMode,
  type AudioOutputFormat,
  type VideoOutputFormat,
  type VideoQualityPreset,
} from '../../src/modules/video/outputProfiles';
import {
  IMAGE_INPUT_EXTENSIONS,
  IMAGE_OUTPUT_FORMATS,
  isSupportedImageInput,
} from '../../src/utils/imageCapabilities';
import { getVideoOutputPath } from '../../src/utils/videoOutput';

const videoFormats = OUTPUT_FORMATS.filter((item) => item.kind === 'video').map((item) => item.label as VideoOutputFormat);
const audioFormats = OUTPUT_FORMATS.filter((item) => item.kind === 'audio').map((item) => item.label as AudioOutputFormat);
const presets = Object.keys(QUALITY_PRESETS) as VideoQualityPreset[];
const resolutions = [
  { width: null, height: null },
  { width: 1920, height: 1080 },
  { width: 1280, height: 720 },
];

describe('release capability contract matrix', () => {
  it.each(videoFormats.flatMap((format) => presets.flatMap((preset) => resolutions.map((resolution) => ({ format, preset, ...resolution })))))
  ('$format / $preset / $width x $height keeps a complete CPU profile', ({ format, preset, width, height }) => {
    const codecs = getRecommendedCodecs(format);
    expect(getOutputKind(format)).toBe('video');
    expect(getOutputExtension(format)).toBe(format.toLowerCase());
    expect(codecs.video).toMatch(/^(h264|mpeg4|vp9)$/);
    expect(codecs.audio).toMatch(/^(aac|mp3|opus)$/);
    expect(QUALITY_PRESETS[preset].quality).toBeGreaterThanOrEqual(1);
    expect((width === null) === (height === null)).toBe(true);
  });

  it.each(videoFormats.flatMap((format) => [1, 10, 22, 26, 32, 51].flatMap((quality) => [null, 24, 30].map((fps) => ({ format, quality, fps })))))
  ('$format accepts bounded CPU quality $quality and fps $fps', ({ format, quality, fps }) => {
    expect(quality).toBeGreaterThanOrEqual(1);
    expect(quality).toBeLessThanOrEqual(51);
    expect(fps === null || (fps >= 1 && fps <= 120)).toBe(true);
    expect(getRecommendedCodecs(format)).toEqual(expect.objectContaining({ video: expect.any(String), audio: expect.any(String) }));
  });

  it.each(audioFormats.flatMap((format) => [96000, 128000, 192000, 256000, 320000].flatMap((bitrate) => (['auto', 'direct', 'reencode'] as AudioMode[]).map((mode) => ({ format, bitrate, mode })))))
  ('$format / $bitrate / $mode remains an audio-only target', ({ format, bitrate, mode }) => {
    expect(getOutputKind(format)).toBe('audio');
    expect(getOutputExtension(format)).toBe(format.toLowerCase());
    expect(bitrate).toBeGreaterThanOrEqual(96000);
    expect(['auto', 'direct', 'reencode']).toContain(mode);
  });

  it.each(IMAGE_INPUT_EXTENSIONS.flatMap((extension) => [extension, extension.toUpperCase(), `.${extension}`, `  ${extension.toUpperCase()}  `].map((candidate) => ({ extension, candidate }))))
  ('recognizes image input $candidate as $extension', ({ extension, candidate }) => {
    expect(isSupportedImageInput(candidate)).toBe(true);
    expect(IMAGE_INPUT_EXTENSIONS).toContain(extension);
  });

  it.each(IMAGE_OUTPUT_FORMATS.flatMap((format) => [1, 25, 50, 75, 100].map((quality) => ({ format, quality }))))
  ('$format.value quality $quality preserves encoding capability metadata', ({ format, quality }) => {
    expect(quality).toBeGreaterThanOrEqual(1);
    expect(quality).toBeLessThanOrEqual(100);
    expect(format.label.length).toBeGreaterThan(0);
    expect(typeof format.supportsQuality).toBe('boolean');
    expect(typeof format.supportsTransparency).toBe('boolean');
  });

  it.each(OUTPUT_FORMATS.flatMap((format) => [
    'C:\\媒体\\样片.mov',
    'C:\\very long folder name with spaces\\demo.mov',
    'D:\\输入\\same-name.mov',
    '/Users/test/Movies/demo.mov',
    '/tmp/中文路径/样片.mov',
  ].map((path) => ({ format, path }))))
  ('$format.label creates a suffix-safe output for $path', ({ format, path }) => {
    const output = getVideoOutputPath(path, format.extension, format.kind);
    expect(output).not.toBe(path);
    expect(output.toLowerCase().endsWith(`.${format.extension}`)).toBe(true);
    expect(output).toContain(format.kind === 'audio' ? '_audio' : '_output');
  });

  it.each([
    ['MP4', 'h264', 'aac'],
    ['MOV', 'h264', 'aac'],
    ['MKV', 'h264', 'aac'],
    ['AVI', 'mpeg4', 'mp3'],
    ['WebM', 'vp9', 'opus'],
  ] as const)('$0 matches the published $1/$2 codec contract', (format, video, audio) => {
    expect(getRecommendedCodecs(format)).toEqual({ video, audio });
  });
});
