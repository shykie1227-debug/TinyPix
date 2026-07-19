# TinyPix 4.0 依赖与许可证设计基线

核验日期：2026-07-17
适用范围：完全离线、Windows x64、C#/.NET 10、WinUI 3 便携版。本文件作为 TinyPix 4.0 依赖选型、构建门禁与发布验收的设计基线。
说明：本基线是工程与再分发核验，不替代律师的正式法律意见。CPU/内存“边界”是 TinyPix 必须主动实施的工程上限；除明确引用的模型参数外，不把估算写成上游承诺。

## 1. 最终冻结结论

| 项目 | 结论 | 冻结状态 |
|---|---|---|
| PDF 页面栅格化 | 采用 `PDFtoImage 5.2.1 + PDFium 147.0.7690 + SkiaSharp 3.119.2`；PDFsharp/PdfPig 继续分别承担编辑/文本提取 | 可冻结，但 PDFium 必须额外带完整第三方许可证目录 |
| 人脸检测与关键点 | 采用 OpenCV Zoo `face_detection_yunet_2023mar.onnx`；一次输出边界框、置信度和 5 个关键点 | 可冻结 |
| 人像前景分割 | 采用 MODNet 官方 ONNX `modnet_photographic_portrait_matting.onnx` | 可冻结到本基线 SHA-256；上游 Google Drive URL 本身不可变性不足，发行时必须使用项目内受控副本和哈希门禁 |
| OCR 中英数据 | 采用官方 `tessdata_fast 4.1.0` 的 `eng.traineddata`、`chi_sim.traineddata`，Tesseract 使用 OEM 1 | 可冻结 |
| FFmpeg 8.1.2 | 源码冻结到官方 tag/提交；只允许自建或逐项审计的 LGPL 构建，禁止 GPL/nonfree | 源码可冻结；`ffmpeg.exe`/`ffprobe.exe` 哈希须在正式构建后冻结 |
| 离线文字水印与字幕 | FFmpeg 的 `subtitles` 滤镜统一使用 `libass 0.17.5 + FreeType 2.14.3 + HarfBuzz 14.2.1 + FriBidi 1.0.16`，并内置 Noto Sans CJK SC 2.004 Regular | 源码、字体、许可证和路径可冻结；原生库随正式 FFmpeg 构建一起生成二进制哈希 |

不采用：RMBG-1.4（CC BY-NC，企业商用不适合）、InsightFace/SCRFD 未逐模型确认商用权利的权重、来源不明的 MODNet 二次转换文件、常见的 GPL “full” FFmpeg Windows 构建。

## 2. PDF 页面栅格化

### 2.1 推荐组合

| 字段 | 锁定值 |
|---|---|
| 托管库 | `PDFtoImage` 5.2.1 |
| 不可变源码 | tag `v5.2.1`，commit `0eb81fdffb9bcd27ba34a4fa0bac63318844de1b` |
| NuGet 资产 | `pdftoimage.5.2.1.nupkg` |
| NuGet SHA-256 | `0db8fd1ec91d711842ef61d7140c8900fc0555d9a7850d932256b703adb7d099` |
| 原生渲染器 | PDFium `147.0.7690`，Chromium build `7690`，Windows x64，V8/XFA 关闭 |
| PDFium 源构建提交 | `bblanchon/pdfium-binaries` commit `9d8f70d3f4c0d37c0a479407805ceab4dd68d516`（NuGet 元数据记录） |
| PDFium 官方发布资产 | `pdfium-win-x64.tgz`，release tag `chromium/7690` |
| PDFium 归档 SHA-256 | `06ef95ac4f9b8897731224639ddf0f185693cb48bc9ee650f1e92f71e0d2a94e`（与 GitHub Release digest 一致） |
| 原生 DLL | `pdfium.dll`，5,802,496 bytes |
| DLL SHA-256 | `15df9dddd81eddc5a177946aa5e34cda821ebc46a51440ecb607f91e99644895` |
| 图像层 | SkiaSharp 3.119.2（由 PDFtoImage 5.2.1 锁定） |
| 建议路径 | `Engines/PDFium/pdfium.dll`；`Licenses/PDFium/LICENSE`；`Licenses/PDFium/third-party/*` |

