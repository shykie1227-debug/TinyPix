# TinyPix Pro v3.5

> 面向普通办公用户的本地媒体处理工具

## 📖 产品介绍

**TinyPix Pro** 是一个完全本地运行的媒体处理工具，不需要联网，不需要账号，所有处理都在你的电脑上完成。

- 🚫 **无联网** • **无上传** • **无账号** • **无遥测** • **无数据收集**
- ✅ 解压即用 • 单 EXE • 绿色版
- 🎯 针对普通用户设计 - 打开就能用，不需要懂专业参数
- 🎨 现代 Windows 11 Fluent Design 风格

## ✨ 功能

### 🖼️ 图片工具
- 图片压缩
- 格式转换 (PNG/JPG/WebP/AVIF/BMP/GIF/HEIC/PSD)
- 尺寸调整
- 裁切
- 旋转
- EXIF 清理
- 批量处理
- 实时预估输出大小

### 🎬 视频工具
- 视频压缩
- 视频转 GIF
- 视频截图
- 视频格式转换
- 视频剪辑
- 视频提取音频


## 🛠️ 开发 & 构建

### 环境要求
- Windows 10 / Windows 11
- Python 3.10+
- Node.js / Rust / FFmpeg 可由 `build.py` 在构建期检查或准备

### 非专业用户一键构建
把整个 `3.5pro` 文件夹复制到 Windows 本地目录后，双击：

```text
一键构建Windows版.bat
```

脚本会自动调用 `build.py`。第一次构建通常需要 10-30 分钟，构建日志在 `logs/` 目录。

构建完成后优先查看：

```text
logs/build_info.json
```

里面会记录最终 EXE 路径和大小。不要使用 `build-windows-local-fixed.bat`，那是旧 v3.0 遗留脚本。

### 开发者命令
- Python 3.10+
- Windows 10 / Windows 11

构建 EXE：
```bash
python build.py
```

构建完成后，`logs/build_info.json` 会记录最终 EXE 位置。常见位置是 Tauri 的 `src-tauri/target/release/` 或 `src-tauri/target/release/bundle/` 目录。

## 🎨 设计源文件
- `/Users/huashu/Downloads/UI设计   设计demo源文件

## 🔒 隐私承诺
所有文件处理**完全本地运行**，不会上传任何数据到任何服务器。

## 📄 许可证
[你自己决定]
