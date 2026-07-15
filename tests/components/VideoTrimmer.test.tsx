import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { invoke } from '@tauri-apps/api/core';
import { open } from '@tauri-apps/plugin-dialog';
import VideoTrimmer from '../../src/components/video/VideoTrimmer';
import { useAppStore } from '../../src/stores/appStore';
import { clearMediaPreviewMemoryCache } from '../../src/hooks/useMediaPreview';

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(async () => vi.fn()),
}));

vi.mock('@tauri-apps/plugin-dialog', () => ({
  open: vi.fn(async () => undefined),
}));

vi.mock('@tauri-apps/api/core', () => ({
  convertFileSrc: (path: string) => `asset://${path}`,
  invoke: vi.fn(async (command: string, args?: { taskId?: string }) => {
    if (command === 'get_video_info') return { duration_secs: 120 };
    if (command === 'prepare_media_preview') return {
      state: 'ready',
      kind: 'proxy-video',
      playbackPath: '/tmp/TinyPix/previews/demo-proxy.mp4',
      posterPath: '/tmp/TinyPix/previews/demo-poster.jpg',
      durationSecs: 120,
      width: 960,
      height: 540,
      fps: 30,
      container: 'mp4',
      videoCodec: 'h264',
      audioCodec: 'aac',
      hasAudio: true,
      isProxy: true,
      taskId: args?.taskId,
    };
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
    clearMediaPreviewMemoryCache();
    useAppStore.setState({ files: [videoFile] });
  });

  it('renders one center preview and one parameter panel', async () => {
    render(<VideoTrimmer />);

    expect(await screen.findByRole('button', { name: '片段 1 保留' }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getAllByText('片段属性')).toHaveLength(1);
    expect(screen.getAllByText('导出设置')).toHaveLength(1);
    expect(screen.getAllByRole('region', { name: '视频剪辑工作区' })).toHaveLength(1);
  });

  it('organizes the editor as a media bin, preview workspace, inspector, and timeline', async () => {
    render(<VideoTrimmer />);

    expect(await screen.findByRole('region', { name: '项目素材' })).toBeInTheDocument();
    expect(screen.getByText('源视频')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '添加素材' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: '视频预览' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: '单轨时间线' })).toBeInTheDocument();
  });

  it('shows an add-video drop zone when the trim workspace has no video', () => {
    useAppStore.setState({ files: [] });

    render(<VideoTrimmer />);

    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /选择本地视频/ })).toBeInTheDocument();
    expect(screen.queryByText('请添加视频文件')).not.toBeInTheDocument();
  });

  it('opens the local video picker from the media bin', async () => {
    render(<VideoTrimmer />);

    fireEvent.click(await screen.findByRole('button', { name: '添加素材' }));

    await waitFor(() => expect(open).toHaveBeenCalledWith({
      multiple: false,
      filters: [{ name: 'Videos', extensions: ['mp4', 'mov', 'mkv', 'avi', 'webm'] }],
    }));
  });
});

describe('VideoTrimmer interactions', () => {
  beforeEach(() => {
    clearMediaPreviewMemoryCache();
    useAppStore.setState({ files: [videoFile] });
    vi.mocked(invoke).mockClear();
  });

  it('renders the video player element when a video is loaded', async () => {
    render(<VideoTrimmer />);
    expect(screen.getByTestId('video-preview-player')).toBeInTheDocument();
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('prepare_media_preview', {
        inputPath: '/tmp/demo.mp4',
        mediaType: 'video',
        taskId: expect.any(String),
      });
    });
    expect(screen.getByTestId('video-preview-player')).toHaveAttribute(
      'src',
      'asset:///tmp/TinyPix/previews/demo-proxy.mp4'
    );
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

  it('renders a real single-track timeline with playhead and segment state', async () => {
    render(<VideoTrimmer />);
    expect(await screen.findByRole('region', { name: '单轨时间线' })).toBeInTheDocument();
    expect(screen.getByLabelText('时间线播放头')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '片段 1 保留' })).toBeInTheDocument();
  });

  it('renders keyboard-accessible in/out fields and the precise-boundary option', async () => {
    render(<VideoTrimmer />);
    await waitFor(() => {
      expect(screen.getByLabelText('入点（秒）')).toBeInTheDocument();
    });
    expect(screen.getByLabelText('出点（秒）')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /精确边界/ })).toBeInTheDocument();
  });

  it('provides the complete single-track editing toolbar', async () => {
    render(<VideoTrimmer />);
    for (const name of [
      '到开头', '上一帧', '播放/暂停', '下一帧', '到结尾',
      '设置入点', '设置出点', '分割片段', '删除选中片段',
      '撤销', '重做', '播放所选', '缩小时间线', '放大时间线', '适配全片',
    ]) {
      expect(await screen.findByRole('button', { name })).toBeInTheDocument();
    }
  });

  it('splits, deletes and restores timeline segments without changing the source file', async () => {
    render(<VideoTrimmer />);
    await waitFor(() => expect(screen.getByLabelText('视频播放进度')).not.toBeDisabled());
    fireEvent.change(screen.getByLabelText('视频播放进度'), { target: { value: '60' } });
    fireEvent.click(screen.getByRole('button', { name: '分割片段' }));
    expect(screen.getAllByRole('button', { name: /片段 \d/ })).toHaveLength(2);

    fireEvent.click(screen.getByRole('button', { name: '删除选中片段' }));
    expect(screen.getByText('已排除')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '撤销' }));
    expect(screen.queryByText('已排除')).not.toBeInTheDocument();
  });

  it('shows an enabled export button when a video is loaded', async () => {
    render(<VideoTrimmer />);
    const exportBtn = await screen.findByRole('button', { name: /合并导出/ });
    await waitFor(() => expect(exportBtn).not.toBeDisabled());
  });

  it('calls get_video_info with the video path after a video is loaded', async () => {
    render(<VideoTrimmer />);
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: '/tmp/demo.mp4' });
    });
  });

  it('uses lossless multi-segment export by default', async () => {
    render(<VideoTrimmer />);
    // 等待 get_video_info 完成，使 trimEnd 更新为 120
    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('get_video_info', { path: '/tmp/demo.mp4' });
    });

    const exportBtn = await screen.findByRole('button', { name: /合并导出/ });
    await waitFor(() => expect(exportBtn).not.toBeDisabled());
    fireEvent.click(exportBtn);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith(
        'export_video_edit',
        expect.objectContaining({
          inputPath: '/tmp/demo.mp4',
          mode: 'lossless',
          segments: [expect.objectContaining({ startSecs: 0, endSecs: 120, included: true })],
        })
      );
    });
  });
});