用途：将 PDF 页面渲染为位图，供页面预览、PDF 转图片、扫描 PDF OCR 前处理和缩略图使用。PDFsharp 不负责通用 PDF 栅格化；PdfPig 更适合文本/结构提取，因此这条依赖是必要补足。

### 2.2 许可证与再分发判断

- PDFtoImage 源码与包：MIT，保留版权及许可文本即可。
- SkiaSharp：MIT；发行清单中需记录实际 NuGet 版本及其原生资产许可。
- PDFium：不是“只有 Apache-2.0 的单一作品”。PDFium 及其编入的 zlib、libpng、freetype、ICU、libjpeg-turbo、OpenJPEG、LCMS、libtiff 等组件各有许可。
- 关键风险：`bblanchon.PDFium.Win32 147.0.7690` NuGet 元数据标为 Apache-2.0，但该 nupkg 中没有发布归档所带的完整 `licenses/` 目录。只复制 NuGet 里的 `pdfium.dll` 和 Apache 文本不够稳妥。
- 门禁：构建时必须从匹配的 `chromium/7690` 官方 x64 归档提取 `LICENSE` 与整个 `licenses/`，原样进入 `Licenses/PDFium/`；不得用其他 Chromium build 的 notices 混配。
- 归档里的 `pdfium.dll` 与 NuGet 147.0.7690 x64 DLL SHA-256 完全一致，证明 notices 与二进制版本对应。

### 2.3 CPU/内存边界

- PDFium 非线程安全；PDFtoImage 明确对 PDFium 调用加锁，因此产品层按“同一时刻只栅格化 1 个 PDF 页面”设计，不增加并发渲染线程。
- 必须逐页流式渲染并及时释放 bitmap，不得一次把整份 PDF 的所有页保留在内存。
- 300 DPI A4 RGBA 原始像素约 `2480 × 3508 × 4 ≈ 33.2 MiB`；Skia、托管复制和编码缓冲可能产生 2–4 份副本。TinyPix 建议单页工作集预算 256 MiB、硬上限 512 MiB。
- 默认预览 150 DPI；导出默认不超过 300 DPI。超过 60 MP 的单页要求用户降低 DPI，避免畸形页面或压缩炸弹导致内存失控。

### 2.4 独立证据渠道

1. PDFtoImage 官方仓库、tag、项目文件与许可证：
   <https://github.com/sungaila/PDFtoImage/tree/v5.2.1>
   <https://github.com/sungaila/PDFtoImage/blob/v5.2.1/src/Directory.Packages.props>
   <https://github.com/sungaila/PDFtoImage/blob/v5.2.1/LICENSE>
2. NuGet 官方包页：<https://www.nuget.org/packages/PDFtoImage/5.2.1>
3. PDFium 二进制官方发布及构建仓库：
   <https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium%2F7690>
   <https://github.com/bblanchon/pdfium-binaries>
4. PDFium 官方源码许可证入口：<https://pdfium.googlesource.com/pdfium/+/refs/heads/main/LICENSE>

## 3. 人脸检测与 5 点关键点

### 3.1 锁定资产

| 字段 | 锁定值 |
|---|---|
| 项目 | OpenCV Zoo YuNet |
| 模型 | `face_detection_yunet_2023mar.onnx` |
| 不可变提交 | `f12e12798e8314f7c074a6656816c048dcc95b7a`（该模型文件最近提交） |
| 文件大小 | 232,589 bytes |
| SHA-256 | `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4`，与 Git LFS oid 一致 |
| 许可证 | MIT；模型目录明确声明目录内全部文件适用 MIT |
| 建议路径 | `Models/Face/face_detection_yunet_2023mar.onnx`；`Licenses/YuNet/LICENSE-MIT.txt` |

用途：证件照人脸定位、单/多/无人脸判定、眼睛水平、鼻尖和嘴角位置、基础侧脸/构图提示。OpenCV `FaceDetectorYN` 输出 15 列：人脸框 4 项、双眼/鼻尖/双嘴角共 10 项、置信度 1 项。

