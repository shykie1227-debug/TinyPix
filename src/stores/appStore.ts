import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export interface FileItem {
  id: string;
  path: string;
  name: string;
  format: string;
  originalSize: number;
  status: 'pending' | 'processing' | 'completed' | 'error';
  outputPath?: string;
  outputSize?: number;
  error?: string;
}

export type OutputFormat = 'webp' | 'png' | 'jpeg' | 'avif' | 'original';

export interface CropPercent {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface ColorAdjust {
  brightness: number;
  contrast: number;
  saturation: number;
  sharpness: number;
}

export interface ProcessOptions {
  outputFormat: OutputFormat;
  quality: number;
  resizeEnabled: boolean;
  resizeMaxPx: number;
  stripExif: boolean;
  preserveTransparency: boolean;
  outputDir?: string;
  rotateDegrees: 0 | 90 | 180 | 270;
  cropPercent?: CropPercent;
  openAfterProcess?: boolean;
  // Edit mode
  editMode?: boolean;
  resizeTargetW?: number;
  resizeTargetH?: number;
  cropPreset?: string;
  colorAdjust?: ColorAdjust;
  flipH?: boolean;
  flipV?: boolean;
}

export type VideoPreset = 'light' | 'standard' | 'extreme';

interface AppState {
  // Files
  files: FileItem[];
  addFiles: (files: FileItem[]) => void;
  removeFile: (id: string) => void;
  updateFile: (id: string, updates: Partial<FileItem>) => void;
  clearFiles: () => void;

  // Options
  options: ProcessOptions;
  setOptions: (options: Partial<ProcessOptions>) => void;
  resetWorkspaceOptions: () => void;

  // Processing
  isProcessing: boolean;
  setProcessing: (v: boolean) => void;
  progress: number; // 0-100
  setProgress: (v: number) => void;

  // Stats
  totalSaved: number; // bytes
  addSaved: (bytes: number) => void;
  resetSaved: () => void;

  // Video compression preset (for status bar estimation)
  videoPreset: VideoPreset;
  setVideoPreset: (preset: VideoPreset) => void;

  // Preset apply helper
  applyPreset: (preset: PresetConfig) => void;
}

export interface PresetConfig {
  label: string;
  format: OutputFormat;
  quality: number;
  maxPx: number;
}

export const PRESETS: PresetConfig[] = [
  { label: '微信分享', format: 'webp', quality: 80, maxPx: 1920 },
  { label: '邮件附件', format: 'jpeg', quality: 75, maxPx: 1080 },
  { label: '网页上传', format: 'webp', quality: 70, maxPx: 800 },
  { label: '极限压缩', format: 'webp', quality: 60, maxPx: 640 },
];

const normalizePath = (path: string) => path.trim().toLowerCase();

export const useAppStore = create<AppState>()(
  persist(
    (set) => ({
      files: [],
      addFiles: (files) =>
        set((state) => {
          const existing = new Set(state.files.map((f) => normalizePath(f.path)));
          const next = files.filter((file) => {
            const key = normalizePath(file.path);
            if (!key || existing.has(key)) return false;
            existing.add(key);
            return true;
          });
          return { files: [...state.files, ...next] };
        }),
      removeFile: (id) =>
        set((state) => ({ files: state.files.filter((f) => f.id !== id) })),
      updateFile: (id, updates) =>
        set((state) => ({
          files: state.files.map((f) => (f.id === id ? { ...f, ...updates } : f)),
        })),
      clearFiles: () => set({ files: [], totalSaved: 0, progress: 0 }),

      options: {
        outputFormat: 'webp',
        quality: 85,
        resizeEnabled: true,
        resizeMaxPx: 1920,
        stripExif: false,
        preserveTransparency: true,
        outputDir: undefined,
        rotateDegrees: 0,
        openAfterProcess: false,
        cropPercent: undefined,
        editMode: false,
        resizeTargetW: undefined,
        resizeTargetH: undefined,
        cropPreset: undefined,
        colorAdjust: { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 },
        flipH: false,
        flipV: false,
      },
      setOptions: (opts) =>
        set((state) => ({ options: { ...state.options, ...opts } })),
      resetWorkspaceOptions: () =>
        set((state) => ({
          options: {
            ...state.options,
            outputFormat: 'webp',
            quality: 85,
            resizeEnabled: true,
            resizeMaxPx: 1920,
            stripExif: false,
            preserveTransparency: true,
            rotateDegrees: 0,
            cropPercent: undefined,
            editMode: false,
            resizeTargetW: undefined,
            resizeTargetH: undefined,
            cropPreset: undefined,
            colorAdjust: { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 },
            flipH: false,
            flipV: false,
          },
        })),

      isProcessing: false,
      setProcessing: (v) => set({ isProcessing: v }),
      progress: 0,
      setProgress: (v) => set({ progress: v }),

      totalSaved: 0,
      addSaved: (bytes) =>
        set((state) => ({ totalSaved: state.totalSaved + bytes })),
      resetSaved: () => set({ totalSaved: 0 }),

      videoPreset: 'standard',
      setVideoPreset: (preset) => set({ videoPreset: preset }),

      applyPreset: (preset) =>
        set((state) => ({
          options: {
            ...state.options,
            outputFormat: preset.format,
            quality: preset.quality,
            resizeEnabled: true,
            resizeMaxPx: preset.maxPx,
          },
        })),
    }),
    {
      name: 'tinypix-options',
      partialize: (state) => ({ options: state.options }),
    }
  )
);
