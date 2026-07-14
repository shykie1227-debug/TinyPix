# TinyPix Media Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a high-fidelity TinyPix Pro media workbench matching the six provided video-tool demo screenshots, while giving image tools a coherent layout in the same product language.

**Architecture:** Keep the current Tauri 2 + React 19 + Zustand architecture. Extract reusable UI primitives for the demo workbench shell, media preview/drop zone, right-side option cards, file queues, and output-path settings so each tool can reuse one visual system without duplicating layout code.

**Tech Stack:** React 19, TypeScript, Tailwind CSS 4 tokens, Zustand, Tauri 2 dialog/invoke APIs, Rust/FFmpeg command layer already present.

## Global Constraints

- Final packaged exe must not network, upload, telemetry, CDN, remote fonts, cloud API, or auto-update checks.
- Development, build, and local testing may use network.
- Use local design references from `/Users/huashu/TinyPix/v3.0 pro/UI设计`.
- Settings button primarily configures the output path.
- Record changes and implementation lessons in `CHANGELOG.md`.
- This workspace is currently not a Git repository; do not claim commits were made.

---

### Task 1: Workbench Shell And Navigation

**Files:**
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/App.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/Sidebar.tsx`
- Create: `/Users/huashu/Desktop/v3.0 pro/src/components/OutputSettingsPanel.tsx`
- Test: `/Users/huashu/Desktop/v3.0 pro/tests/components/AppWorkbench.test.tsx`

**Interfaces:**
- Produces: `OutputSettingsPanel({ onClose }: { onClose: () => void })`.
- Produces: Sidebar prop `onOpenSettings?: () => void`.
- Consumes: `useAppStore().options.outputDir` and `setOptions({ outputDir })`.

- [ ] **Step 1: Write failing UI tests**

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from '../../src/App';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('App workbench shell', () => {
  beforeEach(() => localStorage.clear());

  it('opens directly on the video compression workbench', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: '工作区' })).toBeInTheDocument();
    expect(screen.getByText('视频压缩')).toBeInTheDocument();
    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
  });

  it('uses settings as output path configuration', async () => {
    render(<App />);
    await userEvent.click(screen.getByRole('button', { name: '设置' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('输出路径');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node node_modules/vitest/vitest.mjs run --no-cache --config /private/tmp/tinypix-vitest.config.mjs tests/components/AppWorkbench.test.tsx`
Expected: FAIL because the settings panel does not exist and initial tab may not be the video workbench.

- [ ] **Step 3: Implement the shell**

Update `App.tsx` to default `activeTab` to `'video'`, render the floating image action only for image tools, and mount `OutputSettingsPanel`. Update `Sidebar.tsx` to match the demo sidebar: brand, image/video tool groups, active lime item, and bottom settings button wired to `onOpenSettings`.

- [ ] **Step 4: Implement output settings**

Create `OutputSettingsPanel.tsx` with a modal card, current output path display, directory picker via `open({ directory: true })`, reset-to-source-folder action, close button, and no network behavior.

- [ ] **Step 5: Verify task**

Run the focused test, then `node node_modules/typescript/bin/tsc --noEmit`.

### Task 2: Shared Demo UI Primitives

**Files:**
- Create: `/Users/huashu/Desktop/v3.0 pro/src/components/MediaPreviewStage.tsx`
- Create: `/Users/huashu/Desktop/v3.0 pro/src/components/ToolOptionCard.tsx`
- Create: `/Users/huashu/Desktop/v3.0 pro/src/components/MediaQueue.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/DropZone.tsx`
- Test: `/Users/huashu/Desktop/v3.0 pro/tests/components/DropZone.test.tsx`

**Interfaces:**
- Produces: `MediaPreviewStage` for video preview, placeholder upload stage, thumbnails, trim lanes, and waveform mock sections.
- Produces: `ToolOptionCard` for right-side demo cards with title, children, and optional footer.
- Produces: `MediaQueue` for compact file rows.