边界：这不是 68/106 点精细关键点模型。对 TinyPix 当前的证件照自动裁切、眼线与基本姿态校验足够；不得把 5 点输出宣称为高精度头部姿态或生物识别。侧脸、遮挡、极小脸仍需明确提示用户人工确认。

### 3.2 CPU/内存边界

- 模型仅约 227 KiB，固定采用 OpenCV 4.x 兼容的 2023mar 版本；预处理把检测图长边限制在 1280 px，检测后坐标映射回原图。
- 复用一个 detector/session，批处理最多 2 个轻任务并发；证件照完整流程仍遵守全局“1 个媒体重任务”并发。
- 单次检测建议工作集预算 128 MiB、硬上限 256 MiB；这是包含解码图像和 OpenCV 中间缓冲的产品预算，不是模型本体内存。

### 3.3 再分发风险

- MIT 允许商业使用与再分发；发行包必须保留 YuNet 的版权及许可文本。
- 只进行本地推理，不保存人脸特征模板。TinyPix 应在离线安全说明中写明：人脸框和关键点仅在内存/临时缓存中用于构图，不上传、不建立身份库。

### 3.4 独立证据渠道

1. OpenCV Zoo 模型、README 与目录专属许可证：
   <https://github.com/opencv/opencv_zoo/tree/f12e12798e8314f7c074a6656816c048dcc95b7a/models/face_detection_yunet>
   <https://github.com/opencv/opencv_zoo/blob/f12e12798e8314f7c074a6656816c048dcc95b7a/models/face_detection_yunet/LICENSE>
2. OpenCV 官方 API 头文件对 15 列输出的定义：
   <https://github.com/opencv/opencv/blob/4.x/modules/objdetect/include/opencv2/objdetect/face.hpp>
3. 原始 YuNet 训练仓库：
   <https://github.com/ShiqiYu/libfacedetection.train/tree/a61a428929148171b488f024b5d6774f93cdbc13>

## 4. 人像前景分割

### 4.1 锁定资产

| 字段 | 锁定值 |
|---|---|
| 项目 | MODNet: Trimap-Free Portrait Matting |
| 官方仓库提交 | `28165a451e4610c9d77cfdf925a94610bb2810fb` |
| 模型资产 | `modnet_photographic_portrait_matting.onnx`（官方仓库 ONNX 文档链接的 Image Matting Model） |
| 文件大小 | 25,890,335 bytes |
| SHA-256 | `07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9` |
| 许可证 | Apache-2.0；官方 README 明确“code, models and demos”适用 Apache-2.0（排除指定 GIF） |
| 建议路径 | `Models/Portrait/modnet_photographic_portrait_matting.onnx`；`Licenses/MODNet/LICENSE-APACHE-2.0.txt` |

用途：人像 alpha matte、证件照背景移除、白/蓝/红/自定义背景替换和透明 PNG 输出。

可复核规格：Open Model Zoo 对同源 MODNet 记录为 MobileNetV2 主干、6.4597M 参数、31.1564 GFLOPs，输入 `1×3×512×512`，输出 `1×1×512×512` alpha matte。

### 4.2 冻结与来源风险

- 官方仓库提供 ONNX 下载入口并明确模型许可，但文件托管在 Google Drive，URL 不是内容寻址，也没有官方发布 tag/digest。
- 因此不能把 Google Drive URL 单独视为可重复构建来源。可靠冻结方式是：现在取得文件后以本基线 SHA-256 为唯一门禁，复制到受版本清单控制的本地依赖缓存；构建时不再联网下载。
- 每次发行前校验文件大小与 SHA-256；不一致立即失败，不“自动接受新版”。
- 如需从源码可重复转换，备选是固定 Open Model Zoo `2024.6.0`（commit `e7df86da686d2e1600282422e54f66c2fecea160`）的 `model.yml`、其固定 checkpoint 和转换参数；转换后的 ONNX 仍须由 TinyPix 自己生成并冻结新哈希，不能假设不同 PyTorch/ONNX 版本的输出逐字节一致。

### 4.3 CPU/内存边界

