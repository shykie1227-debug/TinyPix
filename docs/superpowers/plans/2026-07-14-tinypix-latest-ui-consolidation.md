# TinyPix Pro 3.5 最新 UI 收敛与完整功能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以当前 `main` 为唯一基线，落地最新信息架构——视频入口从 5 个收敛为 3 个、图片改为单一“图片处理”工作台，并补齐所有可见图片编辑能力、响应式布局、离线 Windows 便携 EXE 与真实桌面验收。

**Architecture:** 视频侧新增一个 `VideoOutput` 编排组件，将格式转换、压缩质量和音频提取统一到同一输出流程；GIF 与视频剪辑保持独立。图片侧拆分为单一工作台、统一控制面板和 Rust 图像处理管线，所有预览控件必须对应真实导出参数。导航、产品文档、测试与 Windows 构建均以新信息架构为准，旧组件先保留为未路由回退实现，确认新流程稳定后再单独清理。

**Tech Stack:** Tauri 2、React 19、TypeScript、Zustand、Vitest、Testing Library、Rust、`image 0.25`、`react-image-crop`、FFmpeg、Windows 10/11 x64。

## Global Constraints

- 唯一源码目录是 `/Users/huashu/TinyPix/3.5pro`，Git 根目录是 `/Users/huashu/TinyPix`。
- 当前功能基线是 `main`；开始实现前创建隔离 worktree，执行期使用 `superpowers:using-git-worktrees`。
- 视频入口固定为 `视频输出`、`GIF 制作`、`视频剪辑`；首次打开进入 `视频输出`。
- 图片入口固定为 `图片处理`；不得再出现“导出模式/编辑模式”切换。
- 图片单面板采用“基础导出常驻 + 裁切与旋转/调整尺寸/色彩调整折叠区”，高级折叠区默认收起。
- 视频输出采用“基础参数常驻 + 高级设置折叠”；默认 MP4、H.264、AAC、标准质量、原始尺寸、原始帧率。
- 音频输出默认自动判断：编码和容器兼容时直接提取，否则重编码；高级设置允许选择自动、直接提取、重编码。
- 图片编辑的裁切、旋转、镜像、精确尺寸、亮度、对比度、饱和度、锐化、透明通道策略必须进入最终导出文件，不能只改变预览。
- 图片输入只展示经过本地端到端验证的 JPG/JPEG、PNG、WebP、AVIF、BMP、TIFF/TIF、PSD；移除 PDF、PPT/PPTX、AI、EPS、SVG、HEIC 等未实现承诺。
- 图片输出固定为 JPG、PNG、WebP、AVIF、BMP，每一种必须由 Rust 实际编码，不得将 BMP 静默映射为 PNG。
- EXE 在其他电脑运行时不得联网、上传、检查更新、加载远程字体/图片或发送遥测；FFmpeg/FFprobe 必须随便携产物交付。
- 目标窗口是默认 `1200×800`、最小 `900×600`；两种尺寸均不得出现横向溢出、按钮截断或无法触达的主操作。
- 所有行为改动遵循 TDD：先观察失败，再做最小实现；每个任务独立通过后提交一次，不自动推送。
- 当前基线为 38 个测试文件、589 个测试通过；完成后不得减少有效覆盖，测试日志不得残留未包裹 `act(...)` 的警告。

---

## File Structure

### 新建文件

- `src/modules/video/outputProfiles.ts`：视频/音频输出格式、默认编码、质量预设、提示文案和模式判断的唯一来源。
- `src/components/video/VideoOutput.tsx`：视频输出页面的状态、批处理编排、基础/高级 UI 和错误状态。
- `src/components/common/CollapsibleSection.tsx`：图片与视频共用的可访问折叠区。
- `src/components/image/ImageControlsPanel.tsx`：图片处理右栏编排器，只负责组合各控制分区和唯一 CTA。
- `src/components/image/ImageExportControls.tsx`：格式、质量、隐私、透明度与大小估算。
- `src/components/image/ImageTransformControls.tsx`：裁切开关、旋转、镜像和精确尺寸。
- `src/components/image/ImageColorControls.tsx`：亮度、对比度、饱和度、锐化。
- `src/utils/imageCapabilities.ts`：前端输入/输出格式能力表和扩展名判断。
- `tests/components/VideoOutput.test.tsx`：新视频输出流程的组件与命令契约测试。
- `tests/components/CollapsibleSection.test.tsx`：折叠区语义和键盘可访问性测试。
- `tests/components/ImageControlsPanel.test.tsx`：图片单面板交互和 store 参数测试。
- `tests/modules/video/outputProfiles.test.ts`：视频输出默认值、格式分类和编码推荐测试。
- `tests/utils/imageCapabilities.test.ts`：真实格式白名单测试。

### 修改文件

- `src/App.tsx`：工作区类型从 6 项改为 4 项，视频默认路由到 `VideoOutput`，固定中心/右栏布局。
- `src/components/layout/Sidebar.tsx`：视频 3 项、图片 1 项、侧栏宽度 `w-56`。
- `src/components/image/ImageWorkbench.tsx`：删除双模式状态，始终渲染中心预览和 `ImageControlsPanel`。
- `src/components/layout/DropZone.tsx`、`src/components/layout/HomePage.tsx`、`src/utils/mediaFormat.ts`：所有图片选择入口复用真实能力表。
- `src/components/preview/MediaPreviewStage.tsx`：裁切默认值改为全图，只有启用裁切时才写入 `cropPercent`。
- `src/components/preview/ImagePreviewStage.tsx`：根据 `cropEnabled` 显示裁切框，并继续预览旋转/镜像/色彩。
- `src/hooks/useImageProcessor.ts`：将精确尺寸、镜像和色彩参数传给 `process_images`。
- `src/stores/appStore.ts`：新增 `cropEnabled`，删除失去意义的 `editMode`，统一图片参数默认值。
- `src-tauri/Cargo.toml`、`src-tauri/Cargo.lock`：固定 `webp 0.3.1`，让 WebP 质量参数真实生效并避开旧版内存暴露问题。
- `src-tauri/src/commands/process_commands.rs`：扩展 `ProcessOptions` 并映射到 `ImageProcessItem`。
- `src-tauri/src/commands/file_commands.rs`：后端支持格式列表与前端能力表保持一致。
- `src-tauri/src/domain/image_engine.rs`：实现镜像、精确尺寸、色彩、锐化、AVIF/BMP 编码和验证。
- `tests/components/AppWorkbench.test.tsx`、`Sidebar.test.tsx`、`ImageWorkbench.test.tsx`、`VideoCommandArgs.test.tsx`：更新新信息架构和命令断言。
- `tests/hooks/useImageProcessor.test.ts`：验证新增图片参数完整传递。
- `AGENTS.md`、`DESIGN.md`、`CHANGELOG.md`：将旧“视频五项/图片导出”规则替换为本计划确认的新基线。

### 暂时保留但不再路由

- `src/components/video/Compressor.tsx`
- `src/components/video/VideoConverter.tsx`
- `src/components/video/AudioExtractor.tsx`
- `src/components/image/EditPanel.tsx`
- `src/components/image/ExportPanel.tsx`

这些文件在本计划中不删除，避免把 UI 迁移与大规模清理混为一个风险面。新流程验收后另开清理任务，届时再删除死代码和对应旧测试。

---

### Task 1: 锁定最新产品契约与共用折叠组件

**Files:**
- Create: `src/components/common/CollapsibleSection.tsx`
- Modify: `AGENTS.md`
- Modify: `DESIGN.md`
- Test: `tests/components/CollapsibleSection.test.tsx`

**Interfaces:**
- Consumes: React `useId` 和现有 TinyPix 设计 token。
- Produces: `CollapsibleSection({ title, description?, defaultOpen?, children })`；后续视频和图片面板共同使用。

- [ ] **Step 1: 写折叠区失败测试**

```tsx
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import CollapsibleSection from '../../src/components/common/CollapsibleSection';

it('通过按钮展开并暴露可访问状态', async () => {
  render(
    <CollapsibleSection title="高级设置">
      <div>编码器</div>
    </CollapsibleSection>
  );
  const trigger = screen.getByRole('button', { name: '高级设置' });
  expect(trigger).toHaveAttribute('aria-expanded', 'false');
  expect(screen.queryByText('编码器')).not.toBeInTheDocument();
  await userEvent.click(trigger);
  expect(trigger).toHaveAttribute('aria-expanded', 'true');
  expect(screen.getByText('编码器')).toBeInTheDocument();
});
```

- [ ] **Step 2: 运行测试确认失败**

Run: `npx vitest run tests/components/CollapsibleSection.test.tsx --no-cache`

Expected: FAIL，原因是 `CollapsibleSection` 文件不存在。

- [ ] **Step 3: 实现最小可访问折叠组件**

