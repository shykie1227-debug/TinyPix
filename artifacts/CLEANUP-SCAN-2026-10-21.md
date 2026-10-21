# TinyPix 项目冗余文件清理扫描报告

- 扫描时间：2026-10-21 15:2x（GT+8）
- 扫描根：`/Users/huashu/TinyPix`
- 磁盘状态：`/System/Volumes/Data` 已用 900 GiB / 932 GiB，**剩余 7.2 GiB（100%）**
- 项目占用：`3.5pro` 4.2 GB + `.workbuddy` 607 MB
- 扫描方式：只读（`find` / `du` / `sha256` / `git ls-files` / `git check-ignore`），未修改任何文件

---

## 一、判定标准（每类文件的识别依据）

| 类别 | 识别标准 | 为什么可以删 |
|---|---|---|
| A. 构建缓存 | 目录名 `bin`/`obj`，且被 `.gitignore` 第 66 行 `**/bin/`、`**/obj/` 忽略 | `dotnet build` / `dotnet test` 会完整重建；不含任何源码或产物定义 |
| B. 陈旧发布中间目录 | 位于 `artifacts/` 下、不被任何脚本/文档/CI 引用、内容 ⊆ 已交付的 zip | 交付形态已由 `TinyPix.exe` + `Portable.zip` 两个产物代表 |
| C. 历史版本二进制 | 文件名相同、`sha256` 与当前产物不同、时间戳早于当前版本 | 已被新版本取代；源码在 git 中完整保留，可重新构建 |
| D. 一次性诊断抓包 | 由 Process Monitor 等工具生成的 `.pml` / `.csv`，有专用 `.gitignore` 规则，无文档引用 | 单次门禁取证，结论已固化进文档；原始抓包无二次分析价值 |
| E. 运行时缓存 | `.pytest_cache` / `.hypothesis` / `.DS_Store` | 工具自动重建，内容为零信息 |
| F. 保留项 | 当前交付产物 / 当前验证证据 / 高重建成本的唯一副本 / 编译工具链 | —— |

---

## 二、exe 去重结果（哈希级）

全仓 `.exe` 共 7 个（不含 `.workbuddy/tools/dotnet` SDK 里的 40 个工具自带 exe）：

| 文件 | 字节 | SHA-256 前 16 | 结论 |
|---|---:|---|---|
| `artifacts/TinyPix.exe` | 234,252,545 | `3b5a3bde4fc79c17` | **保留**：当前单文件交付物 |
| `artifacts/baseline/TinyPix.exe` | 168,382,782 | `0eada0a58d87dfae` | 旧版（10-15），见 C 类 |
| `artifacts/portable/Engines/ffmpeg.exe` | 135,784,960 | `98112c7bd8b1…` | 随目录清理 |
| `artifacts/portable/Engines/ffprobe.exe` | 135,574,016 | `23dd2ba1f68c…` | 随目录清理 |
| `artifacts/portable/TinyPix.exe` | 284,672 | `84505a00fe927cf7` | **功能重复**：与当前 zip 内成员同一哈希 |
| `artifacts/portable/RestartAgent.exe` | 77,624 | `aaec35e5217f…` | 随目录清理 |
| `artifacts/portable/createdump.exe` | 71,512 | `b6b5611f41d6…` | 随目录清理 |

**结论：不存在字节级完全重复的 exe。** 只有一例功能重复：
`artifacts/portable/TinyPix.exe` 的 SHA-256 与 `TinyPix-4.0-Windows-x64-Portable.zip` 内 `TinyPix.exe` 成员**完全一致**（`84505a00fe927cf7…`），即同一份 apphost 的散落副本。

注意：`artifacts/TinyPix.exe`（234 MB 单文件自解压）与 zip 内 284 KB apphost **不是重复**，是两种交付形态（单文件 vs 自包含多文件目录），两个都要保留。

---

## 三、待清理清单

### A. 构建缓存 —— 1,487,928,942 B（1,419.0 MiB）

