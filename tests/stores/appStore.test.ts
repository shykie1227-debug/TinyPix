import { describe, it, expect } from 'vitest';
import { useAppStore } from '../../src/stores/appStore';

describe('appStore', () => {
  it('has correct initial state', () => {
    const state = useAppStore.getState();
    expect(state.files).toEqual([]);
    expect(state.isProcessing).toBe(false);
    expect(state.progress).toBe(0);
    expect(state.totalSaved).toBe(0);
  });

  it('has default options', () => {
    const state = useAppStore.getState();
    expect(state.options.outputFormat).toBe('webp');
    expect(state.options.quality).toBe(85);
    expect(state.options.resizeEnabled).toBe(true);
    expect(state.options.resizeMaxPx).toBe(1920);
    expect(state.options.stripExif).toBe(false);
    expect(state.options.preserveTransparency).toBe(true);
  });

  it('addFiles updates files array', () => {
    const { addFiles } = useAppStore.getState();
    const testFiles = [
      { id: '1', name: 'test.jpg', path: '/test.jpg', format: 'JPEG', originalSize: 100, status: 'pending' as const },
    ];
    addFiles(testFiles);
    expect(useAppStore.getState().files).toHaveLength(1);
  });

  it('clearFiles resets files', () => {
    const { clearFiles } = useAppStore.getState();
    clearFiles();
    expect(useAppStore.getState().files).toEqual([]);
  });
});