```tsx
import { useId, useState, type ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';

interface CollapsibleSectionProps {
  title: string;
  description?: string;
  defaultOpen?: boolean;
  children: ReactNode;
}

export default function CollapsibleSection({
  title,
  description,
  defaultOpen = false,
  children,
}: CollapsibleSectionProps) {
  const [open, setOpen] = useState(defaultOpen);
  const contentId = useId();
  return (
    <section className="rounded-[18px] border border-outline-variant/10 bg-surface-container-lowest">
      <button
        type="button"
        className="flex min-h-11 w-full items-center gap-3 px-4 py-3 text-left hover:opacity-80"
        aria-expanded={open}
        aria-controls={contentId}
        onClick={() => setOpen((value) => !value)}
      >
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-semibold text-on-surface">{title}</span>
          {description && <span className="block text-xs text-on-surface-variant">{description}</span>}
        </span>
        <ChevronDown size={18} className={open ? 'rotate-180 transition-transform' : 'transition-transform'} />
      </button>
      {open && <div id={contentId} className="border-t border-outline-variant/10 p-4">{children}</div>}
    </section>
  );
}
```

- [ ] **Step 4: 更新产品约束文档**

在 `AGENTS.md` 和 `DESIGN.md` 中写入以下确定文本，并删除所有“视频五项”“图片导出单入口”“首次视频压缩”等旧规则：

```markdown
- 首屏进入“视频输出”。视频工具固定为：视频输出、GIF 制作、视频剪辑。
- 图片工具固定为单一“图片处理”工作台；格式、质量、隐私、裁切、旋转、镜像、尺寸和色彩属于同一流程。
- 默认窗口 1200×800，最小窗口 900×600；主操作在两种尺寸均可触达。
```

- [ ] **Step 5: 验证并提交**

Run: `npx vitest run tests/components/CollapsibleSection.test.tsx --no-cache`

Expected: PASS，1 个测试通过且无 `act(...)` 警告。

```bash
git add AGENTS.md DESIGN.md src/components/common/CollapsibleSection.tsx tests/components/CollapsibleSection.test.tsx
git commit -m "docs: lock latest three-entry UI contract"
```

---

### Task 2: 建立视频输出格式与默认策略的纯函数契约

**Files:**
- Create: `src/modules/video/outputProfiles.ts`
- Test: `tests/modules/video/outputProfiles.test.ts`
- Modify: `src/utils/videoOutput.ts`
- Test: `tests/utils/videoOutput.test.ts`

**Interfaces:**
- Consumes: `AudioFormatLabel`、文件扩展名和输出目录。
- Produces: `OutputFormat`、`VideoOutputSettings`、`getOutputKind()`、`getDefaultSettings()`、`getOutputPath()`。

- [ ] **Step 1: 写默认策略和格式分类失败测试**

```ts
import { describe, expect, it } from 'vitest';
import {
  getDefaultSettings,
  getOutputKind,
  OUTPUT_FORMATS,
} from '../../../src/modules/video/outputProfiles';

describe('video output profiles', () => {
  it('使用 MP4 均衡模式作为首次默认值', () => {
    expect(getDefaultSettings()).toEqual({
      format: 'MP4',
      qualityPreset: 'standard',
      quality: 26,
      videoCodec: 'h264',
      audioCodec: 'aac',
      resolutionWidth: null,
      resolutionHeight: null,
      fps: null,
      audioBitrate: 192000,
      audioMode: 'auto',
    });
  });

  it('将五种视频和四种音频格式分组', () => {
    expect(OUTPUT_FORMATS.filter((item) => item.kind === 'video')).toHaveLength(5);
    expect(OUTPUT_FORMATS.filter((item) => item.kind === 'audio')).toHaveLength(4);
    expect(getOutputKind('WebM')).toBe('video');
    expect(getOutputKind('FLAC')).toBe('audio');
  });
});
```

- [ ] **Step 2: 运行测试确认失败**

Run: `npx vitest run tests/modules/video/outputProfiles.test.ts --no-cache`

Expected: FAIL，原因是模块不存在。

- [ ] **Step 3: 实现完整类型和格式表**

```ts
export type VideoOutputFormat = 'MP4' | 'MOV' | 'AVI' | 'MKV' | 'WebM';
export type AudioOutputFormat = 'MP3' | 'WAV' | 'AAC' | 'FLAC';
export type OutputFormat = VideoOutputFormat | AudioOutputFormat;
export type OutputKind = 'video' | 'audio';
export type VideoQualityPreset = 'light' | 'standard' | 'extreme';
export type AudioMode = 'auto' | 'direct' | 'reencode';

export interface VideoOutputSettings {
  format: OutputFormat;
  qualityPreset: VideoQualityPreset;
  quality: number;
  videoCodec: string;
  audioCodec: string;
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
  { label: 'MP4', kind: 'video', extension: 'mp4', title: '通用视频', hint: '兼容 Windows、手机、网页与社交平台' },
  { label: 'MOV', kind: 'video', extension: 'mov', title: 'Apple 视频', hint: '适合 Apple 设备和剪辑软件' },
  { label: 'AVI', kind: 'video', extension: 'avi', title: '传统兼容', hint: '适合旧设备和旧版软件' },
  { label: 'MKV', kind: 'video', extension: 'mkv', title: '高质量封装', hint: '适合归档和多轨媒体' },
  { label: 'WebM', kind: 'video', extension: 'webm', title: '网页视频', hint: '适合网页和现代浏览器' },
  { label: 'MP3', kind: 'audio', extension: 'mp3', title: '提取音频', hint: '兼容性最高的有损音频' },
  { label: 'WAV', kind: 'audio', extension: 'wav', title: '提取无损音频', hint: '体积较大，适合后期编辑' },
  { label: 'AAC', kind: 'audio', extension: 'aac', title: '提取音频', hint: '适合移动设备和视频制作' },
  { label: 'FLAC', kind: 'audio', extension: 'flac', title: '提取无损音频', hint: '无损压缩，适合收藏和归档' },
];

export const QUALITY_PRESETS = {
  light: { label: '高质量', quality: 20 },
  standard: { label: '均衡', quality: 26 },
  extreme: { label: '小体积', quality: 34 },
} as const;

const RECOMMENDED_CODECS: Record<VideoOutputFormat, { video: string; audio: string }> = {
  MP4: { video: 'h264', audio: 'aac' },
  MOV: { video: 'h264', audio: 'aac' },
  AVI: { video: 'h264', audio: 'mp3' },
  MKV: { video: 'h265', audio: 'aac' },
  WebM: { video: 'vp9', audio: 'opus' },
};

export const getOutputKind = (format: OutputFormat): OutputKind =>
  OUTPUT_FORMATS.find((item) => item.label === format)!.kind;

export const getDefaultSettings = (): VideoOutputSettings => ({
  format: 'MP4',
  qualityPreset: 'standard',
  quality: 26,
  videoCodec: 'h264',
  audioCodec: 'aac',
  resolutionWidth: null,
  resolutionHeight: null,
  fps: null,
  audioBitrate: 192000,
  audioMode: 'auto',
});

export const applyFormatDefaults = (
  current: VideoOutputSettings,
  format: OutputFormat
): VideoOutputSettings => {
  if (getOutputKind(format) === 'audio') return { ...current, format };
  const codecs = RECOMMENDED_CODECS[format as VideoOutputFormat];
  return { ...current, format, videoCodec: codecs.video, audioCodec: codecs.audio };
};
```

- [ ] **Step 4: 为统一输出路径增加失败测试并实现**

```ts
it('按输出类型生成明确后缀', () => {
  expect(getOutputPath('C:\\Media\\demo.mov', 'MP4', 'C:\\Out')).toBe('C:\\Out\\demo_output.mp4');
  expect(getOutputPath('/media/demo.mp4', 'MP3')).toBe('/media/demo_audio.mp3');
});
```

在 `src/utils/videoOutput.ts` 中增加：

```ts
import type { OutputFormat } from '../modules/video/outputProfiles';
import { getOutputKind, OUTPUT_FORMATS } from '../modules/video/outputProfiles';

export const getOutputPath = (path: string, format: OutputFormat, outputDir?: string) => {
  const extension = OUTPUT_FORMATS.find((item) => item.label === format)!.extension;
  const suffix = getOutputKind(format) === 'audio' ? '_audio' : '_output';
  return withVideoSuffix(path, suffix, extension, outputDir);
};
```

- [ ] **Step 5: 验证并提交**

Run: `npx vitest run tests/modules/video/outputProfiles.test.ts tests/utils/videoOutput.test.ts --no-cache`

Expected: PASS，格式数量、默认值、Windows/POSIX 输出路径全部通过。

```bash
git add src/modules/video/outputProfiles.ts src/utils/videoOutput.ts tests/modules/video/outputProfiles.test.ts tests/utils/videoOutput.test.ts
git commit -m "feat: define unified video output profiles"
```

---

### Task 3: 实现“视频输出”组件和自动音频策略

**Files:**
- Create: `src/components/video/VideoOutput.tsx`
- Create: `tests/components/VideoOutput.test.tsx`
- Modify: `src/modules/audio/formatConverter.ts`
- Modify: `src/modules/audio/inspector.ts`

**Interfaces:**
- Consumes: `VideoOutputSettings`、`getOutputPath()`、`AudioFileInspector.inspect()`、`invokeDirectExtract()`、`invokeConvert()`、`useAppStore()`。
- Produces: 默认导出的 `VideoOutput` React 组件；视频调用 `convert_video_format`，音频调用现有音频模块桥接。

- [ ] **Step 1: 写默认 UI 和视频命令失败测试**

