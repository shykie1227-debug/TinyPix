# TinyPix Pro 3.5

TinyPix 是面向普通 Windows 用户的本地图片与视频处理工具。成品是一个免安装的 Windows x64 EXE；运行时不联网、不上传、不需要账号、不写注册表，也不包含遥测或自动更新。

## 当前功能

- 视频输出：MP4、MOV、MKV、AVI、WebM，以及 MP3、WAV、AAC、FLAC 音频输出；批量任务单项失败不会中断队列。
- GIF 制作：起止时间、尺寸、帧率、循环和质量，使用 FFmpeg 调色板滤镜降低色带。
- 视频剪辑：默认无损快速剪辑；需要精确边界时切换为 CPU 重编码。
- 图片处理：JPG/JPEG、PNG、WebP、AVIF、BMP、TIFF/TIF、PSD 输入；JPG、PNG、WebP、AVIF、BMP 输出；支持裁切、旋转、镜像、尺寸、色彩、透明度和 EXIF 清理。

准确格式和参数见 [媒体能力契约](docs/MEDIA_CAPABILITIES.md)。

## 开发验证

```bash
npm ci
npm test
npm run build
cd src-tauri && cargo test --lib && cargo check && cargo fmt --check
cd .. && python3 -m pytest -q
```

## Windows 单 EXE 构建

在 Windows 10/11 x64 普通用户环境运行：

```text
C:\3.5pro\BUILD_WINDOWS.cmd
```

`BUILD_WINDOWS.cmd` 是纯 ASCII + Windows CRLF 的入口，避免中文批处理文件名或编码导致 `python build.py` 被解析成 `ython` / `ld.py` 等错误。也可以在 `C:\3.5pro` 目录手动运行：

```text
python build.py
```

构建脚本下载固定的 Gyan FFmpeg 8.1.2 essentials 包并验证固定 SHA-256，然后把 FFmpeg/FFprobe 作为字节资源编入 Tauri 主程序。最终唯一交付物为：

```text
TinyPix-Pro-3.5.1-Windows-x64-Portable.exe
```

构建日志和产物元数据写入 `logs/`。发布前仍必须在 Windows 普通用户、断网环境执行冷启动、二次启动、图片/视频主流程和缓存重建测试。

## 许可证

TinyPix 使用 [MIT License](LICENSE)。FFmpeg 等第三方组件的版本、许可证、构建来源和源码获取方式见 [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES)，同样内置于应用设置页。
