import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import SegmentedControl from '../../src/components/common/SegmentedControl';

describe('SegmentedControl', () => {
  const options = [
    { label: '轻度', value: 'light' },
    { label: '标准', value: 'standard' },
    { label: '极限', value: 'extreme' },
  ];

  it('渲染所有选项标签', () => {
    render(<SegmentedControl options={options} value="light" onChange={() => {}} />);

    expect(screen.getByText('轻度')).toBeInTheDocument();
    expect(screen.getByText('标准')).toBeInTheDocument();
    expect(screen.getByText('极限')).toBeInTheDocument();
  });

  it('点击未选中选项触发 onChange', () => {
    const onChange = vi.fn();
    render(<SegmentedControl options={options} value="light" onChange={onChange} />);

    fireEvent.click(screen.getByText('标准'));
    expect(onChange).toHaveBeenCalledWith('standard');

    fireEvent.click(screen.getByText('极限'));
    expect(onChange).toHaveBeenCalledWith('extreme');
  });

  it('点击已选中选项仍触发 onChange', () => {
    const onChange = vi.fn();
    render(<SegmentedControl options={options} value="light" onChange={onChange} />);

    fireEvent.click(screen.getByText('轻度'));
    expect(onChange).toHaveBeenCalledWith('light');
  });

  it('选中项具有 aria-selected=true', () => {
    render(<SegmentedControl options={options} value="standard" onChange={() => {}} />);

    const selectedButton = screen.getByRole('tab', { selected: true });
    expect(selectedButton).toHaveTextContent('标准');

    const unselectedButtons = screen.getAllByRole('tab', { selected: false });
    expect(unselectedButtons).toHaveLength(2);
  });

  it('未选中项可交互并切换选中状态', () => {
    const { rerender } = render(
      <SegmentedControl options={options} value="light" onChange={() => {}} />
    );

    expect(screen.getByRole('tab', { selected: true })).toHaveTextContent('轻度');

    rerender(<SegmentedControl options={options} value="extreme" onChange={() => {}} />);

    expect(screen.getByRole('tab', { selected: true })).toHaveTextContent('极限');
  });

  it('应用自定义 className', () => {
    const { container } = render(
      <SegmentedControl options={options} value="light" onChange={() => {}} className="my-class" />
    );

    expect(container.firstChild).toHaveClass('my-class');
  });

  it('空选项渲染为空容器', () => {
    const { container } = render(<SegmentedControl options={[]} value="" onChange={() => {}} />);

    expect(container.firstChild).toBeInTheDocument();
    expect(container.querySelectorAll('button')).toHaveLength(0);
  });
});
