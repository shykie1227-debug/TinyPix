export const IMAGE_INPUT_EXTENSIONS = [
  'jpg',
  'jpeg',
  'png',
  'webp',
  'avif',
  'bmp',
  'tiff',
  'tif',
  'psd',
] as const;

export type ImageInputExtension = (typeof IMAGE_INPUT_EXTENSIONS)[number];
export type ImageOutputFormat = 'jpeg' | 'png' | 'webp' | 'avif' | 'bmp';

export const IMAGE_OUTPUT_FORMATS: ReadonlyArray<{
  value: ImageOutputFormat;
  label: string;
  supportsQuality: boolean;
  supportsTransparency: boolean;
}> = [
  { value: 'jpeg', label: 'JPG', supportsQuality: true, supportsTransparency: false },
  { value: 'png', label: 'PNG', supportsQuality: false, supportsTransparency: true },
  { value: 'webp', label: 'WebP', supportsQuality: true, supportsTransparency: true },
  { value: 'avif', label: 'AVIF', supportsQuality: true, supportsTransparency: true },
  { value: 'bmp', label: 'BMP', supportsQuality: false, supportsTransparency: false },
];

export const isSupportedImageInput = (format: string): format is ImageInputExtension =>
  IMAGE_INPUT_EXTENSIONS.includes(format.trim().toLowerCase().replace(/^\./, '') as ImageInputExtension);
