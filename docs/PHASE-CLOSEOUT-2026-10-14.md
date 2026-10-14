# TinyPix 4.0 阶段交接报告

日期：2026-10-14（构建 2026-09-29，本日回传与收尾）
审计时分支：`codex/build-package-and-lgpl-codec` → 已 `--no-ff` 合入本地 `main`（`34ab6e6`）
远端同步：本地 `main` 领先 `origin/main` 3 个提交，**未推送**（遵守"不擅自 push"约束）

## 1. 进度记录

### 本阶段完成并已提交

| 提交 | 内容 |
|---|---|
| `00e77af` | 首次真实编译贯通（21 处编译错误修复）+ GPL 编码器排除 + 双形态打包 |
| `5bce144` | 产物回传与交付说明、实现状态与索引更新 |
| `34ab6e6` | merge 进本地 main |

- **项目首次真正编译通过**。此前从未编译成功：Windows 10 19045 + .NET SDK 10.0.302 上暴露并修复 21 处编译错误
  （9 个 handler 缺 `using TinyPix.Media.Ffmpeg;`、10 处元组类型不匹配、`VideoSplitHandler.ParsePoints` LINQ 类型错误、
  `EmptyStateControl` 用了 WinUI 不存在的 `BorderStyle`、`VisualState x:Name="DragOver"` 与 `UIElement.DragOver` 冲突、
  `MainWindow.Current` 隐藏基类成员、`VirtualKey.OemComma` 不存在（改 `VK_OEM_COMMA = 188`）、3 个页面缺 DI/Navigation using、
  `JobSubmissionService` 类型误用、`[ObservableProperty]` 字段形式触发 MVVMTK0045）。
- **许可证合规**：新增 `src/TinyPix.Media/Ffmpeg/LgplVideoCodec.cs`，6 处 `libx264` → `libopenh264`，WebM 保留 `libvpx`。
- **引擎随包**：新增 `build/Get-FfmpegEngine.ps1`（BtbN LGPL 静态构建 + SHA-256 + "必须含 libopenh264、不得含 GPL/nonfree"硬门禁）。
- **打包**：新增 `build/Publish-SingleFile.ps1` 与 `IncludeAllContentForSelfExtract`；`LOCAL_RULES.md` §2 补单文件为可选附加产物。
- **CI 修复**：移除失效的 `dotnet workload install microsoft-windows-appsdk`（ID 不存在，且 pwsh 的 `|| echo` 不重置 `$LASTEXITCODE`）；
  补充解决方案构建、三库测试、LGPL 编码器门禁。
- **缺陷修复**：`GifMakerHandler` 依赖基类 `format` 参数导致 `KeyNotFoundException`；5 个写死 Unix 路径分隔符的测试；
  1 个自相矛盾的注册表测试；架构契约中不存在的 `VirtualKey.OemComma` 标记。
- **产物回传**：`artifacts/TinyPix.exe`（160.58 MiB）与 `artifacts/TinyPix-4.0-Windows-x64-Portable.zip`（172.58 MiB），
  Mac 侧与 VM 侧两次独立哈希计算一致。

### 本阶段执行的验证

- C# 单测：**84 全通过**（Core 32 / Media 42 / Infrastructure 10），macOS 与 Windows 两侧各跑一遍。
- Python 契约：**72 全通过**（需 `--basetemp=/tmp/pytest-tp`，否则沙箱下默认临时目录报 EEXIST）。
- 真实转码冒烟（Windows 10 19045，无 UI 控制台进程）：
  - `video.convert` → `in_转换.mp4` 31,336 B ✅
  - `video.gif` → `in_GIF.gif` 43,439 B（两遍 palettegen→paletteuse）✅
  - 源文件 SHA-256 前后一致 ✅（`LOCAL_RULES.md` 验收门槛）
- LGPL 门禁：随包引擎**含 `libopenh264`，不含任何 GPL/nonfree 编码器** ✅

### Gated，尚未完成

- **窗口启动（UI）未自动验证**：见第 3 节"VM 操作硬知识"，需人工双击确认。
- **Windows 11 干净机门禁未做**：DPI（100/125/150%）、Light/Dark/High Contrast、UIA、Narrator、200% 文本缩放、
  F6/Ctrl+J 键盘遍历、拖放全部只在 Win10 19045 上覆盖或完全未覆盖。
- **38/50 工具无 handler**：图片 11、PDF 8、证件照 3 等仍只有目录条目和契约；Media 层只有 FFmpeg 引擎，缺图像/PDF 引擎抽象。
- `IModelRuntime` / `IMediaPreviewService` 只有接口无实现；三个业务页参数面板与 handler 参数未完整接线。
- SBOM / 第三方声明 / 离线安全说明的最终 release gate 未跑。

