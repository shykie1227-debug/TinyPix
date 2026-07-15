import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import StatusBar from '../../src/components/layout/StatusBar';
import { useAppStore } from '../../src/stores/appStore';
import { invoke } from '@tauri-apps/api/core';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

vi.mock('lucide-react', () => ({
  Cpu: () => <svg data-testid="cpu-icon" />,
}));

describe('StatusBar', () => {
  beforeEach(() => {
    vi.mocked(invoke).mockReset();
    vi.mocked(invoke).mockResolvedValue({
      ready: true,
      ffmpegPath: 'C:/TinyPix/engine/ffmpeg.exe',
      ffprobePath: 'C:/TinyPix/engine/ffprobe.exe',
      version: 'ffmpeg version 8.1.1',
      sha256: 'abc123',
      cacheDirectory: 'C:/TinyPix/engine',
      error: null,
    });
    useAppStore.setState({
      files: [],
      isProcessing: false,
      progress: 0,
      totalSaved: 0,
      videoPreset: 'standard',
    });
  });

  const waitForEngineCheck = async () => {
    await waitFor(() => expect(invoke).toHaveBeenCalledWith('get_media_engine_status'));
    await waitFor(() => {
      expect(screen.queryByText('引擎状态: 检查中')).not.toBeInTheDocument();
    });
  };

  it('空闲状态显示 CPU 编码和真实引擎就绪状态', async () => {
    render(<StatusBar />);

    expect(screen.getByText(/CPU 编码/)).toBeInTheDocument();
    expect(await screen.findByText('引擎状态: 就绪')).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith('get_media_engine_status');
  });

  it('处理中状态显示进度百分比', async () => {
    useAppStore.setState({
      isProcessing: true,
      progress: 42,
    });

    render(<StatusBar />);

    expect(screen.getByText('处理进度: 42%')).toBeInTheDocument();
    expect(screen.getByText('引擎状态: 处理中')).toBeInTheDocument();
    await waitForEngineCheck();
  });

  it('有待处理视频时显示原始大小和预计输出', async () => {
    useAppStore.setState({
      files: [
        {
          id: '1',
          path: '/tmp/test.mp4',
          name: 'test.mp4',
          format: 'mp4',
          originalSize: 1024 * 1024 * 10,
          status: 'pending',
        },
      ],
      videoPreset: 'standard',
    });

    render(<StatusBar />);

    expect(screen.getByText(/原始大小/)).toBeInTheDocument();
    expect(screen.getByText(/预计输出/)).toBeInTheDocument();
    await waitForEngineCheck();
  });

  it('有已完成文件时显示压缩比和节省空间', async () => {
    useAppStore.setState({
      files: [
        {
          id: '1',
          path: '/tmp/test.mp4',
          name: 'test.mp4',
          format: 'mp4',
          originalSize: 1024 * 1024 * 10,
          status: 'completed',
          outputSize: 1024 * 1024 * 5,
        },
      ],
    });

    render(<StatusBar />);

    expect(screen.getByText(/压缩比/)).toBeInTheDocument();
    expect(screen.getByText(/节省空间/)).toBeInTheDocument();
    await waitForEngineCheck();
  });

  it('仅显示已完成文件时原始大小只统计已完成项', async () => {
    useAppStore.setState({
      files: [
        {
          id: '1',
          path: '/tmp/done.mp4',
          name: 'done.mp4',
          format: 'mp4',
          originalSize: 1024 * 1024 * 10,
          status: 'completed',
          outputSize: 1024 * 1024 * 2,
        },
        {
          id: '2',
          path: '/tmp/pending.mp4',
          name: 'pending.mp4',
          format: 'mp4',
          originalSize: 1024 * 1024 * 5,
          status: 'pending',
        },
      ],
    });

    render(<StatusBar />);

    expect(screen.getByText(/压缩比/)).toBeInTheDocument();
    expect(screen.getByText(/节省空间/)).toBeInTheDocument();
    await waitForEngineCheck();
  });

  it('没有文件时不显示压缩统计', async () => {
    render(<StatusBar />);

    expect(screen.queryByText(/原始大小/)).not.toBeInTheDocument();
    expect(screen.queryByText(/压缩比/)).not.toBeInTheDocument();
    expect(screen.queryByText(/节省空间/)).not.toBeInTheDocument();
    await waitForEngineCheck();
  });

  it('非视频文件不计入统计', async () => {
    useAppStore.setState({
      files: [
        {
          id: '1',
          path: '/tmp/test.png',
          name: 'test.png',
          format: 'png',
          originalSize: 1024 * 1024 * 5,
          status: 'pending',
        },
      ],
    });

    render(<StatusBar />);

    expect(screen.queryByText(/原始大小/)).not.toBeInTheDocument();
    expect(screen.queryByText(/预计输出/)).not.toBeInTheDocument();
    await waitForEngineCheck();
  });

  it('媒体引擎校验失败时显示不可用而不是稳定', async () => {
    vi.mocked(invoke).mockResolvedValueOnce({
      ready: false,
      ffmpegPath: '',
      ffprobePath: '',
      version: '',
      sha256: '',
      cacheDirectory: '',
      error: '哈希校验失败',
    });

    render(<StatusBar />);

    expect(await screen.findByText('引擎状态: 不可用')).toBeInTheDocument();
    expect(screen.queryByText(/稳定/)).not.toBeInTheDocument();
  });
});
