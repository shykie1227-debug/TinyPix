import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ExportPanel from '../../src/components/image/ExportPanel';
import { useAppStore, type FileItem } from '../../src/stores/appStore';
import { isImageFormat } from '../../src/utils/mediaFormat';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
vi.mock('@tauri-apps/api/event', () => ({ listen: vi.fn(async () => vi.fn()) }));

const imageFile: FileItem = {
  id: 'img-1',
  path: '/Users/test/photo.jpg',
  name: 'photo.jpg',
  format: 'JPEG',
  originalSize: 1024 * 1024 * 4,
  status: 'pending',
};

const imageFile2: FileItem = {
  id: 'img-2',
  path: '/Users/test/design.png',
  name: 'design.png',
  format: 'PNG',
  originalSize: 1024 * 1024 * 8,
  status: 'pending',
};

const setOneImageInStore = () => {
  useAppStore.setState({
    files: [imageFile],
    isProcessing: false,
    progress: 0,
    totalSaved: 0,
    options: {
      ...useAppStore.getState().options,
      outputFormat: 'jpeg',
      quality: 85,
      stripExif: false,
      preserveTransparency: true,
    },
  });
};

const setTwoImagesInStore = () => {
  useAppStore.setState({
    files: [imageFile, imageFile2],
    isProcessing: false,
    progress: 0,
    totalSaved: 0,
    options: {
      ...useAppStore.getState().options,
      outputFormat: 'webp',
      quality: 85,
      stripExif: false,
      preserveTransparency: true,
    },
  });
};

const mockEstimateSizeBatch = vi.fn(async () => 1024 * 1024 * 2);
const mockOnProcess = vi.fn();

