import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from '../../src/App';
import { useAppStore } from '../../src/stores/appStore';
import { invoke } from '@tauri-apps/api/core';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => ({ listen: vi.fn(async () => vi.fn()) }));
vi.mock('@tauri-apps/api/core', async () => ({
  invoke: vi.fn(async (command: string, args?: { path?: string }) => {
    if (command === 'get_video_info') {
      return { duration_secs: 120 };
    }
    if (command === 'create_video_preview') {
      return '/tmp/tinypix-preview/demo.png';
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
    localStorage.clear();
    useAppStore.getState().clearFiles();
    useAppStore.setState({ files: [], totalSaved: 0, progress: 0, isProcessing: false });
  });

  it('opens directly on the video compression workbench', async () => {
    render(<App />);

    expect(screen.getAllByRole('heading', { name: '视频压缩' }).length).toBeGreaterThan(0);
    expect(screen.getAllByText('视频压缩').length).toBeGreaterThan(0);
    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
    expect(await screen.findByText('开始极速压缩')).toBeInTheDocument();
  });

  it('shows only the five video tools from the latest UI guide', () => {
    render(<App />);

    const sidebar = screen.getByRole('complementary');
    ['视频压缩', '视频转 GIF', '视频格式转换', '视频剪辑', '提取音频'].forEach((label) => {
      expect(within(sidebar).getByRole('button', { name: label })).toBeInTheDocument();
    });
    expect(within(sidebar).queryByText('视频截图')).not.toBeInTheDocument();
    expect(within(sidebar).queryByText('图片优化')).not.toBeInTheDocument();
    expect(within(sidebar).queryByText('图片旋转')).not.toBeInTheDocument();
    expect(within(sidebar).queryByText('图片裁剪')).not.toBeInTheDocument();
    expect(screen.queryByText('视频编辑')).not.toBeInTheDocument();
    expect(screen.queryByText('文件压缩')).not.toBeInTheDocument();
  });

  it('switches to the image export workbench', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '图片工具' }));

    const imageWorkbench = screen.getByRole('region', { name: '图片工具工作区' });
    expect(within(imageWorkbench).getByText('图片导出')).toBeInTheDocument();
    expect(within(imageWorkbench).getByText('导出选项')).toBeInTheDocument();
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

  it('keeps the latest top navigation free of history and batch buttons', () => {
    render(<App />);

    expect(screen.getByRole('button', { name: '图片工具' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '视频工具' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '历史记录' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '批量处理' })).not.toBeInTheDocument();
  });

  it('shows the design reset action on the video format conversion workbench', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '视频格式转换' }));

    expect(screen.getByRole('button', { name: '重置' })).toBeInTheDocument();
  });

  it('resets the active video workbench by clearing loaded files and restoring local controls', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '视频格式转换' }));
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
    expect(invoke).toHaveBeenCalledWith('create_video_preview', {
      inputPath: '/Users/huashu/Movies/demo.mp4',
    });
    expect(screen.queryByText('拖拽视频文件到这里')).not.toBeInTheDocument();
  });

  it('keeps video preview visible across all five video tools once a video is loaded', async () => {
    render(<App />);

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('demo.mp4', 'video/mp4')] },
    });
    expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();

    for (const label of ['视频转 GIF', '视频格式转换', '视频剪辑', '提取音频', '视频压缩']) {
      await userEvent.click(screen.getByRole('button', { name: label }));
      expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();
      expect(screen.queryByText('拖拽视频文件到这里')).not.toBeInTheDocument();
    }
  });

  it('falls back to the ffmpeg-generated poster for unsupported embedded codecs', async () => {
    render(<App />);

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [createDroppedFile('h265-demo.mkv', 'video/x-matroska')] },
    });

    expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();
    fireEvent.error(screen.getByTestId('video-preview-player'));
    expect(screen.getByText('内嵌播放器暂不支持此编码')).toBeInTheDocument();
    expect(screen.getByText('FFmpeg 本地处理仍可继续')).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith('create_video_preview', {
      inputPath: '/Users/huashu/Movies/h265-demo.mkv',
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
    expect(screen.queryByText('拖拽文件到这里转换格式')).not.toBeInTheDocument();
  });

  it('uses settings as output path configuration', async () => {
    render(<App />);

    await userEvent.click(screen.getByRole('button', { name: '设置' }));

    expect(screen.getByRole('dialog')).toHaveTextContent('输出路径');
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
