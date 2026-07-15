import { useCallback, useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import type { PreviewDescriptor } from '../types/media';

interface PreviewProgress {
  taskId: string;
  stage: string;
  percent: number;
}

const descriptorCache = new Map<string, PreviewDescriptor>();
const pendingCache = new Map<string, Promise<PreviewDescriptor>>();
const playbackPositions = new Map<string, number>();
const playbackSubscribers = new Map<string, Set<(value: number) => void>>();

const previewKey = (path: string, mediaType: 'image' | 'video', forceProxy: boolean) =>
  `${mediaType}:${forceProxy ? 'proxy' : 'auto'}:${path}`;

const createTaskId = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `preview-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

export const clearMediaPreviewMemoryCache = () => {
  descriptorCache.clear();
  pendingCache.clear();
  playbackPositions.clear();
  playbackSubscribers.clear();
};

const requestPreview = (
  path: string,
  mediaType: 'image' | 'video',
  taskId: string,
  forceProxy: boolean
) => {
  const key = previewKey(path, mediaType, forceProxy);
  const existing = pendingCache.get(key);
  if (existing) return existing;
  const request = Promise.resolve(invoke<PreviewDescriptor>('prepare_media_preview', {
    inputPath: path,
    mediaType,
    taskId,
    ...(forceProxy ? { forceProxy: true } : {}),
  }))
    .then((descriptor) => {
      descriptorCache.set(key, descriptor);
      return descriptor;
    })
    .finally(() => {
      pendingCache.delete(key);
    });
  pendingCache.set(key, request);
  return request;
};

export function useMediaPreview(path: string | undefined, mediaType: 'image' | 'video') {
  const [forceProxy, setForceProxy] = useState(false);
  const key = path ? previewKey(path, mediaType, forceProxy) : '';
  const [descriptor, setDescriptor] = useState<PreviewDescriptor | null>(() =>
    path ? descriptorCache.get(previewKey(path, mediaType, false)) ?? null : null
  );
  const [progress, setProgress] = useState(0);
  const [attempt, setAttempt] = useState(0);
  const [playbackPosition, setPlaybackPositionState] = useState(() =>
    path ? playbackPositions.get(path) ?? 0 : 0
  );
  const taskIdRef = useRef('');

  useEffect(() => {
    if (!path) {
      setDescriptor(null);
      setProgress(0);
      taskIdRef.current = '';
      return;
    }
    let active = true;
    const cached = descriptorCache.get(key);
    if (cached && attempt === 0) {
      setDescriptor(cached);
      setProgress(cached.state === 'ready' ? 100 : 0);
      taskIdRef.current = cached.taskId ?? '';
      return;
    }
    const taskId = createTaskId();
    taskIdRef.current = taskId;
    setDescriptor({
      state: 'probing',
      kind: mediaType === 'image' ? 'image' : 'proxy-video',
      isProxy: false,
      taskId,
    });
    setProgress(0);
    void requestPreview(path, mediaType, taskId, forceProxy)
      .then((next) => {
        if (!active) return;
        taskIdRef.current = next.taskId ?? taskId;
        setDescriptor(next);
        setProgress(next.state === 'ready' ? 100 : 0);
      })
      .catch(() => {
        if (!active) return;
        setDescriptor({
          state: 'error',
          kind: mediaType === 'image' ? 'image' : 'proxy-video',
          isProxy: false,
          taskId,
          error: {
            failureType: 'preview-bridge-failed',
            message: '无法连接本地预览服务，请重试。',
            retryable: true,
          },
        });
      });
    return () => {
      active = false;
    };
  }, [attempt, forceProxy, key, mediaType, path]);

  useEffect(() => {
    let unlisten: (() => void) | undefined;
    let active = true;
    void listen<PreviewProgress>('preview-progress', (event) => {
      if (!active || event.payload.taskId !== taskIdRef.current) return;
      setProgress(Math.max(0, Math.min(100, event.payload.percent)));
      setDescriptor((current) => current
        ? { ...current, state: event.payload.stage === 'probing' ? 'probing' : 'generating' }
        : current);
    }).then((cleanup) => {
      if (active) unlisten = cleanup;
      else cleanup();
    }).catch(() => {
      // Browser-only development and tests may not expose Tauri's event bridge.
    });
    return () => {
      active = false;
      unlisten?.();
    };
  }, [path]);

  const retry = useCallback(() => {
    if (!path) return;
    descriptorCache.delete(key);
    pendingCache.delete(key);
    setAttempt((value) => value + 1);
  }, [key, path]);

  const useProxy = useCallback(() => {
    if (!path || mediaType !== 'video') return;
    const proxyKey = previewKey(path, mediaType, true);
    descriptorCache.delete(proxyKey);
    pendingCache.delete(proxyKey);
    setForceProxy(true);
    setAttempt((value) => value + 1);
  }, [mediaType, path]);

  const cancel = useCallback(async () => {
    const taskId = taskIdRef.current;
    if (!taskId) return false;
    return invoke<boolean>('cancel_preview_task', { taskId });
  }, []);

  const setPlaybackPosition = useCallback((value: number) => {
    if (!path) return;
    playbackPositions.set(path, value);
    setPlaybackPositionState(value);
    playbackSubscribers.get(path)?.forEach((subscriber) => subscriber(value));
  }, [path]);

  useEffect(() => {
    setPlaybackPositionState(path ? playbackPositions.get(path) ?? 0 : 0);
    setForceProxy(false);
  }, [path]);

  useEffect(() => {
    if (!path) return;
    const subscribers = playbackSubscribers.get(path) ?? new Set<(value: number) => void>();
    subscribers.add(setPlaybackPositionState);
    playbackSubscribers.set(path, subscribers);
    return () => {
      subscribers.delete(setPlaybackPositionState);
      if (subscribers.size === 0) playbackSubscribers.delete(path);
    };
  }, [path]);

  return {
    descriptor,
    progress,
    retry,
    forceProxy: useProxy,
    cancel,
    playbackPosition,
    setPlaybackPosition,
  };
}
