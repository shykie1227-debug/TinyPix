import { describe, expect, it } from 'vitest';
import {
  applySharpen,
  buildCanvasFilter,
  mapDisplayCropToSource,
  mapSourceCropToDisplay,
  previewGeometry,
} from '../../src/utils/imagePreviewRenderer';

describe('image preview renderer', () => {
  it('builds the same brightness, contrast and saturation mapping used by export controls', () => {
    expect(buildCanvasFilter({ brightness: 20, contrast: -10, saturation: 30, sharpness: 0 }))
      .toBe('brightness(120%) contrast(90%) saturate(130%)');
  });

  it('swaps preview dimensions for quarter turns and preserves exact output aspect', () => {
    expect(previewGeometry(1200, 800, 90)).toEqual({ width: 800, height: 1200 });
    expect(previewGeometry(1200, 800, 0, 600, 600)).toEqual({ width: 600, height: 600 });
  });

  it('sharpens RGB pixels while preserving their alpha channel', () => {
    const pixels = new Uint8ClampedArray([
      10, 10, 10, 255, 10, 10, 10, 255, 10, 10, 10, 255,
      10, 10, 10, 255, 80, 80, 80, 128, 10, 10, 10, 255,
      10, 10, 10, 255, 10, 10, 10, 255, 10, 10, 10, 255,
    ]);
    const sharpened = applySharpen(pixels, 3, 3, 50);
    expect(sharpened[16]).toBeGreaterThan(80);
    expect(sharpened[19]).toBe(128);
    expect(Array.from(sharpened)).not.toEqual(Array.from(pixels));
  });

  it('maps crop rectangles between source and a clockwise rotated preview', () => {
    const source = { x: 10, y: 20, width: 30, height: 40 };
    const display = mapSourceCropToDisplay(source, 90, false, false);
    expect(display).toEqual({ x: 40, y: 10, width: 40, height: 30 });
    expect(mapDisplayCropToSource(display, 90, false, false)).toEqual(source);
  });

  it('maps crop rectangles through preview mirrors without changing export coordinates', () => {
    const source = { x: 10, y: 20, width: 30, height: 40 };
    const display = mapSourceCropToDisplay(source, 0, true, false);
    expect(display).toEqual({ x: 60, y: 20, width: 30, height: 40 });
    expect(mapDisplayCropToSource(display, 0, true, false)).toEqual(source);
  });
});