describe('ExportPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAppStore.getState().clearFiles();
    mockEstimateSizeBatch.mockImplementation(() => new Promise(() => {}));
  });

  describe('rendering', () => {
    it('renders all 5 section titles', () => {
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText('输入文件')).toBeInTheDocument();
      expect(screen.getByText('输出格式')).toBeInTheDocument();
      expect(screen.getByText('输出质量')).toBeInTheDocument();
      expect(screen.getByText('导出选项')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /开始转换导出/ })).toBeInTheDocument();
    });

    it('shows 5 format options in segmented control', () => {
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByRole('tab', { name: 'JPG' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'PNG' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'WebP' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'AVIF' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'BMP' })).toBeInTheDocument();
    });

    it('shows 3 quality preset buttons', () => {
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByRole('button', { name: '轻度' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: '标准' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: '无损' })).toBeInTheDocument();
    });

    it('shows two export option checkboxes', () => {
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByLabelText('保留透明通道')).toBeInTheDocument();
      expect(screen.getByLabelText('清除 EXIF 信息')).toBeInTheDocument();
    });
  });

  describe('input file info card', () => {
    it('shows file name when a file is present', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText('photo.jpg')).toBeInTheDocument();
    });

    it('shows format badge', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText('JPEG')).toBeInTheDocument();
    });

    it('shows file size', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText(/4.0 MB|4 MB|4096/)).toBeInTheDocument();
    });

    it('shows count for multiple files', () => {
      setTwoImagesInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText(/2.*文件|等 2 个/)).toBeInTheDocument();
    });
  });

  describe('output format segmented control', () => {
    it('has JPG selected by default for jpeg outputFormat', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const jpgBtn = screen.getByRole('tab', { name: 'JPG' });
      expect(jpgBtn.className).toMatch(/active|bg-white|shadow/);
    });

    it('switches to PNG when clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const pngBtn = screen.getByRole('tab', { name: 'PNG' });
      await userEvent.click(pngBtn);

      expect(pngBtn.className).toMatch(/active|bg-white|shadow/);
      expect(useAppStore.getState().options.outputFormat).toBe('png');
    });

    it('switches to WebP when clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const webpBtn = screen.getByRole('tab', { name: 'WebP' });
      await userEvent.click(webpBtn);

      expect(webpBtn.className).toMatch(/active|bg-white|shadow/);
      expect(useAppStore.getState().options.outputFormat).toBe('webp');
    });

    it('switches to AVIF when clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const avifBtn = screen.getByRole('tab', { name: 'AVIF' });
      await userEvent.click(avifBtn);

      expect(avifBtn.className).toMatch(/active|bg-white|shadow/);
      expect(useAppStore.getState().options.outputFormat).toBe('avif');
    });
  });

  describe('quality slider', () => {
    it('shows current quality value', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText('85%')).toBeInTheDocument();
    });

    it('updates quality when 轻度 preset is clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const lightBtn = screen.getByRole('button', { name: '轻度' });
      await userEvent.click(lightBtn);

      expect(useAppStore.getState().options.quality).toBe(60);
      expect(lightBtn.className).toMatch(/bg-primary/);
    });

    it('updates quality when 标准 preset is clicked', async () => {
      setOneImageInStore();
      useAppStore.setState({ options: { ...useAppStore.getState().options, quality: 60 } });
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const standardBtn = screen.getByRole('button', { name: '标准' });
      await userEvent.click(standardBtn);

      expect(useAppStore.getState().options.quality).toBe(85);
      expect(standardBtn.className).toMatch(/bg-primary/);
    });

    it('updates quality when 无损 preset is clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const losslessBtn = screen.getByRole('button', { name: '无损' });
      await userEvent.click(losslessBtn);

      expect(useAppStore.getState().options.quality).toBe(100);
      expect(losslessBtn.className).toMatch(/bg-primary/);
    });
  });

  describe('export options', () => {
    it('preserveTransparency is checked by default for PNG', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const pngBtn = screen.getByRole('tab', { name: 'PNG' });
      await userEvent.click(pngBtn);

      const checkbox = screen.getByLabelText('保留透明通道') as HTMLInputElement;
      expect(checkbox.checked).toBe(true);
    });

    it('preserveTransparency is disabled for JPG', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const checkbox = screen.getByLabelText('保留透明通道') as HTMLInputElement;
      expect(checkbox.disabled).toBe(true);
    });

    it('toggles preserveTransparency when clicked (PNG format)', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const pngBtn = screen.getByRole('tab', { name: 'PNG' });
      await userEvent.click(pngBtn);

      const checkbox = screen.getByLabelText('保留透明通道');
      await userEvent.click(checkbox);

      expect(useAppStore.getState().options.preserveTransparency).toBe(false);
    });

    it('stripExif is unchecked by default', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const checkbox = screen.getByLabelText('清除 EXIF 信息') as HTMLInputElement;
      expect(checkbox.checked).toBe(false);
    });

    it('toggles stripExif when clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const checkbox = screen.getByLabelText('清除 EXIF 信息');
      await userEvent.click(checkbox);

      expect(useAppStore.getState().options.stripExif).toBe(true);
    });

    it('shows EXIF hint text', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      expect(screen.getByText(/移除设备、GPS、时间等元数据/)).toBeInTheDocument();
    });
  });

  describe('CTA button', () => {
    it('is disabled when no files', () => {
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const cta = screen.getByRole('button', { name: /开始转换导出/ });
      expect(cta).toBeDisabled();
    });

    it('is enabled when files exist', () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const cta = screen.getByRole('button', { name: /开始转换导出/ });
      expect(cta).not.toBeDisabled();
    });

    it('calls onProcess when clicked', async () => {
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const cta = screen.getByRole('button', { name: /开始转换导出/ });
      await userEvent.click(cta);

      expect(mockOnProcess).toHaveBeenCalledTimes(1);
    });

    it('is disabled when processing', () => {
      setOneImageInStore();
      useAppStore.setState({ isProcessing: true });
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      const cta = screen.getByRole('button', { name: /开始转换导出|处理中/ });
      expect(cta).toBeDisabled();
    });
  });

  describe('estimated size', () => {
    it('calls estimateSizeBatch with correct params', async () => {
      mockEstimateSizeBatch.mockResolvedValueOnce(1024 * 1024 * 2);
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      await waitFor(() => {
        expect(mockEstimateSizeBatch).toHaveBeenCalled();
      });
    });

    it('shows estimated size text', async () => {
      mockEstimateSizeBatch.mockResolvedValueOnce(1024 * 1024 * 2);
      setOneImageInStore();
      render(<ExportPanel onProcess={mockOnProcess} estimateSizeBatch={mockEstimateSizeBatch} />);

      await waitFor(() => {
        expect(screen.getByText('~2.0 MB')).toBeInTheDocument();
      });
    });
  });
});
