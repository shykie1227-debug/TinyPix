# TinyPix Comprehensive Verification and Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify and complete every TinyPix 3.5 Pro workbench against the latest UI references, then build and launch the Windows release in the local Parallels VM.

**Architecture:** Preserve the current React 19 + TypeScript + Tauri 2 + Rust/FFmpeg architecture. Keep one shared desktop shell, route each media tool into a center preview/work area plus right parameter panel, and keep all runtime processing local through explicit Tauri commands. Use the existing mature open-source dependencies—Tauri, FFmpeg, react-image-crop, and Radix Slider—without adding another UI or media framework.

**Tech Stack:** React 19, TypeScript 5.7, Tailwind CSS 4, Zustand 5, Tauri 2, Rust 2021, FFmpeg, Vitest, pytest, Parallels Desktop Windows 10.

---

### Task 1: Make the Windows build configuration portable

**Files:**
- Modify: `build.py`
- Test: `tests/test_build_script_config.py`

- [ ] **Step 1: Reproduce the failing portability test**

Run:

```bash
python3 -m pytest tests/test_build_script_config.py::test_fix_tauri_config_keeps_frontend_dist_relative -q
```

Expected: FAIL because `fix_tauri_config()` writes an absolute machine-specific path instead of `../dist`.

- [ ] **Step 2: Keep the Tauri frontend directory relative**

Change the configuration assignment to:

```python
old_value = config["build"].get("frontendDist", "")
dist_path = "../dist"
config["build"]["frontendDist"] = dist_path
```

This keeps the copied/staged Windows project relocatable while still resolving to `<project>/dist` from `src-tauri/tauri.conf.json`.

- [ ] **Step 3: Verify the focused and complete build-script tests**

Run:

```bash
python3 -m pytest tests/test_build_script_config.py -q
```

Expected: 7 passed.

### Task 2: Restore one shared application shell

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/components/image/ImageWorkbench.tsx`
- Modify: `tests/components/AppWorkbench.test.tsx`
- Modify: `tests/components/ImageWorkbench.test.tsx`

- [ ] **Step 1: Add a failing regression test for duplicate shell chrome**

Add this test:

```tsx
it('uses one shared sidebar and top navigation in image mode', async () => {
  const user = userEvent.setup();
  render(<App />);
  await user.click(screen.getByRole('button', { name: '图片工具' }));

  expect(screen.getAllByRole('heading', { name: 'TinyPix Pro' })).toHaveLength(1);
  expect(screen.getAllByRole('button', { name: '设置' })).toHaveLength(1);
});
```

- [ ] **Step 2: Run the test and confirm the current nested shell fails**

Run:

```bash
npx vitest run tests/components/AppWorkbench.test.tsx
```

Expected: FAIL because `ImageWorkbench` currently renders a second sidebar and top navigation inside `App`.

- [ ] **Step 3: Make `ImageWorkbench` content-only**

Keep image mode switching, file handling, preview/edit controls, export controls, progress, and reset behavior inside `ImageWorkbench`, but remove its internal application sidebar, top navigation, and full-screen height ownership. Its root becomes:

```tsx
<div
  className="grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(320px,30%)] gap-6 p-8 pb-16"
  role="region"
  aria-label="图片工具工作区"
>
  <section className="min-w-0 space-y-6">{/* preview and queue */}</section>
  <aside className="min-w-[320px] space-y-6">{/* edit/export controls */}</aside>
</div>
```

Use the existing shared `Sidebar`, `TopNavBar`, and `StatusBar` from `App`.

- [ ] **Step 4: Preserve image-mode controls in the shared content**

Render a compact mode switch and reset command above the image work area:

```tsx
<div className="flex items-center justify-between">
  <button role="switch" aria-checked={isExportMode} aria-label="切换模式">
    {isExportMode ? '导出模式' : '编辑模式'}
  </button>
  <button onClick={handleReset} aria-label="重置图片工具">重置</button>
