# TinyPix 3.5 媒体能力契约

此表是界面、文件选择、Rust 校验和测试的共同发布口径。没有通过自动化与 Windows 实测的格式不得出现在发布文案中。

## 图片

| 能力 | 支持范围 |
|---|---|
| 输入 | JPG/JPEG、PNG、WebP、AVIF、BMP、TIFF/TIF、PSD |
| 输出 | JPG、PNG、WebP、AVIF、BMP |
| 处理顺序 | 裁切 → 90° 旋转 → 镜像 → 尺寸 → 色彩 → 透明背景 → 编码 |
| 尺寸 | 等比最长边；或同时填写精确宽高 |
| 色彩 | 亮度、对比度、饱和度、锐化 |
| 隐私 | 可清除 EXIF/GPS/设备信息 |

GIF、HEIC、PDF、PPT、AI、EPS、SVG 不属于图片工作台支持格式。

## 视频输出

| 输出 | 视频编码 | 音频编码 |
|---|---|---|
| MP4 | H.264 | AAC |
| MOV | H.264 | AAC |
| MKV | H.264 | AAC |
| AVI | MPEG-4 Part 2 | MP3 |
| WebM | VP9 | Opus |
| MP3/WAV/AAC/FLAC | 不适用 | 对应稳定 CPU 编码器 |

用途预设为通用兼容、均衡和小体积。首版不提供硬件编码、任意 FFmpeg 命令或专业调色。

## GIF 与剪辑

- GIF：起止时间、尺寸、10/15/24 FPS、循环次数、质量；调色板生成和使用滤镜；最长 60 秒。
- 剪辑：默认无损流复制并提示关键帧误差；精确边界使用 H.264/AAC CPU 重编码。

## 媒体引擎与离线边界

- 构建固定使用 Gyan FFmpeg 8.1.2 essentials ZIP，校验固定 SHA-256 后才允许发布。
- FFmpeg/FFprobe 编入主 EXE，首次运行原子释放到 `%LOCALAPPDATA%\TinyPix\engine\<hash>\`。
- 运行时不联网、不上传、不遥测、不检查更新；不创建服务、计划任务、安装记录或注册表项。
