import { isSupportedImageInput } from './imageCapabilities';

export const isVideoFormat = (format: string) =>
  /^(mp4|mov|avi|mkv|webm)$/i.test(format);

export const isImageFormat = (format: string) => isSupportedImageInput(format);
