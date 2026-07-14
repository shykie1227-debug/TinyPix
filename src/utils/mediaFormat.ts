export const isVideoFormat = (format: string) =>
  /^(mp4|mov|avi|mkv|webm|flv|wmv|gif|m4v|3gp)$/i.test(format);

export const isImageFormat = (format: string) =>
  /^(jpg|jpeg|png|gif|webp|bmp|tiff|tif|avif|ico|psd|heic)$/i.test(format);
