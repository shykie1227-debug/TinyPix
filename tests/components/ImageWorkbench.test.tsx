import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import ImageWorkbench from '../../src/components/image/ImageWorkbench';
import { useAppStore } from '../../src/stores/appStore';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
  convertFileSrc: vi.fn((path: string) => `file://${path}`),
}));

describe('ImageWorkbench mode switcher', () => {
  beforeEach(() => {
    // 每次测试前重置 store
    useAppStore.getState().clearFiles();
    useAppStore.setState({ files: [] });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders the toggle and sidebar brand', () => {
    render(<ImageWorkbench />);
    expect(screen.getByText('图片导出')).toBeInTheDocument();
    expect(screen.getByRole('switch')).toBeInTheDocument();
  });

  it('shows toast when switching modes removes incompatible files', () => {
    useAppStore.getState().addFiles([
      {
        id: 'psd-1',
        path: '/tmp/sample.psd',
        name: 'sample.psd',
        format: 'PSD',
        originalSize: 1024,
        status: 'pending',
      },
    ]);

    render(<ImageWorkbench />);
    // 切换到编辑模式（编辑模式不支持 PSD）
    fireEvent.click(screen.getByRole('switch'));

    expect(
      screen.getByText(/已移除 1 个不兼容当前模式的文件/)
    ).toBeInTheDocument();
  });

  it('does not show toast when no incompatible files', () => {
    useAppStore.getState().addFiles([
      {
        id: 'png-1',
        path: '/tmp/sample.png',
        name: 'sample.png',
        format: 'PNG',
        originalSize: 1024,
        status: 'pending',
      },
    ]);

    render(<ImageWorkbench />);
    fireEvent.click(screen.getByRole('switch'));

    expect(
      screen.queryByText(/已移除/)
    ).not.toBeInTheDocument();
  });
});

describe('ImageWorkbench rotation, flip and file removal', () => {
  beforeEach(() => {
    useAppStore.getState().clearFiles();
    useAppStore.setState({ files: [] });
    // 重置旋转/镜像相关 options，避免 persist 残留影响断言
    useAppStore.setState((state) => ({
      options: {
        ...state.options,
        rotateDegrees: 0,
        flipH: false,
        flipV: false,
      },
    }));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('updates rotateDegrees to 90 when clicking rotate-right in edit mode', () => {
    render(<ImageWorkbench />);
    // 切换到编辑模式（默认导出模式不渲染 RotateFlipBar）
    fireEvent.click(screen.getByRole('switch'));
    // 点击 右旋 90° 按钮
    fireEvent.click(screen.getByRole('button', { name: '右旋 90°' }));

    expect(useAppStore.getState().options.rotateDegrees).toBe(90);
  });

  it('toggles flipH to true when clicking horizontal mirror in edit mode', () => {
    render(<ImageWorkbench />);
    fireEvent.click(screen.getByRole('switch'));
    fireEvent.click(screen.getByRole('button', { name: '水平镜像' }));

    expect(useAppStore.getState().options.flipH).toBe(true);
  });

  it('toggles flipV to true when clicking vertical mirror in edit mode', () => {
    render(<ImageWorkbench />);
    fireEvent.click(screen.getByRole('switch'));
    fireEvent.click(screen.getByRole('button', { name: '垂直镜像' }));

    expect(useAppStore.getState().options.flipV).toBe(true);
  });

  it('removes the file when clicking the remove button', () => {
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
});