```tsx
const videoFile: FileItem = {
  id: 'video-1', path: 'C:\\Media\\demo.mov', name: 'demo.mov',
  format: 'MOV', originalSize: 1024, status: 'pending',
};
const mp3VideoFile: FileItem = {
  id: 'video-2', path: 'C:\\Media\\mp3-source.mp4', name: 'mp3-source.mp4',
  format: 'MP4', originalSize: 1024, status: 'pending',
};
const aacVideoFile: FileItem = {
  id: 'video-3', path: 'C:\\Media\\aac-source.mov', name: 'aac-source.mov',
  format: 'MOV', originalSize: 1024, status: 'pending',
};

it('默认显示 MP4 均衡模式并调用完整视频命令', async () => {
  useAppStore.getState().addFiles([videoFile]);
  render(<VideoOutput />);
  expect(screen.getByRole('button', { name: 'MP4' })).toHaveAttribute('aria-pressed', 'true');
  expect(screen.getByRole('radio', { name: /均衡/ })).toBeChecked();
  await userEvent.click(screen.getByRole('button', { name: '开始处理' }));
  await waitFor(() => expect(invoke).toHaveBeenCalledWith('convert_video_format', {
    inputPath: 'C:\\Media\\demo.mov',
    outputPath: 'C:\\Media\\demo_output.mp4',
    targetFormat: 'mp4',
    quality: 26,
    videoCodec: 'h264',
    resolutionWidth: null,
    resolutionHeight: null,
    fps: null,
    audioCodec: 'aac',
    audioBitrate: 192000,
  }));
});
```

- [ ] **Step 2: 写自动音频策略失败测试**

```tsx
it('兼容时直接提取，不兼容时自动重编码', async () => {
  vi.spyOn(AudioFileInspector.prototype, 'inspect')
    .mockResolvedValueOnce({ codec: 'mp3', sampleRate: 44100, channels: 2, durationSecs: 10, bitrateKbps: 192 })
    .mockResolvedValueOnce({ codec: 'aac', sampleRate: 48000, channels: 2, durationSecs: 20, bitrateKbps: 128 });
  useAppStore.getState().addFiles([mp3VideoFile, aacVideoFile]);
  render(<VideoOutput />);
  await userEvent.click(screen.getByRole('button', { name: 'MP3' }));
  await userEvent.click(screen.getByRole('button', { name: '提取音频' }));
  await waitFor(() => expect(invokeDirectExtract).toHaveBeenCalledTimes(1));
  expect(invokeConvert).toHaveBeenCalledTimes(1);
});
```

- [ ] **Step 3: 运行测试确认失败**

Run: `npx vitest run tests/components/VideoOutput.test.tsx --no-cache`

Expected: FAIL，原因是 `VideoOutput` 尚不存在。

- [ ] **Step 4: 实现批处理编排核心**

`VideoOutput.tsx` 必须使用以下分支，不得直接传 `app`，所有 Tauri 参数保持 camelCase：

```ts
const processFile = async (file: FileItem, settings: VideoOutputSettings) => {
  const outputPath = getOutputPath(file.path, settings.format, options.outputDir);
  if (getOutputKind(settings.format) === 'video') {
    return invoke('convert_video_format', {
      inputPath: file.path,
      outputPath,
      targetFormat: settings.format.toLowerCase(),
      quality: settings.quality,
      videoCodec: settings.videoCodec,
      resolutionWidth: settings.resolutionWidth,
      resolutionHeight: settings.resolutionHeight,
      fps: settings.fps,
      audioCodec: settings.audioCodec,
      audioBitrate: settings.audioBitrate,
    });
  }

  const format = findFormat(settings.format as AudioFormatLabel)!;
  const source = await new AudioFileInspector().inspect(file.path);
  const compatible = isCodecCompatibleWithFormat(source.codec, format.label);
  const mode = settings.audioMode === 'auto'
    ? compatible ? 'direct' : 'reencode'
    : settings.audioMode;
  if (mode === 'direct' && !compatible) {
    throw new Error(`源音频 ${source.codec} 无法直接保存为 ${format.label}`);
  }
  return mode === 'direct'
    ? invokeDirectExtract({ inputPath: file.path, outputPath })
    : invokeConvert({
        inputPath: file.path,
        outputPath,
        format,
        bitrateKbps: format.lossless ? undefined : settings.audioBitrate / 1000,
      });
};
```

批处理循环必须更新全局文件状态并在单个失败后继续：

```ts
const handleProcess = async () => {
  if (processing || videoFiles.length === 0) return;
  setProcessing(true);
  setStatus('准备处理…');
  let completed = 0;
  let failed = 0;
  for (const file of videoFiles) {
    updateFile(file.id, { status: 'processing', error: undefined });
    try {
      const result = await processFile(file, settings) as {
        output_path?: string; output_size?: number; outputPath?: string; outputSize?: number;
      };
      updateFile(file.id, {
        status: 'completed',
        outputPath: result.output_path ?? result.outputPath,
        outputSize: result.output_size ?? result.outputSize,
      });
      completed += 1;
    } catch (error) {
      updateFile(file.id, { status: 'error', error: String(error) });
      failed += 1;
    }
    setStatus(`处理中 ${completed + failed}/${videoFiles.length}`);
  }
  if (completed > 0 && options.openAfterProcess && options.outputDir?.trim()) {
    try {
      await invoke('open_folder', { path: options.outputDir });
    } catch (error) {
      setStatus(`完成 ${completed} 个，失败 ${failed} 个；打开目录失败：${String(error)}`);
      setProcessing(false);
      return;
    }
  }
  setStatus(`完成 ${completed} 个，失败 ${failed} 个`);
  setProcessing(false);
};
```

- [ ] **Step 5: 实现完整 UI 状态**

组件直接渲染以下结构，不引入未定义的中间组件：

```tsx
<div className="flex max-h-[calc(100vh-8rem)] flex-col gap-3 overflow-y-auto pb-20">
  <ToolOptionCard>
    <div className="grid grid-cols-5 gap-2">
      {OUTPUT_FORMATS.map((item) => (
        <button
          key={item.label}
          type="button"
          aria-pressed={settings.format === item.label}
          onClick={() => setSettings((value) => applyFormatDefaults(value, item.label))}
          className={settings.format === item.label ? 'rounded-full bg-secondary-fixed px-2 py-2 text-xs font-bold' : 'rounded-full bg-surface-container-low px-2 py-2 text-xs'}
        >
          {item.label}
        </button>
      ))}
    </div>
    <p className="mt-3 text-xs text-on-surface-variant">
      {OUTPUT_FORMATS.find((item) => item.label === settings.format)!.hint}
    </p>
  </ToolOptionCard>
  {kind === 'video' && (
    <ToolOptionCard>
      <fieldset className="space-y-2">
        <legend className="text-xs font-semibold">质量</legend>
        {Object.entries(QUALITY_PRESETS).map(([value, preset]) => (
          <label key={value} className="flex min-h-11 items-center gap-2">
            <input
              type="radio"
              name="quality"
              value={value}
              checked={settings.qualityPreset === value}
              onChange={() => setSettings((current) => ({
                ...current,
                qualityPreset: value as VideoQualityPreset,
                quality: preset.quality,
              }))}
            />
            {preset.label}
          </label>
        ))}
      </fieldset>
    </ToolOptionCard>
  )}
  <CollapsibleSection title="高级设置" description="编码、尺寸、帧率与音频策略">
    {kind === 'video' ? (
      <div className="space-y-3">
        <label className="block text-xs">视频编码
          <select value={settings.videoCodec} onChange={(event) => setSettings((value) => ({ ...value, videoCodec: event.target.value }))}>
            {['h264', 'h265', 'av1', 'vp9', 'prores'].map((codec) => <option key={codec} value={codec}>{codec}</option>)}
          </select>
        </label>
        <label className="block text-xs">音频编码
          <select value={settings.audioCodec} onChange={(event) => setSettings((value) => ({ ...value, audioCodec: event.target.value }))}>
            {['aac', 'mp3', 'opus', 'flac', 'ac3'].map((codec) => <option key={codec} value={codec}>{codec}</option>)}
          </select>
        </label>
        <label className="block text-xs">帧率
          <select value={settings.fps ?? 'source'} onChange={(event) => setSettings((value) => ({ ...value, fps: event.target.value === 'source' ? null : Number(event.target.value) }))}>
            <option value="source">原始</option><option value="24">24</option><option value="30">30</option><option value="60">60</option>
          </select>
        </label>
      </div>
    ) : (
      <fieldset className="space-y-2">
        <legend className="text-xs font-semibold">音频处理方式</legend>
        {(['auto', 'direct', 'reencode'] as const).map((mode) => (
          <label key={mode} className="flex min-h-11 items-center gap-2">
            <input type="radio" name="audio-mode" checked={settings.audioMode === mode} onChange={() => setSettings((value) => ({ ...value, audioMode: mode }))} />
            {{ auto: '自动判断', direct: '直接提取', reencode: '重编码' }[mode]}
          </label>
        ))}
      </fieldset>
    )}
  </CollapsibleSection>
  <div className="sticky bottom-0 bg-surface-bright pt-2">
    <button type="button" onClick={handleProcess} disabled={!hasVideos || processing} className="btn-apple btn-apple-primary min-h-11 w-full">
      {processing ? '处理中…' : kind === 'audio' ? '提取音频' : '开始处理'}
    </button>
    <p role="status" aria-live="polite" className="mt-2 text-xs text-on-surface-variant">{status}</p>
  </div>
</div>
```

