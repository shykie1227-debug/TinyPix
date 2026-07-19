# TinyPix 项目快速检索目录

> 用途：新分支、新 Agent、Windows VM 或中断恢复后，先用本文件在 60 秒内找到事实源、代码入口、测试和当前门禁。
> 当前开发目标：**TinyPix 4.0 WinUI**；**旧 Tauri** 3.5.1 只作为行为基线保留，功能等价和 Windows 发布验收前不得删除。

## 0｜60 秒开始

1. 确认仓库：`git rev-parse --show-toplevel` 应返回 `/Users/huashu/TinyPix/3.5pro`（Windows 使用实际克隆路径）。
2. 确认状态：`git status --short --branch`，不得覆盖其他分支或工作区的未提交改动。
3. 先读硬约束：[LOCAL_RULES.md](LOCAL_RULES.md)。
4. 按任务查下方“任务到文件路由”，只加载相关文件。
5. `.pen` 文件只能通过 Pencil MCP 读取或修改，禁止用文本工具解析。
6. 宣布完成前读取：[UI 质量门禁](design/UI-QUALITY-GATE.md) 与 [Windows 可行性门禁](docs/architecture/WINDOWS-FEASIBILITY-GATE.md)。

## 1｜当前产品与迁移状态

| 项目 | 当前事实 |
|---|---|
| 产品 | TinyPix 4.0，Windows 10 22H2 / Windows 11 x64，本地媒体工作站 |
| 目标 UI | C#、.NET 10、WinUI 3、Windows App SDK 2.2.x |
| 架构 | 单进程模块化单体：App → Core / Media / Infrastructure |
| 部署 | Unpackaged、自包含、文件夹发布、Portable ZIP，不做单文件 EXE |
| 数据 | 仅保存路径、参数、任务与输出记录；源文件只读 |
| 网络 | 运行时完全离线，无账号、遥测、更新检查或云服务 |
| 迁移 | WinUI 骨架和核心合同已建立；正式 Shell、业务页、全部媒体 Handler 和 Portable 验收未完成 |
| 旧 Tauri | `src/` React 与 `src-tauri/` Rust 仍是 3.5.1 行为事实基线，暂不删除 |
| 当前门禁 | 静态 UI 已冻结；立即执行 Windows 可丢弃原型，真实运行验收仍未通过 |

完成边界以 [IMPLEMENTATION-STATUS.md](docs/architecture/IMPLEMENTATION-STATUS.md) 为准，不从目录存在推断功能已经完成。

## 2｜事实源优先级

发生冲突时按下列顺序解释：

1. [LOCAL_RULES.md](LOCAL_RULES.md)：离线、Portable、原文件、依赖和验收硬约束。
2. [design/TinyPix-4.0.pen](design/TinyPix-4.0.pen)：UI 结构、组件、布局和视觉设计源。
3. [design/UI-SPEC.md](design/UI-SPEC.md)：交互、状态、响应式、键盘和无障碍。
4. [design/FEATURE-MATRIX.md](design/FEATURE-MATRIX.md)：50 个工具的输入、参数、预览、输出和错误边界。
5. [design/WINUI-CONTROL-MAPPING.md](design/WINUI-CONTROL-MAPPING.md)：Pencil 到 WinUI 控件与 ViewModel 映射。
6. [DESIGN.md](DESIGN.md)：语义色、字体、间距、组件和设计冻结规则。
7. [TINPIX-4-ARCHITECTURE.md](docs/architecture/TINPIX-4-ARCHITECTURE.md) 与 ADR：模块和部署决策。
8. 自动测试与 Windows 运行证据：功能事实和完成声明。
9. [README.md](README.md) 与旧计划：背景资料；若仍描述 3.5.1，以以上 TinyPix 4.0 文件为准。

## 3｜任务到文件路由

| 要做什么 | 先读 | 主要实现位置 | 主要验证 |
|---|---|---|---|
| 产品范围、工具增删、默认值 | `design/FEATURE-MATRIX.md`、`LOCAL_RULES.md` | `src/TinyPix.Core/Tools/` | `ToolCatalogTests.cs`、设计合同测试 |
| UI、交互、颜色、图标、响应式 | `.pen`、`UI-SPEC.md`、`DESIGN.md` | `src/TinyPix.App/`（正式）；旧版仅用于行为对照 | Pencil 截图/布局、UIA、缩放和主题门禁 |
| 设置、历史、缓存、Portable 路径 | `ADR-002`、`UI-SPEC.md` | `src/TinyPix.Infrastructure/Settings`、`src/TinyPix.Infrastructure/History`、`src/TinyPix.Infrastructure/Portable` | Infrastructure tests、只读目录和重启恢复 |
| 任务队列、状态、取消、重试 | 架构文档、功能矩阵 | `src/TinyPix.Core/Jobs` | Core tests、进程取消与失败隔离 |
| FFmpeg、预览、媒体执行 | 依赖基线、功能矩阵 | `src/TinyPix.Media/Ffmpeg`；后续媒体 Handler | Media tests、真实媒体探测和输出哈希 |
| OCR、PDF、二维码、模型 | `DEPENDENCY-BASELINE.md`、功能矩阵 | 后续 `TinyPix.Media` Handler | 模型哈希、许可证、真实格式矩阵 |
| WinUI Shell 与自包含启动 | `WINUI-CONTROL-MAPPING.md`、Windows 门禁 | `src/TinyPix.App/` | Windows VM 原型、真实窗口和 UIA |
| Portable 打包、SBOM、许可证 | `ADR-002`、架构文档 | `build/*.ps1`、`assets/Portable/` | `verify-portable-layout.ps1`、全新 VM |
| 旧版行为回归 | `README.md`、`docs/MEDIA_CAPABILITIES.md` | `src/`、`src-tauri/` | Vitest、Cargo、旧版 Windows 基线 |
| 产品/UI 审计 | UI 规格、功能矩阵、当前截图 | `docs/audit/` | 同视口截图、布局快照、运行态限制说明 |

