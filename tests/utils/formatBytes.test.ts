import { describe, it, expect } from 'vitest';
import { formatBytes } from '../../src/utils/formatBytes';

describe('formatBytes', () => {
  it('0 字节显示为 0 B', () => {
    expect(formatBytes(0)).toBe('0 B');
  });

  it('负数显示为 0 B', () => {
    expect(formatBytes(-1)).toBe('0 B');
  });

  it('NaN 显示为 0 B', () => {
    expect(formatBytes(NaN)).toBe('0 B');
  });

  it('小于 1024 字节显示为 B 且不带小数', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1023)).toBe('1023 B');
  });

  it('KB 级别保留一位小数', () => {
    expect(formatBytes(1024)).toBe('1.0 KB');
    expect(formatBytes(1536)).toBe('1.5 KB');
    expect(formatBytes(2048)).toBe('2.0 KB');
  });

  it('MB 级别保留一位小数', () => {
    expect(formatBytes(1024 * 1024)).toBe('1.0 MB');
    expect(formatBytes(1.5 * 1024 * 1024)).toBe('1.5 MB');
    expect(formatBytes(100 * 1024 * 1024)).toBe('100.0 MB');
  });

  it('GB 级别保留一位小数', () => {
    expect(formatBytes(1024 * 1024 * 1024)).toBe('1.0 GB');
    expect(formatBytes(2.5 * 1024 * 1024 * 1024)).toBe('2.5 GB');
  });

  it('TB 级别按 GB 显示（超出最大单位）', () => {
    const tb = 1024 * 1024 * 1024 * 1024;
    expect(formatBytes(tb)).toBe('1024.0 GB');
    expect(formatBytes(2 * tb)).toBe('2048.0 GB');
  });

  it('小数精度正确四舍五入', () => {
    expect(formatBytes(1024 + 512)).toBe('1.5 KB');
    expect(formatBytes(1024 + 100)).toBe('1.1 KB');
    expect(formatBytes(1024 + 999)).toBe('2.0 KB');
  });
});
