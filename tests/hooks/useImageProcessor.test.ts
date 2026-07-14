import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useImageProcessor } from '../../src/hooks/useImageProcessor';
import { useAppStore } from '../../src/stores/appStore';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';

// Mock Tauri APIs
vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(() => Promise.resolve(vi.fn())),
}));

describe('useImageProcessor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.setState({
      files: [],
      isProcessing: false,
      progress: 0,
      totalSaved: 0,
      options: {
        outputFormat: 'webp',
        quality: 85,
        resizeEnabled: true,
        resizeMaxPx: 1920,
        stripExif: true,
        outputDir: undefined,
        rotateDegrees: 0,
        openAfterProcess: false,
        cropPercent: undefined,
      },
    });
  });

  it('returns startProcess, estimateSize, estimateSizeBatch', () => {
    const { result } = renderHook(() => useImageProcessor());
    expect(result.current).toHaveProperty('startProcess');
    expect(result.current).toHaveProperty('estimateSize');
    expect(result.current).toHaveProperty('estimateSizeBatch');
  });

  it('passes image crop and rotation options to the batch processor', async () => {
    vi.mocked(invoke).mockResolvedValue([]);
    useAppStore.getState().addFiles([
      {
        id: 'img-1',
        path: '/tmp/sample.png',
        name: 'sample.png',
        format: 'PNG',
        originalSize: 1024,
        status: 'pending',
      },
    ]);
    const { result } = renderHook(() => useImageProcessor());

    await act(async () => {
      await result.current.startProcess({
        outputFormat: 'webp',
        quality: 82,
        resizeEnabled: true,
        resizeMaxPx: 1600,
        stripExif: true,
        rotateDegrees: 90,
        cropPercent: { x: 10, y: 12, width: 70, height: 60 },
      });
    });

    expect(invoke).toHaveBeenCalledWith('process_images', {
      files: [{ id: 'img-1', path: '/tmp/sample.png' }],
      options: {
        format: 'webp',
        quality: 82,
        resize_enabled: true,
        resize_max_px: 1600,
        strip_exif: true,
        output_dir: undefined,
        rotate_degrees: 90,
        crop_percent: { x: 10, y: 12, width: 70, height: 60 },
      },
    });
  });

  it('opens the configured output folder after batch processing when enabled', async () => {
    vi.mocked(invoke).mockResolvedValue([]);
    useAppStore.getState().addFiles([
      {
        id: 'img-open-dir',
        path: '/tmp/source.png',
        name: 'source.png',
        format: 'PNG',
        originalSize: 1024,
        status: 'pending',
      },
    ]);
    const { result } = renderHook(() => useImageProcessor());

    await act(async () => {
      await result.current.startProcess({
        outputFormat: 'png',
        quality: 85,
        resizeEnabled: true,
        resizeMaxPx: 1920,
        stripExif: false,
        outputDir: '/tmp/tinypix-output',
        openAfterProcess: true,
      });
    });

    expect(invoke).toHaveBeenLastCalledWith('open_folder', {
      path: '/tmp/tinypix-output',
    });
  });

  describe('exportWithCrop', () => {
    it('exists as a function on the hook return', () => {
      const { result } = renderHook(() => useImageProcessor());
      expect(typeof result.current.exportWithCrop).toBe('function');
    });

    it('does nothing when there are no eligible image files', async () => {
      const { result } = renderHook(() => useImageProcessor());
      await act(async () => {
        await result.current.exportWithCrop({
          path: '/tmp/never.png',
          crop: { x: 0, y: 0, width: 100, height: 100 },
          outputFormat: 'png',
          quality: 85,
        });
      });
      expect(invoke).not.toHaveBeenCalled();
    });

    it('invokes crop_image_cmd with converted pixel coords', async () => {
      vi.mocked(invoke).mockResolvedValue(new Uint8Array());
      useAppStore.getState().addFiles([
        {
          id: 'img-crop',
          path: '/tmp/crop.png',
          name: 'crop.png',
          format: 'PNG',
          originalSize: 4096,
          status: 'pending',
        },
      ]);
      const { result } = renderHook(() => useImageProcessor());

      // Mock HTMLImageElement to provide naturalWidth/Height for crop math
      const mockImg = {
        naturalWidth: 1000,
        naturalHeight: 500,
      } as HTMLImageElement;

      await act(async () => {
        await result.current.exportWithCrop({
          path: '/tmp/crop.png',
          crop: { x: 10, y: 20, width: 50, height: 50, unit: '%' },
          imageRef: mockImg,
          outputFormat: 'jpeg',
          quality: 90,
        });
      });

      expect(invoke).toHaveBeenCalledWith('crop_image_cmd', {
        path: '/tmp/crop.png',
        x: 100,           // 10% of 1000
        y: 100,           // 20% of 500
        width: 500,       // 50% of 1000
        height: 250,      // 50% of 500
        outputFormat: 'jpeg',
        quality: 90,
      });
    });

    it('marks the file as processing then completed on success', async () => {
      vi.mocked(invoke).mockResolvedValue(new Uint8Array());
      useAppStore.getState().addFiles([
        {
          id: 'img-flow',
          path: '/tmp/flow.png',
          name: 'flow.png',
          format: 'PNG',
          originalSize: 2048,
          status: 'pending',
        },
      ]);
      const { result } = renderHook(() => useImageProcessor());
      const mockImg = {
        naturalWidth: 800,
        naturalHeight: 600,
      } as HTMLImageElement;

      await act(async () => {
        await result.current.exportWithCrop({
          path: '/tmp/flow.png',
          crop: { x: 0, y: 0, width: 100, height: 100, unit: '%' },
          imageRef: mockImg,
          outputFormat: 'png',
          quality: 85,
        });
      });

      const finalFile = useAppStore.getState().files.find((f) => f.id === 'img-flow');
      expect(finalFile?.status).toBe('completed');
    });

    it('marks the file as error when crop_image_cmd throws', async () => {
      vi.mocked(invoke).mockRejectedValue(new Error('crop failed'));
      useAppStore.getState().addFiles([
        {
          id: 'img-err',
          path: '/tmp/err.png',
          name: 'err.png',
          format: 'PNG',
          originalSize: 1024,
          status: 'pending',
        },
      ]);
      const { result } = renderHook(() => useImageProcessor());
      const mockImg = {
        naturalWidth: 400,
        naturalHeight: 300,
      } as HTMLImageElement;

      await act(async () => {
        await result.current.exportWithCrop({
          path: '/tmp/err.png',
          crop: { x: 0, y: 0, width: 100, height: 100, unit: '%' },
          imageRef: mockImg,
          outputFormat: 'png',
          quality: 85,
        });
      });

      const finalFile = useAppStore.getState().files.find((f) => f.id === 'img-err');
      expect(finalFile?.status).toBe('error');
      expect(finalFile?.error).toContain('crop failed');
    });
  });
});

