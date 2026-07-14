import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import ChipButton from '../../src/components/common/ChipButton';

describe('ChipButton', () => {
  const options = [
    { label: 'Option A', value: 'a' },
    { label: 'Option B', value: 'b' },
    { label: 'Option C', value: 'c' },
  ];

  it('renders all option labels', () => {
    render(<ChipButton options={options} value="a" onChange={() => {}} />);

    expect(screen.getByText('Option A')).toBeInTheDocument();
    expect(screen.getByText('Option B')).toBeInTheDocument();
    expect(screen.getByText('Option C')).toBeInTheDocument();
  });

  it('calls onChange with the clicked option value', () => {
    const onChange = vi.fn();
    render(<ChipButton options={options} value="a" onChange={onChange} />);

    fireEvent.click(screen.getByText('Option B'));
    expect(onChange).toHaveBeenCalledWith('b');
  });

  it('applies custom className to the container', () => {
    const { container } = render(
      <ChipButton options={options} value="a" onChange={() => {}} className="custom-class" />
    );

    expect(container.firstChild).toHaveClass('custom-class');
  });

  it('selected option has primary background style', () => {
    render(<ChipButton options={options} value="b" onChange={() => {}} />);

    const selectedChip = screen.getByText('Option B').closest('button');
    expect(selectedChip).toHaveClass('bg-primary');
  });

  it('unselected options have surface-container-low background', () => {
    render(<ChipButton options={options} value="a" onChange={() => {}} />);

    const unselectedChip = screen.getByText('Option B').closest('button');
    expect(unselectedChip).toHaveClass('bg-surface-container-low');
  });
});
