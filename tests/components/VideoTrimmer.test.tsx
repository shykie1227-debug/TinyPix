import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { invoke } from '@tauri-apps/api/core';
import VideoTrimmer from '../../src/components/video/VideoTrimmer';
import { useAppStore } from '../../src/stores/appStore';

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(async () => vi.fn()),
}));

vi.mock('@tauri-apps/api/core', () => ({
  convertFileSrc: (path: string) => `asset://${path}`,
  invoke: vi.fn(async (command: string) => {
    if (command === 'get_video_info') return { duration_secs: 120 };
    if (command === 'create_video_preview') return '/tmp/tinypix-preview/demo.png';
    return {
      output_path: '/tmp/demo_edited.mp4',
      original_size: 1024,
      output_size: 512,
      saved_bytes: 512,
      processing_time_secs: 1,
    };
  }),
}));

const videoFile = {
  id: 'video-1',
  path: '/tmp/demo.mp4',
  name: 'demo.mp4',
  format: 'MP4',
  originalSize: 1024,
  status: 'pending' as const,
};

describe('VideoTrimmer workspace', () => {
  beforeEach(() => {
    useAppStore.setState({ files: [videoFile] });
  });

  it('renders one center preview and one parameter panel', async () => {
    render(<VideoTrimmer />);

    await waitFor(() => {
      expect(screen.getByText('入点: 00:00:00')).toBeInTheDocument();
    });
    expect(screen.getAllByText('片段属性')).toHaveLength(1);
    expect(screen.getAllByText('导出设置')).toHaveLength(1);
    expect(screen.getAllByRole('region', { name: '视频剪辑工作区' })).toHaveLength(1);
  });

  it('shows an add-video drop zone when the trim workspace has no video', () => {
    useAppStore.setState({ files: [] });

    render(<VideoTrimmer />);

    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /选择本地视频/ })).toBeInTheDocument();
    expect(screen.queryByText('请添加视频文件')).not.toBeInTheDocument();
  });
});

describe('VideoTrimmer interactions', () => {
  beforeEach(() => {
    useAppStore.setState({ files: [videoFile] });
    vi.mocked(invoke).mockClear();
  });

  it('renders the video player element when a video is loaded', async () => {
    render(<VideoTrimmer />);
    expect(screen.getByTestId('video-preview-player')).toBeInTheDocument();
    // 等待 get_video_info 异步 effect settle，避免 act 警告
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: '/tmp/demo.mp4' });
    });
  });

  it('does not render the video player when no video is present', () => {
    useAppStore.setState({ files: [] });
    render(<VideoTrimmer />);
    expect(screen.queryByTestId('video-preview-player')).not.toBeInTheDocument();
  });

  it('renders trim timeline labels for in/out/selection points', async () => {
    render(<VideoTrimmer />);
    await waitFor(() => {
      expect(screen.getByText(/入点:/)).toBeInTheDocument();
    });
    expect(screen.getByText(/出点:/)).toBeInTheDocument();
    expect(screen.getByText(/选中:/)).toBeInTheDocument();
  });

  it('renders parameter sliders for speed, volume, brightness and contrast', async () => {
    render(<VideoTrimmer />);
    await waitFor(() => {
      expect(screen.getByText('播放速度')).toBeInTheDocument();
    });
    expect(screen.getByText('音量')).toBeInTheDocument();
    expect(screen.getByText('亮度')).toBeInTheDocument();
    expect(screen.getByText('对比度')).toBeInTheDocument();
    expect(screen.getAllByRole('slider').length).toBeGreaterThanOrEqual(4);
  });

  it('shows an enabled export button when a video is loaded', async () => {
    render(<VideoTrimmer />);
    const exportBtn = await screen.findByRole('button', { name: /开始渲染导出/ });
    expect(exportBtn).not.toBeDisabled();
  });

  it('calls get_video_info with the video path after a video is loaded', async () => {
    render(<VideoTrimmer />);
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: '/tmp/demo.mp4' });
    });
  });

  it('calls edit_and_export_video with mapped parameters when exporting', async () => {
    render(<VideoTrimmer />);
    // 等待 get_video_info 完成，使 trimEnd 更新为 120
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: '/tmp/demo.mp4' });
    });

    const exportBtn = await screen.findByRole('button', { name: /开始渲染导出/ });
    fireEvent.click(exportBtn);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'edit_and_export_video',
        expect.objectContaining({
          inputPath: '/tmp/demo.mp4',
          outputPath: '/tmp/demo_edited.mp4',
          startSecs: 0,
          endSecs: 120,
          speed: 1,
          format: 'mp4',
        })
      );
    });
  });
});
