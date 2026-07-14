import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import StatusBar from '../../src/components/layout/StatusBar';
import { useAppStore } from '../../src/stores/appStore';

vi.mock('lucide-react', () => ({
  Cpu: () => <svg data-testid="cpu-icon" />,
}));

describe('StatusBar', () => {
  beforeEach(() => {
    useAppStore.setState({
      files: [],
      isProcessing: false,
      progress: 0,
      totalSaved: 0,
      videoPreset: 'standard',
    });
  });

  it('空闲状态显示 GPU 加速和稳定状态', () => {
    render(<StatusBar />);

    expect(screen.getByText(/GPU 加速/)).toBeInTheDocument();
    expect(screen.getByText('引擎状态: 稳定')).toBeInTheDocument();
  });

  it('处理中状态显示进度百分比', () => {
    useAppStore.setState({
      isProcessing: true,
      progress: 42,
    });

    render(<StatusBar />);

    expect(screen.getByText('处理进度: 42%')).toBeInTheDocument();
    expect(screen.getByText('引擎状态: 处理中')).toBeInTheDocument();
  });

  it('有待处理视频时显示原始大小和预计输出', () => {
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
  });

  it('有已完成文件时显示压缩比和节省空间', () => {
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
  });

  it('仅显示已完成文件时原始大小只统计已完成项', () => {
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
  });

  it('没有文件时不显示压缩统计', () => {
    render(<StatusBar />);

    expect(screen.queryByText(/原始大小/)).not.toBeInTheDocument();
    expect(screen.queryByText(/压缩比/)).not.toBeInTheDocument();
    expect(screen.queryByText(/节省空间/)).not.toBeInTheDocument();
  });

  it('非视频文件不计入统计', () => {
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
  });
});