- 固定 512×512、batch=1、CPUExecutionProvider；只创建并复用 1 个 ONNX Runtime session；禁止并发跑多个 MODNet session。
- 31.1564 GFLOPs 表明它属于媒体重任务。建议单次推理工作集预算 512 MiB、硬上限 1 GiB；原始大图只保留一份，推理用缩放副本，最终 alpha 再映射回原尺寸。
- 输入图片硬上限 50 MP；超过时先生成受控预览/缩放副本。输出大图合成采用行/块处理，避免同时持有多份全尺寸 RGBA。
- 低内存机器必须允许取消并给出“模型内存不足”，不能让系统分页后无响应。

### 4.4 再分发风险

- Apache-2.0 允许商业使用与再分发；保留 LICENSE、版权/归属信息及修改说明。
- 训练数据本身未随模型仓库再分发。本基线依据上游对“models”的明确许可；仍应在 `THIRD_PARTY_NOTICES.txt` 标明模型名称、来源、用途、commit、文件哈希和 Apache-2.0。
- 不采用 BRIA RMBG-1.4 作为替代，因为其常见权重许可是 CC BY-NC 4.0，与企业商业发行目标冲突。

### 4.5 独立证据渠道

1. MODNet 官方仓库、ONNX 说明和许可证：
   <https://github.com/ZHKKKe/MODNet/tree/28165a451e4610c9d77cfdf925a94610bb2810fb>
   <https://github.com/ZHKKKe/MODNet/blob/28165a451e4610c9d77cfdf925a94610bb2810fb/onnx/README.md>
   <https://github.com/ZHKKKe/MODNet/blob/28165a451e4610c9d77cfdf925a94610bb2810fb/LICENSE>
2. Intel Open Model Zoo 的独立模型规格、转换配置和法律说明：
   <https://github.com/openvinotoolkit/open_model_zoo/tree/2024.6.0/models/public/modnet-photographic-portrait-matting>
   <https://docs.openvino.ai/2023.3/omz_models_model_modnet_photographic_portrait_matting.html>

## 5. OCR 中英 tessdata

### 5.1 锁定资产

项目：Tesseract 官方 `tessdata_fast` 4.1.0，commit `65727574dfcd264acbb0c3e07860e4e9e9b22185`。这些数据适用于 Tesseract 4.0 及以上，包括 Tesseract 5；仅支持 LSTM 引擎，TinyPix 必须固定 `OEM=1`。

| 资产 | 用途 | 大小 | SHA-256 | 建议路径 |
|---|---|---:|---|---|
| `eng.traineddata` | 英文 OCR | 4,113,088 bytes | `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2` | `Models/Tesseract/tessdata/eng.traineddata` |
| `chi_sim.traineddata` | 简体中文 OCR | 2,469,156 bytes | `a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730` | `Models/Tesseract/tessdata/chi_sim.traineddata` |

建议默认语言：`chi_sim+eng`；纯英文页面可单独使用 `eng` 以降低耗时。`osd.traineddata` 只在启用自动方向/脚本检测时需要，不把它伪装成中英识别的强制资产；若产品决定启用 OSD，应作为单独依赖重新下载、核验完整大小并冻结 SHA-256。

### 5.2 许可证与再分发

- `tessdata_fast` 仓库许可证为 Apache-2.0，允许商业再分发。
- 发行包保留 Apache-2.0 文本，在第三方清单中分别登记两个 traineddata 的版本、commit、大小、SHA-256。
- 不允许启动时下载语言包；模型缺失或哈希不符时提示“本地 OCR 模型缺失/损坏”，并提供离线修复包流程。

### 5.3 CPU/内存边界

- `tessdata_fast` 是官方定位的整数化 LSTM、速度最快方案；比 `tessdata_best` 更符合便携离线工具的默认体验。
- 每个 OCR 任务只处理 1 页，最多 1 个 OCR worker；扫描 PDF 逐页渲染、识别、落盘后释放。
- 输入硬上限 25 MP；默认把有效文字区控制在 300 DPI 左右。建议单页工作集预算 512 MiB、硬上限 1 GiB。
- 对低对比、倾斜、手写体、复杂表格必须提示识别结果需校对；Tesseract 不能保证版面或表格结构完整恢复。

