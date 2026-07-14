import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import EditPanel from '../../src/components/image/EditPanel';
import { useAppStore } from '../../src/stores/appStore';

vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
  convertFileSrc: vi.fn((path: string) => `file://${path}`),
}));

vi.mock('react-image-crop', () => ({
  __esModule: true,
  default: ({ children, crop, onChange, onComplete, keepSelection }: any) => (
    <div data-testid="react-crop" data-crop={JSON.stringify(crop)}>
      {children}
    </div>
  ),
  'type Crop': {},
}));

const mocks = vi.hoisted(() => ({
  startProcess: vi.fn(),
  exportWithCrop: vi.fn(),
}));

vi.mock('../../src/hooks/useImageProcessor', () => ({
  useImageProcessor: () => ({
    startProcess: mocks.startProcess,
    cancelProcess: vi.fn(),
    estimateSize: vi.fn(),
    estimateSizeBatch: vi.fn(),
    exportWithCrop: mocks.exportWithCrop,
  }),
}));

describe('EditPanel', () => {
  it('renders all 4 sections', () => {
    render(<EditPanel />);

    expect(screen.getByText('裁切')).toBeInTheDocument();
    expect(screen.getByText('调整尺寸')).toBeInTheDocument();
    expect(screen.getByText('色彩调整')).toBeInTheDocument();
    expect(screen.getByText('开始极速导出')).toBeInTheDocument();
  });

  it('renders crop preset tabs', () => {
    render(<EditPanel />);

    expect(screen.getByText('证件照')).toBeInTheDocument();
    expect(screen.getByText('常用比例')).toBeInTheDocument();
    expect(screen.getByText('自定义')).toBeInTheDocument();
  });

  it('renders color adjustment sliders', () => {
    render(<EditPanel />);

    expect(screen.getByText('亮度')).toBeInTheDocument();
    expect(screen.getByText('对比')).toBeInTheDocument();
    expect(screen.getByText('饱和')).toBeInTheDocument();
    expect(screen.getByText('锐化')).toBeInTheDocument();
  });

  it('renders resize preset buttons', () => {
    render(<EditPanel />);

    expect(screen.getByText('50%')).toBeInTheDocument();
    expect(screen.getByText('75%')).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(screen.getByText('200%')).toBeInTheDocument();
  });

  it('disables CTA when no files', () => {
    render(<EditPanel />);

    const ctaButton = screen.getByRole('button', { name: /开始极速导出/i });
    expect(ctaButton).toBeDisabled();
  });

  it('shows collapse/expand toggle', () => {
    render(<EditPanel />);

    expect(screen.getByText('收起')).toBeInTheDocument();
  });

  it('CTA reflects isProcessing state from store (disabled when processing)', () => {
    useAppStore.setState({ isProcessing: true });

    render(<EditPanel />);
    const ctaButton = screen.getByRole('button', { name: /处理中|开始极速导出/i });
    expect(ctaButton).toBeDisabled();
    expect(ctaButton.textContent).toContain('处理中');
  });
});

describe('EditPanel 交互行为', () => {
  beforeEach(() => {
    Object.defineProperty(HTMLImageElement.prototype, 'naturalWidth', {
      value: 800,
      configurable: true,
    });
    Object.defineProperty(HTMLImageElement.prototype, 'naturalHeight', {
      value: 600,
      configurable: true,
    });
    useAppStore.setState({
      files: [],
      isProcessing: false,
      options: {
        ...useAppStore.getState().options,
        colorAdjust: { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 },
        cropPercent: undefined,
      },
    });
    mocks.startProcess.mockClear();
    mocks.exportWithCrop.mockClear();
  });

  it('点击导出按钮触发 exportWithCrop（有裁切区域时）', async () => {
    useAppStore.setState({
      files: [
        {
          id: '1',
          path: '/test/img.png',
          name: 'img.png',
          format: 'png',
          originalSize: 1000,
          status: 'pending',
        },
      ],
    });

    render(<EditPanel />);

    // 等待图片预览渲染，使 cropImageRef 被设置
    await screen.findByTestId('react-crop');

    const exportButton = screen.getByRole('button', { name: /开始极速导出/i });
    await userEvent.click(exportButton);

    expect(mocks.exportWithCrop).toHaveBeenCalledTimes(1);
    expect(mocks.exportWithCrop).toHaveBeenCalledWith(
      expect.objectContaining({
        path: '/test/img.png',
        outputFormat: 'png',
        quality: 85,
      })
    );
  });

  it('点击常用比例预设时更新 cropPercent', async () => {
    useAppStore.setState({
      files: [
        {
          id: '1',
          path: '/test/img.png',
          name: 'img.png',
          format: 'png',
          originalSize: 1000,
          status: 'pending',
        },
      ],
    });

    render(<EditPanel />);

    await screen.findByTestId('react-crop');

    // 切换到常用比例 tab
    await userEvent.click(screen.getByText('常用比例'));

    // 点击 1:1 预设
    await userEvent.click(screen.getByText('1:1'));

    const { cropPercent } = useAppStore.getState().options;
    expect(cropPercent).toBeDefined();
    // 1:1 预设：800x600 图片，imgAspect=1.333 > targetAspect=1
    // newH=100, newW=(1/1.333)*100=75
    expect(cropPercent.width).toBe(75);
    expect(cropPercent.height).toBe(100);
  });

  it('色彩滑块变更时更新 brightness 和 contrast', () => {
    render(<EditPanel />);

    const sliders = screen.getAllByRole('slider');
    // 顺序：亮度、对比、饱和、锐化
    const brightnessSlider = sliders[0];
    const contrastSlider = sliders[1];

    fireEvent.change(brightnessSlider, { target: { value: '40' } });
    expect(useAppStore.getState().options.colorAdjust.brightness).toBe(40);

    fireEvent.change(contrastSlider, { target: { value: '-20' } });
    expect(useAppStore.getState().options.colorAdjust.contrast).toBe(-20);
  });
});
