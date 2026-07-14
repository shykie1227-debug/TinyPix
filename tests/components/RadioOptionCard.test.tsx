import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import RadioOptionCard from '../../src/components/common/RadioOptionCard';

describe('RadioOptionCard', () => {
  const options = [
    { label: 'Small', desc: '适合手机端使用', value: 'small', badge: '推荐' },
    { label: 'Medium', desc: '平衡画质与体积', value: 'medium' },
    { label: 'Large', value: 'large' },
  ];

  it('renders all option labels', () => {
    render(<RadioOptionCard options={options} value="small" onChange={() => {}} />);

    expect(screen.getByText('Small')).toBeInTheDocument();
    expect(screen.getByText('Medium')).toBeInTheDocument();
    expect(screen.getByText('Large')).toBeInTheDocument();
  });

  it('renders descriptions when provided', () => {
    render(<RadioOptionCard options={options} value="small" onChange={() => {}} />);

    expect(screen.getByText('适合手机端使用')).toBeInTheDocument();
    expect(screen.getByText('平衡画质与体积')).toBeInTheDocument();
  });

  it('renders badge when provided', () => {
    render(<RadioOptionCard options={options} value="small" onChange={() => {}} />);

    expect(screen.getByText('推荐')).toBeInTheDocument();
  });

  it('calls onChange when an option is clicked', () => {
    const onChange = vi.fn();
    render(<RadioOptionCard options={options} value="small" onChange={onChange} />);

    fireEvent.click(screen.getByText('Medium').closest('[role="radio"]'));
    expect(onChange).toHaveBeenCalledWith('medium');
  });

  it('selected option has border-secondary-fixed class', () => {
    render(<RadioOptionCard options={options} value="medium" onChange={() => {}} />);

    const selectedOption = screen.getByText('Medium').closest('[role="radio"]');
    expect(selectedOption).toHaveClass('border-secondary-fixed');
  });

  it('unselected option has border-outline-variant/20 class', () => {
    render(<RadioOptionCard options={options} value="small" onChange={() => {}} />);

    const unselectedOption = screen.getByText('Medium').closest('[role="radio"]');
    expect(unselectedOption).toHaveClass('border-outline-variant/20');
  });

  it('applies custom className to the container', () => {
    const { container } = render(
      <RadioOptionCard options={options} value="small" onChange={() => {}} className="custom-class" />
    );

    expect(container.firstChild).toHaveClass('custom-class');
  });

  it('has radio input with correct checked state', () => {
    render(<RadioOptionCard options={options} value="medium" onChange={() => {}} />);

    const radioInputs = screen.getAllByRole('radio');
    const checkedInput = radioInputs.find((input) => (input as HTMLInputElement).checked);
    expect(checkedInput).toBeDefined();
    expect((checkedInput as HTMLInputElement).value).toBe('medium');
  });
});