分辨率高级区沿用 `VideoConverter` 已验证的“原始/1080p/720p/480p/4K/自定义”值和 `64..7680` 偶数校验；自定义尺寸只有同时合法时写入 `resolutionWidth/resolutionHeight`。

- [ ] **Step 6: 增加批量错误和输出目录测试**

```tsx
it('单个文件失败后继续处理并汇总结果', async () => {
  vi.mocked(invoke)
    .mockRejectedValueOnce(new Error('bad file'))
    .mockResolvedValueOnce({ output_path: 'ok.mp4' });
  render(<VideoOutput />);
  await userEvent.click(screen.getByRole('button', { name: '开始处理' }));
  expect(await screen.findByRole('status')).toHaveTextContent('完成 1 个，失败 1 个');
});
```

- [ ] **Step 7: 验证并提交**

Run: `npx vitest run tests/components/VideoOutput.test.tsx tests/modules/audio --no-cache`

Expected: PASS；视频参数完整、自动音频策略、人工覆盖、单文件失败继续和输出目录全部通过。

```bash
git add src/components/video/VideoOutput.tsx tests/components/VideoOutput.test.tsx src/modules/audio/formatConverter.ts src/modules/audio/inspector.ts
git commit -m "feat: add unified video output workflow"
```

---

### Task 4: 将导航从视频 5 项收敛为 3 项并修复桌面布局

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/components/layout/Sidebar.tsx`
- Modify: `src/components/video/VideoTrimmer.tsx`
- Modify: `src/components/video/GifMaker.tsx`
- Test: `tests/components/AppWorkbench.test.tsx`
- Test: `tests/components/Sidebar.test.tsx`

**Interfaces:**
- Consumes: `VideoOutput`、`GifMaker`、`VideoTrimmer`。
- Produces: `WorkspaceTab = 'image' | 'video' | 'gif' | 'trim'`，其中 `video` 表示视频输出。

- [ ] **Step 1: 将 App 集成测试改为新产品契约并确认失败**

```tsx
it('默认打开视频输出且侧栏只显示三个视频入口', async () => {
  render(<App />);
  const sidebar = screen.getByRole('complementary');
  ['视频输出', 'GIF 制作', '视频剪辑'].forEach((label) => {
    expect(within(sidebar).getByRole('button', { name: label })).toBeInTheDocument();
  });
  ['视频压缩', '视频格式转换', '提取音频'].forEach((label) => {
    expect(within(sidebar).queryByRole('button', { name: label })).not.toBeInTheDocument();
  });
  expect(await screen.findByRole('button', { name: '开始处理' })).toBeInTheDocument();
});
```

Run: `npx vitest run tests/components/AppWorkbench.test.tsx tests/components/Sidebar.test.tsx --no-cache`

Expected: FAIL，仍显示五个入口。

- [ ] **Step 2: 修改 App 路由和元数据**

```ts
type WorkspaceTab = 'image' | 'video' | 'gif' | 'trim';

const TAB_META: Record<WorkspaceTab, TabMeta> = {
  image: { label: '图片处理', description: '编辑、压缩、格式转换与隐私清理', mediaType: 'image' },
  video: { label: '视频输出', description: '压缩、格式转换与音频提取', mediaType: 'video' },
  gif: { label: 'GIF 制作', description: '从视频片段生成 GIF', mediaType: 'video' },
  trim: { label: '视频剪辑', description: '选择入点出点并调整画面', mediaType: 'video' },
};
```

删除 `Compressor`、`VideoConverter`、`AudioExtractor` 的 lazy route import，新增：

```ts
const VideoOutput = lazy(() => import('./components/video/VideoOutput'));
```

右栏只允许：

```tsx
{activeTab === 'video' && <VideoOutput />}
{activeTab === 'gif' && <GifMaker />}
```

- [ ] **Step 3: 修改侧边栏和固定布局**

侧边栏数组固定为：

```ts
const VIDEO_NAV_ITEMS = [
  { id: 'video' as const, label: '视频输出', icon: Download },
  { id: 'gif' as const, label: 'GIF 制作', icon: Film },
  { id: 'trim' as const, label: '视频剪辑', icon: Scissors },
];
```

`Sidebar` 根元素改为 `w-56`。非剪辑视频页面布局改为：

```tsx
<div className="flex min-w-0 gap-5 px-5 py-4">
  <div className="min-w-0 flex-1 space-y-4">{previewAndFiles}</div>
  <aside className="w-[300px] min-w-[280px] flex-none">{toolPanel}</aside>
</div>
```

`VideoTrimmer` 使用相同的 `flex/min-w-0/w-[300px]` 规则；`GifMaker` 去除多余 `mb-8/mt-8`，统一为 12px 控制栈。

- [ ] **Step 4: 验证三个入口共享预览**

```tsx
const dropVideo = (name: string) => {
  const file = new File(['video'], name, { type: 'video/mp4' });
  Object.defineProperty(file, 'path', { value: `C:\\Media\\${name}` });
  fireEvent.drop(screen.getByText('拖拽视频文件到这里').closest('div')!, {
    dataTransfer: { files: [file] },
  });
};

it('载入视频后在三个入口之间切换仍保留预览', async () => {
  render(<App />);
  dropVideo('demo.mp4');
  for (const label of ['GIF 制作', '视频剪辑', '视频输出']) {
    await userEvent.click(screen.getByRole('button', { name: label }));
    expect(await screen.findByTestId('video-preview-poster')).toBeInTheDocument();
  }
});
```

- [ ] **Step 5: 验证并提交**

Run: `npx vitest run tests/components/AppWorkbench.test.tsx tests/components/Sidebar.test.tsx tests/components/VideoTrimmer.test.tsx tests/components/GifMaker.test.tsx --no-cache`

Expected: PASS；不存在五入口旧文案，三个入口均保留已加载视频。

```bash
git add src/App.tsx src/components/layout/Sidebar.tsx src/components/video/VideoTrimmer.tsx src/components/video/GifMaker.tsx tests/components/AppWorkbench.test.tsx tests/components/Sidebar.test.tsx
git commit -m "feat: consolidate video navigation to three tools"
```

---

### Task 5: 建立真实图片格式能力表并改为单一“图片处理”工作台

**Files:**
- Create: `src/utils/imageCapabilities.ts`
- Create: `tests/utils/imageCapabilities.test.ts`
- Create: `src/components/image/ImageControlsPanel.tsx`
- Create: `src/components/image/ImageExportControls.tsx`
- Create: `src/components/image/ImageTransformControls.tsx`
- Create: `src/components/image/ImageColorControls.tsx`
- Create: `tests/components/ImageControlsPanel.test.tsx`
- Modify: `src/components/image/ImageWorkbench.tsx`
- Modify: `src/components/layout/DropZone.tsx`
- Modify: `src/components/layout/HomePage.tsx`
- Modify: `src/utils/mediaFormat.ts`
- Modify: `src/stores/appStore.ts`
- Modify: `src-tauri/src/commands/file_commands.rs`
- Test: `tests/components/ImageWorkbench.test.tsx`
- Test: `tests/components/DropZone.test.tsx`
- Test: `tests/utils/mediaFormat.test.ts`

**Interfaces:**
- Consumes: `ProcessOptions`、`CollapsibleSection`、`RotateFlipBar`、`useImageProcessor()`。
- Produces: 单一图片工作台和统一图片参数；`cropEnabled` 决定是否导出裁切。

- [ ] **Step 1: 写真实格式白名单失败测试**

```ts
import { expect, it } from 'vitest';
import { IMAGE_INPUT_EXTENSIONS, IMAGE_OUTPUT_FORMATS } from '../../src/utils/imageCapabilities';

it('只暴露真实本地图片能力', () => {
  expect(IMAGE_INPUT_EXTENSIONS).toEqual(['jpg', 'jpeg', 'png', 'webp', 'avif', 'bmp', 'tiff', 'tif', 'psd']);
  expect(IMAGE_INPUT_EXTENSIONS).not.toContain('pdf');
  expect(IMAGE_INPUT_EXTENSIONS).not.toContain('heic');
  expect(IMAGE_OUTPUT_FORMATS.map((item) => item.value)).toEqual(['jpeg', 'png', 'webp', 'avif', 'bmp']);
});
```

- [ ] **Step 2: 实现能力表并运行测试**

```ts
export const IMAGE_INPUT_EXTENSIONS = [
  'jpg', 'jpeg', 'png', 'webp', 'avif', 'bmp', 'tiff', 'tif', 'psd',
] as const;

export const IMAGE_OUTPUT_FORMATS = [
  { label: 'JPG', value: 'jpeg', supportsTransparency: false },
  { label: 'PNG', value: 'png', supportsTransparency: true },
  { label: 'WebP', value: 'webp', supportsTransparency: true },
  { label: 'AVIF', value: 'avif', supportsTransparency: true },
  { label: 'BMP', value: 'bmp', supportsTransparency: false },
] as const;

export const isSupportedImageInput = (extension: string) =>
  IMAGE_INPUT_EXTENSIONS.includes(extension.toLowerCase() as typeof IMAGE_INPUT_EXTENSIONS[number]);
