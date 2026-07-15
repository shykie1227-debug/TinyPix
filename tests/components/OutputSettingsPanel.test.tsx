import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { invoke } from '@tauri-apps/api/core';
import OutputSettingsPanel from '../../src/components/preview/OutputSettingsPanel';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));

describe('OutputSettingsPanel media engine and licenses', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(invoke).mockResolvedValue({
      ready: true,
      ffmpegPath: 'C:\\Users\\user\\AppData\\Local\\TinyPix\\engine\\hash\\ffmpeg.exe',
      ffprobePath: 'C:\\Users\\user\\AppData\\Local\\TinyPix\\engine\\hash\\ffprobe.exe',
      version: 'ffmpeg version 8.1.1',
      sha256: 'abc123',
      cacheDirectory: 'C:\\Users\\user\\AppData\\Local\\TinyPix\\engine\\hash',
      error: null,
    });
  });

  it('shows embedded engine version, offline statement, and open-source notices', async () => {
    render(<OutputSettingsPanel onClose={vi.fn()} />);
    expect(await screen.findByText('ffmpeg version 8.1.1')).toBeInTheDocument();
    expect(screen.getByText(/运行时不联网/)).toBeInTheDocument();
    expect(screen.getAllByText(/TinyPix.*MIT/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/FFmpeg.*GPL/).length).toBeGreaterThan(0);
  });

  it('clears the engine cache and refreshes status', async () => {
    render(<OutputSettingsPanel onClose={vi.fn()} />);
    await screen.findByText('ffmpeg version 8.1.1');
    await userEvent.click(screen.getByRole('button', { name: '清理媒体引擎缓存' }));
    await waitFor(() => expect(invoke).toHaveBeenCalledWith('clear_media_engine_cache'));
    expect(invoke).toHaveBeenCalledWith('get_media_engine_status');
  });

  it('clears generated media previews from the settings panel', async () => {
    render(<OutputSettingsPanel onClose={vi.fn()} />);
    await screen.findByText('ffmpeg version 8.1.1');
    await userEvent.click(screen.getByRole('button', { name: '清理预览缓存' }));
    await waitFor(() => expect(invoke).toHaveBeenCalledWith('clear_preview_cache'));
    expect(await screen.findByText('预览缓存已清理')).toBeInTheDocument();
  });

  it('does not expose raw bridge errors when engine status is unavailable', async () => {
    vi.mocked(invoke).mockRejectedValueOnce(new TypeError("Cannot read properties of undefined (reading 'invoke')"));
    render(<OutputSettingsPanel onClose={vi.fn()} />);
    expect(await screen.findByText('暂时无法读取媒体引擎状态，请在桌面应用中重试。')).toBeInTheDocument();
    expect(screen.queryByText(/TypeError|undefined|invoke/)).not.toBeInTheDocument();
  });
});
