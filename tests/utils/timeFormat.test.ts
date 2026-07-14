import { describe, it, expect } from 'vitest';
import { formatDuration } from '../../src/utils/timeFormat';

describe('formatDuration', () => {
  it('formats zero', () => {
    expect(formatDuration(0)).toBe('00:00:00');
  });
  it('formats seconds only', () => {
    expect(formatDuration(45)).toBe('00:00:45');
  });
  it('formats minutes and seconds', () => {
    expect(formatDuration(125)).toBe('00:02:05');
  });
  it('formats hours', () => {
    expect(formatDuration(3725)).toBe('01:02:05');
  });
  it('handles negative as 00:00:00', () => {
    expect(formatDuration(-1)).toBe('00:00:00');
  });
  it('handles NaN as 00:00:00', () => {
    expect(formatDuration(NaN)).toBe('00:00:00');
  });
  it('truncates fractional seconds', () => {
    expect(formatDuration(59.9)).toBe('00:00:59');
  });
});