```

Run: `npx vitest run tests/utils/imageCapabilities.test.ts --no-cache`

Expected: PASS。

随后让 `isImageFormat()`、首页文件选择器、DropZone 默认扩展名和 Rust `get_supported_formats` 全部使用同一组 9 个扩展名；删除 GIF、ICO、HEIC、PDF、PPT/PPTX、AI、EPS、SVG。`tests/utils/mediaFormat.test.ts` 和 `tests/components/DropZone.test.tsx` 必须分别断言这些格式不会被接收。

- [ ] **Step 3: 先将 store 契约改为真实单面板参数**

```ts
export type OutputFormat = 'webp' | 'png' | 'jpeg' | 'avif' | 'bmp';

export interface ProcessOptions {
  outputFormat: OutputFormat;
  quality: number;
  resizeEnabled: boolean;
  resizeMaxPx: number;
  resizeTargetW?: number;
  resizeTargetH?: number;
  stripExif: boolean;
  preserveTransparency: boolean;
  outputDir?: string;
  rotateDegrees: 0 | 90 | 180 | 270;
  cropEnabled: boolean;
  cropPercent?: CropPercent;
  cropPreset?: string;
  colorAdjust: ColorAdjust;
  flipH: boolean;
  flipV: boolean;
  openAfterProcess?: boolean;
}
```

默认值必须是 `cropEnabled: false`、`cropPercent: undefined`、旋转/镜像/色彩为中性值，避免打开文件就自动裁掉 20%。

- [ ] **Step 4: 写单面板失败测试**

```tsx
it('只显示一个图片处理面板且高级区默认收起', async () => {
  render(<ImageWorkbench />);
  expect(screen.getByRole('heading', { name: '图片处理' })).toBeInTheDocument();
  expect(screen.queryByRole('switch', { name: '切换模式' })).not.toBeInTheDocument();
  expect(screen.getByText('输出设置')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: '裁切与旋转' })).toHaveAttribute('aria-expanded', 'false');
  expect(screen.getByRole('button', { name: '调整尺寸' })).toHaveAttribute('aria-expanded', 'false');
  expect(screen.getByRole('button', { name: '色彩调整' })).toHaveAttribute('aria-expanded', 'false');
});
```

Run: `npx vitest run tests/components/ImageWorkbench.test.tsx tests/components/ImageControlsPanel.test.tsx --no-cache`

Expected: FAIL，仍存在双模式开关。

- [ ] **Step 5: 实现四个单一职责图片组件**

`ImageControlsPanel.tsx` 只组合控件和唯一 CTA：

```tsx
interface Props {
  onProcess: () => void;
  estimateSizeBatch: (items: Array<{ path: string; format: string; quality: number }>) => Promise<number>;
}

export default function ImageControlsPanel({ onProcess, estimateSizeBatch }: Props) {
  const { files, options, isProcessing } = useAppStore();
  const hasPendingImages = files.some((file) => isSupportedImageInput(file.format) && file.status === 'pending');
  return (
    <div className="flex max-h-[calc(100vh-8rem)] flex-col gap-3 overflow-y-auto pb-20">
      <ImageExportControls estimateSizeBatch={estimateSizeBatch} />
      <CollapsibleSection title="裁切与旋转" description="裁切、90°旋转与镜像">
        <ImageTransformControls mode="crop-rotate" />
      </CollapsibleSection>
      <CollapsibleSection title="调整尺寸" description="按长边或精确像素调整">
        <ImageTransformControls mode="resize" />
      </CollapsibleSection>
      <CollapsibleSection title="色彩调整" description="亮度、对比度、饱和度与锐化">
        <ImageColorControls />
      </CollapsibleSection>
      <div className="sticky bottom-0 bg-surface-bright pt-2">
        <button type="button" onClick={onProcess} disabled={!hasPendingImages || isProcessing} className="btn-apple btn-apple-primary min-h-11 w-full">
          {isProcessing ? '处理中…' : '开始处理'}
        </button>
      </div>
    </div>
  );
}
```

`ImageExportControls.tsx` 只写入输出格式、质量和隐私选项：

```tsx
interface ImageExportControlsProps {
  estimateSizeBatch: (items: Array<{ path: string; format: string; quality: number }>) => Promise<number>;
}

export default function ImageExportControls({ estimateSizeBatch }: ImageExportControlsProps) {
  const { files, options, setOptions } = useAppStore();
  const [estimatedBytes, setEstimatedBytes] = useState<number | null>(null);
  useEffect(() => {
    void estimateSizeBatch(files.map((file) => ({ path: file.path, format: options.outputFormat, quality: options.quality })))
      .then(setEstimatedBytes)
      .catch(() => setEstimatedBytes(null));
  }, [estimateSizeBatch, files, options.outputFormat, options.quality]);
  const selected = IMAGE_OUTPUT_FORMATS.find((item) => item.value === options.outputFormat)!;
  const qualityEnabled = ['jpeg', 'webp', 'avif'].includes(options.outputFormat);
  return (
    <ToolOptionCard>
      <h3 className="mb-3 text-sm font-semibold">输出设置</h3>
      <div className="grid grid-cols-5 gap-1">
        {IMAGE_OUTPUT_FORMATS.map((item) => (
          <button
            key={item.value}
            type="button"
            aria-pressed={options.outputFormat === item.value}
            onClick={() => setOptions({
              outputFormat: item.value,
              preserveTransparency: item.supportsTransparency ? options.preserveTransparency : false,
            })}
          >
            {item.label}
          </button>
        ))}
      </div>
      <label className="block text-xs font-semibold">质量</label>
      <CustomSlider ariaLabel="质量" min={1} max={100} value={options.quality} disabled={!qualityEnabled} onChange={(quality) => setOptions({ quality })} />
      {!qualityEnabled && <p className="text-xs text-on-surface-variant">PNG 与 BMP 使用无损编码，不使用质量参数</p>}
      <label><input type="checkbox" checked={options.stripExif} onChange={() => setOptions({ stripExif: !options.stripExif })} /> 清除 EXIF</label>
      <label className={!selected.supportsTransparency ? 'opacity-50' : ''}>
        <input type="checkbox" disabled={!selected.supportsTransparency} checked={selected.supportsTransparency && options.preserveTransparency} onChange={() => setOptions({ preserveTransparency: !options.preserveTransparency })} /> 保留透明通道
      </label>
      <p className="text-xs text-on-surface-variant">预计大小：{estimatedBytes === null ? '—' : formatBytes(estimatedBytes)}</p>
    </ToolOptionCard>
  );
}
```

`ImageTransformControls.tsx` 按 `mode` 只渲染对应控件，裁切开关必须清除无效区域：

```tsx
interface ImageTransformControlsProps { mode: 'crop-rotate' | 'resize' }

export default function ImageTransformControls({ mode }: ImageTransformControlsProps) {
  const { options, setOptions } = useAppStore();
  const setCropEnabled = (enabled: boolean) => setOptions({
    cropEnabled: enabled,
    cropPercent: enabled ? { x: 0, y: 0, width: 100, height: 100 } : undefined,
  });
  if (mode === 'crop-rotate') return (
    <div className="space-y-3">
      <label><input type="checkbox" checked={options.cropEnabled} onChange={(event) => setCropEnabled(event.target.checked)} /> 启用裁切</label>
      <RotateFlipBar
        rotation={options.rotateDegrees}
        onRotationChange={(value) => setOptions({ rotateDegrees: value as 0 | 90 | 180 | 270 })}
        onFlipH={() => setOptions({ flipH: !options.flipH })}
        onFlipV={() => setOptions({ flipV: !options.flipV })}
      />
    </div>
  );
  return (
    <div className="space-y-3">
      <label><input type="checkbox" checked={options.resizeEnabled} onChange={(event) => setOptions({ resizeEnabled: event.target.checked })} /> 限制长边</label>
      <input type="number" min={1} max={16384} value={options.resizeMaxPx} onChange={(event) => setOptions({ resizeMaxPx: Number(event.target.value) })} aria-label="最大边长" />
      <div className="grid grid-cols-2 gap-2">
        <input type="number" min={1} max={16384} value={options.resizeTargetW ?? ''} onChange={(event) => setOptions({ resizeTargetW: event.target.value ? Number(event.target.value) : undefined })} aria-label="精确宽度" />
        <input type="number" min={1} max={16384} value={options.resizeTargetH ?? ''} onChange={(event) => setOptions({ resizeTargetH: event.target.value ? Number(event.target.value) : undefined })} aria-label="精确高度" />
      </div>
    </div>
  );
}
```

`ImageColorControls.tsx` 使用统一范围，重置按钮恢复中性值：

```tsx
const COLOR_FIELDS = [
  ['brightness', '亮度', -100, 100],
  ['contrast', '对比度', -100, 100],
  ['saturation', '饱和度', -100, 100],
  ['sharpness', '锐化', 0, 100],
] as const;

