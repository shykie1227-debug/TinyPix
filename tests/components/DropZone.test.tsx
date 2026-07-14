import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import DropZone from '../../src/components/layout/DropZone';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('DropZone', () => {
  it('shows video demo copy and MKV chip', () => {
    render(<DropZone mediaType="video" onFilesAdded={() => {}} />);

    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
    expect(screen.getByText('MKV')).toBeInTheDocument();
    expect(screen.getByText('选择本地视频')).toBeInTheDocument();
  });

  it('shows image copy in the same visual language', () => {
    render(<DropZone mediaType="image" onFilesAdded={() => {}} />);

    expect(screen.getByText('拖拽图片文件到这里')).toBeInTheDocument();
    expect(screen.getByText('WebP')).toBeInTheDocument();
    expect(screen.getByText('选择本地图片')).toBeInTheDocument();
  });

  it('supports the professional export-mode formats from the image design', () => {
    render(
      <DropZone
        mediaType="image"
        onFilesAdded={() => {}}
        title="拖拽文件到这里转换格式"
        subtitle="支持 PSD, PDF, PPT, EPS, AI, SVG, TIFF, BMP 等格式"
        acceptButton="选取文件"
        formats={['PSD', 'PDF', 'PPT']}
        extensions={['psd', 'pdf', 'ppt', 'pptx', 'eps', 'ai', 'svg', 'tiff', 'tif', 'bmp']}
      />
    );

    expect(screen.getByText('拖拽文件到这里转换格式')).toBeInTheDocument();
    expect(screen.getByText('PDF')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '选取文件' })).toBeInTheDocument();
  });

  it('rejects dropped video files larger than 4GB with an inline error', async () => {
    const onFilesAdded = vi.fn();
    render(<DropZone mediaType="video" onFilesAdded={onFilesAdded} />);
    const oversized = new File(['demo'], 'too-large.mp4', { type: 'video/mp4' });
    Object.defineProperty(oversized, 'size', { value: 4 * 1024 * 1024 * 1024 + 1 });
    Object.defineProperty(oversized, 'path', { value: '/Users/huashu/Movies/too-large.mp4' });

    fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
      dataTransfer: { files: [oversized] },
    });

    expect(await screen.findByText('文件超过 4GB 限制')).toBeInTheDocument();
    expect(onFilesAdded).not.toHaveBeenCalled();
  });
});
