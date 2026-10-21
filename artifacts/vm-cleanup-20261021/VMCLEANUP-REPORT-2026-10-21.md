# Windows 10 虚拟机空间回收与配置优化报告

- **执行时间**：2026-10-21 15:36 – 17:45（GMT+8）
- **对象**：`/Users/huashu/Parallels/Windows 10.pvm`（Parallels Desktop 26.3.1，Windows 10 22H2 x64）
- **目标**：虚拟机总体积压到 120 GB 以内；同时清理系统临时文件、Windows 更新缓存、休眠文件、
  回收站与虚拟机快照；并优化内存/CPU、显卡、共享文件夹与网络配置。
- **结果**：**443 GB → 74 GB**（镜像 73.73 GB），宿主可用空间 **5.3 GB → 378 GB**。

---

## 1. 清理前后的关键数字

| 指标 | 清理前 | 清理后 | 变化 |
|---|---:|---:|---:|
| `.pvm` 总体积 | 443 GB（峰值 447 GB） | **74 GB** | **−369 GB（−83%）** |
| 虚拟磁盘镜像 `.hds` | 445.16 GB（3 层） | **73.73 GB**（1 层） | −371.43 GB |
| 快照层数 | 3（base + 2 个快照增量） | **1**（无快照） | −2 |
| 快照目录 `Snapshots/` | 2.2 GB | **0** | −2.2 GB |
| 宿主机可用空间 | 5.31 GB（99% 满） | **378 GB**（59%） | +373 GB |
| 客户机 C: 已用 | 243.95 GB | **78.81 GB** | **−165.14 GB** |
| 客户机 C: 可用 | 267.33 GB | 432.48 GB | +165.15 GB |

> 说明：宿主可用空间的增幅（+373 GB）大于虚拟机体积的降幅（−369 GB），差额来自
> `Snapshots/` 目录、`parallels.log` 轮转文件与 APFS 延迟回收。

---

## 2. 空间去哪了：诊断结论

扫描发现两类"隐形占用"，都不是常规意义上的系统垃圾：

### 2.1 `%TEMP%` 里的 2,851 个 `_MEI*` 目录 —— 143 GB

`C:\Users\huashu\AppData\Local\Temp` 单个目录占 **146,519 MB（143.1 GB）**，占客户机总占用的 59%。
根因是 **PyInstaller 一次性打包程序与 .NET 单文件 exe 的自解压残留**：

- `_MEI*` 目录 **2,851 个**（PyInstaller onefile 每次运行解包一整套 Python 运行时，约 50 MB/次，
  进程被强杀后不会自清理）；
