/**
 * 时间格式化与解析工具
 *
 * 抽取自 VideoTrimmer.tsx、TrimTimeline.tsx 的重复实现。
 */

/**
 * 秒数 → HH:MM:SS 字符串
 *
 * @example
 * formatDuration(65) // "00:01:05"
 */
export function formatDuration(secs: number): string {
  if (!Number.isFinite(secs) || secs < 0) return '00:00:00';
  const h = Math.floor(secs / 3600);
  const m = Math.floor((secs % 3600) / 60);
  const s = Math.floor(secs % 60);
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

/**
 * formatDuration 的别名（保持向后兼容）
 */
export const formatTime = formatDuration;
