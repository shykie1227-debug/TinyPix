import { describe, it, expect, vi, beforeEach } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import {
  AudioFileInspector,
  isDirectExtractCompatible,
  isCodecCompatibleWithFormat,
  parseFfprobeOutput,
  type AudioSourceInfo,
} from '../../../src/modules/audio/inspector';
import { AUDIO_FORMATS } from '../../../src/modules/audio/formatConverter';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('M1 AudioFileInspector', () => {
  describe('isCodecCompatibleWithFormat (pure)', () => {
    it('aac source is compatible with m4a (same codec, different container)', () => {
      expect(isCodecCompatibleWithFormat('aac', 'M4A')).toBe(true);
    });

    it('aac source is NOT compatible with mp3', () => {
      expect(isCodecCompatibleWithFormat('aac', 'MP3')).toBe(false);
    });

    it('mp3 source is compatible with mp3', () => {
      expect(isCodecCompatibleWithFormat('mp3', 'MP3')).toBe(true);
    });

    it('opus source is compatible with opus-like target', () => {
      expect(isCodecCompatibleWithFormat('opus', 'M4A')).toBe(false);
      expect(isCodecCompatibleWithFormat('opus', 'MP3')).toBe(false);
    });

    it('flac source is not directly compatible with any lossy target', () => {
      expect(isCodecCompatibleWithFormat('flac', 'MP3')).toBe(false);
      expect(isCodecCompatibleWithFormat('flac', 'AAC')).toBe(false);
    });

    it('pcm source is compatible with WAV (lossless passthrough)', () => {
      expect(isCodecCompatibleWithFormat('pcm', 'WAV')).toBe(true);
    });

    it('unknown codec is never compatible', () => {
      expect(isCodecCompatibleWithFormat('unknown', 'MP3')).toBe(false);
      expect(isCodecCompatibleWithFormat('unknown', 'M4A')).toBe(false);
    });
  });

  describe('isDirectExtractCompatible (pure)', () => {
    const baseInfo: AudioSourceInfo = {
      codec: 'aac',
      sampleRate: 48000,
      channels: 2,
      durationSecs: 60,
    };

    it('returns true when target format matches source codec', () => {
      expect(isDirectExtractCompatible(baseInfo, AUDIO_FORMATS[4])).toBe(true); // M4A
    });

    it('returns false when target format requires transcoding', () => {
      expect(isDirectExtractCompatible(baseInfo, AUDIO_FORMATS[0])).toBe(false); // MP3
    });
  });

  describe('parseFfprobeOutput (pure)', () => {
    it('parses valid ffprobe JSON with aac codec', () => {
      const json = JSON.stringify({
        streams: [
          {
            codec_name: 'aac',
            sample_rate: '48000',
            channels: 2,
            duration: '120.5',
            bit_rate: '128000',
          },
        ],
      });
      const result = parseFfprobeOutput(json);
      expect(result.codec).toBe('aac');
      expect(result.sampleRate).toBe(48000);
      expect(result.channels).toBe(2);
      expect(result.durationSecs).toBe(120.5);
      expect(result.bitrateKbps).toBe(128);
    });

    it('returns unknown when no streams', () => {
      const json = JSON.stringify({ streams: [] });
      const result = parseFfprobeOutput(json);
      expect(result.codec).toBe('unknown');
    });

    it('returns unknown on invalid JSON', () => {
      const result = parseFfprobeOutput('not json');
      expect(result.codec).toBe('unknown');
    });

    it('handles missing optional fields', () => {
      const json = JSON.stringify({
        streams: [{ codec_name: 'mp3', channels: 1 }],
      });
      const result = parseFfprobeOutput(json);
      expect(result.codec).toBe('mp3');
      expect(result.sampleRate).toBe(0);
      expect(result.durationSecs).toBe(0);
      expect(result.bitrateKbps).toBeUndefined();
    });
  });

  describe('AudioFileInspector (class)', () => {
    it('exposes inspect and isDirectExtractCompatible', () => {
      const inspector = new AudioFileInspector();
      expect(typeof inspector.inspect).toBe('function');
      expect(typeof inspector.isDirectExtractCompatible).toBe('function');
    });

    it('isDirectExtractCompatible delegates to pure function', () => {
      const inspector = new AudioFileInspector();
      const info: AudioSourceInfo = {
        codec: 'aac',
        sampleRate: 44100,
        channels: 2,
        durationSecs: 30,
      };
      expect(inspector.isDirectExtractCompatible(info, AUDIO_FORMATS[4])).toBe(true);
      expect(inspector.isDirectExtractCompatible(info, AUDIO_FORMATS[0])).toBe(false);
    });
  });
});

