import { describe, it, expect } from 'vitest';
import { isVideoFormat, isImageFormat } from '../../src/utils/mediaFormat';

describe('isVideoFormat', () => {
  it('识别常见视频格式', () => {
    expect(isVideoFormat('mp4')).toBe(true);
    expect(isVideoFormat('mov')).toBe(true);
    expect(isVideoFormat('avi')).toBe(true);
    expect(isVideoFormat('mkv')).toBe(true);
    expect(isVideoFormat('webm')).toBe(true);
    expect(isVideoFormat('flv')).toBe(true);
    expect(isVideoFormat('wmv')).toBe(true);
    expect(isVideoFormat('gif')).toBe(true);
    expect(isVideoFormat('m4v')).toBe(true);
    expect(isVideoFormat('3gp')).toBe(true);
  });

  it('不区分大小写', () => {
    expect(isVideoFormat('MP4')).toBe(true);
    expect(isVideoFormat('Mov')).toBe(true);
    expect(isVideoFormat('WEBM')).toBe(true);
  });

  it('非视频格式返回 false', () => {
    expect(isVideoFormat('jpg')).toBe(false);
    expect(isVideoFormat('png')).toBe(false);
    expect(isVideoFormat('webp')).toBe(false);
    expect(isVideoFormat('txt')).toBe(false);
    expect(isVideoFormat('pdf')).toBe(false);
  });

  it('空字符串返回 false', () => {
    expect(isVideoFormat('')).toBe(false);
  });
});

describe('isImageFormat', () => {
  it('识别常见图片格式', () => {
    expect(isImageFormat('jpg')).toBe(true);
    expect(isImageFormat('jpeg')).toBe(true);
    expect(isImageFormat('png')).toBe(true);
    expect(isImageFormat('gif')).toBe(true);
    expect(isImageFormat('webp')).toBe(true);
    expect(isImageFormat('bmp')).toBe(true);
    expect(isImageFormat('tiff')).toBe(true);
    expect(isImageFormat('tif')).toBe(true);
    expect(isImageFormat('avif')).toBe(true);
    expect(isImageFormat('ico')).toBe(true);
    expect(isImageFormat('psd')).toBe(true);
    expect(isImageFormat('heic')).toBe(true);
  });

  it('不区分大小写', () => {
    expect(isImageFormat('JPG')).toBe(true);
    expect(isImageFormat('PNG')).toBe(true);
    expect(isImageFormat('WEBP')).toBe(true);
  });

  it('非图片格式返回 false', () => {
    expect(isImageFormat('mp4')).toBe(false);
    expect(isImageFormat('mov')).toBe(false);
    expect(isImageFormat('txt')).toBe(false);
    expect(isImageFormat('pdf')).toBe(false);
  });

  it('空字符串返回 false', () => {
    expect(isImageFormat('')).toBe(false);
  });

  it('GIF 同时被识别为视频和图片格式', () => {
    expect(isVideoFormat('gif')).toBe(true);
    expect(isImageFormat('gif')).toBe(true);
  });
});
