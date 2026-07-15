import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from '../../src/App';
import { useAppStore } from '../../src/stores/appStore';
import { invoke } from '@tauri-apps/api/core';
import { clearMediaPreviewMemoryCache } from '../../src/hooks/useMediaPreview';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => ({ listen: vi.fn(async () => vi.fn()) }));
vi.mock('@tauri-apps/api/core', async () => ({
  invoke: vi.fn(async (command: string, args?: { path?: string; inputPath?: string; mediaType?: string; taskId?: string; forceProxy?: boolean }) => {
    if (command === 'get_video_info') {
      return { duration_secs: 120 };
    }
    if (command === 'get_media_engine_status') {
      return { ready: true, ffmpegPath: 'ffmpeg', ffprobePath: 'ffprobe', version: '8.1.1', sha256: 'abc', cacheDirectory: '/tmp', error: null };
    }
    if (command === 'prepare_media_preview') {
      const isImage = args?.mediaType === 'image';
      const isProxy = Boolean(args?.forceProxy) || /h265|\.mov$|\.mkv$/i.test(args?.inputPath || '');
      return {
        state: 'ready',
        kind: isImage ? 'image' : isProxy ? 'proxy-video' : 'direct-video',
        playbackPath: isImage
          ? '/tmp/TinyPix/previews/sample.png'
          : isProxy
            ? '/tmp/TinyPix/previews/proxy.mp4'
            : args?.inputPath,
        posterPath: isProxy ? '/tmp/TinyPix/previews/poster.jpg' : undefined,
        durationSecs: isImage ? undefined : 120,
        width: isImage ? 800 : 1920,
        height: isImage ? 600 : 1080,
        fps: isImage ? undefined : 30,
        container: isImage ? undefined : 'mp4',
        videoCodec: isImage ? undefined : isProxy ? 'hevc' : 'h264',
        audioCodec: isImage ? undefined : 'aac',
        hasAudio: isImage ? undefined : true,
        isProxy,
        taskId: args?.taskId,
      };
    }
    const path = args?.path || 'demo.mp4';
    const isImage = /\.(png|jpe?g|webp)$/i.test(path);
    return {
      file_name: path.split('/').pop() || (isImage ? 'demo.png' : 'demo.mp4'),
      extension: isImage ? 'png' : 'mp4',
      mime_type: isImage ? 'image/png' : 'video/mp4',
      size_bytes: 1024 * 1024,
    };
  }),
  convertFileSrc: (path: string) => `asset://${path}`,
}));

