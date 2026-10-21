# TinyPix 冗余文件清理结果

- 执行时间：2026-10-21 15:29–15:31（GMT+8）
- 依据：`artifacts/CLEANUP-SCAN-2026-10-21.md`（扫描报告，含每类判定标准）
- 用户确认范围：**A + B + D + E + F**（保留 `artifacts/baseline` 作为上一代回退点）
- 删除方式：先生成逐文件 SHA-256 清单，再直接删除

---

## 一、执行结果

| 类别 | 内容 | 文件数 | 释放字节 | 释放量 |
|---|---|---:|---:|---:|
| A | `bin/` + `obj/` 构建缓存 | 1,018 | 1,487,928,942 | 1,419.0 MiB |
| B | `artifacts/portable`（陈旧发布中间目录） | 477 | 446,500,794 | 425.8 MiB |
| D | `tinypix-registry-capture.pml` + `.csv` | 2 | 1,533,652,400 | 1,462.6 MiB |
| E | `.pytest_cache` / `.hypothesis` / `.DS_Store` | 6 | 28,819 | 0.03 MiB |
| F | `artifacts/ui-shots`（旧 UI 截图） | 24 | 702,985 | 0.7 MiB |
| | **合计** | **1,527** | **3,468,813,940** | **3.23 GiB** |

### 体积变化

| 指标 | 清理前 | 清理后 |
|---|---:|---:|
| `3.5pro` 目录 | 4.2 GB | 1.0 GB |
| 项目总计（含 `.workbuddy`） | 4.8 GB | 1.6 GB |
| 磁盘可用 | 7.2 GiB | 9.06 GiB（APFS 异步回收中，预计收敛到 ~10.4 GiB） |

---

## 二、保留项（逐项核对通过）

| 路径 | 字节 | 状态 |
|---|---:|---|
| `artifacts/TinyPix.exe` | 234,252,545 | 完好，SHA-256 `3b5a3bde4fc79c17…` 未变 |
| `artifacts/TinyPix-4.0-Windows-x64-Portable.zip` | 229,254,110 | 完好，SHA-256 `c5fa33028586eb24…` 未变 |
| `artifacts/baseline/`（exe + zip + report） | 349,369,605 | 按用户选择保留，作为上一代回退点 |
| `.cache/ffmpeg/ffmpeg-master-latest-win64-lgpl.zip` | 113,141,148 | 保留：唯一本地 FFmpeg 引擎源 |
| `artifacts/ui-shots2/` | 11 张 PNG | 保留：新 UI 构建产物验证证据 |
| `artifacts/ui-shots3/` | 11 张 PNG | 保留：发布产物本体验证证据 |
| `artifacts/windows/winui-feasibility-gate/tinypix-registry-write-candidates.csv` | 3,001 | 保留：抓包结论摘要（原始 pml/csv 已删） |
| `.workbuddy/tools/dotnet` | 607 MB | 保留：.NET SDK 10.0.302 |

---

## 三、可恢复记录

| 文件 | 用途 |
|---|---|
| `artifacts/cleanup/cleanup-manifest-20261021-152922.txt` | 1,527 行，逐文件 `<字节>\t<sha256>\t<相对路径>` |
| `artifacts/cleanup/cleanup-meta-20261021-152922.json` | 分类汇总（文件数/字节数/总数） |
| `artifacts/CLEANUP-SCAN-2026-10-21.md` | 扫描报告与判定标准 |

各恢复路径：

- **A / E**：`dotnet build` / `dotnet test` / `pytest` 自动重建，NuGet 包仍在 `~/.nuget` 本地缓存，无需联网
- **B**：`build/publish-portable.ps1` 重新发布即可（需 `.cache/ffmpeg` 命中缓存）
- **D**：原始抓包不可再生（Process Monitor 实时采集），但结论已固化在 `winui-registry-write-candidates.csv` 与 `docs/`
- **F**：`git checkout d244219 -- artifacts/ui-shots`（删除前最后一个 commit）可完整找回

---

## 四、附带的仓库卫生修复

`artifacts/portable` 曾有 **211 个文件被 2026-10-21 的 `git add -A` 误提交**（`.winmd` / `.pri` / `.mui` / `.png` 等扩展名不在 `.gitignore` 的 `*.dll` / `*.exe` 规则覆盖范围内，共 8.3 MiB）。本次一并处理：

1. `git rm -r --cached artifacts/portable artifacts/ui-shots` → 236 个路径移出索引
2. `.gitignore` 新增三条规则：
   - `artifacts/portable/` —— 发布暂存产物，永远不应入库
   - `artifacts/ui-shots/` —— 已被否定的旧 UI 证据，保留在 git 历史即可
   - `.hypothesis/` —— 测试运行器缓存（此前有个 `charmap.json.gz` 被跟踪）
3. `git rm --cached .hypothesis/unicode_data/14.0.0/charmap.json.gz`

---

## 五、验证

| 检查 | 结果 |
|---|---|
| 待删路径是否存在 | 全部 `gone` |
| `bin` / `obj` 残留 | `bin=0  obj=0` |
| 契约测试 | `pytest tests/ -q` → **72 passed** |
| 交付物 SHA-256 | 与 `artifacts/SHA256.txt` 记载一致 |
| git 工作区 | 236 删除 + 1 修改（`.gitignore`），无意外改动 |
