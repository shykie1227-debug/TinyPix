import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import { useCallback, useEffect, useRef } from 'react';
import { useAppStore } from '../stores/appStore';
import { isImageFormat } from '../utils/mediaFormat';

interface ProcessPayload {
  id: string;
  status: 'processing' | 'completed' | 'error';
  outputPath?: string;
  outputSize?: number;
  error?: string;
  savedBytes?: number;
}

interface ProgressPayload {
  current: number;
  total: number;
}

interface BatchCompletePayload {
  results: Array<{
    input_path: string;
    output_path: string;
    original_size: number;
    new_size: number;
    saved_bytes: number;
    success: boolean;
    error?: string;
  }>;
  total_saved: number;
}

interface SizeEstimate {
  original_bytes: number;
  estimated_bytes: number;
  label: string;
}

const isTauriAvailable = () =>
  typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window;

export function useImageProcessor() {
  const { files, updateFile, setProcessing, setProgress, addSaved, resetSaved } = useAppStore();
  const unlistenRef = useRef<UnlistenFn[]>([]);
  const processPromiseRef = useRef<Promise<unknown> | null>(null);

  // Listen for backend events
  useEffect(() => {
    if (!isTauriAvailable()) return;

    const setup = async () => {
      try {
        const unlistenProgress = await listen<ProgressPayload>('process-progress', (event) => {
          const pct = Math.round((event.payload.current / event.payload.total) * 100);
          setProgress(pct);
        });
        unlistenRef.current.push(unlistenProgress);

        const unlistenComplete = await listen<ProcessPayload>('process-complete', (event) => {
          const { id, status, outputPath, outputSize, error, savedBytes } = event.payload;
          updateFile(id, {
            status,
            outputPath,
            outputSize,
            error,
          });
          if (savedBytes) addSaved(savedBytes);
        });
        unlistenRef.current.push(unlistenComplete);

        const unlistenError = await listen<{ id: string; error: string }>('process-error', (event) => {
          updateFile(event.payload.id, {
            status: 'error',
            error: event.payload.error,
          });
        });
        unlistenRef.current.push(unlistenError);

        const unlistenBatchComplete = await listen<BatchCompletePayload>('batch-complete', (_event) => {
          setProcessing(false);
          setProgress(100);
          processPromiseRef.current = null;
        });
        unlistenRef.current.push(unlistenBatchComplete);
      } catch {
        // Tauri event bridge is unavailable in plain browser previews.
      }
    };

    setup();

    return () => {
      unlistenRef.current.forEach((unlisten) => unlisten());
    };
  }, [updateFile, setProcessing, setProgress, addSaved]);

  const startProcess = useCallback(
    async (options: {
      outputFormat: string;
      quality: number;
      resizeEnabled: boolean;
      resizeMaxPx: number;
      stripExif: boolean;
      outputDir?: string;
      openAfterProcess?: boolean;
      rotateDegrees?: 0 | 90 | 180 | 270;
      cropPercent?: {
        x: number;
        y: number;
        width: number;
        height: number;
      };
      preserveTransparency?: boolean;
      opacityPercent?: number;
      resizeTargetW?: number;
      resizeTargetH?: number;
      colorAdjust?: {
        brightness: number;
        contrast: number;
        saturation: number;
        sharpness: number;
      };
      flipH?: boolean;
      flipV?: boolean;
    }) => {
      if (files.length === 0) return;
      const targetFiles = files.filter(
        (file) => file.status === 'pending' && isImageFormat(file.format)
      );
      if (targetFiles.length === 0) return;

      setProcessing(true);
      setProgress(0);
      resetSaved();

      // Mark all as processing
      targetFiles.forEach((f) => updateFile(f.id, { status: 'processing' }));

      // Store promise so cancelProcess() can await it
      const promise = invoke('process_images', {
        files: targetFiles.map((f) => ({ id: f.id, path: f.path })),
        options: {
          format: options.outputFormat,
          quality: options.quality,
          resize_enabled: options.resizeEnabled,
          resize_max_px: options.resizeMaxPx,
          strip_exif: options.stripExif,
          output_dir: options.outputDir,
          rotate_degrees: options.rotateDegrees ?? 0,
          crop_percent: options.cropPercent,
          preserve_transparency: options.preserveTransparency ?? true,
          opacity_percent: options.opacityPercent ?? 100,
          resize_target_width: options.resizeTargetW,
          resize_target_height: options.resizeTargetH,
          color_adjust: options.colorAdjust ?? {
            brightness: 0,
            contrast: 0,
            saturation: 0,
            sharpness: 0,
          },
          flip_h: options.flipH ?? false,
          flip_v: options.flipV ?? false,
        },
      }).catch((err) => {
        targetFiles.forEach((f) =>
          updateFile(f.id, { status: 'error', error: String(err) })
        );
        setProcessing(false);
        setProgress(0);
        processPromiseRef.current = null;
      });
      processPromiseRef.current = promise;
      await promise;
      if (options.openAfterProcess && options.outputDir?.trim()) {
        await invoke('open_folder', { path: options.outputDir });
      }
    },
    [files, updateFile, setProcessing, setProgress, resetSaved]
  );

  /** 取消正在进行的批处理 */
  const cancelProcess = useCallback(async () => {
    try {
      await invoke('cancel_process');
    } catch (err) {
      console.warn('cancel_process error:', err);
    }
    setProcessing(false);
    setProgress(0);
    processPromiseRef.current = null;
  }, [setProcessing, setProgress]);

  const estimateSize = useCallback(
    async (path: string, format: string, quality: number): Promise<number> => {
      try {
        const estimate = await invoke<SizeEstimate>('estimate_size', {
          path,
          outputFormat: format,
          quality,
        });
        return estimate.estimated_bytes;
      } catch {
        return 0;
      }
    },
    []
  );

  const estimateSizeBatch = useCallback(
    async (
      items: Array<{ path: string; format: string; quality: number }>
    ): Promise<number> => {
      if (items.length === 0) return 0;
      try {
        const total = await invoke<number>('estimate_size_batch', { files: items });
        return total;
      } catch {
        let sum = 0;
        for (const f of items) {
          try {
            sum += await invoke<number>('estimate_size', {
              path: f.path,
              outputFormat: f.format,
              quality: f.quality,
            });
          } catch {
            // skip
          }
        }
        return sum;
      }
    },
    []
  );

  /**
   * 编辑模式导出：基于 react-image-crop 的百分比区域调用 crop_image_cmd
   *
   * 行为约定：
   * - 仅处理 file.path 与传入 path 匹配、status='pending' 的文件
   * - 文件状态：pending → processing → completed/error
   * - 失败时 file.error 写入错误信息
   * - 全程更新 store.isProcessing / store.progress 以驱动 UI
   */
  interface ExportWithCropArgs {
    path: string;
    crop: { x: number; y: number; width: number; height: number; unit?: '%' | 'px' };
    imageRef?: HTMLImageElement | null;
    outputFormat: string;
    quality: number;
  }

  const exportWithCrop = useCallback(
    async (args: ExportWithCropArgs) => {
      const target = files.find(
        (f) => f.path === args.path && f.status === 'pending' && isImageFormat(f.format)
      );
      if (!target) return;

      setProcessing(true);
      setProgress(0);
      resetSaved();
      updateFile(target.id, { status: 'processing' });

      try {
        if (args.crop && args.crop.width > 0 && args.crop.height > 0 && args.imageRef) {
          const scaleX = args.imageRef.naturalWidth / 100;
          const scaleY = args.imageRef.naturalHeight / 100;
          const x = Math.round(args.crop.x * scaleX);
          const y = Math.round(args.crop.y * scaleY);
          const width = Math.round(args.crop.width * scaleX);
          const height = Math.round(args.crop.height * scaleY);

          await invoke('crop_image_cmd', {
            path: args.path,
            x,
            y,
            width,
            height,
            outputFormat: args.outputFormat,
            quality: args.quality,
          });
        }

        setProgress(100);
        updateFile(target.id, { status: 'completed' });
      } catch (err) {
        updateFile(target.id, { status: 'error', error: String(err) });
      } finally {
        setProcessing(false);
      }
    },
    [files, updateFile, setProcessing, setProgress, resetSaved]
  );

  return { startProcess, cancelProcess, estimateSize, estimateSizeBatch, exportWithCrop };
}
