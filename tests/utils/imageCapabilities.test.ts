import { describe, expect, it } from 'vitest';
import {
  IMAGE_INPUT_EXTENSIONS,
  IMAGE_OUTPUT_FORMATS,
  isSupportedImageInput,
} from '../../src/utils/imageCapabilities';

describe('image capabilities', () => {
  it('exposes only the verified local image input extensions', () => {
    expect(IMAGE_INPUT_EXTENSIONS).toEqual([
      'jpg',
      'jpeg',
      'png',
      'webp',
      'avif',
      'bmp',
      'tiff',
      'tif',
      'psd',
    ]);
  });

  it('rejects formats that the product cannot reliably process', () => {
    for (const format of ['gif', 'heic', 'ico', 'svg', 'pdf', 'ppt', 'ai', 'eps']) {
      expect(isSupportedImageInput(format)).toBe(false);
    }
  });

  it('exports exactly five real image formats', () => {
    expect(IMAGE_OUTPUT_FORMATS.map((item) => item.value)).toEqual([
      'jpeg',
      'png',
      'webp',
      'avif',
      'bmp',
    ]);
  });
});