</div>
```

- [ ] **Step 5: Verify both component suites**

Run:

```bash
npx vitest run tests/components/AppWorkbench.test.tsx tests/components/ImageWorkbench.test.tsx
npx tsc --noEmit
```

Expected: both suites pass and TypeScript reports no errors.

### Task 3: Integrate video trimming without nested workbench columns

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/components/video/VideoTrimmer.tsx`
- Create: `tests/components/VideoTrimmer.test.tsx`

- [ ] **Step 1: Add a failing layout regression test**

Create:

```tsx
it('renders the trim preview and parameter panel once', () => {
  useAppStore.setState({ files: [videoFile] });
  render(<VideoTrimmer />);

  expect(screen.getAllByText('片段属性')).toHaveLength(1);
  expect(screen.getAllByText('导出设置')).toHaveLength(1);
  expect(screen.getAllByLabelText('视频剪辑工作区')).toHaveLength(1);
});
```

- [ ] **Step 2: Run the test and verify it fails for the missing workspace contract**

Run:

```bash
npx vitest run tests/components/VideoTrimmer.test.tsx
```

Expected: FAIL because the component has no `视频剪辑工作区` region and is mounted inside the generic right column.

- [ ] **Step 3: Route trimming as a full center-plus-right workspace**

In `WorkspaceContent`, special-case the trim tool:

```tsx
{activeTab === 'trim' ? (
  <VideoTrimmer />
) : (
  <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
    {/* shared preview and right-side tool panel */}
  </div>
)}
```

Add `role="region" aria-label="视频剪辑工作区"` to `VideoTrimmer`, keep its preview/timeline as the center area, and keep its parameter controls as the right area. This prevents a complete two-column editor from being nested inside the generic right parameter column.

- [ ] **Step 4: Verify metadata, preview fallback, and export parameter mapping**

Run:

```bash
npx vitest run tests/components/VideoTrimmer.test.tsx tests/components/VideoCommandArgs.test.tsx tests/modules/editExportBridge.test.ts
```

Expected: all tests pass, including `create_video_preview` fallback and `edit_and_export_video` arguments.