### 5.4 独立证据渠道

1. Tesseract 官方数据仓库 tag、资产与许可证：
   <https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0>
   <https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/LICENSE>
2. Tesseract 官方文档对 fast/best、OEM 与兼容性的说明：
   <https://tesseract-ocr.github.io/tessdoc/Data-Files.html>
   <https://github.com/tesseract-ocr/tessdoc/blob/main/Data-Files.md>

## 6. FFmpeg 8.1.2：只允许 LGPL 配置

### 6.1 可冻结项

| 字段 | 锁定值 |
|---|---|
| 版本 | FFmpeg 8.1.2 |
| 官方 tag | `n8.1.2` |
| tag 对象 | `1c2c67c0b9f7f66ab32c19dcf7f227bcd290aa4c` |
| 解引用 commit | `38b88335f99e76ed89ff3c93f877fdefce736c13` |
| 官方源码资产 | `ffmpeg-8.1.2.tar.xz` 及 `.asc` 签名 |
| 建议路径 | `Engines/FFmpeg/ffmpeg.exe`、`ffprobe.exe`；`Licenses/FFmpeg/*`；`Sources/FFmpeg/ffmpeg-8.1.2.tar.xz` 或同下载位置 |

当前基线不预填 `ffmpeg.exe`/`ffprobe.exe` SHA-256：官方发布的是源码，不是 TinyPix 所需的 Windows x64 LGPL 二进制；二进制哈希取决于编译器、配置和依赖。它是正式 Windows x64 LGPL 构建产物的强制生成门禁：构建完成后计算并写入 `build-manifest.json`，哈希缺失或复核不一致均禁止发布，不能抄用第三方构建的哈希。

### 6.2 许可前提与构建门禁

FFmpeg 默认主体为 LGPL-2.1-or-later；启用可选 GPL 部分后整个 FFmpeg 变为 GPL；启用 nonfree 后产物不可再分发。TinyPix 的强制门禁：

1. 配置中不得出现 `--enable-gpl`、`--enable-nonfree`、`--enable-version3`。
2. 明确传入 `--disable-gpl --disable-nonfree --disable-version3`，并保存完整 configure 行和构建日志。
3. 禁止 GPL 外部库：`libx264`、`libx265`、`libxvid`、`libvidstab`、`rubberband`、`frei0r`、`libdavs2` 等。
4. 禁止需要 version3 的组合：如 GMP、libaribb24、liblensfun，以及会迫使升级许可证版本的 Apache-2.0 组合（官方 LICENSE.md 列出的 VMAF、mbedTLS、RK MPI、OpenCORE、VisualOn）。
5. 禁止 nonfree 组合，例如 `libfdk-aac`；OpenSSL 也必须按 FFmpeg 8.1.2 的实际配置规则审计，便携离线版没有网络需求，建议完全不启用 TLS 协议栈。
6. 构建后自动执行并归档：`ffmpeg -version`、`ffmpeg -buildconf`、`ffmpeg -L`、`ffmpeg -encoders`、`ffmpeg -filters`。
7. CI 看到 `enable-gpl`、`enable-nonfree`、`enable-version3`、`libx264`、`libx265`、`libfdk_aac` 等任何禁用标记即失败。

### 6.3 文字水印与字幕的冻结实现

TinyPix 不使用 `drawtext` 建立第二套字体管线。静态文字水印先在本地生成一个覆盖完整时长的
UTF-8 ASS cue，SRT/VTT/ASS 字幕统一交给 FFmpeg `subtitles` 滤镜和 libass 渲染；图片水印继续
使用 FFmpeg 内置 `overlay`。导入 ASS 时保留时间轴和 libass 可支持的基础样式，外部字体名统一
回退到内置 Noto Sans CJK SC，界面明确提示不保证复杂脚本、动画和外部字体的像素级还原。