| 路径 | 体积 |
|---|---:|
| `3.5pro/tests/TinyPix.Media.Tests/bin/Debug` | 906 MiB |
| `3.5pro/tests/TinyPix.Media.Tests/bin/Release` | 209 MiB |
| `3.5pro/tools/TinyPix.Smoke/bin/Debug` | 212 MiB |
| `3.5pro/tests/TinyPix.Infrastructure.Tests/bin` | 77 MiB |
| `3.5pro/tests/TinyPix.Core.Tests/bin` | 11 MiB |
| 各项目 `obj/` 与 `src/*/bin/`（14 处） | ~6 MiB |

判定依据：`.gitignore:66 **/bin/`、`**/obj/` → `git check-ignore` 已确认全部被忽略。
附加证据：`tests/TinyPix.Media.Tests/bin/Debug/net10.0/runtimes/ios/native/onnxruntime.xcframework.zip`（41 MiB）是 NuGet 包解压残留，macOS/Linux 侧永不加载。
风险：无。删除后 `dotnet build` 会重新还原（NuGet 包在 `~/.nuget` 缓存中，不需联网重下）。

### B. `3.5pro/artifacts/portable` —— 446,500,794 B（425.8 MiB）

- 477 个文件，其中 `Engines/ffmpeg.exe` 135.8 MB + `ffprobe.exe` 135.6 MB 占九成
- **是全仓唯一被误提交入库的产物目录**：211 个文件（8.3 MiB）进了 git 索引，来源是 2026-10-21 那次 `git add -A` —— 这些扩展名（`.winmd` / `.pri` / `.mui` / `.png`）不在 `.gitignore` 的 `*.dll` / `*.exe` 规则覆盖范围内
- `artifacts/portable/Config/settings.json` 与 `assets/Portable/Config/settings.json` **哈希相同**（`c92d07a83166…`）→ 纯重复副本
- 全仓 `grep "artifacts/portable"`（排除 `.git` 与 `artifacts/` 自身）**零命中**：无脚本、无文档、无 CI 引用
- 与当前 zip 的关系：目录 477 文件全部能在 zip 的 519 条目里找到，且 `TinyPix.exe` 哈希一致；但 zip 还多出 42 项（`Magick.NET` / `OnnxRuntime` / `Models/`），说明这是**更早一次 publish 的残留**，不是当前 zip 的暂存目录

判定依据：B 类（陈旧发布中间目录）。
风险：低。`build/publish-portable.ps1` 每次都会重新生成 `artifacts/publish-win-x64` 与 `artifacts/TinyPix-4.0`，不读这个目录。
**附带必须做的事**：`git rm -r --cached artifacts/portable` + 在 `.gitignore` 增加 `artifacts/portable/`，否则删了工作树文件后 git 会显示 211 个删除。

### C. `3.5pro/artifacts/baseline` —— 349,369,605 B（333.2 MiB）

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| `TinyPix.exe` | 168,382,782 | `0eada0a58d87dfae…` |
| `TinyPix-4.0-Windows-x64-Portable.zip` | 180,986,172 | `d38c109d08c9d9be…` |
| `baseline-report.json` + 两个 `.sha256` | 651 B | 结论摘要 |

判定依据：C 类。`baseline-report.json` 记录 `builtAt: 2026-10-15T20:26`、`windowsBuild: 10.0.19045`，对应的是**旧 UI**版本，已被 2026-10-21 版（`3b5a3bde…` / `c5fa3302…`）取代。目录本身被 `.gitignore:83` 忽略。
风险：**中**。这是唯一保留的「上一代可回退二进制」。源码在 git 中完整（`main` 历史全在），可重建，但 FFmpeg 用 BtbN 滚动 `latest` 标签，**重建无法得到字节一致的产物**。
建议：默认执行删除；若需保留回退点，改为移出仓库另行存档（例如外置盘）。

### D. 一次性诊断抓包 —— 1,533,652,400 B（1,462.6 MiB）

