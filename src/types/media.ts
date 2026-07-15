export interface MediaFailure {
  failureType: string;
  message: string;
  retryable: boolean;
}

export interface MediaEngineStatus {
  ready: boolean;
  ffmpegPath: string;
  ffprobePath: string;
  version: string;
  sha256: string;
  cacheDirectory: string;
  error: string | null;
}

export interface PreviewDescriptor {
  state: 'probing' | 'generating' | 'ready' | 'error' | 'cancelled';
  kind: 'image' | 'direct-video' | 'proxy-video';
  playbackPath?: string;
  posterPath?: string;
  durationSecs?: number;
  width?: number;
  height?: number;
  fps?: number;
  container?: string;
  videoCodec?: string;
  audioCodec?: string;
  hasAudio?: boolean;
  isProxy: boolean;
  taskId?: string;
  error?: MediaFailure;
}
