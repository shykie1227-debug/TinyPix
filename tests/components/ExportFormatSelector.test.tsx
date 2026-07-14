import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ExportFormatSelector from '../../src/components/image/ExportFormatSelector';

describe('ExportFormatSelector', () => {
  it('renders all three format options', () => {
    render(<ExportFormatSelector value="mp4" onChange={() => {}} />);
    expect(screen.getByRole('radio', { name: 'MP4' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'MOV' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'MKV' })).toBeInTheDocument();
  });

  it('marks the current value as checked', () => {
    render(<ExportFormatSelector value="mov" onChange={() => {}} />);
    expect(screen.getByRole('radio', { name: 'MOV' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: 'MP4' })).toHaveAttribute('aria-checked', 'false');
  });

  it('invokes onChange with the selected format', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<ExportFormatSelector value="mp4" onChange={onChange} />);
    await user.click(screen.getByRole('radio', { name: 'MKV' }));
    expect(onChange).toHaveBeenCalledWith('mkv');
  });

  it('disables all options when disabled prop is set', () => {
    render(<ExportFormatSelector value="mp4" onChange={() => {}} disabled />);
    expect(screen.getByRole('radio', { name: 'MP4' })).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'MOV' })).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'MKV' })).toBeDisabled();
  });
});