describe('estimateSizeBatch fallback', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('calls estimate_size with outputFormat key (not format) when batch command fails', async () => {
    // First invoke (estimate_size_batch) rejects to trigger the fallback path;
    // subsequent invoke (estimate_size) resolves with a numeric estimate.
    vi.mocked(invoke)
      .mockRejectedValueOnce(new Error('batch unavailable'))
      .mockResolvedValueOnce(100);

    const { result } = renderHook(() => useImageProcessor());

    await act(async () => {
      await result.current.estimateSizeBatch([
        { path: '/tmp/a.png', format: 'webp', quality: 80 },
      ]);
    });

    expect(invoke).toHaveBeenCalledWith('estimate_size', {
      path: '/tmp/a.png',
      outputFormat: 'webp',
      quality: 80,
    });
  });
});

describe('useImageProcessor cancel and event listeners', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (window as unknown as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__ = {};
    useAppStore.setState({
      files: [],
      isProcessing: false,
      progress: 0,
      totalSaved: 0,
      options: {
        outputFormat: 'webp',
        quality: 85,
        resizeEnabled: true,
        resizeMaxPx: 1920,
        stripExif: true,
        outputDir: undefined,
        rotateDegrees: 0,
        openAfterProcess: false,
        cropPercent: undefined,
      },
    });
  });

  afterEach(() => {
    delete (window as unknown as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__;
  });

  it('invokes cancel_process command when cancelProcess is called', async () => {
    vi.mocked(invoke).mockResolvedValue(undefined);
    const { result } = renderHook(() => useImageProcessor());
    await act(async () => {
      await result.current.cancelProcess();
    });
    expect(invoke).toHaveBeenCalledWith('cancel_process');
  });

  it('registers process-progress event listener on mount', async () => {
    renderHook(() => useImageProcessor());
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(listen).toHaveBeenCalledWith('process-progress', expect.any(Function));
  });

  it('registers process-complete event listener on mount', async () => {
    renderHook(() => useImageProcessor());
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(listen).toHaveBeenCalledWith('process-complete', expect.any(Function));
  });

  it('registers process-error event listener on mount', async () => {
    renderHook(() => useImageProcessor());
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(listen).toHaveBeenCalledWith('process-error', expect.any(Function));
  });

  it('registers batch-complete event listener on mount', async () => {
    renderHook(() => useImageProcessor());
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(listen).toHaveBeenCalledWith('batch-complete', expect.any(Function));
  });
});
