import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import ImageWorkbench from '../../src/components/image/ImageWorkbench';
import { useAppStore } from '../../src/stores/appStore';
import { clearMediaPreviewMemoryCache } from '../../src/hooks/useMediaPreview';
import { invoke } from '@tauri-apps/api/core';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => ({ listen: vi.fn(async () => vi.fn()) }));
vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(async (command: string, args?: { inputPath?: string; taskId?: string }) => {
    if (command === 'prepare_media_preview') {
      return {
        state: 'ready',
        kind: 'image',
        playbackPath: '/tmp/TinyPix/previews/image.png',
        width: 800,
        height: 600,
        isProxy: false,
        taskId: args?.taskId,
      };
    }
    return [];
  }),
  convertFileSrc: vi.fn((path: string) => `file://${path}`),
}));

describe('ImageWorkbench single-panel workflow', () => {
  beforeEach(() => {
    clearMediaPreviewMemoryCache();
    localStorage.clear();
    useAppStore.getState().clearFiles();
    useAppStore.getState().resetWorkspaceOptions();
  });

  it('renders one image processing workspace without a mode switch', () => {
    render(<ImageWorkbench />);

    expect(screen.getByRole('heading', { name: '图片处理' })).toBeInTheDocument();
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
    for (const section of ['格式与质量', '裁切与方向', '尺寸', '色彩调整', '隐私与透明度']) {
      expect(screen.getByText(section)).toBeInTheDocument();
    }
    expect(screen.getAllByRole('button', { name: '开始处理' })).toHaveLength(1);
  });

  it('updates rotation and both flip options directly from the single panel', () => {
    render(<ImageWorkbench />);
    fireEvent.click(screen.getByRole('button', { name: '右旋 90°' }));
    fireEvent.click(screen.getByRole('button', { name: '水平镜像' }));
    fireEvent.click(screen.getByRole('button', { name: '垂直镜像' }));

    expect(useAppStore.getState().options.rotateDegrees).toBe(90);
    expect(useAppStore.getState().options.flipH).toBe(true);
    expect(useAppStore.getState().options.flipV).toBe(true);
  });

  it('stores exact size, color and real BMP output options', () => {
    render(<ImageWorkbench />);
    fireEvent.click(screen.getByRole('radio', { name: 'BMP' }));
    fireEvent.change(screen.getByLabelText('精确宽度'), { target: { value: '800' } });
    fireEvent.change(screen.getByLabelText('精确高度'), { target: { value: '600' } });
    fireEvent.change(screen.getByLabelText('亮度'), { target: { value: '20' } });
    fireEvent.change(screen.getByLabelText('锐化'), { target: { value: '35' } });

    expect(useAppStore.getState().options.outputFormat).toBe('bmp');
    expect(useAppStore.getState().options.resizeTargetW).toBe(800);
    expect(useAppStore.getState().options.resizeTargetH).toBe(600);
    expect(useAppStore.getState().options.colorAdjust).toEqual({
      brightness: 20,
      contrast: 0,
      saturation: 0,
      sharpness: 35,
    });
  });

  it('removes a queued image from the workspace', () => {
    useAppStore.getState().addFiles([
      {
        id: 'png-remove',
        path: '/tmp/sample.png',
        name: 'sample.png',
        format: 'PNG',
        originalSize: 1024,
        status: 'pending',
      },
    ]);
    render(<ImageWorkbench />);

    fireEvent.click(screen.getByRole('button', { name: '移除 sample.png' }));
    expect(useAppStore.getState().files).toHaveLength(0);
  });

  it('stores opacity as an export parameter', () => {
    render(<ImageWorkbench />);
    fireEvent.change(screen.getByRole('slider', { name: '透明度' }), { target: { value: '45' } });
    expect(useAppStore.getState().options.opacityPercent).toBe(45);
  });

  it('keeps crop unset when an image is loaded until the user changes the crop selection', async () => {
    useAppStore.getState().addFiles([
      {
        id: 'png-full-frame',
        path: '/tmp/full-frame.png',
        name: 'full-frame.png',
        format: 'PNG',
        originalSize: 2048,
        status: 'pending',
      },
    ]);

    render(<ImageWorkbench />);

    await waitFor(() => {
      expect(screen.getByTestId('image-crop-preview')).toBeInTheDocument();
    });
    expect(useAppStore.getState().options.cropPercent).toBeUndefined();
  });

  it('renders rotation, mirror, color, sharpness and opacity through the canvas preview', async () => {
    useAppStore.getState().addFiles([
      {
        id: 'png-canvas',
        path: '/tmp/canvas.png',
        name: 'canvas.png',
        format: 'PNG',
        originalSize: 4096,
        status: 'pending',
      },
    ]);
    render(<ImageWorkbench />);
    fireEvent.click(screen.getByRole('button', { name: '右旋 90°' }));
    fireEvent.click(screen.getByRole('button', { name: '水平镜像' }));
    fireEvent.change(screen.getByLabelText('亮度'), { target: { value: '25' } });
    fireEvent.change(screen.getByLabelText('锐化'), { target: { value: '40' } });
    fireEvent.change(screen.getByRole('slider', { name: '透明度' }), { target: { value: '55' } });

    const canvas = await screen.findByTestId('image-crop-preview');
    expect(canvas.tagName).toBe('CANVAS');
    expect(canvas).toHaveAttribute('data-preview-rotation', '90');
    expect(canvas).toHaveAttribute('data-preview-flip-h', 'true');
    expect(canvas).toHaveAttribute('data-preview-brightness', '25');
    expect(canvas).toHaveAttribute('data-preview-sharpness', '40');
    expect(canvas).toHaveAttribute('data-preview-opacity', '55');
  });

  it('lets the user choose the current preview image while keeping batch parameters shared', async () => {
    useAppStore.getState().addFiles([
      { id: 'first', path: '/tmp/first.png', name: 'first.png', format: 'PNG', originalSize: 10, status: 'pending' },
      { id: 'second', path: '/tmp/second.png', name: 'second.png', format: 'PNG', originalSize: 20, status: 'pending' },
    ]);
    render(<ImageWorkbench />);

    expect(await screen.findByText('当前预览')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '预览 second.png' }));

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('prepare_media_preview', {
        inputPath: '/tmp/second.png',
        mediaType: 'image',
        taskId: expect.any(String),
      });
    });
    expect(screen.getByRole('button', { name: '预览 second.png' })).toHaveAttribute('aria-pressed', 'true');
  });
});
