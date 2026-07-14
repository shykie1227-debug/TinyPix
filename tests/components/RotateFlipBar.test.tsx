import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import RotateFlipBar from '../../src/components/image/RotateFlipBar';

describe('RotateFlipBar', () => {
  it('renders 4 action buttons and a slider with accessible labels', () => {
    render(
      <RotateFlipBar
        rotation={0}
        onRotationChange={() => {}}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    expect(screen.getByRole('button', { name: /左旋 90°/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /右旋 90°/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /水平镜像/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /垂直镜像/i })).toBeInTheDocument();
    expect(screen.getByRole('slider')).toBeInTheDocument();
  });

  it('left-rotate button calls onRotationChange with 270 when starting at 0', () => {
    const onRotationChange = vi.fn();
    render(
      <RotateFlipBar
        rotation={0}
        onRotationChange={onRotationChange}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /左旋 90°/i }));
    expect(onRotationChange).toHaveBeenCalledWith(270);
  });

  it('right-rotate button calls onRotationChange with 90 when starting at 0', () => {
    const onRotationChange = vi.fn();
    render(
      <RotateFlipBar
        rotation={0}
        onRotationChange={onRotationChange}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /右旋 90°/i }));
    expect(onRotationChange).toHaveBeenCalledWith(90);
  });

  it('right-rotate wraps from 270 back to 0', () => {
    const onRotationChange = vi.fn();
    render(
      <RotateFlipBar
        rotation={270}
        onRotationChange={onRotationChange}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /右旋 90°/i }));
    expect(onRotationChange).toHaveBeenCalledWith(0);
  });

  it('left-rotate wraps from 90 back to 0', () => {
    const onRotationChange = vi.fn();
    render(
      <RotateFlipBar
        rotation={90}
        onRotationChange={onRotationChange}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /左旋 90°/i }));
    expect(onRotationChange).toHaveBeenCalledWith(0);
  });

  it('horizontal-flip button calls onFlipH', () => {
    const onFlipH = vi.fn();
    render(
      <RotateFlipBar
        rotation={0}
        onRotationChange={() => {}}
        onFlipH={onFlipH}
        onFlipV={() => {}}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /水平镜像/i }));
    expect(onFlipH).toHaveBeenCalledTimes(1);
  });

  it('vertical-flip button calls onFlipV', () => {
    const onFlipV = vi.fn();
    render(
      <RotateFlipBar
        rotation={0}
        onRotationChange={() => {}}
        onFlipH={() => {}}
        onFlipV={onFlipV}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /垂直镜像/i }));
    expect(onFlipV).toHaveBeenCalledTimes(1);
  });

  it('slider has min=0, max=270, step=90 and reflects normalized rotation', () => {
    render(
      <RotateFlipBar
        rotation={180}
        onRotationChange={() => {}}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    const slider = screen.getByRole('slider') as HTMLInputElement;
    expect(slider.min).toBe('0');
    expect(slider.max).toBe('270');
    expect(slider.step).toBe('90');
    expect(slider.value).toBe('180');
  });

  it('slider change to 90 calls onRotationChange with 90', () => {
    const onRotationChange = vi.fn();
    render(
      <RotateFlipBar
        rotation={0}
        onRotationChange={onRotationChange}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    const slider = screen.getByRole('slider');
    fireEvent.change(slider, { target: { value: '90' } });
    expect(onRotationChange).toHaveBeenCalledWith(90);
  });

  it.each([
    [0, 90],
    [90, 0],
    [180, 0],
    [270, 0],
  ])('slider change to %i calls onRotationChange with %i', (target, startRotation) => {
    const onRotationChange = vi.fn();
    render(
      <RotateFlipBar
        rotation={startRotation}
        onRotationChange={onRotationChange}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    const slider = screen.getByRole('slider');
    fireEvent.change(slider, { target: { value: String(target) } });
    expect(onRotationChange).toHaveBeenCalledWith(target);
  });

  it('displays normalized rotation value with degree sign', () => {
    render(
      <RotateFlipBar
        rotation={90}
        onRotationChange={() => {}}
        onFlipH={() => {}}
        onFlipV={() => {}}
      />
    );

    expect(screen.getByText('90°')).toBeInTheDocument();
  });

  describe('normalizeRotation (observable via rendered value)', () => {
    it.each([
      [0, '0°'],
      [90, '90°'],
      [180, '180°'],
      [270, '270°'],
      [45, '45°'],
      [135, '135°'],
      [360, '0°'],
      [-90, '270°'],
      [450, '90°'],
      [-180, '180°'],
    ])('normalizes rotation=%i to displayed value %s', (rotation, expectedText) => {
      render(
        <RotateFlipBar
          rotation={rotation}
          onRotationChange={() => {}}
          onFlipH={() => {}}
          onFlipV={() => {}}
        />
      );

      expect(screen.getByText(expectedText)).toBeInTheDocument();
    });
  });
});
