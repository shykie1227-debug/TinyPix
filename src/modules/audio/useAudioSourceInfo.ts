/**
 * useAudioSourceInfo Hook
 *
 * 职责：封装 M1 (AudioFileInspector) 的调用，对外提供 React 友好的状态机。
 *
 * 状态机：
 * - idle: 未提供 inputPath
 * - loading: invoke 中
 * - ready: 已获取源信息
 * - error: 后端失败（仍可继续操作，仅失去智能提示能力）
 */

import { useEffect, useState } from 'react';
import { AudioFileInspector } from './inspector';
import type { AudioSourceInfo } from './inspector';

export type AudioSourceState = 'idle' | 'loading' | 'ready' | 'error';

export interface UseAudioSourceInfoResult {
  state: AudioSourceState;
  info: AudioSourceInfo | null;
  error: string | null;
  reload: () => void;
}

export function useAudioSourceInfo(inputPath: string | null): UseAudioSourceInfoResult {
  const [state, setState] = useState<AudioSourceState>('idle');
  const [info, setInfo] = useState<AudioSourceInfo | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!inputPath) {
      setState('idle');
      setInfo(null);
      setError(null);
      return;
    }

    let cancelled = false;
    setState('loading');
    setError(null);

    new AudioFileInspector()
      .inspect(inputPath)
      .then((data) => {
        if (cancelled) return;
        setInfo(data);
        setState('ready');
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : String(err));
        setState('error');
      });

    return () => {
      cancelled = true;
    };
  }, [inputPath, reloadKey]);

  return {
    state,
    info,
    error,
    reload: () => setReloadKey((k) => k + 1),
  };
}
