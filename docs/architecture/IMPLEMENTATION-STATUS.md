# TinyPix 4.0 implementation status

Updated: 2026-10-14（CI 首次全绿后）

## Implemented and locally verified

- Four-project modular-monolith solution and one-way project references.
- Frozen 50-item built-in tool catalog and eight template categories; archive
  compression/extraction is intentionally excluded from Toolbox.
- Job state machine, workload concurrency, cancellation, failure isolation,
  interrupted recovery, and retry-as-new-job behavior.
- Output preflight for unwritable directories, insufficient space, existing
  output conflicts, and unconditional source-overwrite rejection.
- FFmpeg progress parsing, bounded stderr capture, abnormal exit result, and
  process-tree cancellation.
- Required `portable.flag`, portable mutable directories, write probes, atomic
  JSON settings, SQLite recent/history retention, and restart interruption.
- Portable publication scripts for folder publish, manifest, dependency list,
  CycloneDX SBOM, ZIP, SHA-256, licenses, and offline-security notice.
- Design contracts for semantic foregrounds, interactive borders, media-stage
  text, five-region F6 traversal, Ctrl+J, task states, and dark/high-contrast separation.
- Repository Pencil source reopened through Pencil MCP; the current source has
  36 top-level nodes (35 boards plus the root-level reusable `SettingsDialog`)
  and 26 reusable components, including the single reusable
  `SettingsDialog` referenced by all three settings evidence frames. The 2026-07-19 representative
  recheck and current screenshots are recorded in
  `docs/audit/2026-07-19-recheck/`.
- 2026-07-19 local regression: Core 25, Media 5, Infrastructure 9 .NET tests;
  App locked restore with Windows targeting; 640 Vitest, 120 Rust, 26 Pytest,
  frontend production build, and Rust check passed.
- 2026-07-19 retrieval/audit contract recheck: 24 focused Python contracts
  passed and `git diff --check` passed.
- 2026-07-22 Windows 10 disposable WinUI gate: unpackaged/self-contained
  launch, 100/125/150% display DPI, Light/Dark/High Contrast, settings dialog,
  F6/Ctrl+J, Narrator-active focus walk, picker/drag-drop, image/video/FFmpeg
  fallback, progress/cancel/failure isolation, source hashing, read-only root,
  offline execution, registry classification, cold start, and 200% text scaling
  passed. The gate UI is explicitly marked non-final and is not an implementation source.
- Process Monitor final classification: 12 raw candidates, 11 existing-system-key
  opens, zero new keys, zero TinyPix-owned writes, and one Windows BAM
  execution-state value. Full raw evidence is retained locally and the compact
  candidate/report evidence is in `artifacts/windows/winui-feasibility-gate/`.
- 2026-07-31 formal WinUI shell skeleton in `src/TinyPix.App` (commit e3585e7):
  App composition root enforcing the `portable.flag` boundary and wiring
  `JobQueueService`, `ISettingsStore`, `IHistoryRepository`, `ICacheService`;
  MainWindow with top category NavigationView and the four-region workbench
  (`LeftFilePanel` / `WorkbenchFrame` / `RightParameterPanel` / `TaskQueue`);
  shell keyboard contract (F6 five-region cycle, Ctrl+J task queue, J/K/L
  timeline transport, Ctrl+, settings); the single reusable `SettingsDialog`;
  default `VideoOutputPage` landing page; and `LocalFileCacheService` (the
  missing `ICacheService` implementation). Two new v4 architecture contracts
  pin the shell structure and keyboard/offline boundaries; full pytest 48/48
  and `git diff --check` green on macOS. Contract-level only: no Windows
  runtime window/UIA evidence yet.
- 2026-08-14 phase 1 视频输出业务页：Core `ToolHandlerRegistry`、Media
  `VideoConvertHandler`/`VideoCompressHandler`（FFmpeg 参数构建、质量→CRF、命名
  ` (n)` 冲突）、`IFfmpegEngineProvider` 与 App `JobSubmissionService` +
  `PortableFfmpegEngineProvider` 装配、VideoOutputPage 接线；新增 C# 单测与
  `tests/test_v4_phase1_video_output.py` 结构契约（macOS pytest 全过）。dotnet
  编译与 Windows 11 运行门禁待补。
- 2026-08-14 phase 2 GIF 制作业务页：Media `GifMakerHandler`（video.gif 两遍
  palettegen→paletteuse，时长上限 30s 按 FEATURE-MATRIX 冻结口径）、`VideoOutputNaming`
  扩展 gif 格式、`VideoOutputHandlerBase` 改为可子类化（virtual 方法 + protected Engine）；
  App 注册 `GifMakerHandler`、MainWindow 顶部导航新增"GIF 制作"并切到 `GifMakerPage`；
  新增 C# 单测与 `tests/test_v4_phase2_gif_maker.py` 结构契约（macOS pytest 全过）。
  dotnet 编译与 Windows 11 运行门禁待补。