export default function ImageColorControls() {
  const { options, setOptions } = useAppStore();
  const update = (key: keyof ColorAdjust, value: number) => setOptions({ colorAdjust: { ...options.colorAdjust, [key]: value } });
  return (
    <div className="space-y-3">
      {COLOR_FIELDS.map(([key, label, min, max]) => (
        <div key={key}>
          <label className="mb-1 block text-xs font-semibold">{label}</label>
          <CustomSlider ariaLabel={label} min={min} max={max} value={options.colorAdjust[key]} onChange={(value) => update(key, value)} />
        </div>
      ))}
      <button type="button" onClick={() => setOptions({ colorAdjust: { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 } })}>重置色彩</button>
      <p className="text-xs text-on-surface-variant">锐化在导出时精确应用</p>
    </div>
  );
}
```

裁切开关的状态更新核心为：

```ts
const setCropEnabled = (enabled: boolean) => setOptions({
  cropEnabled: enabled,
  cropPercent: enabled ? { x: 0, y: 0, width: 100, height: 100 } : undefined,
});
```

精确尺寸必须同时满足宽高 `1..16384`，否则显示内联错误并禁止 CTA；色彩范围固定为亮度/对比度/饱和度 `-100..100`、锐化 `0..100`。

- [ ] **Step 6: 简化 ImageWorkbench**

删除 `isExportMode`、`handleModeSwitch`、`EXPORT_FORMATS`/`EDIT_FORMATS` 分支和 toast。工作台使用：

```tsx
<div className="flex min-w-0 gap-5 px-5 py-4" role="region" aria-label="图片处理工作区">
  <section className="min-w-0 flex-1 space-y-4">
    <h2 className="text-xl font-semibold">图片处理</h2>
    {files.length === 0 ? (
      <DropZone
        mediaType="image"
        onFilesAdded={handleFilesAdded}
        extensions={[...IMAGE_INPUT_EXTENSIONS]}
        title="拖拽图片到这里"
        subtitle="支持 JPG、PNG、WebP、AVIF、BMP、TIFF 和 PSD"
      />
    ) : (
      <MediaPreviewStage mode="image" title="图片预览" subtitle="所有调整均在本地完成" files={imageFiles} mediaType="image" />
    )}
  </section>
  <aside className="w-[300px] min-w-[280px] flex-none">
    <ImageControlsPanel onProcess={onProcess} estimateSizeBatch={estimateSizeBatch} />
  </aside>
</div>
```

- [ ] **Step 7: 验证并提交**

Run: `npx vitest run tests/components/ImageWorkbench.test.tsx tests/components/ImageControlsPanel.test.tsx tests/utils/imageCapabilities.test.ts --no-cache`

Expected: PASS；没有模式切换，没有不支持格式，折叠区状态和 store 参数正确。

```bash
git add src/utils/imageCapabilities.ts src/utils/mediaFormat.ts src/components/layout/DropZone.tsx src/components/layout/HomePage.tsx src/components/image/ImageControlsPanel.tsx src/components/image/ImageExportControls.tsx src/components/image/ImageTransformControls.tsx src/components/image/ImageColorControls.tsx src/components/image/ImageWorkbench.tsx src/stores/appStore.ts src-tauri/src/commands/file_commands.rs tests/utils/imageCapabilities.test.ts tests/utils/mediaFormat.test.ts tests/components/DropZone.test.tsx tests/components/ImageControlsPanel.test.tsx tests/components/ImageWorkbench.test.tsx
git commit -m "feat: unify image editing and export workbench"
```

---

### Task 6: 补齐 Rust 图片导出管线并保证预览参数真实生效

**Files:**
- Modify: `src/hooks/useImageProcessor.ts`
- Test: `tests/hooks/useImageProcessor.test.ts`
- Modify: `src-tauri/Cargo.toml`
- Modify: `src-tauri/Cargo.lock`
- Modify: `src-tauri/src/commands/process_commands.rs`
- Modify: `src-tauri/src/domain/image_engine.rs`

**Interfaces:**
- Consumes: `ProcessOptions` 中的 `cropEnabled`、精确尺寸、镜像和色彩参数。
- Produces: Rust `ImageColorAdjust`、扩展后的 `ImageProcessItem` 和真实 JPG/PNG/WebP/AVIF/BMP 输出。

- [ ] **Step 1: 写前端命令参数失败测试**

```ts
expect(invoke).toHaveBeenCalledWith('process_images', {
  files: [{ id: 'image-1', path: '/images/demo.png' }],
  options: {
    format: 'avif',
    quality: 85,
    resize_enabled: false,
    resize_max_px: 1920,
    resize_target_width: 1200,
    resize_target_height: 800,
    strip_exif: true,
    preserve_transparency: false,
    output_dir: undefined,
    rotate_degrees: 90,
    crop_percent: { x: 10, y: 10, width: 80, height: 80 },
    flip_h: true,
    flip_v: false,
    color_adjust: { brightness: 10, contrast: 20, saturation: -10, sharpness: 30 },
  },
});
```

- [ ] **Step 2: 运行前端测试确认失败并扩展 hook**

Run: `npx vitest run tests/hooks/useImageProcessor.test.ts --no-cache`

Expected: FAIL，新增字段尚未发送。

`startProcess` 的参数和 invoke 对象必须增加：

```ts
resizeTargetW?: number;
resizeTargetH?: number;
flipH?: boolean;
flipV?: boolean;
colorAdjust?: ColorAdjust;
cropEnabled?: boolean;
preserveTransparency?: boolean;
```

```ts
resize_target_width: options.resizeTargetW,
resize_target_height: options.resizeTargetH,
crop_percent: options.cropEnabled ? options.cropPercent : undefined,
preserve_transparency: options.preserveTransparency ?? true,
flip_h: options.flipH ?? false,
flip_v: options.flipV ?? false,
color_adjust: options.colorAdjust ?? { brightness: 0, contrast: 0, saturation: 0, sharpness: 0 },
```

- [ ] **Step 3: 先写 Rust 纯函数测试**

```rust
fn test_quadrant_image(width: u32, height: u32) -> image::RgbaImage {
    image::ImageBuffer::from_fn(width, height, |x, y| {
        match (x < width / 2, y < height / 2) {
            (true, true) => image::Rgba([255, 0, 0, 255]),
            (false, true) => image::Rgba([0, 255, 0, 255]),
            (true, false) => image::Rgba([0, 0, 255, 255]),
            (false, false) => image::Rgba([255, 255, 0, 255]),
        }
    })
}

#[test]
fn process_pipeline_applies_crop_rotate_flip_resize_and_color() {
    let img = DynamicImage::ImageRgba8(test_quadrant_image(100, 80));
    let options = ImageTransformOptions {
        crop_percent: Some(ImageCropPercent { x: 0.0, y: 0.0, width: 50.0, height: 100.0 }),
        rotate_degrees: 90,
        flip_h: true,
        flip_v: false,
        resize_target_width: Some(40),
        resize_target_height: Some(20),
        color_adjust: ImageColorAdjust { brightness: 10, contrast: 20, saturation: -10, sharpness: 30 },
        preserve_transparency: true,
    };
    let result = apply_transforms(img, &options).unwrap();
    assert_eq!(result.dimensions(), (40, 20));
    assert_ne!(result.to_rgba8().get_pixel(0, 0), result.to_rgba8().get_pixel(39, 0));
}

#[test]
fn encodes_real_avif_and_bmp_signatures() {
    let img = DynamicImage::new_rgb8(4, 4);
    let avif = encode_to_format(&img, "avif", 85).unwrap();
    let bmp = encode_to_format(&img, "bmp", 85).unwrap();
    assert_eq!(&avif[4..12], b"ftypavif");
    assert_eq!(&bmp[..2], b"BM");
}

#[test]
fn webp_quality_changes_lossy_output() {
    let img = DynamicImage::ImageRgba8(test_quadrant_image(256, 256));
    let low = encode_to_format(&img, "webp", 30).unwrap();
    let high = encode_to_format(&img, "webp", 90).unwrap();
    assert_ne!(low, high);
    assert!(low.starts_with(b"RIFF") && high.starts_with(b"RIFF"));
}

#[test]
fn flatten_transparency_uses_white_background() {
    let img = DynamicImage::ImageRgba8(image::RgbaImage::from_pixel(1, 1, image::Rgba([0, 0, 0, 0])));
    let flattened = flatten_alpha_on_white(img).to_rgba8();
    assert_eq!(flattened.get_pixel(0, 0).0, [255, 255, 255, 255]);
}
```

在 `src-tauri/Cargo.toml` 固定已修复安全公告的版本：

```toml
webp = "=0.3.1"
```

- [ ] **Step 4: 扩展 Rust 数据结构和参数验证**

```rust
#[derive(Debug, Clone, serde::Deserialize, Default)]
pub struct ImageColorAdjust {
    pub brightness: i16,
    pub contrast: i16,
    pub saturation: i16,
    pub sharpness: u8,
}

#[derive(Debug, Clone, Default)]
pub struct ImageTransformOptions {
    pub crop_percent: Option<ImageCropPercent>,
    pub rotate_degrees: u16,
    pub flip_h: bool,
    pub flip_v: bool,
    pub resize_target_width: Option<u32>,
    pub resize_target_height: Option<u32>,
    pub color_adjust: ImageColorAdjust,
    pub preserve_transparency: bool,
}