describe('AudioFileInspector.inspect (Tauri bridge)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('invokes inspect_audio command with inputPath param', async () => {
    vi.mocked(invoke).mockResolvedValue({
      codec: 'aac',
      sample_rate: 48000,
      channels: 2,
      duration_secs: 10,
      bitrate_kbps: 128,
    });
    const inspector = new AudioFileInspector();
    await inspector.inspect('/path/to/video.mp4');
    expect(invoke).toHaveBeenCalledWith('inspect_audio', {
      inputPath: '/path/to/video.mp4',
    });
  });

  it('maps backend AudioSourceInfo (snake_case) to frontend AudioSourceInfo (camelCase)', async () => {
    vi.mocked(invoke).mockResolvedValue({
      codec: 'aac',
      sample_rate: 48000,
      channels: 2,
      duration_secs: 120.5,
      bitrate_kbps: 128,
    });
    const inspector = new AudioFileInspector();
    const info = await inspector.inspect('/in.mp4');
    expect(info).toEqual({
      codec: 'aac',
      sampleRate: 48000,
      channels: 2,
      durationSecs: 120.5,
      bitrateKbps: 128,
    });
  });

  it('propagates invoke rejection', async () => {
    vi.mocked(invoke).mockRejectedValueOnce(new Error('ffprobe 失败'));
    const inspector = new AudioFileInspector();
    await expect(inspector.inspect('/in.mp4')).rejects.toThrow('ffprobe 失败');
  });

  it('passes empty path through to invoke without client-side validation', async () => {
    vi.mocked(invoke).mockResolvedValue({
      codec: 'unknown',
      sample_rate: 0,
      channels: 2,
      duration_secs: 0,
    });
    const inspector = new AudioFileInspector();
    await inspector.inspect('');
    expect(invoke).toHaveBeenCalledWith('inspect_audio', { inputPath: '' });
  });

  it('falls back to unknown when backend returns object with missing fields', async () => {
    vi.mocked(invoke).mockResolvedValue({});
    const inspector = new AudioFileInspector();
    const info = await inspector.inspect('/in.mp4');
    expect(info.codec).toBe('unknown');
    expect(info.sampleRate).toBe(0);
    expect(info.durationSecs).toBe(0);
  });
});

describe('AudioFileInspector.getCompatibleFormats', () => {
  it('returns M4A and AAC for aac codec', () => {
    const inspector = new AudioFileInspector();
    const formats = inspector.getCompatibleFormats({
      codec: 'aac',
      sampleRate: 48000,
      channels: 2,
      durationSecs: 60,
    });
    expect(formats.map((f) => f.label)).toEqual(['M4A', 'AAC']);
  });

  it('returns MP3 for mp3 codec', () => {
    const inspector = new AudioFileInspector();
    const formats = inspector.getCompatibleFormats({
      codec: 'mp3',
      sampleRate: 44100,
      channels: 2,
      durationSecs: 60,
    });
    expect(formats.map((f) => f.label)).toEqual(['MP3']);
  });

  it('returns FLAC for flac codec', () => {
    const inspector = new AudioFileInspector();
    const formats = inspector.getCompatibleFormats({
      codec: 'flac',
      sampleRate: 48000,
      channels: 2,
      durationSecs: 60,
    });
    expect(formats.map((f) => f.label)).toEqual(['FLAC']);
  });

  it('returns WAV for pcm codec', () => {
    const inspector = new AudioFileInspector();
    const formats = inspector.getCompatibleFormats({
      codec: 'pcm',
      sampleRate: 48000,
      channels: 2,
      durationSecs: 60,
    });
    expect(formats.map((f) => f.label)).toEqual(['WAV']);
  });

  it('returns empty array for opus codec (no direct-extract target)', () => {
    const inspector = new AudioFileInspector();
    const formats = inspector.getCompatibleFormats({
      codec: 'opus',
      sampleRate: 48000,
      channels: 2,
      durationSecs: 60,
    });
    expect(formats).toEqual([]);
  });

  it('returns empty array for unknown codec', () => {
    const inspector = new AudioFileInspector();
    const formats = inspector.getCompatibleFormats({
      codec: 'unknown',
      sampleRate: 0,
      channels: 2,
      durationSecs: 0,
    });
    expect(formats).toEqual([]);
  });
});