- 2026-08-14 phase 3 视频编辑业务页：Media 新增 `VideoEditHandlerBase`（BuildJobs
  抽象 + ExecuteAsync 循环 + PreviewOutputPaths/PreviewArguments + AtempoChain）与
  9 个 handler（trim/split/merge/geometry/speed-volume/audio/overlay/frames/metadata，
  全部 FFmpeg 基础、命名与 FEATURE-MATRIX 对齐）；`VideoOutputNaming.BuildOutputPathByExtension`
  支持任意扩展名；`JobSubmissionService` 扩展 `VideoEditHandlerBase` 预览；App 注册 9 handler；
  MainWindow 顶部导航新增"视频编辑"并切到 `VideoEditorPage`；新增 C# 单测与
  `tests/test_v4_phase3_video_edit.py`（macOS pytest 18 passed 含阶段1/2）。dotnet 编译与
  Windows 11 运行门禁待补。

## Gated, not claimed complete

- Static UI design freeze: passed. Standard, compact, and High Contrast settings
  evidence now uses one responsive `SettingsDialog` with a complete-root scrim;
  the repository Pencil source, exports, UI specification, and control mapping
  agree. This static result does not prove runtime focus or input behavior.
- WinUI shell skeleton (single native shell, top NavigationView, four-region
  workbench, one reusable SettingsDialog, default video-output page) is
  implemented and contract-verified on macOS as of 2026-07-31; formal business
  pages beyond the skeleton and all media handlers remain not implemented.
  Windows 10 prototype behavior passed, but the overall gate remains open until
  a clean Windows 11 x64 run exists. Per explicit user direction on 2026-07-31
  the shell phase proceeded without that VM gate, so Windows runtime
  window/UIA verification is still owed before any completion claim.
- Phase 1 视频输出 Handler（video.convert / video.compress）已在 Windows 10 19045 上真实
  编译并跑通 `video.convert`（输出 mp4 31,336 B）；`video.compress` 仅参数层覆盖。
  Windows 11 干净机的 UIA、取消/失败隔离、DPI/主题组合仍欠，不得据此声称功能完成。
- Phase 2 GIF 制作 Handler（video.gif）已在 Windows 10 19045 上真实跑通两遍
  palettegen→paletteuse 管线（输出 gif 43,439 B）；调色板质量、UIA 与取消/失败隔离尚未在
  Windows 11 验证。GIF 时长上限按 FEATURE-MATRIX 取 30s（与 MEDIA_CAPABILITIES 60s 文档冲突，
  已统一到冻结口径）。
- Phase 3 视频编辑 9 个 Handler（trim/split/merge/geometry/speed-volume/audio/overlay/
  frames/metadata）已实现并 macOS 结构契约验证：参数构造、命名冲突与校验仅在本机 pytest
  结构契约与 C# 单测（Windows 编译跑）覆盖；真实 FFmpeg 转码、合并/水印/字幕烧录等行为、
  UIA 与取消/失败隔离尚未在 Windows 11 验证；当前为契约级实现，不得据此声称功能完成。
  图片工具（11 个）未实现：Media 层当前仅 FFmpeg 引擎，缺图像引擎抽象，留作 phase 4。
- 2026-09-29 首次真实编译贯通（此前项目从未编译成功）：在 Windows 10 19045 + .NET SDK
  10.0.302 上修复 21 处编译错误后 `TinyPix.sln` 全量 Release 构建通过；84 个 C# 单测与
  72 个 Python 契约全绿。排除 GPL 编码器（6 处 `libx264` → `libopenh264`，新增
  `src/TinyPix.Media/Ffmpeg/LgplVideoCodec.cs`）；新增 `build/Get-FfmpegEngine.ps1`
  （BtbN LGPL 静态构建 + SHA-256 + libopenh264 硬门禁）与 `build/Publish-SingleFile.ps1`。
  产物：单文件 `TinyPix.exe` 160.58 MiB、便携 ZIP 172.58 MiB，均自带 ffmpeg/ffprobe。
  真实转码冒烟通过：`video.convert` → `in_转换.mp4`、`video.gif` → `in_GIF.gif`，
  源文件 SHA-256 前后一致。交付细节见 `docs/DELIVERY-2026-10-14.md`。
- 2026-10-14 GitHub Actions run `38538164421` 全流程通过（Restore / Build / Test / LGPL 门禁 /
  便携发布 / 单文件发布 / 产物上传），为该项目 CI 首次成功。此前失败已修复：LGPL 门禁误伤注释
  （改为只扫代码行）；publish 触发 NU1004（publish 时关闭 locked mode 并快照还原 lock 文件）；
  Windows PowerShell 5.1 兼容三处（`utf8NoBOM`、`Path.GetRelativePath`、`ZipFile` 程序集）。
  已知遗留：FFmpeg 用 `latest` 滚动标签，CI 与本地拿到不同构建，产物字节级不可复现。
- App project restore/build, the formal App publish, the Portable ZIP and the
  single-file EXE now pass on the configured Windows 10 VM (2026-09-29).
  Clean-machine runtime acceptance remains incomplete: the UI window launch and
  the Windows 11 DPI/theme/UIA/Narrator matrix are still owed. The automated
  launch attempt could not cover the window because the VM exec channel runs as
  SYSTEM in a non-interactive session (`UserInteractive=False`); a minimal WinUI
  app without any TinyPix XAML failed identically, which isolates the cause to
  the harness rather than the product. Manual double-click confirmation is
  required before claiming a runnable window.
- Media/OCR/PDF/model handlers and all 50 end-user workflows are contracts or
  catalog entries, not complete feature implementations yet.

See `WINDOWS-FEASIBILITY-GATE.md` and ADR-003 for the exact evidence required.