| 路径 | 字节 | 时间 |
|---|---:|---|
| `artifacts/windows/winui-feasibility-gate/tinypix-registry-capture.pml` | 1,068,746,234 | 2026-07-22 |
| `artifacts/windows/winui-feasibility-gate/tinypix-registry-capture.csv` | 464,906,166 | 2026-07-22 |

判定依据：D 类。`.gitignore:71-72` 有两条专用规则；`docs/` 内零引用；`winui-feasibility-gate` 目录里的 PNG 与 `diagnostics/` 已保留结论证据。
**保留**：同目录 `tinypix-registry-write-candidates.csv`（3,001 B）—— 这是从抓包提炼出的结论摘要，体积可忽略、信息密度高。

### E. 运行时缓存 —— 28,819 B（28 KB）

`.pytest_cache/`（24 KB）、`.hypothesis/`（24 KB 内 `.gitignore` 未覆盖的 `charmap.json.gz`）、2 个 `.DS_Store`（已被 `.gitignore:13` 忽略）。

### F. 旧 UI 截图 —— 702,985 B（0.7 MiB）

`artifacts/ui-shots/`（24 个文件，2026-10-20 第一轮截图，全部是**被否定的旧 UI**）。当前证据在 `artifacts/ui-shots2/`（新 UI 构建产物）与 `artifacts/ui-shots3/`（发布产物本体）。
风险：低但需注意 —— 这 24 个文件**已被 git 跟踪**，删除会产生 git 删除记录（历史仍可找回）。

---

## 四、保留清单（不动）

| 路径 | 体积 | 理由 |
|---|---:|---|
| `artifacts/TinyPix.exe` | 223.4 MiB | 当前单文件交付物 |
| `artifacts/TinyPix-4.0-Windows-x64-Portable.zip` | 218.6 MiB | 当前便携交付物 |
| `.cache/ffmpeg/ffmpeg-master-latest-win64-lgpl.zip` | 107.9 MiB | **唯一本地 FFmpeg 引擎源**；删了下次出包要重下，VM 直连 GitHub 极慢 |
| `artifacts/ui-shots2` / `ui-shots3` | 2.6 MiB | 当前 UI 与发布产物验证证据 |
| `.workbuddy/tools/dotnet` | 607 MB | .NET SDK 10.0.302，项目编译必需 |
| `artifacts/windows/winui-feasibility-gate/*.png`、`diagnostics/` | ~15 MiB | 门禁结论证据 |
| `artifacts/baseline/baseline-report.json` + `.sha256` | 651 B | 上一代版本指纹，可随 C 类一起走或单独留 |

---

## 五、汇总

| 类别 | 可释放 |
|---|---:|
| A 构建缓存 | 1,419.0 MiB |
| B `artifacts/portable` | 425.8 MiB |
| C `artifacts/baseline` | 333.2 MiB |
| D 诊断抓包 | 1,462.6 MiB |
| E 运行时缓存 | 0.03 MiB |
| F 旧 UI 截图 | 0.7 MiB |
| **合计** | **3,818,183,545 B ≈ 3.56 GiB** |

执行后项目从约 4.8 GB 降到约 1.3 GB，磁盘剩余从 7.2 GiB 回到约 10.7 GiB。

## 六、可恢复记录方案

删除前统一生成 `artifacts/cleanup/cleanup-manifest-<时间戳>.txt`，逐文件记录：

```
<字节>  <sha256>  <相对路径>
```

- A/B/E 类：有清单即可，文件可由 `dotnet build` / `publish-portable.ps1` / `pytest` 重建
- C 类：清单中的 sha256 与 `baseline-report.json` 互为交叉校验；重建产物字节不同，需靠此清单确认「曾经是什么」
- D 类：清单记录抓包指纹；结论摘要 `winui-registry-write-candidates.csv` 单独保留
- F 类：git 中仍可 `git checkout <commit> -- artifacts/ui-shots` 找回
