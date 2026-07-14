import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import ToolOptionCard from '../../src/components/common/ToolOptionCard';

describe('ToolOptionCard', () => {
  it('renders title when provided', () => {
    render(
      <ToolOptionCard title="画质设置">
        <p>内容区域</p>
      </ToolOptionCard>
    );

    expect(screen.getByText('画质设置')).toBeInTheDocument();
  });

  it('renders subtitle when provided', () => {
    render(
      <ToolOptionCard title="画质设置" subtitle="调整输出视频的画质参数">
        <p>内容区域</p>
      </ToolOptionCard>
    );

    expect(screen.getByText('调整输出视频的画质参数')).toBeInTheDocument();
  });

  it('renders children content', () => {
    render(
      <ToolOptionCard title="画质设置">
        <div data-testid="child-content">子内容</div>
      </ToolOptionCard>
    );

    expect(screen.getByTestId('child-content')).toBeInTheDocument();
    expect(screen.getByText('子内容')).toBeInTheDocument();
  });

  it('applies custom className to the card', () => {
    const { container } = render(
      <ToolOptionCard className="custom-card-class">
        <p>内容</p>
      </ToolOptionCard>
    );

    const section = container.querySelector('section');
    expect(section).toHaveClass('custom-card-class');
  });

  it('has correct base styling classes', () => {
    const { container } = render(
      <ToolOptionCard title="测试">
        <p>内容</p>
      </ToolOptionCard>
    );

    const section = container.querySelector('section');
    expect(section).toHaveClass('bg-surface-container-lowest');
    expect(section).toHaveClass('p-6');
  });

  it('does not render title section when neither title nor subtitle is provided', () => {
    const { container } = render(
      <ToolOptionCard>
        <p>只有内容</p>
      </ToolOptionCard>
    );

    const headerDiv = container.querySelector('section > div');
    expect(headerDiv).toBeNull();
  });
});
