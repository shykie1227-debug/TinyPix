/**
 * 字节数 → 可读字符串（B / KB / MB / GB）
 *
 * 抽取自 7 个组件的重复实现，作为单一来源。
 * 行为兼容 StatusBar.tsx 的原版（"0 B" 而非 "0 KB"）。
 */
export function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'] as const;
  const level = Math.min(
    Math.floor(Math.log(bytes) / Math.log(1024)),
    units.length - 1
  );
  const value = bytes / Math.pow(1024, level);
  return `${value.toFixed(level === 0 ? 0 : 1)} ${units[level]}`;
}