const createDroppedFile = (name: string, type: string, size = 1024) => {
  const file = new File(['demo'], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  Object.defineProperty(file, 'path', { value: `/Users/huashu/Movies/${name}` });
  return file;
};

describe('App workbench shell', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clearMediaPreviewMemoryCache();
    localStorage.clear();
    useAppStore.getState().clearFiles();
    useAppStore.setState({ files: [], totalSaved: 0, progress: 0, isProcessing: false });
  });

  it('opens directly on the unified video output workbench', async () => {
    render(<App />);

    expect(screen.getAllByRole('heading', { name: '视频输出' }).length).toBeGreaterThan(0);
    expect(screen.getAllByText('视频输出').length).toBeGreaterThan(0);
    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
    expect(await screen.findByText('开始输出')).toBeInTheDocument();
  });

  it('shows only the three consolidated video tools', async () => {
    render(<App />);

    const sidebar = screen.getByRole('complementary');
    ['视频输出', 'GIF 制作', '视频剪辑'].forEach((label) => {
      expect(within(sidebar).getByRole('button', { name: label })).toBeInTheDocument();
    });
    for (const label of ['视频压缩', '视频转 GIF', '视频格式转换', '提取音频']) {
      expect(within(sidebar).queryByRole('button', { name: label })).not.toBeInTheDocument();
    }
    expect(within(sidebar).queryByText('视频截图')).not.toBeInTheDocument();
    expect(within(sidebar).queryByText('图片优化')).not.toBeInTheDocument();
    expect(within(sidebar).queryByText('图片旋转')).not.toBeInTheDocument();
    expect(within(sidebar).queryByText('图片裁剪')).not.toBeInTheDocument();
    expect(screen.queryByText('视频编辑')).not.toBeInTheDocument();
    expect(screen.queryByText('文件压缩')).not.toBeInTheDocument();
    expect(await screen.findByText('引擎状态: 就绪')).toBeInTheDocument();
  });

  it('switches to the image export workbench', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '图片工具' }));

    const imageWorkbench = screen.getByRole('region', { name: '图片工具工作区' });
    expect(within(imageWorkbench).getByRole('heading', { name: '图片处理' })).toBeInTheDocument();
    expect(within(imageWorkbench).queryByRole('switch')).not.toBeInTheDocument();
  });

  it('uses one shared sidebar and top navigation in image mode', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: '图片工具' }));

    expect(screen.getAllByRole('heading', { name: 'TinyPix Pro' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: '设置' })).toHaveLength(1);
  });

  it('shows only one visible reset action in image mode', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: '图片工具' }));

    expect(screen.getAllByText('重置')).toHaveLength(1);
    expect(screen.getByRole('button', { name: '重置' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '重置图片工具' })).not.toBeInTheDocument();
  });

  it('keeps the latest top navigation free of history and batch buttons', async () => {
    render(<App />);

    expect(screen.getByRole('button', { name: '图片工具' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '视频工具' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '历史记录' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '批量处理' })).not.toBeInTheDocument();
    expect(await screen.findByText('引擎状态: 就绪')).toBeInTheDocument();
  });

  it('shows the design reset action on the video format conversion workbench', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '视频输出' }));

    expect(screen.getByRole('button', { name: '重置' })).toBeInTheDocument();
  });

  it('resets the active video workbench by clearing loaded files and restoring local controls', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '视频输出' }));
    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('demo.mov', 'video/quicktime')] },
    });
    expect((await screen.findAllByText('demo.mov')).length).toBeGreaterThan(0);

    await userEvent.click(screen.getByRole('button', { name: '重置' }));

    expect(screen.queryByText('demo.mov')).not.toBeInTheDocument();
    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
  });

  it('shows a real video player with a local poster after dropping a video on compression', async () => {
    render(<App />);

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('demo.mp4', 'video/mp4')] },
    });

    expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();
    expect(screen.getByTestId('video-preview-player')).toBeInTheDocument();
    expect(screen.getAllByText('demo.mp4').length).toBeGreaterThan(0);
    expect(invoke).toHaveBeenCalledWith('prepare_media_preview', {
      inputPath: '/Users/huashu/Movies/demo.mp4',
      mediaType: 'video',
      taskId: expect.any(String),
    });
    expect(screen.queryByText('拖拽视频文件到这里')).not.toBeInTheDocument();
  });

  it('keeps video preview visible across all three video tools once a video is loaded', async () => {
    render(<App />);

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('demo.mp4', 'video/mp4')] },
    });
    expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();

    for (const label of ['GIF 制作', '视频剪辑', '视频输出']) {
      await userEvent.click(screen.getByRole('button', { name: label }));
      expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();
      expect(screen.queryByText('拖拽视频文件到这里')).not.toBeInTheDocument();
    }
  });

  it('uses the ffmpeg-generated playable proxy for unsupported embedded codecs', async () => {
    render(<App />);

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('h265-demo.mkv', 'video/x-matroska')] },
    });

    expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();
    expect(screen.getByTestId('video-preview-player')).toHaveAttribute(
      'src',
      'asset:///tmp/TinyPix/previews/proxy.mp4'
    );
    expect(invoke).toHaveBeenCalledWith('prepare_media_preview', {
      inputPath: '/Users/huashu/Movies/h265-demo.mkv',
      mediaType: 'video',
      taskId: expect.any(String),
    });
  });

  it('regenerates a local proxy when WebView rejects an otherwise direct video', async () => {
    render(<App />);

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('direct-demo.mp4', 'video/mp4')] },
    });

    const directPlayer = await screen.findByTestId('video-preview-player');
    expect(directPlayer).toHaveAttribute('src', 'asset:///Users/huashu/Movies/direct-demo.mp4');
    fireEvent.error(directPlayer);

    await waitFor(() => {
      expect(invoke).toHaveBeenCalledWith('prepare_media_preview', expect.objectContaining({
        inputPath: '/Users/huashu/Movies/direct-demo.mp4',
        mediaType: 'video',
        forceProxy: true,
      }));
      expect(screen.getByTestId('video-preview-player')).toHaveAttribute(
        'src',
        'asset:///tmp/TinyPix/previews/proxy.mp4'
      );
    });
  });

  it('shows image file preview after adding an image in image tools', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '图片工具' }));

    act(() => {
      useAppStore.getState().addFiles([
        {
          id: 'test-img-1',
          name: 'sample.png',
          path: '/Users/huashu/Pictures/sample.png',
          format: 'PNG',
          originalSize: 1024 * 1024,
          status: 'pending',
        },
      ]);
    });

    expect((await screen.findAllByText('sample.png')).length).toBeGreaterThanOrEqual(2);
    expect(invoke).toHaveBeenCalledWith('prepare_media_preview', {
      inputPath: '/Users/huashu/Pictures/sample.png',
      mediaType: 'image',
      taskId: expect.any(String),
    });
    expect(screen.queryByText('拖拽文件到这里转换格式')).not.toBeInTheDocument();
  });

  it('uses settings as output path configuration', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '设置' }));

    expect(screen.getByRole('dialog')).toHaveTextContent('输出、媒体引擎与开源许可');
    expect(screen.getByText('选择目录')).toBeInTheDocument();
    expect(screen.getByText('跟随源文件')).toBeInTheDocument();
    expect(screen.getByText('处理完成后自动打开文件夹')).toBeInTheDocument();
    expect(screen.getByText('导出完成时自动打开输出目录')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '保存' })).toBeInTheDocument();
  });

  it('persists the output auto-open setting from the settings panel', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: '设置' }));
    await user.click(screen.getByRole('switch', { name: '处理完成后自动打开文件夹' }));

    expect(useAppStore.getState().options.openAfterProcess).toBe(true);
  });
});
