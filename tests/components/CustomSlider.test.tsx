import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import CustomSlider from '../../src/components/common/CustomSlider';

describe('CustomSlider', () => {
  it('renders a range input with correct min/max/step/value', () => {
    render(<CustomSlider min={0} max={100} step={1} value={50} onChange={() => {}} />);

    const slider = screen.getByRole('slider') as HTMLInputElement;
    expect(slider).toBeInTheDocument();
    expect(slider.min).toBe('0');
    expect(slider.max).toBe('100');
    expect(slider.step).toBe('1');
    expect(slider.value).toBe('50');
  });

  it('calls onChange with the new value when slider changes', () => {
    const onChange = vi.fn();
    render(<CustomSlider min={0} max={100} step={1} value={50} onChange={onChange} />);

    const slider = screen.getByRole('slider');
    fireEvent.change(slider, { target: { value: '75' } });

    expect(onChange).toHaveBeenCalledWith(75);
  });

  it('renders marks when provided', () => {
    const marks = [
      { value: 0, label: '低' },
      { value: 50, label: '中' },
      { value: 100, label: '高' },
    ];

    render(<CustomSlider min={0} max={100} step={1} value={50} onChange={() => {}} marks={marks} />);

    expect(screen.getByText('低')).toBeInTheDocument();
    expect(screen.getByText('中')).toBeInTheDocument();
    expect(screen.getByText('高')).toBeInTheDocument();
  });

  it('does not render marks when not provided', () => {
    const { container } = render(
      <CustomSlider min={0} max={100} step={1} value={50} onChange={() => {}} />
    );

    const marksContainer = container.querySelector('.slider-marks');
    expect(marksContainer).not.toBeInTheDocument();
  });

  it('applies custom className to the container', () => {
    const { container } = render(
      <CustomSlider min={0} max={100} step={1} value={50} onChange={() => {}} className="custom-class" />
    );

    expect(container.firstChild).toHaveClass('custom-class');
  });

  it('has slider-apple class on the input', () => {
    render(<CustomSlider min={0} max={100} step={1} value={50} onChange={() => {}} />);

    const slider = screen.getByRole('slider');
    expect(slider).toHaveClass('slider-apple');
  });
});
