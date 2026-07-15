export type VideoOutputFormat = 'MP4' | 'MOV' | 'MKV' | 'AVI' | 'WebM';
export type AudioOutputFormat = 'MP3' | 'WAV' | 'AAC' | 'FLAC';
export type OutputFormat = VideoOutputFormat | AudioOutputFormat;
export type OutputKind = 'video' | 'audio';
export type VideoQualityPreset = 'compatible' | 'balanced' | 'compact';
export type AudioMode = 'auto' | 'direct' | 'reencode';

export interface VideoOutputSettings {
  format: OutputFormat;
  qualityPreset: VideoQualityPreset;
  quality: number;
  resolutionWidth: number | null;
  resolutionHeight: number | null;
  fps: number | null;
  audioBitrate: number;
  audioMode: AudioMode;
}

export const OUTPUT_FORMATS: ReadonlyArray<{
  label: OutputFormat;
  kind: OutputKind;
  extension: string;
  title: string;
  hint: string;
}> = [
  { label: 'MP4', kind: 'video', extension: 'mp4', title: '通用视频', hint: '手机、Windows 和网页兼容性最好' },
  { label: 'MOV', kind: 'video', extension: 'mov', title: '剪辑交换', hint: '适合 Apple 设备和剪辑软件' },
  { label: 'MKV', kind: 'video', extension: 'mkv', title: '归档封装', hint: '适合多轨和高质量视频' },
  { label: 'AVI', kind: 'video', extension: 'avi', title: '传统设备', hint: '面向旧版播放器和设备' },
  { label: 'WebM', kind: 'video', extension: 'webm', title: '网页视频', hint: '适合现代浏览器' },
  { label: 'MP3', kind: 'audio', extension: 'mp3', title: '通用音频', hint: '兼容性最好的音频文件' },
  { label: 'WAV', kind: 'audio', extension: 'wav', title: '无损音频', hint: '适合后期编辑，体积较大' },
  { label: 'AAC', kind: 'audio', extension: 'aac', title: '移动音频', hint: '适合移动设备和视频制作' },
  { label: 'FLAC', kind: 'audio', extension: 'flac', title: '无损压缩', hint: '适合收藏和归档' },
];

export const QUALITY_PRESETS: Record<VideoQualityPreset, { label: string; quality: number; hint: string }> = {
  compatible: { label: '通用兼容', quality: 22, hint: '画质优先，适合分享和长期保存' },
  balanced: { label: '均衡', quality: 26, hint: '推荐，兼顾画质与文件大小' },
  compact: { label: '小体积', quality: 32, hint: '更省空间，适合发送和临时使用' },
};

const RECOMMENDED_CODECS: Record<VideoOutputFormat, { video: string; audio: string }> = {
  MP4: { video: 'h264', audio: 'aac' },
  MOV: { video: 'h264', audio: 'aac' },
  MKV: { video: 'h264', audio: 'aac' },
  AVI: { video: 'mpeg4', audio: 'mp3' },
  WebM: { video: 'vp9', audio: 'opus' },
};

export const getOutputKind = (format: OutputFormat): OutputKind =>
  OUTPUT_FORMATS.find((item) => item.label === format)?.kind ?? 'video';

export const getOutputExtension = (format: OutputFormat): string =>
  OUTPUT_FORMATS.find((item) => item.label === format)?.extension ?? 'mp4';

export const getRecommendedCodecs = (format: VideoOutputFormat) => RECOMMENDED_CODECS[format];

export const getDefaultSettings = (): VideoOutputSettings => ({
  format: 'MP4',
  qualityPreset: 'balanced',
  quality: QUALITY_PRESETS.balanced.quality,
  resolutionWidth: null,
  resolutionHeight: null,
  fps: null,
  audioBitrate: 192000,
  audioMode: 'auto',
});