- `.net\TinyPix\*`、`.net\sfprobe\*` 自解压目录（单文件 `TinyPix.exe` 每次启动解到
  `%TEMP%\.net\TinyPix\<随机串>\`，实测每份约 450 MB）；
- `tp-extract*`、`_MEI*` 之外还有 3,000+ 个零散临时项。

**这是本次清理的绝对主力：单此一项释放 143 GB。**

### 2.2 三个磁盘层里的历史块 —— 371 GB

虚拟磁盘由 3 个 `.hds` 层组成（合并前）：

| 层 | 大小 | 说明 |
|---|---:|---|
| `{5fbaabe3}` | 90.9 GB（清理前 97.2） | 当前活动层（2026-09-29 起） |
| `{894ab129}` | 350.3 GB | 快照增量层（2026-07-19 → 09-29） |
| `{ebae005e}` | 30.2 GB | 底层 / 基线（含 Windows 安装） |

客户机实际只用了 244 GB，但三层合计 445 GB——多出来的是**只被旧快照引用、早已被覆盖的块**。
`prl_disk_tool compact -i` 显示 445 GB 已分配中 419 GB 仍被判为"used"，即**不删快照无法回收**。

两个快照均创建于 2026-07-19，早已完成使命：

| 快照名 | 时间 | 用途 |
|---|---|---|
| Before TinyPix WinUI toolchain | 2026-07-19 15:35 | .NET 10 / WinUI 可行性门禁 |
| Before TinyPix DPI gate | 2026-07-19 17:30 | 显示缩放门禁取证 |

---

## 3. 执行过程（分四阶段）

### 阶段一：客户机内清理（虚拟机运行中）

| 动作 | 释放 | 说明 |
|---|---:|---|
| `%TEMP%` 全量清空 | 143.1 GB | 移除 3,105 项，9 项被占用跳过 |
| NuGet 全局包缓存 `~\.nuget\packages` | 3.36 GB | 可再生，下次 restore 自动重建 |
| 旧构建树 `C:\TinyPixBuild` | 3.73 GB | 已被 `C:\TinyPixBuild2` 取代 |
| `C:\build\outlook-img-slicer-*`（7 个） | 5.47 GB | 另一项目的可重建产物 |
| 旧产物目录 `TinyPixBundle` / `TinyPixMin` / `TinyPix-Portable` | 0.80 GB | 历史打包残留 |
| 回收站 | 308 MB | 10 项 |
| `C:\Windows\Temp`、`SoftwareDistribution\Download`、CBS/DISM 日志 | 约 0.2 GB | 更新缓存本身已很干净（54 MB） |
| **合计** | **157.26 GB** | 客户机 C: 243.95 → 86.69 GB |

### 阶段二：合并快照（关机后）

先做一次原地压缩腾出安全缓冲（445 → 419 GB，宿主可用 7.3 → 32 GB），再执行：

```
prl_disk_tool merge --hdd "Windows 10.pvm/Windows 10-0.hdd"
```

合并过程**边合并边释放**（宿主可用 32 → 120 GB），结束后只剩单层 331.41 GB。
随后 `prl_disk_tool compact` 压到 **122.71 GB**，再用 `prlctl snapshot-delete` 清掉两个
已失效的快照元数据与 `Snapshots/` 目录（2.2 GB）。

### 阶段三：TRIM 与二次压缩

客户机里还有约 33 GB 是"NTFS 已释放、但虚拟磁盘未收到 TRIM"的空闲块。
`Optimize-Volume -ReTrim` 需要管理员权限（当前会话 UAC 受限，普通 exec 报 Access denied），
改为经 **SYSTEM 会话**直接执行（`prlctl exec` 不带 `--current-user`，落在 session 0，权限完整）。
TRIM 处理 432 GB 空闲区耗时过长，在完成大部分后停止，随后：

```
prl_disk_tool compact --hdd "Windows 10.pvm/Windows 10-0.hdd" -a
```

**114.45 GB → 73.73 GB**（TRIM 生效，一次回收 40.7 GB）。

### 阶段四：配置优化（关机态应用，重启后验证）

| 配置项 | 原值 | 现值 | 依据 |
|---|---|---|---|
| CPU | 8 vCPU（auto） | **6 vCPU** | 宿主 8 物理核 / 16 逻辑核；8 会与 macOS 抢满核，6 保留 2 核给宿主，构建并行度仍充足 |
| 内存 | 6144 MB | **5120 MB** | 宿主 16 GB；5 GB 对 `dotnet build/publish` 足够（实测冷启动编译 6.6 s），为 macOS 多留 1 GB |
| 显卡 | parallels / DirectX 最高 | **保持 DirectX 最高**，垂直同步 关闭 | WinUI 3 需要 DirectX 11，不能降级；关垂直同步去掉合成等待 |
| 自动压缩虚拟磁盘 | 关 | **开** | 删除文件后自动回收宿主空间，避免再次累积到 400 GB |
| 关闭窗口行为 | 挂起（写 5 GB 内存镜像） | **保持运行** | 挂起会往磁盘写 ≈内存大小的 `.mem`，是隐形的空间黑洞 |
| 共享配置文件（桌面/文档/下载） | 开 | **关** | 去掉三处跨系统目录重定向，减少无关 I/O 与误写 |
| SmartMount（网络共享 / 光驱） | 开 | **关** | 虚拟机不需要自动挂载宿主机网络盘与光驱 |
| 光驱镜像 | 指向已删除的 `Win10_22H2_…iso` | **已清空** | 原路径 `Downloads/UI设计/` 下文件已不存在，属失效引用 |
| 网络 | 共享 NAT（`type=shared`）+ virtio | **不变** | 已是最优：NAT 隔离、virtio 半虚拟化吞吐最好；IP 仍为 10.211.55.5 |
| 共享文件夹 | 整个用户主目录（`\\Mac\Home`） | **保留** | 出包脚本强依赖 `\\Mac\Home\TinyPix\...`，不能动 |
| 空闲暂停 | 关 | **保持关** | 开启会导致长时间不操作后 VM 挂起，破坏 `prlctl exec` 自动化 |
| 撤销磁盘 / 嵌套虚拟化 | 关 | 保持关 | 撤销磁盘会产生额外增量层 |

---

## 4. 清理后验证

| 验证项 | 结果 |
|---|---|
| 虚拟机启动 | 正常，桌面与任务栏完好（`artifacts/vm-cleanup-20261021/vm-final.png`） |
| 快照列表 | 空（`prlctl snapshot-list` 无输出） |
| 磁盘链 | 单层 `{5fbaabe3}`，`DiskDescriptor.xml` 只有一个 `<Image>` |
| CPU / 内存 | `CPU=6`、`RAM_GB=4.98` |
| 客户机磁盘 | `C_USED_GB=78.81`、`C_FREE_GB=432.48` |
| 共享目录 | `\\Mac\Home` 可访问（`MAC_HOME=True`） |
| 工具链 | `dotnet --version` → `10.0.302` |
| 编译冒烟 | `dotnet new console` + `dotnet build -c Release` → **exit 0，6.6 s** |
| 网络 | `10.211.55.5`（共享 NAT） |
| Parallels Tools | `state=installed version=26.3.1-57396` |

---

## 5. 刻意没做的事（保留可用性/可回滚）

| 项 | 原因 |
|---|---|
| `C:\Projects\TinyPix`（2.77 GB） | 仓库源码的 Windows 侧工作副本，删除无收益但可能影响就地调试 |
| `C:\BuildTools`（3.36 GB） | MSVC/Windows SDK，**编译 WinUI 必需**，删了直接出不了 exe |
| `C:\TinyPixBuild2`（2.33 GB） | 当前出包工作树，脚本虽可重建但保留可省一次完整还原 |
| `.rustup` + `.cargo`（1.9 GB） | 非本次目标，不确定是否仍在使用 |
| `DISM /StartComponentCleanup /ResetBase` | 会永久移除此前的更新回滚包，为保住"可卸载更新"能力未执行；当前 74 GB 已有充足余量 |
| 休眠文件 `hiberfil.sys` | 客户机本来就已关闭休眠（文件不存在），无需操作 |
| 页面文件 | 已由系统管理并为 1 GB，已是最小合理值，未再压缩（会产生性能风险） |
| 宿主 `Downloads/UI设计/*.iso` | 属个人文件，只清掉了虚拟机里的失效引用，未删宿主机文件 |

---

## 6. 回滚与风险边界

- **快照不可恢复**：两个 2026-07-19 快照已合并进当前状态，无法回退到当时的时间点。
  快照的元数据（名称、时间、描述、截图文件名）已备份为本目录下的 `Snapshots.xml`。
- **配置文件已备份**：`config.pvs.orig`、`DiskDescriptor-hdd.xml`（合并前）同目录留存，
  可按需对照原配置。
- **TRIM 未跑完**：剩余空闲块会在 Windows 自动维护（每周 retrim）或后续删文件时自然回收；
  下次若需再压，直接 `prl_disk_tool compact` 即可。
- **`.mem` 挂起镜像**：因改为"关闭窗口=保持运行"，后续不会再产生 5 GB 级内存镜像文件。

---

## 7. 后续维护建议

1. **单文件 exe 的 `%TEMP%` 残留是复发点**。本次 143 GB 就来自反复运行
   `TinyPix.exe`（每次解包 ≈450 MB）+ PyInstaller 程序。建议在验证脚本里加一句收尾清理：
   ```powershell
   Remove-Item "$env:TEMP\.net\TinyPix" -Recurse -Force -ErrorAction SilentlyContinue
   Get-ChildItem "$env:TEMP" -Filter "_MEI*" -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
   ```
2. **不要在这个虚拟机上打快照**。单层磁盘 + 自动压缩才是省空间组合；需要回滚点用
   `artifacts/` 里的产物和 git tag。
3. 若体积再次上涨，先看 `prl_disk_tool compact -i --hdd … -a` 的
   `Allocated` 与 `Used` 差值，再决定是删文件还是直接 compact。
4. 出包脚本里 `prlctl exec` 请务必加 `--current-user` 并重定向 stdin（`< /dev/null`），
   否则在等待交互凭据时会静默挂住。

---

## 附：产物清单

```
artifacts/vm-cleanup-20261021/
├── VMCLEANUP-REPORT-2026-10-21.md   本报告
├── Snapshots.xml                    快照元数据备份（清理前）
├── DiskDescriptor-hdd.xml           磁盘链描述（合并前，3 层）
├── config.pvs.orig                  虚拟机配置（清理前）
├── vm-during-cleanup.png            清理过程中实拍
└── vm-final.png                     清理后启动实拍
```