## 4｜代码目录速查

```text
TinyPix.sln
src/
├── TinyPix.App/             WinUI 组合根和正式 UI（尚未形成完整 Shell）
├── TinyPix.Core/            业务模型、接口、校验、工具目录、任务队列
├── TinyPix.Media/           FFmpeg 与后续图片/OCR/PDF/二维码/模型实现
├── TinyPix.Infrastructure/  Portable、设置、SQLite、缓存、日志、文件系统
├── components/              旧 Tauri React UI，仅用于行为等价与迁移参考
└── App.tsx                  旧 Tauri 前端入口
src-tauri/                   旧 Rust/Tauri 命令与 FFmpeg 基线
tests/
├── TinyPix.*.Tests/         新 C# 模块测试
├── test_v4_*.py             架构和设计合同
└── components|hooks|...     旧版 Vitest 行为回归
design/                      Pencil、功能矩阵、UI 规格、控件映射、导出图
docs/architecture/           ADR、实现状态、Windows 门禁
assets/Portable/             Portable 发布目录模板
build/                       PowerShell 发布、SBOM、布局验证脚本
```

依赖方向固定为：`TinyPix.App → TinyPix.Core / TinyPix.Media / TinyPix.Infrastructure`，Media 和 Infrastructure 只依赖 Core，Core 不依赖任何外层项目。

## 5｜快速搜索关键词

优先使用 `rg`；不要搜索 `.pen` 内容。

```bash
rg -n "video\.trim|code\.generate|ToolDescriptor" design src/TinyPix.Core tests --glob '!*.pen'
rg -n "SettingsDialog|portable.flag|History|Cache" design docs src/TinyPix.* tests --glob '!*.pen'
rg -n "时间轴|J/K/L|I/O|Ctrl\+J|F6" design docs tests --glob '!*.pen'
rg -n "尚未|未完成|未验证|Gated|gate" docs/architecture design --glob '!*.pen'
```

## 6｜验证命令

### 跨平台合同

```bash
python3 -m pytest -q tests/test_v4_design_contracts.py tests/test_v4_architecture_contract.py
git diff --check
```

### Windows 上的新架构

```powershell
dotnet test tests/TinyPix.Core.Tests/TinyPix.Core.Tests.csproj
dotnet test tests/TinyPix.Media.Tests/TinyPix.Media.Tests.csproj
dotnet test tests/TinyPix.Infrastructure.Tests/TinyPix.Infrastructure.Tests.csproj
dotnet restore src/TinyPix.App/TinyPix.App.csproj --locked-mode -p:EnableWindowsTargeting=true
```

### 旧 Tauri 行为基线

```bash
npm test
npm run build
cd src-tauri && cargo test --lib && cargo check
```

自动测试不能替代 Windows 10/11 的窗口、拖放、FFmpeg、注册表、断网、Narrator、缩放和 Portable ZIP 验收。

## 7｜Git 分支工作流

1. 从同步后的 `main` 创建 `codex/<任务名>` 分支。
2. 开始前运行 `git status --short --branch`，保护现有未提交成果。
3. 一个分支只解决一个可验收目标；大任务按门禁拆分。
4. 提交前运行相关测试、`git diff --check` 和敏感文件检查。
5. 先合并到本地 `main`，确认工作区干净、分支无独有提交，再同步 `origin/main`。
6. 只有 `git merge-base --is-ancestor <branch> main` 成功后，才用 `git branch -d <branch>` 删除本地分支。
7. 禁止未经明确授权执行 `reset --hard`、无范围 `clean`、强制推送或删除用户成果。

## 8｜Windows 可行性门禁：静态 UI 冻结后的入口

设置弹窗静态冻结证据已通过：1200×800、900×600 和 High Contrast 使用同一弹窗语义，完整遮罩 `XamlRoot`，Pencil、UI 规格、控件映射和审计证据一致。现在不再扩展静态页面，立即执行 [Windows 可行性门禁](docs/architecture/WINDOWS-FEASIBILITY-GATE.md)：

1. 在 Windows VM 重跑合同和 C# 测试。
2. 在 `%TEMP%` 创建可丢弃 WinUI 原型，不复制原型 UI 到正式工程。
3. 验证 Unpackaged/self-contained 启动、四区 Shell、唯一设置弹窗。
4. 验证拖放、图片/视频预览、FFmpeg 进度/取消/异常退出。
5. 验证 900×600、1200×800、Light/Dark/High Contrast、键盘和 Narrator。
6. 验证 `portable.flag`、目录不可写、断网和无主动注册表写入。
7. 输出证据报告；全部通过后才进入正式 WinUI Shell。

## 9｜禁止误判

- 有目录、接口或工具 ID，不等于功能已经实现。
- 有 Pencil 页面，不等于 WinUI 控件、键盘或无障碍已经工作。
- 有打包脚本，不等于 Portable ZIP 已在全新 Windows VM 通过。
- 不把旧 Tauri 页面直接复制成 WinUI 架构，也不在功能等价前删除旧源码。
- 不从 Desktop、临时目录或其他 worktree 的 `.pen` 覆盖仓库设计源。
