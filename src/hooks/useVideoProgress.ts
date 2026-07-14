import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import { useEffect, useRef } from 'react';

interface VideoProgress {
  current_secs: number;
  total_secs: number;
  progress_pct: number;
}

const isTauriAvailable = () =>
  typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;

export function useVideoProgress(onProgress?: (pct: number) => void) {
  const unlistenRef = useRef<UnlistenFn[]>([]);

  useEffect(() => {
    if (!isTauriAvailable()) return;

    const setup = async () => {
      try {
        const unlisten = await listen<VideoProgress>('video-progress', (event) => {
          onProgress?.(event.payload.progress_pct);
        });
        unlistenRef.current.push(unlisten);
      } catch {
        // Tauri not available (test/dev environment)
      }
    };
    setup();
    return () => {
      unlistenRef.current.forEach((u) => u());
    };
  }, [onProgress]);
}