### Task 4: Close UI-design and interaction gaps

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/components/layout/Sidebar.tsx`
- Modify: `src/components/layout/DropZone.tsx`
- Modify: `src/components/preview/MediaPreviewStage.tsx`
- Modify: `src/components/video/Compressor.tsx`
- Modify: `src/components/video/GifMaker.tsx`
- Modify: `src/components/video/VideoConverter.tsx`
- Modify: `src/components/video/AudioExtractor.tsx`
- Modify: `src/index.css`
- Test: matching files under `tests/components/`

- [ ] **Step 1: Add focused assertions before each behavior change**

Tests must assert:

```tsx
expect(screen.getAllByRole('button', { name: /视频压缩|视频转 GIF|视频格式转换|视频剪辑|提取音频/ })).toHaveLength(5);
expect(screen.getByText('拖拽视频文件到这里')).toBeInTheDocument();
expect(screen.getByRole('button', { name: '开始极速压缩' })).toBeDisabled();
expect(screen.getByText('内嵌播放器暂不支持此编码')).toBeInTheDocument();
```

- [ ] **Step 2: Verify each new assertion fails for the intended gap**

Run the matching component test immediately after adding each assertion. A test that passes before implementation must be rewritten so it covers the actual missing behavior.

- [ ] **Step 3: Apply the minimal UI changes**

Keep these invariant design values:

```css
--primary: #000000;
--secondary-container: #b4f400;
--surface-bright: #f5f5f7;
--surface-container-lowest: #ffffff;
--surface-container-low: #f2f2f7;
--outline-variant: #d1d1d6;
--radius-card: 18px;
```

All primary actions remain black pill buttons; selected/progress states remain lime; cards remain 18px; runtime assets remain local; interaction feedback uses opacity without scale transforms.

- [ ] **Step 4: Verify wide and narrow layouts**

Run the Vite preview at 1200×800 and 900×600. Confirm the left feature rail, center preview, right parameter panel, and bottom execution/status area remain visible without overlap.

- [ ] **Step 5: Run the focused UI suites**

Run:

```bash
npx vitest run tests/components/AppWorkbench.test.tsx tests/components/DropZone.test.tsx tests/components/Compressor.test.tsx tests/components/GifMaker.test.tsx tests/components/VideoConverter.test.tsx tests/components/AudioExtractor.test.tsx tests/components/ImageWorkbench.test.tsx tests/components/VideoTrimmer.test.tsx
```

Expected: all focused tests pass.

### Task 5: Remove asynchronous test warnings

**Files:**
- Modify: `tests/components/ExportPanel.test.tsx`
- Modify: `tests/components/AudioExtractor.test.tsx`
- Modify: `tests/components/VideoConverterParamMatrix.test.tsx`

- [ ] **Step 1: Capture the warning-producing tests**

Run:

```bash
npx vitest run tests/components/ExportPanel.test.tsx tests/components/AudioExtractor.test.tsx tests/components/VideoConverterParamMatrix.test.tsx 2>&1
```

Expected: tests pass but stderr contains `not wrapped in act(...)`.

- [ ] **Step 2: Await observable state changes**

Use `findBy*`, `waitFor`, or explicit timer advancement around the state update. Example:

```tsx
await waitFor(() => {
  expect(screen.getByTestId('file-info-card')).toBeInTheDocument();
});
```

For fake timers:

```tsx
await act(async () => {
  vi.advanceTimersByTime(150);
});
```

- [ ] **Step 3: Verify warning-free focused output**

Run the same command and confirm stderr contains no React `act(...)` warnings.

### Task 6: Run complete regression and offline audits

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `QA-ASSESSMENT-REPORT.md`

- [ ] **Step 1: Run all automated checks**

Run:

```bash
npx vitest run
npx tsc --noEmit
npm run build
python3 -m pytest tests/test_build_script_config.py -q
cargo check --manifest-path src-tauri/Cargo.toml
cargo test --manifest-path src-tauri/Cargo.toml --lib
```

Expected: zero test failures, zero type errors, successful frontend build, successful Rust check, and successful Rust unit tests.

- [ ] **Step 2: Audit the offline runtime**

Run:

```bash
rg -n "fetch\\(|XMLHttpRequest|axios|sentry|telemetry|googleapis|gstatic|cdn\\.|WebSocket|reqwest|hyper|tokio::net|std::net|curl|wget" src src-tauri/src index.html package.json src-tauri/Cargo.toml
```

Expected: no runtime external-network implementation. Build-time download logic remains confined to `build.py`.

- [ ] **Step 3: Record evidence**

Append the exact test counts, build output, UI checks, offline audit result, and known environmental constraints to `CHANGELOG.md` and `QA-ASSESSMENT-REPORT.md`.

### Task 7: Build and launch the Windows release in Parallels

**Files:**
- Verify: `build.py`
- Verify: `一键构建Windows版.bat`
- Verify: `logs/build_info.json`

- [ ] **Step 1: Confirm the VM and toolchain**

Run from macOS:

```bash
prlctl list --all
prlctl exec "Windows 10" cmd /c "python --version && node --version && %USERPROFILE%\\.cargo\\bin\\cargo.exe --version"
```

Expected: Windows 10 is running and Python, Node, and Rust report usable versions.

- [ ] **Step 2: Run the same user-facing build entrypoint**

Run `python build.py` from the shared project path in the Windows guest. The script may stage the project into `%LOCALAPPDATA%\TinyPixBuild`, but caches remain outside the cleaned target.

- [ ] **Step 3: Verify the build metadata and executable**

Read `logs/build_info.json`, confirm `build_success` is true, confirm the output EXE exists, and inspect its file size.

- [ ] **Step 4: Launch and verify a real window**

Launch the built executable, confirm the process remains running, capture the VM screen with:

```bash
prlctl capture "Windows 10" --file /tmp/tinypix-windows-release.png
```

The captured screen must show the TinyPix Pro top-level window on the video compression workbench.

- [ ] **Step 5: Commit the verified change set**

Stage only TinyPix 3.5 Pro source, tests, and documentation changed by this pass. Exclude secrets, caches, generated dependencies, and unrelated repository changes.

Commit with:

```bash
git commit -m "fix(tinypix): complete UI verification and Windows release"
```