- [ ] **Step 1: Write failing tests for copy and format chips**

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import DropZone from '../../src/components/DropZone';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('DropZone demo copy', () => {
  it('shows video demo copy and MKV chip', () => {
    render(<DropZone mediaType="video" onFilesAdded={() => {}} />);
    expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
    expect(screen.getByText('MKV')).toBeInTheDocument();
  });

  it('shows image copy in the same visual language', () => {
    render(<DropZone mediaType="image" onFilesAdded={() => {}} />);
    expect(screen.getByText('拖拽图片文件到这里')).toBeInTheDocument();
    expect(screen.getByText('WebP')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node node_modules/vitest/vitest.mjs run --no-cache --config /private/tmp/tinypix-vitest.config.mjs tests/components/DropZone.test.tsx`
Expected: FAIL if old text or WebP video chip remains.

- [ ] **Step 3: Implement primitives**

Add reusable card/stage/queue components using existing color tokens: `surface-container-lowest`, `secondary-fixed`, `primary`, 24px cards, restrained shadows, local font fallbacks.

- [ ] **Step 4: Update DropZone**

Use demo copy, correct video chips `MP4/MOV/MKV`, include max-size hints, and preserve Tauri file picker behavior.

- [ ] **Step 5: Verify task**

Run DropZone tests and `tsc --noEmit`.

### Task 3: Video Tools High-Fidelity Implementation

**Files:**
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/Compressor.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/GifMaker.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/VideoScreenshot.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/VideoConverter.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/VideoTrimmer.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/VideoEditor.tsx`
- Test: `/Users/huashu/Desktop/v3.0 pro/tests/components/VideoWorkbench.test.tsx`

**Interfaces:**
- Consumes: `MediaPreviewStage`, `ToolOptionCard`, `MediaQueue`.
- Preserves: existing Tauri commands `compress_video`, `create_gif`, `extract_frame`, `export_thumbnail`, `convert_video_format`, `extract_audio`, `trim_video`, `mirror_video`, `rotate_video`, `change_video_speed`.

- [ ] **Step 1: Write focused render tests**

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import Compressor from '../../src/components/Compressor';
import GifMaker from '../../src/components/GifMaker';
import VideoScreenshot from '../../src/components/VideoScreenshot';
import VideoConverter from '../../src/components/VideoConverter';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('Video workbench tools', () => {
  it('renders compression presets and resolution options', () => {
    render(<Compressor embedded />);
    expect(screen.getByText('压缩等级')).toBeInTheDocument();
    expect(screen.getByText('标准压缩')).toBeInTheDocument();
    expect(screen.getByText('输出分辨率')).toBeInTheDocument();
  });

  it('renders gif, screenshot, and conversion controls', () => {
    render(<GifMaker />);
    expect(screen.getByText('开始转换')).toBeInTheDocument();
    render(<VideoScreenshot />);
    expect(screen.getByText('PNG')).toBeInTheDocument();
    render(<VideoConverter />);
    expect(screen.getByText('处理队列')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node node_modules/vitest/vitest.mjs run --no-cache --config /private/tmp/tinypix-vitest.config.mjs tests/components/VideoWorkbench.test.tsx`
Expected: FAIL while old labels/layout remain.

- [ ] **Step 3: Implement six video demo layouts**

Map screenshots to components:
- Compression: large upload stage + compression radio cards + resolution grid + black primary action.
- GIF: large preview, time controls, FPS/quality slider, “开始转换”.
- Screenshot: preview with thumbnail strip, format segmented control, interval/cover toggles.
- Conversion: upload area + processing queue + output format grid + preset list + quality slider.
- Editor: preview, playback controls, timeline selection, transform card, output card.
- Audio extraction: upload stage + waveform card + audio format grid + quality slider.

- [ ] **Step 4: Preserve backend actions**

Keep all existing Tauri invocations and disabled states; only improve layout/copy unless a backend argument is clearly already supported.

- [ ] **Step 5: Verify task**

Run focused video tests, `tsc --noEmit`, and visually check the Vite screen.

### Task 4: Image Tools Layout And Product Completion

**Files:**
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/ControlPanel.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/ImageRotator.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/components/Cropper.tsx`
- Modify: `/Users/huashu/Desktop/v3.0 pro/src/App.tsx`
- Test: `/Users/huashu/Desktop/v3.0 pro/tests/components/ImageWorkbench.test.tsx`

**Interfaces:**
- Consumes: `ToolOptionCard`, `MediaQueue`, `useImageProcessor()`.
- Preserves: image process options `outputFormat`, `quality`, `resizeEnabled`, `resizeMaxPx`, `stripExif`, `outputDir`.

- [ ] **Step 1: Write focused tests**

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import ControlPanel from '../../src/components/ControlPanel';

vi.mock('@tauri-apps/plugin-dialog', () => ({ open: vi.fn() }));
vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));

describe('Image workbench', () => {
  it('renders image optimization controls in video-demo style', () => {
    render(<ControlPanel onProcess={() => {}} estimateSizeBatch={async () => 0} />);
    expect(screen.getByText('输出格式')).toBeInTheDocument();
    expect(screen.getByText('质量压缩')).toBeInTheDocument();
    expect(screen.getByText('隐私信息')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node node_modules/vitest/vitest.mjs run --no-cache --config /private/tmp/tinypix-vitest.config.mjs tests/components/ImageWorkbench.test.tsx`
Expected: FAIL until labels/layout are updated.

- [ ] **Step 3: Implement image optimization panel**

Use the same right-side card system with format segmented controls, quality slider, resize preset grid, EXIF privacy toggle, output path summary, and black primary action.

- [ ] **Step 4: Polish rotate and crop tools**

Make rotate/crop use preview/stage + right card controls instead of isolated forms, preserving existing options and actions.

- [ ] **Step 5: Verify task**

Run image tests and `tsc --noEmit`.

### Task 5: Validation, Runtime Safety, And Changelog

**Files:**
- Modify: `/Users/huashu/Desktop/v3.0 pro/CHANGELOG.md`
- Optional modify: `/Users/huashu/Desktop/v3.0 pro/README.md`

**Interfaces:**
- Produces: audit record for UI changes, testing, and remaining known limitations.

- [ ] **Step 1: Run full local verification**

Run:
```bash
node node_modules/typescript/bin/tsc --noEmit
node node_modules/vite/bin/vite.js build
env CARGO_TARGET_DIR=/private/tmp/tinypix-cargo-target cargo check
```

Expected: PASS for TypeScript, Vite build, and Rust check.

- [ ] **Step 2: Run targeted component tests**

Run:
```bash
node node_modules/vitest/vitest.mjs run --no-cache --config /private/tmp/tinypix-vitest.config.mjs tests/components/AppWorkbench.test.tsx tests/components/DropZone.test.tsx tests/components/VideoWorkbench.test.tsx tests/components/ImageWorkbench.test.tsx
```

Expected: PASS for new/updated focused tests.

- [ ] **Step 3: Audit runtime no-network rule**

Run:
```bash
rg -n "fetch\\(|XMLHttpRequest|axios|sentry|telemetry|googleapis|gstatic|cdn\\.|https?://" src src-tauri/src src-tauri/tauri.conf.json index.html package.json src-tauri/Cargo.toml
```

Expected: no runtime external-network code. Development-only references must be documented if present.

- [ ] **Step 4: Update changelog**

Add a 2026-06-24 entry covering high-fidelity demo alignment, output-path settings, image tool layout completion, test/build results, and implementation lessons.

- [ ] **Step 5: Final handoff**

Report changed files, verification commands, known pre-existing test gaps, and manual exe build reminder.