| 资产 | 锁定版本与来源 | SHA-256 / 提交 | 许可证与发行路径 |
|---|---|---|---|
| libass | 0.17.5，tag `0.17.5`，commit `4a05d8127f525943ebf45fdc6497c9e665947f0d`，官方 `libass-0.17.5.tar.xz` 460,148 bytes | `2dca25c0e0c837ddf00b52011b3f82cac1e4ddd3ad018227806b0c2288864acc` | ISC；`Sources/Subtitle/libass-0.17.5.tar.xz`、`Licenses/libass/COPYING` |
| FreeType | 2.14.3，tag `VER-2-14-3`，commit `0a0221a1347e2f1e07c395263540026e9a0aa7c7`，官方 `freetype-2.14.3.tar.xz` | `36bc4f1cc413335368ee656c42afca65c5a3987e8768cc28cf11ba775e785a5f` | 选择 FreeType License；`Sources/Subtitle/freetype-2.14.3.tar.xz`、`Licenses/FreeType/FTL.TXT` |
| HarfBuzz | 14.2.1，tag `14.2.1`，commit `56feae4035bdd48f62ba2b8d8c16232d4d89b3a4`，官方 `harfbuzz-14.2.1.tar.xz` 19,559,952 bytes | `a54a5d8e9380a41fbb762ce367bcbf7704792dfca0d93f1bbca86c5a57902e0e` | MIT-style；`Sources/Subtitle/harfbuzz-14.2.1.tar.xz`、`Licenses/HarfBuzz/COPYING` |
| FriBidi | 1.0.16，tag `v1.0.16`，commit `68162babff4f39c4e2dc164a5e825af93bda9983`，官方 `fribidi-1.0.16.tar.xz` 1,098,260 bytes | `1b1cde5b235d40479e91be2f0e88a309e3214c8ab470ec8a2744d82a5a9ea05c` | LGPL-2.1-or-later；`Sources/Subtitle/fribidi-1.0.16.tar.xz`、`Licenses/FriBidi/COPYING` |
| Noto Sans CJK SC Regular | 2.004，tag/commit `Sans2.004` / `523d033d6cb47f4a80c58a35753646f5c3608a78`，`NotoSansCJKsc-Regular.otf` 16,437,364 bytes | `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b` | SIL OFL-1.1；`Templates/Fonts/NotoSansCJKsc-Regular.otf`、`Licenses/NotoSansCJK/OFL.txt` |

构建必须启用 `--enable-libass --enable-libfreetype --enable-libharfbuzz --enable-libfribidi`，但仍保留
`--disable-gpl --disable-nonfree --disable-version3`。Windows 版 libass 使用 DirectWrite 字体 provider，
任务显式传入 `Templates/Fonts` 作为 `fontsdir`，不扫描或依赖用户字体目录。启动自检必须同时确认
`subtitles` 滤镜、四项原生依赖、内置字体大小与哈希；任一缺失都把水印/字幕工具标为“引擎损坏”
并阻止入队，不允许静默禁用 4.0 已承诺功能。最终静态/动态原生库的版本、许可证和哈希进入 SBOM；
`ffmpeg.exe` 的最终哈希继续按 6.1 的正式构建门禁生成。

### 6.4 功能影响

- 不能把常见 GPL Windows “full build”直接放入 TinyPix。
- H.264/H.265 编码不能依赖 libx264/libx265。应优先验证 Windows Media Foundation 编码器（如可用的 FFmpeg MF 路径）或另行审核 LGPL/BSD 兼容编码器；编码器缺失时必须在 UI 中如实显示，不得静默切换到 GPL 构建。
- FFmpeg 8.1.2 的部分滤镜本身属于 GPL（官方 `LICENSE.md` 明列，例如 `vf_eq`、`vf_delogo`、`vf_cropdetect` 等）。功能矩阵必须以最终 `ffmpeg -filters` 的 LGPL 构建结果为准；需要这些能力时改由 Magick.NET/OpenCV/自有算法实现，不能为了补功能打开 GPL。

### 6.5 再分发义务

- TinyPix 通过子进程调用独立 `ffmpeg.exe`/`ffprobe.exe`，不把 FFmpeg 库静态链接进 TinyPix 主程序；仍须履行 FFmpeg 二进制的 LGPL 再分发义务。
- 随发行包或同一可长期访问位置提供完全匹配的 FFmpeg 8.1.2 源码、LGPLv2.1 文本、构建配置、补丁/变更说明和第三方依赖许可。
- About/许可证页及下载说明标明使用 FFmpeg 和源码取得位置；不得移除版权信息，不得把 FFmpeg 宣称为 TinyPix 自有代码。
- 如果以后改为直接链接 FFmpeg DLL，按官方清单采用动态链接并重新做 LGPL 合规审查。

