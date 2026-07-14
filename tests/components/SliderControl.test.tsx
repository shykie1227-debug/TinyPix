import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SliderControl from '../../src/components/common/SliderControl';

describe('SliderControl', () => {
  it('renders label, current value, and min/max annotations', () => {
    render(
      <SliderControl
        label="播放速度"
        value={1.0}
        min={0.25}
        max={4}
        step={0.25}
        onChange={() => {}}
        format={(v) => `${v.toFixed(2)}x`}
      />
    );

    expect(screen.getByText('播放速度')).toBeInTheDocument();
    expect(screen.getByText('1.00x')).toBeInTheDocument();
    expect(screen.getByText('0.25x')).toBeInTheDocument();
    expect(screen.getByText('4.00x')).toBeInTheDocument();
  });

  it('formats percentage values correctly', () => {
    render(
      <SliderControl
        label="音量"
        value={100}
        min={0}
        max={200}
        step={5}
        onChange={() => {}}
        format={(v) => `${v}%`}
      />
    );

    expect(screen.getByText('100%')).toBeInTheDocument();
  });

  it('invokes onChange with the new value when slider is moved', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <SliderControl
        label="亮度"
        value={0}
        min={-100}
        max={100}
        step={5}
        onChange={onChange}
        format={(v) => `${v}`}
      />
    );

    const thumb = screen.getByRole('slider');
    expect(thumb).toBeInTheDocument();

    await user.keyboard('{Tab}');
    expect(thumb).toHaveFocus();

    await user.keyboard('{ArrowRight}');
    expect(onChange).toHaveBeenCalledWith(5);
  });

  it('is keyboard accessible with proper ARIA attributes', () => {
    render(
      <SliderControl
        label="对比度"
        value={0}
        min={-100}
        max={100}
        step={5}
        onChange={() => {}}
        format={(v) => `${v}`}
      />
    );

    const thumb = screen.getByRole('slider');
    expect(thumb).toHaveAttribute('aria-valuemin', '-100');
    expect(thumb).toHaveAttribute('aria-valuemax', '100');
    expect(thumb).toHaveAttribute('aria-valuenow', '0');
  });

  it('uses opacity feedback without scale transforms', () => {
    render(
      <SliderControl
        label="亮度"
        value={0}
        min={-100}
        max={100}
        step={5}
        onChange={() => {}}
        format={(v) => `${v}`}
      />
    );

    const thumb = screen.getByRole('slider');
    expect(thumb.className).not.toContain('scale-');
    expect(thumb.className).toContain('hover:opacity-80');
  });
});