#[derive(Debug, serde::Deserialize)]
pub struct ProcessOptions {
    pub format: String,
    pub quality: f64,
    pub resize_enabled: bool,
    pub resize_max_px: u32,
    pub resize_target_width: Option<u32>,
    pub resize_target_height: Option<u32>,
    pub strip_exif: bool,
    #[serde(default = "default_preserve_transparency")]
    pub preserve_transparency: bool,
    pub output_dir: Option<String>,
    pub rotate_degrees: Option<u16>,
    pub crop_percent: Option<ImageCropPercent>,
    #[serde(default)]
    pub flip_h: bool,
    #[serde(default)]
    pub flip_v: bool,
    #[serde(default)]
    pub color_adjust: ImageColorAdjust,
}

fn default_preserve_transparency() -> bool { true }

#[derive(Debug, Clone)]
pub struct ImageProcessItem {
    pub input_path: String,
    pub output_format: String,
    pub quality: u8,
    pub resize_max_px: Option<u32>,
    pub resize_target_width: Option<u32>,
    pub resize_target_height: Option<u32>,
    pub strip_exif: bool,
    pub preserve_transparency: bool,
    pub rotate_degrees: u16,
    pub crop_percent: Option<ImageCropPercent>,
    pub flip_h: bool,
    pub flip_v: bool,
    pub color_adjust: ImageColorAdjust,
}
```

`process_images` 构造 `ImageProcessItem` 时逐字段复制；精确尺寸存在时令 `resize_max_px = None`，否则由 `resize_enabled` 决定长边缩放。JPG/BMP 强制 `preserve_transparency = false`；PNG/WebP/AVIF 尊重前端开关。不得在命令层静默丢弃任何前端字段。

验证规则固定为：质量 `1..100`；旋转 `0/90/180/270`；精确宽高必须同时存在且均为 `1..16384`；色彩三项 `-100..100`；锐化 `0..100`；裁切百分比必须位于 `0..100` 且区域不能超界。

- [ ] **Step 5: 实现确定顺序的图像管线**

处理顺序固定为“裁切 → 旋转 → 镜像 → 精确尺寸或长边缩放 → 色彩 → 编码”：

```rust
pub fn apply_transforms(
    mut img: DynamicImage,
    options: &ImageTransformOptions,
) -> Result<DynamicImage, String> {
    if let Some(crop) = &options.crop_percent {
        img = crop_by_percent(&img, crop.x, crop.y, crop.width, crop.height)?;
    }
    img = rotate_image(&img, options.rotate_degrees);
    if options.flip_h { img = img.fliph(); }
    if options.flip_v { img = img.flipv(); }
    match (options.resize_target_width, options.resize_target_height) {
        (Some(width), Some(height)) => {
            validate_dimensions(width, height)?;
            img = img.resize_exact(width, height, image::imageops::FilterType::Lanczos3);
        }
        (None, None) => {}
        _ => return Err("精确尺寸必须同时提供宽度和高度".to_string()),
    }
    img = apply_color_adjustments(img, &options.color_adjust);
    if !options.preserve_transparency {
        img = flatten_alpha_on_white(img);
    }
    Ok(img)
}
```

色彩实现使用明确换算：亮度 `-100..100` 映射到 `brighten(-255..255)`；对比度直接传 `adjust_contrast(-100..100)`；饱和度使用 RGB 与 Rec.709 灰度的线性插值；锐化使用 `unsharpen(1.0 + sharpness / 50.0, 1)`。

```rust
fn validate_dimensions(width: u32, height: u32) -> Result<(), String> {
    if !(1..=16_384).contains(&width) || !(1..=16_384).contains(&height) {
        return Err("图片宽高必须在 1..16384 之间".to_string());
    }
    Ok(())
}

fn apply_color_adjustments(mut img: DynamicImage, adjust: &ImageColorAdjust) -> DynamicImage {
    if adjust.brightness != 0 {
        img = img.brighten((adjust.brightness as f32 * 2.55).round() as i32);
    }
    if adjust.contrast != 0 {
        img = img.adjust_contrast(adjust.contrast as f32);
    }
    if adjust.saturation != 0 {
        img = apply_saturation(img, adjust.saturation);
    }
    if adjust.sharpness != 0 {
        img = img.unsharpen(1.0 + adjust.sharpness as f32 / 50.0, 1);
    }
    img
}

fn flatten_alpha_on_white(img: DynamicImage) -> DynamicImage {
    let mut rgba = img.to_rgba8();
    for pixel in rgba.pixels_mut() {
        let alpha = pixel[3] as u16;
        for channel in 0..3 {
            pixel[channel] = ((pixel[channel] as u16 * alpha + 255 * (255 - alpha)) / 255) as u8;
        }
        pixel[3] = 255;
    }
    DynamicImage::ImageRgba8(rgba)
}

fn apply_saturation(img: DynamicImage, saturation: i16) -> DynamicImage {
    let factor = 1.0 + saturation as f32 / 100.0;
    let mut rgba = img.to_rgba8();
    for pixel in rgba.pixels_mut() {
        let r = pixel[0] as f32;
        let g = pixel[1] as f32;
        let b = pixel[2] as f32;
        let luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        pixel[0] = (luma + (r - luma) * factor).clamp(0.0, 255.0) as u8;
        pixel[1] = (luma + (g - luma) * factor).clamp(0.0, 255.0) as u8;
        pixel[2] = (luma + (b - luma) * factor).clamp(0.0, 255.0) as u8;
    }
    DynamicImage::ImageRgba8(rgba)
}
```

- [ ] **Step 6: 实现真实 AVIF/BMP 编码**

```rust
pub fn encode_to_webp(img: &DynamicImage, quality: u8) -> Result<Vec<u8>, String> {
    let rgba = img.to_rgba8();
    let (width, height) = rgba.dimensions();
    let encoded = webp::Encoder::from_rgba(rgba.as_raw(), width, height)
        .encode(quality.clamp(1, 100) as f32);
    Ok(encoded.to_vec())
}

pub fn encode_to_avif(img: &DynamicImage, quality: u8) -> Result<Vec<u8>, String> {
    let rgba = img.to_rgba8();
    let (width, height) = rgba.dimensions();
    let mut buffer = Vec::new();
    image::codecs::avif::AvifEncoder::new_with_speed_quality(&mut buffer, 6, quality.clamp(1, 100))
        .write_image(rgba.as_raw(), width, height, image::ExtendedColorType::Rgba8)
        .map_err(|error| format!("AVIF 编码失败: {error}"))?;
    Ok(buffer)
}

pub fn encode_to_format(img: &DynamicImage, format: &str, quality: u8) -> Result<Vec<u8>, String> {
    let mut buffer = Vec::new();
    let image_format = match format.to_lowercase().as_str() {
        "jpg" | "jpeg" => return encode_to_jpeg(img, quality),
        "png" => ImageFormat::Png,
        "webp" => return encode_to_webp(img, quality),
        "avif" => return encode_to_avif(img, quality),
        "bmp" => ImageFormat::Bmp,
        other => return Err(format!("不支持的输出格式: {other}")),
    };
    img.write_to(&mut std::io::Cursor::new(&mut buffer), image_format)
        .map_err(|error| format!("{format} 编码失败: {error}"))?;
    Ok(buffer)
}
```

- [ ] **Step 7: 验证 Rust 与前端契约并提交**

Run: `npx vitest run tests/hooks/useImageProcessor.test.ts --no-cache`

Run: `cd src-tauri && cargo test --lib domain::image_engine commands::process_commands`

Expected: 前端参数对象完全匹配；Rust 管线顺序、边界、AVIF/BMP 文件签名全部通过。

Run: `cargo install cargo-audit --locked && cd src-tauri && cargo tree -i webp && cargo audit`

Expected: 使用 `webp 0.3.1`；RustSec 不报告 `RUSTSEC-2024-0443` 或其他未处理漏洞。

```bash
git add src/hooks/useImageProcessor.ts tests/hooks/useImageProcessor.test.ts src-tauri/Cargo.toml src-tauri/Cargo.lock src-tauri/src/commands/process_commands.rs src-tauri/src/domain/image_engine.rs
git commit -m "feat: make all image adjustments affect exported files"
```

---

### Task 7: 修复图片预览、测试警告和全局 UI 状态

**Files:**
- Modify: `src/components/preview/MediaPreviewStage.tsx`
- Modify: `src/components/preview/ImagePreviewStage.tsx`
- Modify: `src/hooks/useCssFilterPreview.ts`
- Test: `tests/components/ImagePreviewStage.test.tsx`
- Test: `tests/components/ImageWorkbench.test.tsx`
- Test: `tests/hooks/useCssFilterPreview.test.ts`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: `cropEnabled`、`ProcessOptions`。
- Produces: 中性默认预览、启用后裁切框、旋转/镜像/色彩预览和无警告测试日志。

- [ ] **Step 1: 写“默认不裁切”失败测试**

```tsx
const addImage = (name: string) => act(() => {
  useAppStore.getState().addFiles([{
    id: 'image-1',
    path: `/images/${name}`,
    name,
    format: 'PNG',
    originalSize: 1024,
    status: 'pending',
  }]);
});

