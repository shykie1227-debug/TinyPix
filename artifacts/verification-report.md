# TinyPix Pro 3.5.0 验证报告

## 交付产物

- 文件：`/Users/huashu/Desktop/tiny/TinyPix-Pro-3.5.0-Windows-x64-Portable.exe`
- 大小：223,005,696 bytes（212.67 MiB）
- SHA-256：`c999007cd3b01752238cd29bb936b704b352462f0f7470b6ceec3a434e9a9759`
- Windows：Windows 10 x64 19045，普通用户 `F1B7\huashu`
- 媒体引擎：FFmpeg/FFprobe 8.1.2 essentials，内置于单 EXE

## 自动化验证

- 前端：34 个文件、612 个 Vitest 测试通过；无 `act(...)`、Promise 或控制台警告。
- 前端构建：TypeScript 与 Vite production build 通过。
- Rust：95 个测试通过；`cargo check`、`cargo fmt --check` 通过。
- Python：11 个构建脚本测试通过；仅有本机 Requests 依赖版本提示。
- 仓库：`git diff --check` 通过。
- 依赖：`npm audit --omit=dev` 为 0 个运行时漏洞；全量 npm audit 仍报告 7 个仅开发工具链问题（Vitest/esbuild/form-data，不进入 EXE），升级需要跨 Vitest 大版本，未在本次稳定版中强制更新。`cargo audit` 退出码 0，保留 18 个来自跨平台传递依赖的 allowed warnings，Windows 交付物未发现被拒绝的 RustSec 漏洞。

## 媒体与视觉矩阵

- Windows 媒体矩阵 14/14：MP4、MOV、MKV、AVI、WebM，MP3、WAV、AAC、FLAC，GIF，无损剪辑、精确剪辑、无音轨输入、损坏文件拒绝。
- UI 视觉审计 20/20：视频输出、GIF、视频剪辑、图片工作台、设置页；覆盖 1200×800、900×600 和 100%/125%/150% 缩放。
- 全部审计项无页面水平溢出、文字裁切、脚本错误或主操作按钮不可见；设置页主按钮固定可见，内容区独立滚动。

## Windows 离线与绿色版验证

- 断网冷启动：1,189 ms（门槛 15,000 ms）。
- 首次引擎释放与校验：5,790 ms；窗口保持响应。
- 第二次启动：355 ms（门槛 5,000 ms）。
- 运行进程 TCP 连接：0。
- 注册表安装项、服务、计划任务在运行前后均未变化。
- FFmpeg 与 FFprobe 释放后的 SHA-256 校验通过，缓存清理后可重建。

## 证据

- `artifacts/windows/launch-validation.json`
- `artifacts/windows/media-matrix-report.json`
- `artifacts/ui/layout-report.json`
- `artifacts/windows/windows-cold-start.png`
- `artifacts/windows/windows-settings-engine.png`
- `logs/build.log`（完整 Windows 构建日志；最终 UI 增量构建的准确哈希以本报告和 `artifacts/final-build-info.json` 为准）

## 范围说明

- 当前交付采用稳定通用 CPU 编码；硬件编码、自定义 FFmpeg 命令、多轨时间线和专业调色不在本版范围。
- 未提交、未推送、未创建 GitHub Release。
