import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

export interface ClipProperties {
  trimStart: number;
  trimEnd: number;
  speed: number;
  volume: number;
  brightness: number;
  contrast: number;
  exportFormat: 'mp4' | 'mov' | 'mkv';
  setTrimStart: (v: number) => void;
  setTrimEnd: (v: number) => void;
  setSpeed: (v: number) => void;
  setVolume: (v: number) => void;
  setBrightness: (v: number) => void;
  setContrast: (v: number) => void;
  setExportFormat: (v: 'mp4' | 'mov' | 'mkv') => void;
  setTrimRange: (start: number, end: number) => void;
  reset: () => void;
}

const DEFAULTS = {
  speed: 1,
  volume: 100,
  brightness: 0,
  contrast: 0,
  exportFormat: 'mp4' as const,
};

/**
 * 模块11：视频片段属性状态模块（ClipPropertyStateModule）
 * 集中管理 5 个片段属性 + 裁切范围 + 导出格式
 */
export function useClipProperties(duration: number): ClipProperties {
  const [trimStart, setTrimStart] = useState(0);
  const [trimEnd, setTrimEnd] = useState(duration);
  const previousDurationRef = useRef(duration);
  const [speed, setSpeed] = useState(DEFAULTS.speed);
  const [volume, setVolume] = useState(DEFAULTS.volume);
  const [brightness, setBrightness] = useState(DEFAULTS.brightness);
  const [contrast, setContrast] = useState(DEFAULTS.contrast);
  const [exportFormat, setExportFormat] = useState<'mp4' | 'mov' | 'mkv'>(DEFAULTS.exportFormat);

  const setTrimRange = useCallback((start: number, end: number) => {
    setTrimStart(start);
    setTrimEnd(end);
  }, []);

  const reset = useCallback(() => {
    setTrimStart(0);
    setTrimEnd(duration);
    setSpeed(DEFAULTS.speed);
    setVolume(DEFAULTS.volume);
    setBrightness(DEFAULTS.brightness);
    setContrast(DEFAULTS.contrast);
    setExportFormat(DEFAULTS.exportFormat);
  }, [duration]);

  useEffect(() => {
    if (duration <= 0 || duration === previousDurationRef.current) return;
    previousDurationRef.current = duration;
    setTrimStart((value) => Math.min(value, Math.max(0, duration - 0.1)));
    setTrimEnd((value) => (value <= 0 || value > duration ? duration : value));
  }, [duration]);

  return useMemo(
    () => ({
      trimStart,
      trimEnd,
      speed,
      volume,
      brightness,
      contrast,
      exportFormat,
      setTrimStart,
      setTrimEnd,
      setSpeed,
      setVolume,
      setBrightness,
      setContrast,
      setExportFormat,
      setTrimRange,
      reset,
    }),
    [trimStart, trimEnd, speed, volume, brightness, contrast, exportFormat, setTrimRange, reset]
  );
}