it('载入图片时不自动写入 80% 裁切区域', async () => {
  render(<ImageWorkbench />);
  addImage('demo.png');
  expect(await screen.findByTestId('image-crop-preview')).toBeInTheDocument();
  expect(useAppStore.getState().options.cropEnabled).toBe(false);
  expect(useAppStore.getState().options.cropPercent).toBeUndefined();
});
```

- [ ] **Step 2: 修改预览裁切契约**

`MediaPreviewStage` 默认 crop 使用全图：

```ts
const FULL_CROP: Crop = { unit: '%', x: 0, y: 0, width: 100, height: 100 };
```

只有 `options.cropEnabled` 时才调用 `syncCropToStore`；关闭时传给 `ImagePreviewStage` 的 `crop` 为 `undefined`，`ImagePreviewStageProps.crop` 改为 `Crop | undefined`。

- [ ] **Step 3: 保持 CSS 预览参数与导出范围一致**

```ts
export const useCssFilterPreview = ({ brightness, contrast, saturation }: Params) => {
  const brightnessPct = 100 + Math.max(-100, Math.min(100, brightness));
  const contrastPct = 100 + Math.max(-100, Math.min(100, contrast));
  const saturationPct = 100 + Math.max(-100, Math.min(100, saturation));
  return `brightness(${brightnessPct}%) contrast(${contrastPct}%) saturate(${saturationPct}%)`;
};
```

锐化控件旁固定显示“导出时精确应用”；其余三项和镜像/旋转必须实时预览。

- [ ] **Step 4: 清理现有测试警告**

所有直接调用 Zustand 的测试更新包在：

```tsx
act(() => {
  useAppStore.getState().removeFile('image-1');
});
```

测试中的预期失败日志使用 `vi.spyOn(console, 'error').mockImplementation(() => {})` 局部拦截，并在 `afterEach` 恢复，保证全量测试 stderr 没有非预期输出。

- [ ] **Step 5: 更新 CHANGELOG 并验证**

CHANGELOG 新增一条 2026-07-14 记录，包含：视频 5→3、视频输出统一、图片单面板、真实图片导出链路、支持格式收口、测试与 Windows 验收状态；未完成的 Windows 项不得提前写成完成。

Run: `npx vitest run tests/components/ImagePreviewStage.test.tsx tests/components/ImageWorkbench.test.tsx tests/hooks/useCssFilterPreview.test.ts --no-cache`

Expected: PASS，stderr 无 React `act(...)` 警告。

```bash
git add src/components/preview/MediaPreviewStage.tsx src/components/preview/ImagePreviewStage.tsx src/hooks/useCssFilterPreview.ts tests/components/ImagePreviewStage.test.tsx tests/components/ImageWorkbench.test.tsx tests/hooks/useCssFilterPreview.test.ts CHANGELOG.md
git commit -m "fix: align image preview with exported adjustments"
```

---

### Task 8: 全量回归、视觉验收、离线审计和 Windows 便携 EXE

**Files:**
- Modify only if a check fails: `build.py`
- Modify only if a check fails: `src-tauri/tauri.conf.json`
- Test: `tests/test_build_script_config.py`
- Record: `CHANGELOG.md`

**Interfaces:**
- Consumes: Tasks 1–7 的全部功能。
- Produces: 可直接运行的 Windows x64 便携目录，包含主 EXE、FFmpeg、FFprobe、构建日志和验收记录。

- [ ] **Step 1: 运行前端目标回归**

Run:

```bash
npx vitest run \
  tests/components/VideoOutput.test.tsx \
  tests/components/AppWorkbench.test.tsx \
  tests/components/Sidebar.test.tsx \
  tests/components/ImageControlsPanel.test.tsx \
  tests/components/ImageWorkbench.test.tsx \
  tests/hooks/useImageProcessor.test.ts \
  --no-cache
```

Expected: 全部 PASS，无 `act(...)`、未捕获 Promise、非预期 `console.error`。

- [ ] **Step 2: 运行完整静态与单元验证**

Run:

```bash
npx tsc --noEmit
npx vitest run --no-cache
npm run build
cd src-tauri && cargo fmt --check && cargo test --lib && cargo check
cd .. && python3 -m pytest tests/test_build_script_config.py -q
git diff --check
```

Expected: 每条命令退出码 0；Vitest 测试数不少于 589；Rust 和 Python 测试全部通过。

- [ ] **Step 3: 浏览器视觉检查两个窗口尺寸**

启动 `npm run dev`，分别将视口设为 `1200×800` 和 `900×600`，逐页保存截图并核对：

```text
视频输出：9 个格式按钮不重叠；基础参数和 CTA 可见；高级区可滚动。
GIF 制作：右栏无横向溢出；CTA 可触达。
视频剪辑：中心预览和 300px 右栏并排；时间线不被遮挡。
图片处理：没有模式开关；四个分区存在；CTA 可触达；中心预览实时变化。
侧边栏：宽 224px；视频只有 3 项；图片只有 1 项；设置入口保留。
```

Expected: 两种尺寸 `document.documentElement.scrollWidth === document.documentElement.clientWidth`，所有主按钮边界位于视口内。

- [ ] **Step 4: 执行运行时离线审计**

Run:

```bash
rg -n "fetch\(|XMLHttpRequest|https?://|WebSocket|axios|reqwest|ureq|telemetry|sentry|mixpanel|update" src src-tauri/src index.html package.json src-tauri/Cargo.toml
```

Expected: 没有运行时外联实现；允许出现的 `http://localhost:5173` 仅存在于 Tauri 开发配置。

- [ ] **Step 5: 在真实 Windows 10/11 x64 环境构建**

在 Windows PowerShell 中执行：

```powershell
Set-Location C:\Mac\Home\TinyPix\3.5pro
python build.py
```

Expected: 构建退出码 0；产物目录包含 `tinypix.exe`、`ffmpeg.exe`、`ffprobe.exe`，且没有运行时下载步骤。

- [ ] **Step 6: 执行 Windows 桌面端到端验收**

使用真实 Windows 桌面或 `windows-desktop-e2e`，按顺序验证：

```text
1. 断开网络后双击 tinypix.exe，应用在 10 秒内打开。
2. 默认进入“视频输出”，侧栏只有视频输出/GIF 制作/视频剪辑。
3. MP4 均衡输出成功；MOV→MP4 转换成功；MP3 自动提取成功。
4. GIF 制作和视频剪辑分别完成一次真实 FFmpeg 输出。
5. JPG 执行裁切+旋转+镜像+精确尺寸+色彩+锐化并导出。
6. JPG/PNG/WebP/AVIF/BMP 各导出一次并用文件头或系统查看器确认真实格式。
7. PSD/TIFF 各导入一次并成功导出 PNG。
8. PDF/PPT/AI/EPS/SVG/HEIC 不出现在文件选择支持列表。
9. 900×600 和 1200×800 下没有横向溢出，所有 CTA 可触达。
10. Windows 防火墙/资源监视器中无外部网络连接。
```

Expected: 十项全部通过；失败项记录输入文件、页面、操作、错误文案、日志与截图，修复后从目标测试重新回归。

- [ ] **Step 7: 生成最终交付清单并提交**

在 `CHANGELOG.md` 写入当前轮实际证据，记录：提交、测试数量、构建命令、EXE 绝对路径、文件大小、SHA-256、Windows 版本、离线测试结果和仍未人工验证的项目。

Windows 生成哈希：

```powershell
Get-FileHash .\target\release\tinypix.exe -Algorithm SHA256
Get-Item .\target\release\tinypix.exe, .\target\release\ffmpeg.exe, .\target\release\ffprobe.exe |
  Select-Object FullName, Length, LastWriteTime
```

```bash
git add CHANGELOG.md build.py src-tauri/tauri.conf.json tests/test_build_script_config.py
git commit -m "build: validate offline Windows portable release"
```

只添加实际发生变化的文件；若 `build.py`、`tauri.conf.json`、`tests/test_build_script_config.py` 无需修改，不得为了凑提交而触碰它们。

---

## Final Acceptance Matrix

| 领域 | 必须通过 |
|---|---|
| 信息架构 | 视频 3 项、图片 1 项；不存在旧五入口和双模式开关 |
| 视频输出 | MP4 默认、5 视频格式、4 音频格式、自动直提/重编码、批量失败不中断 |
| 图片处理 | 裁切、旋转、镜像、精确尺寸、亮度、对比度、饱和度、锐化真实进入成品 |
| 图片格式 | 9 种已验证输入、5 种真实输出；无 PDF/PPT/AI/EPS/SVG/HEIC 虚假承诺 |
| UI | 1200×800 与 900×600 无横向溢出、无按钮截断、CTA 可触达 |
| 自动化 | TypeScript、Vitest、Vite、Rust、Python、`git diff --check` 全绿且无测试警告 |
| Windows | 真实 Windows 构建、启动和五条媒体主流程通过 |
| 隐私 | 断网可用、无外部连接、无上传、遥测、更新检查和远程资源 |
| 交付 | EXE、FFmpeg、FFprobe、SHA-256、日志、截图、CHANGELOG 齐全 |

## Explicit Assumptions

- 用户已确认：视频 5→3 和图片单面板是最新产品方向。
- 用户已选择：图片编辑能力全部打通到真实导出链路。
- 用户未回答专业文档格式范围，采用推荐默认：本版只展示并验证真实本地图片格式，不引入 PDF/PPT/AI/EPS 转换器。
- 旧视频/图片组件暂时保留但不路由，减少迁移风险；删除工作不属于本计划。
- 当前计划不改变 Tauri 命令名；只扩展 `process_images.options` 的字段。
- 不创建云服务、账号系统、自动更新、遥测或任何运行时联网能力。