### 6.6 独立证据渠道

1. FFmpeg 官方法律页及合规清单：<https://ffmpeg.org/legal.html>
2. FFmpeg 8.1.2 官方源码许可证：
   <https://github.com/FFmpeg/FFmpeg/blob/38b88335f99e76ed89ff3c93f877fdefce736c13/LICENSE.md>
   <https://git.ffmpeg.org/gitweb/ffmpeg.git/tag/n8.1.2>
3. FFmpeg 官方源码发布目录：<https://ffmpeg.org/releases/>
4. 字幕与字体依赖的一手发布/许可证：
   <https://github.com/libass/libass/releases/tag/0.17.5>
   <https://freetype.org/index.html>
   <https://github.com/harfbuzz/harfbuzz/releases/tag/14.2.1>
   <https://github.com/fribidi/fribidi/releases/tag/v1.0.16>
   <https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004>

## 7. 建议写入构建系统的离线清单

正式构建清单至少记录：

```text
PDFtoImage 5.2.1
  pdftoimage.5.2.1.nupkg
  sha256 0db8fd1ec91d711842ef61d7140c8900fc0555d9a7850d932256b703adb7d099

PDFium 147.0.7690 / chromium 7690 / win-x64 / no-v8 / no-xfa
  pdfium-win-x64.tgz
  sha256 06ef95ac4f9b8897731224639ddf0f185693cb48bc9ee650f1e92f71e0d2a94e
  pdfium.dll
  sha256 15df9dddd81eddc5a177946aa5e34cda821ebc46a51440ecb607f91e99644895

YuNet 2023mar
  face_detection_yunet_2023mar.onnx
  sha256 8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4

MODNet photographic portrait matting
  modnet_photographic_portrait_matting.onnx
  sha256 07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9

tessdata_fast 4.1.0
  eng.traineddata
  sha256 7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2
  chi_sim.traineddata
  sha256 a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730

FFmpeg 8.1.2 source
  tag n8.1.2
  commit 38b88335f99e76ed89ff3c93f877fdefce736c13
  binary sha256: BUILD-TIME REQUIRED（由正式 Windows x64 LGPL 构建产物生成并复核；缺失即禁止发布）

Subtitle rendering stack
  libass 0.17.5 source sha256 2dca25c0e0c837ddf00b52011b3f82cac1e4ddd3ad018227806b0c2288864acc
  FreeType 2.14.3 source sha256 36bc4f1cc413335368ee656c42afca65c5a3987e8768cc28cf11ba775e785a5f
  HarfBuzz 14.2.1 source sha256 a54a5d8e9380a41fbb762ce367bcbf7704792dfca0d93f1bbca86c5a57902e0e
  FriBidi 1.0.16 source sha256 1b1cde5b235d40479e91be2f0e88a309e3214c8ab470ec8a2744d82a5a9ea05c
  NotoSansCJKsc-Regular.otf 2.004 sha256 2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b
```

## 8. 发布前必须再验的事项

1. 锁定 Tesseract 5 的具体 Windows x64 原生构建、Leptonica 版本及其全部许可证；本基线当前只冻结 tessdata 资产。
2. 锁定 ONNX Runtime CPU、OpenCvSharp native runtime、SkiaSharp native assets 的具体 NuGet 版本和 RID 产物哈希。
3. 对最终 FFmpeg 二进制运行许可门禁，并以真实 `-buildconf/-L/-encoders/-filters` 更新功能矩阵。
4. 用 SBOM 和发布目录扫描确认不存在 `x264/x265/fdk/nonfree` DLL 或字符串证据。
5. 在 Windows 10 22H2 与 Windows 11 x64 普通用户 VM 中做峰值内存实测；若实测高于本基线预算，降低输入上限或进一步串行化，不放宽保护。