## 2. 下一步计划

**P1：人工确认窗口启动 + Win11 门禁**（0.5 次会话）
在 Windows 机器（VM 内用资源管理器双击，或真机）打开 `TinyPix.exe`，确认四分区主窗口出现；
然后在 Win11 上重复 DPI/主题/UIA/Narrator 检查。这步通过前不得声称"可运行的 TinyPix 4.0"。

**P2：Phase 4 图片工作台**（2–4 次会话）
引入图像引擎抽象与 handler：resize / crop / rotate / flip / convert / compress / 调整 / 水印 / EXIF 查看清除 /
拼图联系表 / 批处理。依赖选型与许可证需先确认（SkiaSharp vs Magick.NET）。保持"一个图片工作台入口"的产品边界。

**P3：证件照与本地模型门禁**（3–5 次会话）
模型运行时校验、许可证与哈希校验、纯 CPU 推理边界；证件照模板、人脸定位、换背景、DPI/mm/像素校验、打印排版。

**P4：工具箱工作流**（3–6 次会话，按族拆分）
二维码/条码生成与识别、PDF 页面管理、OCR、录屏、批量重命名、校验和、取色、剪贴板存图、元数据工具。
不重新引入重复文件检测与压缩包压缩解压。

**P5：最终发布门禁**（1–2 次会话）
Win10 + Win11 干净机验收，产出最终 Portable ZIP、SHA-256、SBOM、manifest、第三方声明、离线安全说明。

## 3. 给下一个 Agent 的实施指引

### VM 操作硬知识（踩过的坑，别重蹈）

- 用 `/Applications/Parallels Desktop.app/Contents/MacOS/prlctl`，**不要**用 `/usr/local/bin/prlctl`（包装脚本被 macOS 拒绝）。
- VM 长时间不操作会变成 `suspended`，先 `prlctl resume "Windows 10"` 再 exec，否则命令静默失败。
- `prlctl exec` 必须 nohup 后台跑，日志写回 `\\Mac\Home`（即 `/Users/huashu`）；前台调用会被中断。
- **`prlctl exec` 是 SYSTEM 非交互会话**（`UserInteractive=False`），WinUI 的 `Microsoft.UI.Input.dll` 必然 fail-fast
  （0xC000027B / 0xC0000602）。已用不含任何本项目 XAML 的最小 WinUI 应用对照证实。**不要试图用这条通道验证窗口**，
  改用无 UI 的控制台冒烟测试验证媒体链路。
- 源码 robocopy 到 `C:\TinyPixBuild\src` 后必须 `attrib -R`，否则 NuGet 改 lock 文件报 Access denied。
- 大文件（如 173 MB 的 FFmpeg 包）在 **Mac 侧下载**再经共享目录拷进 VM；VM 内直连 GitHub 只有 ~22 KB/s。
- 回传产物同样走共享目录，VM 内 `Compress-Archive` 后 `Copy-Item` 到 `\\Mac\Home\TinyPix\3.5pro\artifacts`，两边各算一次 SHA-256 比对。
- 残留 powershell 会锁住正在下载的文件；`taskkill /F /IM powershell.exe` 会连当前 exec 会话一起杀，应先杀再开新会话。

### 构建与打包

```powershell
pwsh ./build/publish-portable.ps1      # 便携 ZIP（正式形态）
pwsh ./build/Publish-SingleFile.ps1    # 单文件 TinyPix.exe（可选形态）
```

- **Windows App SDK 版本铁律**：WinUI 与 Runtime 必须同属一个版本序列（现为 2.2.x）。错配能编译，但运行时 fail-fast 0xC000027B。
- 单文件 exe 需同时满足：`WindowsPackageType=None` + `SelfContained` + `WindowsAppSDKSelfContained` +
  `EnableMsixTooling` + `IncludeAllContentForSelfExtract` + `PublishSingleFile`。
- macOS 只能编译/测试 Core / Media / Infrastructure 三个 `net10.0` 库（SDK 在 `/Users/huashu/TinyPix/.workbuddy/tools/dotnet`，
  加 `-p:EnableWindowsTargeting=true` 可 restore App）；**出 exe 必须走 VM**。

### 交付与文档口径

- 产物与验证步骤见 `docs/DELIVERY-2026-10-14.md`；完整调研见 `docs/SURVEY-2026-09-28.md`。
- 阶段进度记两处：`CHANGELOG.md`（顶部条目）+ `docs/architecture/IMPLEMENTATION-STATUS.md`（Implemented + Gated 两段）。
- FEATURE-MATRIX 为冻结权威（如 GIF 时长 30s 优于 MEDIA_CAPABILITIES 的 60s）。
- 有目录/接口/工具 ID ≠ 功能已实现；**有 exe ≠ 窗口已验证**。
