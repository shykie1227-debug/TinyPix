import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import VideoPreviewStage from '../../src/components/preview/VideoPreviewStage';

const baseProps = {
  assetUrl: 'asset://demo.mp4',
  posterUrl: '',
  videoPlaybackFailed: false,
  videoPreviewFailed: false,
  videoPreviewLoading: false,
  onVideoPlaybackFailed: vi.fn(),
  onVideoPreviewFailed: vi.fn(),
};

describe('VideoPreviewStage', () => {
  it('uses one custom playback bar for GIF mode instead of native controls', () => {
    render(<VideoPreviewStage {...baseProps} mode="gif" />);

    expect(screen.getByTestId('video-preview-player')).not.toHaveAttribute('controls');
    expect(screen.getByRole('slider', { name: 'GIF 预览进度' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '播放' })).toBeInTheDocument();
  });

  it('keeps native controls for ordinary video preview', () => {
    render(<VideoPreviewStage {...baseProps} mode="preview" />);

    expect(screen.getByTestId('video-preview-player')).toHaveAttribute('controls');
    expect(screen.queryByRole('slider', { name: 'GIF 预览进度' })).not.toBeInTheDocument();
  });

  it('seeks the GIF preview through the single custom slider', () => {
    const onPlaybackTime = vi.fn();
    render(<VideoPreviewStage {...baseProps} mode="gif" onPlaybackTime={onPlaybackTime} />);

    const video = screen.getByTestId('video-preview-player') as HTMLVideoElement;
    Object.defineProperty(video, 'duration', { configurable: true, value: 10 });
    fireEvent.loadedMetadata(video);

    const slider = screen.getByRole('slider', { name: 'GIF 预览进度' });
    fireEvent.change(slider, { target: { value: '4.2' } });

    expect(video.currentTime).toBe(4.2);
    expect(onPlaybackTime).toHaveBeenCalledWith(4.2);
  });
});
