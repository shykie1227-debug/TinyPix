import { describe, expect, it } from 'vitest';
import { calculateAppZoom, calculateInitialWindowSize } from '../../src/utils/appZoom';

describe('desktop app zoom', () => {
  it('uses the 1080x640 virtual design baseline and never enlarges the UI', () => {
    expect(calculateAppZoom(1080, 640)).toBe(1);
    expect(calculateAppZoom(1200, 800)).toBe(1);
  });

  it('scales the whole workbench down without going below 80 percent', () => {
    expect(calculateAppZoom(900, 520)).toBeCloseTo(0.8125, 4);
    expect(calculateAppZoom(800, 480)).toBe(0.8);
  });

  it('keeps the initial window within 88 percent of the display work area', () => {
    expect(calculateInitialWindowSize(1229, 688)).toEqual({ width: 1080, height: 605 });
    expect(calculateInitialWindowSize(1920, 1080)).toEqual({ width: 1080, height: 640 });
  });
});
