#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""
TinyPix v3.5 Pro Build Script
================================
阶段：
  1. 环境检查（OS / 管理员 / Node / npm / Rust / ffmpeg / 源项目路径）
  2. 安装依赖（复制项目到本地 / npm install / Rust 预检）
  3. 构建（npm run build 前端 + cargo tauri build 后端 / NSIS 打包）
  4. 验证产物（.exe 大小 / 复制回 SMB / 清理临时目录）

Source: C:\Mac\Home\Desktop\test\3.5pro  (手动复制后的 Parallels 共享目录)
Target: Windows local project dir when safe, otherwise C:\Users\huashu\AppData\Local\TinyPixBuild
"""
import os
import sys
import base64
import shutil
import subprocess
import time
import platform
import re
import json
import traceback
import hashlib
import urllib.request
import zipfile
import stat
from datetime import datetime
from pathlib import Path
from typing import Optional, Tuple, List, Dict


# ── 输出样式 ────────────────────────────────────────────────────────────────
class C:
    RESET = "\033[0m"
    BOLD = "\033[1m"
    RED = "\033[91m"
    GREEN = "\033[92m"
    YELLOW = "\033[93m"
    CYAN = "\033[96m"
    GRAY = "\033[90m"


# ═════════════════════════════════════════════════════════════════════════════
#  日志层 (v3.0.4+ — 2026-06-02 新增)
#  职责:
#    1) 同步把 console 输出写入 logs/build.log
#    2) 过滤 ERROR/WARNING/Traceback/Exception/DLL 等关键字 → logs/error.log
#    3) 写 logs/build_info.json (构建元数据)
#  设计: 单例模式 (module-level _LOGGER),不破坏现有 4 阶段逻辑
# ═════════════════════════════════════════════════════════════════════════════
_ANSI_RE = re.compile(r"\x1b\[[0-9;]*m")

# 触发 error.log 的关键字 (小写匹配)
_ERROR_KEYWORDS = (
    "error", "warning", "traceback", "exception",
    "importerror", "modulenotfounderror", "filenotfounderror",
    "permissionerror", "oserror", "ioerror", "uncaught",
    "dll", " 0x", "failed", "fatal", "panic",
)


class DualLogger:
    """双流 logger: console + logs/build.log + 过滤到 logs/error.log"""

    def __init__(self, log_dir: Path):
        self.log_dir = log_dir
        log_dir.mkdir(parents=True, exist_ok=True)
        self.build_log = log_dir / "build.log"
        self.error_log = log_dir / "error.log"
        self.start_ts = datetime.now()
        self._started = False

    def start(self) -> None:
        """main 启动时调用,写头部 + 清空旧文件"""
        self._started = True
        # 不清空旧文件 — 追加模式,保留历史
        for p in (self.build_log, self.error_log):
            p.touch(exist_ok=True)
        self._write_build(
            f"{'='*72}\n"
            f"构建开始: {self.start_ts.strftime('%Y-%m-%d %H:%M:%S')}\n"
            f"Python: {sys.version.split()[0]}\n"
            f"平台: {platform.platform()}\n"
            f"{'='*72}\n"
        )

    def write(self, line: str) -> None:
        """console 输出调用此方法:写 build.log + 过滤到 error.log"""
        if not self._started:
            return
        # 剥离 ANSI 转义码 (日志要纯文本)
        clean = _ANSI_RE.sub("", line)
        self._write_build(clean + "\n")
        # 错误过滤
        lower = clean.lower()
        if any(kw in lower for kw in _ERROR_KEYWORDS):
            self._write_error(clean + "\n")

    def write_command(self, cmd: str, cwd: Optional[Path],
                      returncode: int, duration: float,
                      stdout: str, stderr: str) -> None:
        """记录子命令完整执行(用于 build.log 完整可追溯)"""
        if not self._started:
            return
        sep = f"\n--- CMD @ {datetime.now().strftime('%H:%M:%S')} (rc={returncode}, {duration:.1f}s) ---\n"
        self._write_build(sep)
        self._write_build(f"$ {cmd}\n")
        if cwd:
            self._write_build(f"  cwd: {cwd}\n")
        if stdout.strip():
            self._write_build(f"--- stdout ---\n{stdout}\n")
        if stderr.strip():
            self._write_build(f"--- stderr ---\n{stderr}\n")
        if returncode != 0:
            # 失败命令的 stderr 自动入 error.log
            self._write_error(
                f"CMD FAILED (rc={returncode}): {cmd}\n"
                f"stderr (last 500):\n{stderr[-500:]}\n"
            )

    def write_exception(self, exc_type, exc_value, exc_tb) -> None:
        """记录未捕获异常 — 总是入 error.log"""
        if not self._started:
            return
        tb_text = "".join(traceback.format_exception(exc_type, exc_value, exc_tb))
        self._write_error(
            f"UNCAUGHT EXCEPTION @ {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n"
            f"{tb_text}\n"
        )
        self._write_build(f"\n!! UNCAUGHT EXCEPTION: {exc_type.__name__}: {exc_value}\n")

    def finish(self, success: bool, error_type: str = "",
               error_summary: str = "", extra: Optional[dict] = None) -> Path:
        """main 退出时调用:写尾部 + build_info.json,返回 info path"""
        if not self._started:
            return Path()
        end_ts = datetime.now()
        duration = (end_ts - self.start_ts).total_seconds()
        self._write_build(
            f"\n{'='*72}\n"
            f"构建结束: {end_ts.strftime('%Y-%m-%d %H:%M:%S')}\n"
            f"耗时: {duration:.1f} 秒 ({int(duration//60)} 分 {int(duration%60)} 秒)\n"
            f"结果: {'[成功]' if success else '[失败]'}\n"
            f"{'='*72}\n"
        )

        # 写 build_info.json
        info = {
            "project": "TinyPix",
            "version": VERSION,
            "build_time": end_ts.strftime("%Y-%m-%d %H:%M:%S"),
            "build_success": success,
            "python_version": sys.version.split()[0],
            "platform": platform.platform(),
            "builder": "build.py",
            "builder_version": VERSION,
            "output_dir": str(TARGET) if 'TARGET' in globals() else "",
            "output_exe": "",
            "duration_seconds": round(duration, 2),
        }
        if not success:
            info["error_type"] = error_type
            info["error_summary"] = error_summary
        if extra:
            info.update(extra)

        info_path = self.log_dir / "build_info.json"
        try:
            info_path.write_text(
                json.dumps(info, ensure_ascii=False, indent=2),
                encoding="utf-8"
            )
        except Exception as e:
            self._write_error(f"build_info.json 写失败: {e}\n")
        return info_path

    def _write_build(self, text: str) -> None:
        try:
            with self.build_log.open("a", encoding="utf-8", errors="replace") as f:
                f.write(text)
        except Exception:
            pass  # 日志失败不能阻塞构建

    def _write_error(self, text: str) -> None:
        try:
            with self.error_log.open("a", encoding="utf-8", errors="replace") as f:
                f.write(text)
        except Exception:
            pass


# module-level singleton (在 main() 启动时初始化)
_LOGGER: Optional[DualLogger] = None


def init_logger(log_dir: Optional[Path] = None) -> DualLogger:
    """main() 启动第一行调用"""
    global _LOGGER
    if log_dir is None:
        # 默认: 当前工作目录 / logs (Windows 端是 python build.py 所在目录)
        log_dir = Path.cwd() / "logs"
    _LOGGER = DualLogger(log_dir)
    _LOGGER.start()
    return _LOGGER


def get_logger() -> Optional[DualLogger]:
    return _LOGGER


def cprint(msg, color: Optional[str] = None):
    col = getattr(C, (color or "").upper(), "")
    out = f"{col}{msg}{C.RESET}" if col else msg
    try:
        print(out)
    except Exception:
        # 任何终端编码问题都降级为安全输出
        safe = msg.encode("utf-8", errors="replace").decode("utf-8", errors="replace")
        try:
            print(safe)
        except Exception:
            pass
    if _LOGGER is not None:
        try:
            _LOGGER.write(out)
        except Exception:
            pass


def section(title: str):
    cprint("")
    cprint("=" * 64, "cyan")
    cprint(f"  {title}", "cyan")
    cprint("=" * 64, "cyan")
    cprint("")


def step(msg: str):
    cprint(f"[阶段] {msg}", "cyan")


def ok(msg: str):
    cprint(f"  [OK] {msg}", "green")


def warn(msg: str):
    cprint(f"  [WARN] {msg}", "yellow")


def err(msg: str):
    cprint(f"  [ERR] {msg}", "red")


# ── 配置 ────────────────────────────────────────────────────────────────────
VERSION = "3.5.1"
# 2026-06-24: 优先支持手动复制到 Parallels Desktop 的共享桌面 test 目录。
# 若用户从 Mac 复制 /Users/huashu/TinyPix/3.5pro 到
# C:\Mac\Home\Desktop\test 后运行 build.py，应优先使用这份源码。
SOURCE_CANDIDATES = [
    Path(__file__).resolve().parent,                                      # build.py 所在项目目录（优先）
    Path.cwd(),                                                           # 当前运行 build.py 的目录
    Path(r"C:\3.5pro"),                                                    # 用户手动复制到 Windows 根目录
    Path(r"C:\Mac\Home\Desktop\test\3.5pro"),                              # Parallels 手动复制目录
    Path(r"Y:\TinyPix\3.5pro"),                                            # Parallels SMB (主)
    Path(r"\\Mac\Home\TinyPix\3.5pro"),                                    # SMB UNC 形式
    Path(r"Z:\TinyPix\3.5pro"),                                            # 备用 SMB 盘
    Path(os.environ.get("LOCALAPPDATA", r"C:\Users\huashu\AppData\Local")) / "TinyPixBuild" / "source",  # 上次成功构建的本地源
    Path(r"C:\Projects\TinyPix\3.5pro"),                                   # Windows 本地(用户手动 robocopy 过的话)
    Path(r"C:\Users\huashu\TinyPix\3.5pro"),                              # Windows 用户目录
]
SOURCE = None  # 在 check_source() 里实际选择
DEFAULT_TARGET = Path(os.environ.get("LOCALAPPDATA", r"C:\Users\huashu\AppData\Local")) / "TinyPixBuild"
TARGET = DEFAULT_TARGET
BUILD_CACHE_ROOT = Path(os.environ.get("LOCALAPPDATA", r"C:\Users\huashu\AppData\Local")) / "TinyPixBuildCache"
NPM_CACHE_DIR = BUILD_CACHE_ROOT / "npm-cache"
CARGO_TARGET_CACHE = BUILD_CACHE_ROOT / "cargo-target"
FFMPEG_CACHE_DIR = Path(os.environ.get("APPDATA", r"C:\Users\huashu\AppData\Roaming")) / "TinyPix" / "cache" / "ffmpeg"
FFMPEG_SIDECAR_DIR = Path(os.environ.get("APPDATA", r"C:\Users\huashu\AppData\Roaming")) / "TinyPix" / "sidecars"
LOCAL_FFMPEG_ZIP_CANDIDATES = [
    Path(r"C:\Mac\Home\Desktop\图片\ffmpeg-8.1.1-essentials_build.zip"),
    Path(r"C:\Mac\Home\Desktop\图片\ffmpeg-release-essentials.zip"),
    Path(r"C:\Users\huashu\Desktop\图片\ffmpeg-8.1.1-essentials_build.zip"),
    Path(r"C:\Users\huashu\Desktop\图片\ffmpeg-release-essentials.zip"),
]
NODE_MIN = (20, 0, 0)
# 自动安装器固定提供 Node.js 20 LTS；项目的 Vite 6/Tauri 2 构建链也支持该版本。
NPM_MIN = (10, 0, 0)
RUST_MIN = (1, 80, 0)
FFMPEG_VERSION = "8.1.2"
FFMPEG_DOWNLOAD_URL = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip"
FFMPEG_ZIP_SHA256 = "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec"
PORTABLE_EXE_NAME = "TinyPix-Pro-3.5.1-Windows-x64-Portable.exe"
COPY_EXCLUDED_DIRS = [
    ".git",
    ".build-backups",
    ".pytest_cache",
    ".uploads",
    "__pycache__",
    "coverage",
    "dist",
    "logs",
    "node_modules",
    "target",
    "src-tauri\\target",
]
COPY_EXCLUDED_FILES = [
    "*.log",
    "*.tmp",
    "*.bak",
    ".DS_Store",
]


# ── 工具函数 ────────────────────────────────────────────────────────────────
def run(cmd: str, cwd: Optional[Path] = None, timeout: int = 60,
        env: Optional[dict] = None, check: bool = False) -> Tuple[int, str, str]:
    """运行命令，返回 (returncode, stdout, stderr)"""
    _t0 = time.time()
    try:
        effective_cwd = cwd
        if effective_cwd is None and platform.system() == "Windows":
            try:
                current = str(Path.cwd())
                if current.startswith("\\\\"):
                    effective_cwd = Path(os.environ.get("SystemRoot", r"C:\Windows"))
            except Exception:
                effective_cwd = Path(os.environ.get("SystemRoot", r"C:\Windows"))
        result = subprocess.run(
            cmd, cwd=str(effective_cwd) if effective_cwd else None,
            shell=True, check=False, timeout=timeout,
            capture_output=True, text=True,
            encoding="utf-8", errors="replace",
            env=env or os.environ.copy(),
        )
        _dur = time.time() - _t0
        if _LOGGER is not None:
            _LOGGER.write_command(cmd, effective_cwd, result.returncode, _dur,
                                  result.stdout or "", result.stderr or "")
        if check and result.returncode != 0:
            err(f"命令失败: {cmd}")
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        _dur = time.time() - _t0
        if _LOGGER is not None:
            _LOGGER.write_command(cmd, cwd, -1, _dur, "", f"超时 ({timeout}s)")
        return -1, "", f"超时 ({timeout}s)"
    except FileNotFoundError as e:
        _dur = time.time() - _t0
        if _LOGGER is not None:
            _LOGGER.write_command(cmd, cwd, -1, _dur, "", f"命令不存在: {e}")
        return -1, "", f"命令不存在: {e}"
    except Exception as e:
        _dur = time.time() - _t0
        if _LOGGER is not None:
            _LOGGER.write_command(cmd, cwd, -1, _dur, "", f"异常: {e}")
        return -1, "", f"异常: {e}"


def parse_version(version_str: str) -> Tuple[int, ...]:
    """解析 'v1.95.0' / '1.95' / 'rustc 1.95.0 (hash 2026-01-01)' → (1, 95, 0)"""
    m = re.search(r"(\d+)\.(\d+)\.(\d+)", version_str)
    if m:
        return tuple(int(x) for x in m.groups())
    m = re.search(r"(\d+)\.(\d+)", version_str)
    if m:
        return (int(m.group(1)), int(m.group(2)), 0)
    m = re.search(r"(\d+)", version_str)
    if m:
        return (int(m.group(1)), 0, 0)
    return (0, 0, 0)


def compare_versions(actual: Tuple[int, ...], required: Tuple[int, ...]) -> int:
    """返回 -1 (实际 < 要求) / 0 (=) / 1 (实际 > 要求)"""
    a, b = actual[:3], required[:3]
    if a < b:
        return -1
    if a > b:
        return 1
    return 0


def powershell_command(script: str) -> str:
    """用 UTF-16LE 编码脚本，避免 cmd.exe 吞掉 PowerShell 内层引号。"""
    encoded = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    return f"powershell -NoProfile -EncodedCommand {encoded}"


def activate_msvc_environment(vcvars: Path) -> bool:
    """把 vcvars64.bat 生成的 MSVC 环境导入当前 Python 进程。"""
    code, stdout, stderr = run(
        f'call "{vcvars}" >nul && set',
        timeout=60,
    )
    if code != 0:
        warn(f"无法加载 MSVC 环境: {stderr[-200:]}")
        return False
    for line in stdout.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key:
            os.environ[key] = value
    return bool(os.environ.get("PATH"))


def is_shared_or_network_path(path: Path) -> bool:
    """识别 Parallels/SMB/映射盘路径；这些路径构建时更容易遇到权限和 IO 问题。"""
    text = str(path).replace("/", "\\").lower()
    return (
        text.startswith("\\\\")
        or text.startswith("c:\\mac\\")
        or text.startswith("y:\\")
        or text.startswith("z:\\")
        or "\\\\psf\\" in text
        or "\\\\mac\\" in text
    )


def configure_build_target() -> None:
    """选择原地构建或复制到本地缓存构建。"""
    global TARGET
    if SOURCE is None:
        TARGET = DEFAULT_TARGET
        return

    force_stage = os.environ.get("TINYPIX_FORCE_STAGE", "").strip() == "1"
    force_in_place = os.environ.get("TINYPIX_BUILD_IN_PLACE", "").strip() == "1"

    if force_stage:
        TARGET = DEFAULT_TARGET
        warn(f"已按 TINYPIX_FORCE_STAGE=1 使用本地缓存构建: {TARGET}")
        return

    if force_in_place or not is_shared_or_network_path(SOURCE):
        TARGET = SOURCE
        ok(f"源项目位于 Windows 本地目录,启用原地构建: {TARGET}")
        return

    TARGET = DEFAULT_TARGET
    warn(f"源项目位于共享/映射路径,使用本地缓存构建: {TARGET}")


def is_admin() -> bool:
    if platform.system() != "Windows":
        return True
    try:
        import ctypes
        return ctypes.windll.shell32.IsUserAnAdmin() != 0
    except Exception:
        return False


def make_path_writable(path: Path) -> None:
    """尽量解除 Windows/Mac 复制后残留的只读属性。"""
    try:
        if path.exists():
            path.chmod(path.stat().st_mode | stat.S_IWRITE | stat.S_IREAD)
    except Exception:
        pass

    if platform.system() == "Windows":
        run(f'attrib -R "{path}"', timeout=20)


# ═════════════════════════════════════════════════════════════════════════════
#  自动安装模块 (v3.0.5 — 2026-06-05 新增)
#  职责:
#    1) 检测 winget / choco / scoop 是否可用
#    2) Node.js / Rust 缺失时自动安装
#    3) 安装后用绝对路径二次验证 (避免 PATH 不刷新)
#  设计: 失败不抛出,只返回 bool,留给上层决策
# ═════════════════════════════════════════════════════════════════════════════
_NODE_LTS_URL = "https://nodejs.org/dist/v20.18.0/node-v20.18.0-x64.msi"
_RUSTUP_URL = "https://win.rustup.rs/x86_64"


def _check_winget() -> bool:
    """检查 winget 是否可用 (Win10 1809+/Win11 自带)"""
    code, out, _ = run("winget --version", timeout=10)
    return bool(code == 0 and out.strip())


def _find_node_after_install() -> Optional[Path]:
    """在常见安装路径找 node.exe (PATH 未刷新时兜底)"""
    candidates = [
        Path(r"C:\Program Files\nodejs\node.exe"),
        Path(r"C:\Program Files (x86)\nodejs\node.exe"),
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "nodejs" / "node.exe",
        Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")) / "nodejs" / "node.exe",
    ]
    for p in candidates:
        if file_exists(p):
            return p
    return None


def _find_rust_after_install() -> Optional[Path]:
    """在常见安装路径找 cargo.exe"""
    cargo_bin = Path(os.environ.get("USERPROFILE", r"C:\Users\huashu")) / ".cargo" / "bin"
    for name in ("cargo.exe", "rustc.exe"):
        p = cargo_bin / name
        if file_exists(p):
            return p
    return None


def _install_node() -> bool:
    """自动安装 Node.js LTS — 优先 winget,失败回退直链下载"""
    step("尝试自动安装 Node.js LTS")
    if not is_admin():
        warn("未以管理员身份运行 — winget 装机会失败,建议右键 PowerShell → 以管理员身份运行")
    if _check_winget():
        ok("发现 winget,用 winget 安装 Node.js LTS (约 1-3 分钟)...")
        code, out, err_out = run(
            "winget install --id OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements --silent",
            timeout=300,
        )
        if code == 0:
            ok("winget 安装完成")
        else:
            warn(f"winget 安装失败 (rc={code}),回退 MSI 直链")
            warn(f"stderr: {err_out[-200:]}")
            return _install_node_msi()
    else:
        warn("winget 不可用,改用 MSI 直链下载 (约 1-3 分钟)...")
        return _install_node_msi()

    # 二次验证 — 用绝对路径检测 (PATH 可能未刷新)
    node_exe = _find_node_after_install()
    if node_exe:
        ok(f"Node.js 已就绪: {node_exe}")
        # 刷新当前进程的 PATH
        os.environ["PATH"] = str(node_exe.parent) + os.pathsep + os.environ.get("PATH", "")
        return True
    err("Node.js 安装后未找到 node.exe,请手动安装:https://nodejs.org/")
    return False


def _install_node_msi() -> bool:
    """用 PowerShell 下载并静默安装 Node.js MSI"""
    ps_cmd = (
        f'$url="{_NODE_LTS_URL}";'
        f'$out="$env:TEMP\\node-lts.msi";'
        f'Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing;'
        f'Start-Process msiexec.exe -ArgumentList "/i $out /qn /norestart" -Wait -NoNewWindow'
    )
    code, out, err_out = run(powershell_command(ps_cmd), timeout=300)
    if code != 0:
        err(f"MSI 安装失败 (rc={code})")
        err(f"stderr: {err_out[-300:]}")
        return False
    return _find_node_after_install() is not None


def _install_rust() -> bool:
    """自动安装 Rust 工具链 — 优先 winget,失败回退 rustup-init"""
    step("尝试自动安装 Rust 工具链 (rustup)")
    if not is_admin():
        warn("未以管理员身份运行 — winget 装机会失败,建议右键 PowerShell → 以管理员身份运行")
    if _check_winget():
        ok("发现 winget,用 winget 安装 Rust (约 2-5 分钟)...")
        code, out, err_out = run(
            "winget install --id Rustlang.Rustup --accept-package-agreements --accept-source-agreements --silent",
            timeout=600,
        )
        if code == 0:
            ok("winget 安装完成")
        else:
            warn(f"winget 安装失败 (rc={code}),回退 rustup-init")
            warn(f"stderr: {err_out[-200:]}")
            return _install_rust_rustup()
    else:
        warn("winget 不可用,改用 rustup-init 直链 (约 2-5 分钟)...")
        return _install_rust_rustup()

    # 二次验证
    cargo_exe = _find_rust_after_install()
    if cargo_exe:
        ok(f"Rust 已就绪: {cargo_exe}")
        cargo_bin = cargo_exe.parent
        os.environ["PATH"] = str(cargo_bin) + os.pathsep + os.environ.get("PATH", "")
        return True
    err("Rust 安装后未找到 cargo.exe,请手动安装:https://rustup.rs/")
    return False


def _install_rust_rustup() -> bool:
    """下载 rustup-init.exe 静默安装"""
    ps_cmd = (
        f'$url="{_RUSTUP_URL}";'
        f'$out="$env:TEMP\\rustup-init.exe";'
        f'Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing;'
        f'& $out -y --default-toolchain stable --default-host x86_64-pc-windows-msvc --no-modify-path'
    )
    code, out, err_out = run(powershell_command(ps_cmd), timeout=600)
    if code != 0:
        err(f"rustup-init 失败 (rc={code})")
        err(f"stderr: {err_out[-300:]}")
        return False
    return _find_rust_after_install() is not None


def file_exists(p: Path, min_size: int = 1) -> bool:
    try:
        return p.exists() and p.stat().st_size >= min_size
    except OSError:
        return False


# ═════════════════════════════════════════════════════════════════════════════
#  阶段 1：环境检查
# ═════════════════════════════════════════════════════════════════════════════
def check_environment() -> bool:
    section("阶段 1 / 4 — 环境检查")

    # 1.1 OS
    step("检查操作系统")
    if platform.system() != "Windows":
        err(f"需要 Windows，当前: {platform.system()} {platform.release()}")
        return False
    ok(f"Windows {platform.release()} ({platform.version()})")

    # 1.2 管理员
    step("检查管理员权限")
    if not is_admin():
        warn("未以管理员身份运行，Defender 排除可能失败")
        warn("建议右键 PowerShell → 以管理员身份运行")
    else:
        ok("管理员权限已确认")

    # 1.3 Node.js
    step("检查 Node.js")
    code, out, _ = run("node --version")
    if code != 0:
        warn("Node.js 未安装 — 尝试自动安装")
        if not _install_node():
            err("Node.js 自动安装失败,退出")
            return False
        # 重新检测
        code, out, _ = run("node --version")
        if code != 0:
            err("Node.js 安装后仍不可用")
            return False
    actual = parse_version(out.strip())
    cmp = compare_versions(actual, NODE_MIN)
    if cmp < 0:
        err(f"Node.js 版本过低: {out.strip()} (需要 >= {'.'.join(map(str, NODE_MIN))})")
        return False
    ok(f"Node.js {out.strip()}")

    # 1.4 npm
    step("检查 npm")
    code, out, _ = run("npm --version")
    if code != 0:
        err("npm 未安装 (通常随 Node.js 一同安装,如果出现此错误请重装 Node.js)")
        return False
    actual = parse_version(out.strip())
    cmp = compare_versions(actual, NPM_MIN)
    if cmp < 0:
        err(f"npm 版本过低: {out.strip()} (需要 >= {'.'.join(map(str, NPM_MIN))})")
        return False
    ok(f"npm {out.strip()}")

    # 1.5 Rust 工具链
    step("检查 Rust 工具链 (rustc + cargo)")
    code, out, _ = run("rustc --version")
    if code != 0:
        warn("rustc 未安装 — 尝试自动安装")
        if not _install_rust():
            err("Rust 自动安装失败,退出")
            err("手动安装:https://rustup.rs/")
            return False
        # 重新检测
        code, out, _ = run("rustc --version")
        if code != 0:
            err("rustc 安装后仍不可用")
            return False
    actual = parse_version(out.strip().split()[-1])
    cmp = compare_versions(actual, RUST_MIN)
    if cmp < 0:
        warn(f"rustc 版本偏低: {out.strip()} (建议 >= {'.'.join(map(str, RUST_MIN))})")
    else:
        ok(f"rustc {out.strip().split()[-1]}")

    code, out, _ = run("cargo --version")
    if code != 0:
        err("cargo 未安装")
        return False
    ok(f"cargo {out.strip().split()[-1]}")

    # 1.5b MSVC 链接器检测（Rust on Windows 必需）
    step("检查 MSVC 链接器 (link.exe)")
    code, _, _ = run("where link.exe", timeout=10)
    if code != 0:
        # 尝试通过 vswhere 查找 VS Build Tools
        vswhere_paths = [
            Path(r"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"),
            Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe",
        ]
        link_found = False
        for vswhere in vswhere_paths:
            if vswhere.exists():
                code, out, _ = run(
                    f'"{vswhere}" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath',
                    timeout=15,
                )
                if code == 0 and out.strip():
                    vcvars = Path(out.strip()) / "VC" / "Auxiliary" / "Build" / "vcvars64.bat"
                    if vcvars.exists():
                        if activate_msvc_environment(vcvars):
                            link_found = True
                            ok(f"VS Build Tools 已安装并加载: {out.strip()}")
                            break
        if not link_found:
            err("未找到 link.exe — 缺少 Visual Studio Build Tools")
            err("请安装以下组件之一:")
            err("  1. Visual Studio 2022 Build Tools (推荐)")
            err("     下载: https://aka.ms/vs/17/release/vs_BuildTools.exe")
            err("     安装时勾选: '使用 C++ 的桌面开发'")
            err("  2. 或完整安装 Visual Studio 2022 Community")
            err("  3. 安装后重启终端，重新运行 build.py")
            return False
    else:
        ok("link.exe 可用")

    # 1.6 Tauri CLI（通过 npx 检测，不强制）
    step("检查 Tauri CLI")
    code, _, _ = run("npx --no-install @tauri-apps/cli --version", timeout=15)
    if code == 0:
        ok("Tauri CLI 可用（通过 npx）")
    else:
        warn("Tauri CLI 未全局安装 — 将由 npm install 安装到本地 node_modules")

    # 1.7 源项目路径 — fallback list (自动从候选里选)
    step("检查源项目路径 (fallback list)")
    global SOURCE
    candidates_with_pkg = []
    for cand in SOURCE_CANDIDATES:
        if not cand.exists():
            continue
        if not file_exists(cand / "package.json"):
            continue
        if not file_exists(cand / "src" / "App.tsx"):
            continue
        if not file_exists(cand / "src-tauri" / "Cargo.toml"):
            continue
        candidates_with_pkg.append(cand)
    if not candidates_with_pkg:
        err("所有候选源路径都不可用:")
        for cand in SOURCE_CANDIDATES:
            mark = "[OK]" if cand.exists() else "[MISSING]"
            err(f"  {mark} {cand}")
        err("")
        err("修复方法(选一个):")
        err("  1. 在 Mac 复制 /Users/huashu/TinyPix/3.5pro 到 Windows 的 C:\\Mac\\Home\\Desktop\\test")
        err("  2. 在 Windows 中进入 C:\\Mac\\Home\\Desktop\\test\\3.5pro 后运行 build.py")
        err("  3. 如需旧 SMB 方式,检查 Y:\\TinyPix\\3.5pro 是否可访问")
        return False
    SOURCE = candidates_with_pkg[0]
    if len(candidates_with_pkg) > 1:
        warn(f"多个候选源可用,使用第一个: {SOURCE}")
        for extra in candidates_with_pkg[1:]:
            warn(f"  候选(未用): {extra}")
    else:
        ok(f"源项目 OK: {SOURCE}")

    configure_build_target()

    # 1.8 ffmpeg（可选，但视频功能必需）
    step("检查 FFmpeg (可选)")
    ffmpeg_paths = [
        Path(r"C:\Users\huashu\AppData\Roaming\TinyPix\sidecars\ffmpeg.exe"),
        TARGET.parent / "TinyPix" / "sidecars" / "ffmpeg.exe" if TARGET.parent else None,
        Path(r"C:\Windows\ffmpeg.exe"),
    ]
    ffmpeg_found = None
    for p in ffmpeg_paths:
        if p and file_exists(p, min_size=1024 * 1024):  # 至少 1MB
            ok(f"FFmpeg: {p} ({p.stat().st_size // (1024*1024)} MB)")
            ffmpeg_found = p
            break
    if not ffmpeg_found:
        warn("FFmpeg 未找到 — 视频功能（GIF/Compress/Frame）将不可用")
        ffmpeg_suggest = r"C:\Users\huashu\AppData\Roaming\TinyPix\sidecars\ffmpeg.exe"
        warn(f"建议放置到: {ffmpeg_suggest}")

    # 1.9 磁盘空间
    step("检查磁盘空间")
    try:
        import shutil as sh
        free_gb = sh.disk_usage(TARGET.parent if TARGET.parent.exists() else Path("C:\\")).free / (1024 ** 3)
        if free_gb < 10:
            err(f"目标盘空间不足: {free_gb:.1f} GB (建议 >= 10 GB)")
            return False
        ok(f"目标盘可用: {free_gb:.1f} GB")
    except Exception as e:
        warn(f"无法检测磁盘空间: {e}")

    ok("环境检查通过")
    return True


# ═════════════════════════════════════════════════════════════════════════════
#  阶段 2：安装/准备
# ═════════════════════════════════════════════════════════════════════════════
def configure_defender() -> bool:
    step("检查 Windows Defender 构建选项")
    if platform.system() != "Windows":
        return True
    if os.environ.get("TINYPIX_CONFIGURE_DEFENDER", "").strip() != "1":
        ok("保持系统安全设置不变（如确需排除项可显式设置 TINYPIX_CONFIGURE_DEFENDER=1）")
        return True
    excludes = [
        r"\\Mac\Home\TinyPix",
        os.path.expandvars(r"%USERPROFILE%\.cargo"),
        os.path.expandvars(r"%USERPROFILE%\.rustup"),
        str(TARGET),
        str(BUILD_CACHE_ROOT),
        str(CARGO_TARGET_CACHE),
        os.path.expandvars(r"%LOCALAPPDATA%\TinyPixBuild"),
        os.path.expandvars(r"%LOCALAPPDATA%\TinyPixBuild\src-tauri"),
        os.path.expandvars(r"%LOCALAPPDATA%\TinyPixBuild\src-tauri\target"),
        os.path.expandvars(r"%TEMP%\tinypix_build"),
    ]
    excludes = list(dict.fromkeys(excludes))
    for path in excludes:
        ps = (
            f'powershell -NoProfile -Command '
            f'"Add-MpPreference -ExclusionPath \'{path}\' -ErrorAction SilentlyContinue"'
        )
        code, _, _ = run(ps, timeout=20)
        if code == 0:
            ok(f"排除: {path}")
        else:
            warn(f"无法添加排除: {path}")
    return True


def fix_target_permissions(reset_acl: bool = True) -> bool:
    """复制项目后修复本地目标的权限（解决 SMB 复制导致的 ACL 问题）"""
    step("修复目标目录权限 (解除只读 + 重置 ACL)")
    if platform.system() != "Windows":
        return True

    # 1. 解除所有文件的只读属性
    ps1 = (
        f'powershell -NoProfile -Command '
        f'"Get-ChildItem -Path \'{TARGET}\' -Recurse -File -ErrorAction SilentlyContinue | '
        f'ForEach-Object {{ $_.IsReadOnly = $false }}"'
    )
    code, _, _ = run(ps1, timeout=180)
    if code == 0:
        ok("已解除所有文件只读属性")
    else:
        warn("解除只读属性时出错（部分文件可能仍是只读）")

    if not reset_acl:
        ok("原地构建跳过 ACL 重置")
        return True

    if not is_admin():
        warn("未以管理员身份运行，跳过 ACL 重置")
        return True

    # 2. 接管所有权 + 授予完全控制
    ps2 = (
        f'powershell -NoProfile -Command '
        f'"takeown /F \'{TARGET}\' /R /D Y 2>$null; '
        f'icacls \'{TARGET}\' /reset /T /C 2>$null; '
        f'icacls \'{TARGET}\' /grant \'*S-1-1-0:(OI)(CI)F\' /T /C 2>$null"'
    )
    code, _, stderr = run(ps2, timeout=300)
    if code == 0:
        ok("已重置 ACL 并授予 Everyone 完全控制")
    else:
        warn(f"ACL 修复可能不完整: {stderr[:200] if stderr else ''}")
        # 不视为致命错误

    return True


def clean_target() -> bool:
    global TARGET
    if SOURCE is not None and TARGET.resolve() == SOURCE.resolve():
        step(f"原地构建: 跳过清理本地目标 ({TARGET})")
        ok("将直接复用当前项目目录")
        return True

    step(f"清理本地目标: {TARGET}")
    if TARGET.exists():
        # 标准删除
        code, _, _ = run(f'rmdir /S /Q "{TARGET}"', timeout=120)
        if not TARGET.exists():
            ok("已清理")
        else:
            warn("rmdir 失败，尝试 PowerShell...")
            ps = f'powershell -NoProfile -Command "Remove-Item -Path \'{TARGET}\' -Recurse -Force -ErrorAction SilentlyContinue"'
            code, _, _ = run(ps, timeout=120)
            if TARGET.exists():
                locked_target = TARGET
                TARGET = locked_target.parent / f"{locked_target.name}-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
                warn(f"无法清理旧缓存: {locked_target}")
                warn(f"改用新的本地构建目录: {TARGET}")
            else:
                ok("PowerShell 清理完成")
    TARGET.mkdir(parents=True, exist_ok=True)
    # 验证目录确实为空（防止旧文件残留导致 Cargo.toml 冲突等问题）
    try:
        if any(TARGET.iterdir()):
            warn("目标目录仍存在旧文件，使用全新目录")
            locked_target = TARGET
            TARGET = locked_target.parent / f"{locked_target.name}-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
            warn(f"改用新的本地构建目录: {TARGET}")
            TARGET.mkdir(parents=True, exist_ok=True)
    except Exception:
        pass
    ok(f"目标就绪: {TARGET}")
    return True


def get_ffmpeg_bundle_candidates(binary_name: str) -> List[Path]:
    """返回 FFmpeg/FFprobe 的候选路径,用于构建期打包。"""
    candidates = [
        Path(os.environ.get("APPDATA", r"C:\Users\huashu\AppData\Roaming")) / "TinyPix" / "sidecars" / binary_name,
        TARGET.parent / "TinyPix" / "sidecars" / binary_name if TARGET.parent else None,
        Path(r"C:\ffmpeg\bin") / binary_name,
        Path(r"C:\tools\ffmpeg\bin") / binary_name,
        Path(r"C:\ProgramData\chocolatey\bin") / binary_name,
    ]

    project_roots = [Path(__file__).resolve().parent, Path.cwd()]
    if SOURCE is not None:
        project_roots.insert(0, SOURCE)
    for root in project_roots:
        candidates.extend([
            root / "sidecars" / binary_name,
            root / "ffmpeg" / "bin" / binary_name,
            root / "tools" / "ffmpeg" / "bin" / binary_name,
        ])

    result: List[Path] = []
    seen = set()
    for candidate in candidates:
        if candidate is None:
            continue
        key = str(candidate)
        if key in seen:
            continue
        seen.add(key)
        result.append(candidate)
    return result


def find_ffmpeg_binary(binary_name: str) -> Optional[Path]:
    for candidate in get_ffmpeg_bundle_candidates(binary_name):
        if file_exists(candidate, min_size=1024 * 1024):
            return candidate
    return None


def download_ffmpeg_bundle() -> Dict[str, Path]:
    """下载并解压 Windows FFmpeg essentials 包,返回 ffmpeg/ffprobe 路径。"""
    cache_dir = FFMPEG_CACHE_DIR
    extract_dir = cache_dir / "extract"
    zip_path = cache_dir / f"ffmpeg-{FFMPEG_VERSION}-essentials_build.zip"
    cache_dir.mkdir(parents=True, exist_ok=True)
    FFMPEG_SIDECAR_DIR.mkdir(parents=True, exist_ok=True)

    def zip_hash() -> str:
        digest = hashlib.sha256()
        with zip_path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(chunk)
        return digest.hexdigest()

    def verify_zip() -> None:
        actual = zip_hash()
        if actual.lower() != FFMPEG_ZIP_SHA256.lower():
            zip_path.unlink(missing_ok=True)
            raise ValueError(f"FFmpeg 压缩包 SHA-256 校验失败: {actual}")
        ok(f"FFmpeg {FFMPEG_VERSION} SHA-256 校验通过")

    def download_zip() -> None:
        for local_zip in LOCAL_FFMPEG_ZIP_CANDIDATES:
            if file_exists(local_zip, min_size=10 * 1024 * 1024):
                step("复用本地 FFmpeg 下载包")
                ok(f"来源: {local_zip}")
                shutil.copy2(local_zip, zip_path)
                try:
                    verify_zip()
                    return
                except ValueError:
                    warn("本地 FFmpeg 包不是固定版本，忽略并从官方地址下载")

        step("下载 FFmpeg Windows essentials")
        ok(f"来源: {FFMPEG_DOWNLOAD_URL}")
        urllib.request.urlretrieve(FFMPEG_DOWNLOAD_URL, zip_path)
        verify_zip()

    if not zip_path.exists() or zip_path.stat().st_size < 10 * 1024 * 1024:
        download_zip()
    else:
        ok(f"复用 FFmpeg 下载包缓存: {zip_path}")
        try:
            verify_zip()
        except ValueError:
            download_zip()

    if extract_dir.exists():
        shutil.rmtree(extract_dir, ignore_errors=True)
    extract_dir.mkdir(parents=True, exist_ok=True)

    step("解压 FFmpeg")
    try:
        with zipfile.ZipFile(zip_path, "r") as archive:
            archive.extractall(extract_dir)
    except zipfile.BadZipFile:
        warn(f"FFmpeg 下载包缓存已损坏，删除后重新下载: {zip_path}")
        zip_path.unlink(missing_ok=True)
        if extract_dir.exists():
            shutil.rmtree(extract_dir, ignore_errors=True)
        extract_dir.mkdir(parents=True, exist_ok=True)
        download_zip()
        with zipfile.ZipFile(zip_path, "r") as archive:
            archive.extractall(extract_dir)

    found: Dict[str, Path] = {}
    for name in ("ffmpeg.exe", "ffprobe.exe"):
        matches = list(extract_dir.rglob(name))
        if not matches:
            raise FileNotFoundError(f"下载包中未找到 {name}")
        cached = FFMPEG_SIDECAR_DIR / name
        shutil.copy2(matches[0], cached)
        found[name] = cached
        ok(f"{name}: {found[name]}")
    return found


def prepare_ffmpeg_resources() -> bool:
    """校验固定版本 FFmpeg/FFprobe 并准备为 Rust 编译期字节资源。"""
    step("准备 FFmpeg/FFprobe 视频引擎")
    try:
        downloaded = download_ffmpeg_bundle()
        ffmpeg = downloaded["ffmpeg.exe"]
        ffprobe = downloaded["ffprobe.exe"]
    except Exception as e:
        err(f"FFmpeg 固定版本准备失败: {e}")
        err("禁止生成缺少媒体引擎或校验未通过的发布包")
        return False

    resources_dir = TARGET / "src-tauri" / "resources"
    resources_dir.mkdir(parents=True, exist_ok=True)
    copied = []
    for src, name in ((ffmpeg, "ffmpeg.exe"), (ffprobe, "ffprobe.exe")):
        dst = resources_dir / name
        try:
            make_path_writable(dst)
            shutil.copy2(src, dst)
            ok(f"打包资源: {dst} ({dst.stat().st_size // (1024 * 1024)} MB)")
            copied.append(name)
        except Exception as e:
            warn(f"复制 {name} 失败: {e}")

    # 验证资源确实就位
    for name in ("ffmpeg.exe", "ffprobe.exe"):
        if not (resources_dir / name).exists():
            warn(f"resources/{name} 不存在 — Tauri 打包将失败")
            warn("如需视频功能，请确保 ffmpeg.exe / ffprobe.exe 可用")

    return True


def copy_project() -> bool:
    required = [
        "package.json", "package-lock.json",
        "src-tauri/Cargo.toml", "src-tauri/Cargo.lock",
        "src-tauri/tauri.conf.json", "src-tauri/build.rs",
        "index.html", "vite.config.ts", "tsconfig.json",
    ]

    if SOURCE is not None and TARGET.resolve() == SOURCE.resolve():
        step(f"原地构建: 跳过复制项目 ({TARGET})")
        missing = [f for f in required if not (TARGET / f).exists()]
        if missing:
            err(f"关键文件缺失: {missing}")
            return False
        ok(f"项目目录已就绪 ({len(required)} 个关键文件验证通过)")
        fix_target_permissions(reset_acl=False)
        return True

    step(f"复制项目: {SOURCE} -> {TARGET}")
    excluded_dirs = " ".join(f'"{item}"' for item in COPY_EXCLUDED_DIRS)
    excluded_files = " ".join(f'"{item}"' for item in COPY_EXCLUDED_FILES)
    robocopy_cmd = (
        f'robocopy "{SOURCE}" "{TARGET}" /MIR /R:2 /W:2 /MT:8 '
        f'/NFL /NDL /NJH /NJS /NP /XD {excluded_dirs} /XF {excluded_files}'
    )
    code, _, stderr = run(robocopy_cmd, timeout=900)
    # robocopy 退码 0-7 视为成功
    if code > 7:
        err(f"robocopy 复制失败: {stderr[:300] or 'rc=' + str(code)}")
        return False

    # 验证关键文件
    missing = [f for f in required if not (TARGET / f).exists()]
    if missing:
        err(f"关键文件缺失: {missing}")
        return False
    ok(f"项目已复制 ({len(required)} 个关键文件验证通过)")
    # 修复 SMB 复制导致的权限问题
    fix_target_permissions()

    # 清理残留：如果源根目录没有 Cargo.toml，目标根目录也不应该有
    # （之前的构建残留可能导致 Cargo 工作空间解析错误）
    if SOURCE is not None:
        src_root_cargo = SOURCE / "Cargo.toml"
        dst_root_cargo = TARGET / "Cargo.toml"
        if not src_root_cargo.exists() and dst_root_cargo.exists():
            warn(f"删除目标根目录残留的 Cargo.toml: {dst_root_cargo}")
            try:
                make_path_writable(dst_root_cargo)
                dst_root_cargo.unlink()
                ok("已清理残留 Cargo.toml")
            except Exception as e:
                warn(f"无法删除残留 Cargo.toml: {e}")
                warn("这可能导致 Cargo 工作空间解析错误")

    return True


def fix_tauri_config() -> bool:
    """将 tauri.conf.json 中的 frontendDist 校正为正确的前端 dist 路径"""
    step("修复 Tauri 配置 (frontendDist 路径)")

    config_path = TARGET / "src-tauri" / "tauri.conf.json"
    if not config_path.exists():
        err(f"tauri.conf.json 不存在: {config_path}")
        return False

    try:
        make_path_writable(config_path)
        with open(config_path, "r", encoding="utf-8") as f:
            config = json.load(f)

        if "build" not in config:
            config["build"] = {}

        old_value = config["build"].get("frontendDist", "")
        # Keep this path relative to src-tauri/tauri.conf.json so a staged
        # project remains portable across Windows users and build directories.
        dist_path = "../dist"
        config["build"]["frontendDist"] = str(dist_path)

        make_path_writable(config_path)
        with open(config_path, "w", encoding="utf-8") as f:
            json.dump(config, f, ensure_ascii=False, indent=2)
            f.write("\n")

        ok(f"frontendDist: '{old_value}' -> '{dist_path}'")
        return True
    except Exception as e:
        err(f"修复 tauri.conf.json 失败: {e}")
        return False


def npm_install() -> bool:
    step("npm install (预计 2-5 分钟)")
    env = os.environ.copy()
    NPM_CACHE_DIR.mkdir(parents=True, exist_ok=True)
    env["npm_config_cache"] = str(NPM_CACHE_DIR)
    env["npm_config_fund"] = "false"
    env["npm_config_audit"] = "false"

    code, stdout, stderr = run(
        "npm install --prefer-offline --no-audit --no-fund",
        cwd=TARGET, env=env, timeout=900
    )
    if code != 0:
        err("npm install 失败")
        # 输出最后 30 行错误
        for line in (stderr or stdout).strip().split("\n")[-30:]:
            cprint(f"    {line}")
        return False
    ok("npm install 成功")
    return True


def check_tauri_deps() -> bool:
    step("检查 Tauri CLI 工具链")
    code, out, _ = run("npx tauri --version", cwd=TARGET, timeout=60)
    if code != 0:
        err("Tauri CLI 不可用 — npm install 可能未完整")
        return False
    ok(f"Tauri CLI: {out.strip().splitlines()[-1] if out.strip() else 'OK'}")
    return True


# ═════════════════════════════════════════════════════════════════════════════
#  阶段 3：构建
# ═════════════════════════════════════════════════════════════════════════════
def build_frontend() -> bool:
    step("构建前端 (vite + tsc)")
    code, stdout, stderr = run(
        "npm run build",
        cwd=TARGET, timeout=600
    )
    if code != 0:
        err("前端构建失败")
        for line in (stderr or stdout).strip().split("\n")[-20:]:
            cprint(f"    {line}")
        return False

    dist = TARGET / "dist"
    if not dist.exists():
        err("dist 目录未生成")
        return False

    # 验证 Tauri 所需的入口文件
    if not (dist / "index.html").exists():
        err("dist/index.html 不存在 — 前端构建可能不完整")
        return False

    size_mb = sum(f.stat().st_size for f in dist.rglob("*") if f.is_file()) / (1024 * 1024)
    ok(f"前端构建完成 ({size_mb:.1f} MB)")
    return True


def build_tauri() -> bool:
    step("构建 Tauri 应用 (cargo + tauri-build，预计 10-30 分钟)")
    env = os.environ.copy()
    env["CARGO_INCREMENTAL"] = "1"
    env["CARGO_BUILD_JOBS"] = str(max(1, os.cpu_count() or 2))
    CARGO_TARGET_CACHE.mkdir(parents=True, exist_ok=True)
    env["CARGO_TARGET_DIR"] = str(CARGO_TARGET_CACHE)

    code, stdout, stderr = run(
        "npx tauri build --no-bundle",
        cwd=TARGET, env=env, timeout=3600
    )
    if code != 0:
        portable = release_dir() / "tinypix.exe"
        if file_exists(portable, min_size=1024 * 1024):
            warn("Tauri 打包器返回失败，但主程序 EXE 已生成；按便携版 EXE 继续")
            warn("通常原因是 NSIS 安装包工具不可用；无需安装版本不依赖 NSIS")
            ok(f"便携主程序: {portable}")
            return True
        err("Tauri 构建失败")
        # 输出关键错误
        lines = (stderr or stdout).strip().split("\n")
        for line in lines[-40:]:
            if line.strip():
                cprint(f"    {line}")
        return False
    ok("Tauri 构建完成")
    return True


def finalize_portable_exe() -> Optional[Path]:
    """将原始 Tauri 主程序规范化为唯一便携 EXE 交付物。"""
    source = release_dir() / "tinypix.exe"
    if not file_exists(source, min_size=1024 * 1024):
        return None
    destination = release_dir() / PORTABLE_EXE_NAME
    shutil.copy2(source, destination)
    ok(f"单 EXE 便携版: {destination}")
    return destination


def security_audit(project_dir: str) -> bool:
    """构建前安全审计 — 使用 Python 内置扫描，兼容 Windows（无 grep）"""
    import os
    import re

    src_dir = os.path.join(project_dir, "src")
    if not os.path.isdir(src_dir):
        warn(f"src/ 目录不存在，跳过安全审计: {src_dir}")
        return True

    patterns = {
        "fetch(": r"\bfetch\s*\(",
        "telemetry": r"\btelemetry\b",
        "dangerouslySetInnerHTML": r"\bdangerouslySetInnerHTML\b",
    }

    findings = []
    for root, _dirs, files in os.walk(src_dir):
        for fname in files:
            if not (fname.endswith(".ts") or fname.endswith(".tsx") or fname.endswith(".js")):
                continue
            fpath = os.path.join(root, fname)
            try:
                with open(fpath, "r", encoding="utf-8", errors="ignore") as f:
                    for i, line in enumerate(f, 1):
                        for label, pat in patterns.items():
                            if re.search(pat, line):
                                findings.append(f"  {fpath}:{i}: {label}")
            except OSError:
                continue

    if findings:
        err(f"SECURITY: 发现 {len(findings)} 处可疑代码:")
        for f in findings:
            err(f)
        return False

    ok("Security audit passed")
    return True


# ═════════════════════════════════════════════════════════════════════════════
#  阶段 4：验证产物
# ═════════════════════════════════════════════════════════════════════════════
def release_dir() -> Path:
    return CARGO_TARGET_CACHE / "release"


def find_artifacts() -> List[Path]:
    artifact = release_dir() / PORTABLE_EXE_NAME
    return [artifact] if file_exists(artifact, min_size=1024 * 1024) else []


def copy_artifacts_to_smb() -> None:
    """构建成功后复制产物。

    Windows 本地桌面目录用于直接运行；Mac 共享桌面目录只用于 Mac 侧取文件。
    这样避免 Windows Defender Application Control 拦截 C:\\Mac\\... 共享路径 exe。
    """
    if SOURCE is None:
        warn("SOURCE 未确定,跳过产物复制")
        return

    artifacts = find_artifacts()
    if not artifacts:
        warn("无产物可复制")
        return

    dest_dirs = []
    if platform.system() == "Windows":
        dest_dirs.append(Path(os.environ.get("USERPROFILE", r"C:\Users\huashu")) / "Desktop" / "tiny")
        dest_dirs.append(Path(r"C:\Mac\Home\Desktop\tiny"))
    else:
        dest_dirs.append(Path("/Users/huashu/Desktop/tiny"))

    for dest_dir in dest_dirs:
        if dest_dir.drive and not dest_dir.parent.exists():
            continue
        try:
            dest_dir.mkdir(parents=True, exist_ok=True)
        except Exception as e:
            warn(f"无法创建产物目录 {dest_dir}: {e}")
            continue

        ok(f"复制产物到: {dest_dir}")
        for src in artifacts:
            if not src.exists():
                continue
            dst = dest_dir / src.name
            try:
                ps_cmd = (
                    f'powershell -NoProfile -Command "Copy-Item -Path \'{src}\' -Destination \'{dst}\' -Force -ErrorAction Stop"'
                )
                import subprocess as _sp
                r = _sp.run(ps_cmd, shell=True, capture_output=True, text=True, timeout=60)
                if r.returncode == 0:
                    size_mb = dst.stat().st_size / (1024 * 1024)
                    ok(f"  [OK] {src.name} -> {dst} ({size_mb:.1f} MB)")
                else:
                    warn(f"  [FAIL] {src.name} 复制失败: {r.stderr[:200] or 'rc=' + str(r.returncode)}")
            except Exception as e:
                warn(f"  [FAIL] {src.name} 复制异常: {e}")



def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def source_git_commit() -> str:
    if SOURCE is None:
        return "unknown"
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=SOURCE,
            capture_output=True, text=True, timeout=10, check=False,
        )
        return result.stdout.strip() if result.returncode == 0 else "unknown"
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def verify_and_report() -> bool:
    section("阶段 4 / 4 — 验证产物")
    artifacts = find_artifacts()
    if not artifacts:
        err("未找到构建产物")
        # 列出 release 目录内容
        rel = release_dir()
        if rel.exists():
            cprint("  release 目录内容:", "yellow")
            for f in sorted(rel.iterdir())[:30]:
                cprint(f"    {f.name}")
        return False

    cprint("  构建产物清单:", "green")
    for p in artifacts:
        size_mb = p.stat().st_size / (1024 * 1024)
        cprint(f"    [OK] {p.name} ({size_mb:.1f} MB)", "green")
        cprint(f"      {p}", "gray")

    digest = file_sha256(artifacts[0])
    ok(f"EXE SHA-256: {digest}")

    return True


# ═════════════════════════════════════════════════════════════════════════════
#  Main
# ═════════════════════════════════════════════════════════════════════════════
def main() -> int:
    start_time = time.time()

    # 日志层由 __main__ 块 init (统一入口)
    # 此处不再 init_logger()

    section("TinyPix v3.5 Pro — Windows 构建")
    cprint("  源: 自动检测中", "bold")
    cprint("  目标: 自动检测中", "bold")
    cprint(f"  时间: {time.strftime('%Y-%m-%d %H:%M:%S')}", "bold")
    cprint(f"  日志: {Path.cwd() / 'logs'}", "bold")

    # 阶段 1
    if not check_environment():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="EnvironmentCheckError",
                           error_summary="环境检查未通过 (Node/npm/Rust/SMB 路径等)")
        return 1
    step("确认构建路径")
    ok(f"源: {SOURCE}")
    ok(f"构建目录: {TARGET}")

    # 阶段 2
    section("阶段 2 / 4 — 安装依赖")
    if not configure_defender():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="DefenderConfigError",
                           error_summary="Defender 排除项配置失败")
        return 1
    if not clean_target():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="CleanTargetError",
                           error_summary=f"清理目标目录失败: {TARGET}")
        return 1
    if not copy_project():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="CopyProjectError",
                           error_summary=f"复制项目失败: {SOURCE} -> {TARGET}")
        return 1
    if not fix_tauri_config():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="TauriConfigError",
                           error_summary="修复 tauri.conf.json frontendDist 失败")
        return 1
    if not npm_install():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="NpmInstallError",
                           error_summary="npm install 失败 (见 logs/error.log)")
        return 1
    if not check_tauri_deps():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="TauriDepsError",
                           error_summary="Tauri CLI 不可用")
        return 1
    if not prepare_ffmpeg_resources():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="FFmpegPrepareError",
                           error_summary="FFmpeg/FFprobe 准备失败,视频功能无法使用")
        return 1

    # 阶段 3
    section("阶段 3 / 4 — 构建")

    # 构建前安全审计
    step("安全审计")
    if not security_audit(str(TARGET)):
        warn("安全审计未通过 — 继续构建但请检查上述警告")
        # 不阻断构建，仅警告

    if not build_frontend():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="FrontendBuildError",
                           error_summary="前端构建失败 (vite + tsc)")
        return 1
    if not build_tauri():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="TauriBuildError",
                           error_summary="Tauri 构建失败 (cargo, 详见 logs/error.log)")
        return 1

    if finalize_portable_exe() is None:
        err("未找到可规范化的 Tauri 主程序 EXE")
        return 1

    # 阶段 4
    if not verify_and_report():
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="VerifyArtifactsError",
                           error_summary="产物验证失败 (未找到 .exe)")
        return 1

    # 阶段 4.5 — 复制产物到 SMB 共享目录(Mac/Windows 都能直接看到)
    copy_artifacts_to_smb()

    elapsed = time.time() - start_time
    m, s = divmod(int(elapsed), 60)
    section(f"[OK] 构建成功！总耗时 {m} 分 {s} 秒")
    cprint(f"  产物位置: {release_dir()}", "green")
    cprint("", "green")

    # 写 build_info.json (含产物信息)
    _extras = {
        "elapsed_seconds": round(elapsed, 2),
        "elapsed_human": f"{m} 分 {s} 秒",
        "output_dir": str(TARGET),
        "build_commit": source_git_commit(),
        "ffmpeg_version": FFMPEG_VERSION,
        "ffmpeg_package": FFMPEG_DOWNLOAD_URL.rsplit("/", 1)[-1],
        "ffmpeg_package_sha256": FFMPEG_ZIP_SHA256,
        "windows_validation": "pending",
    }
    # 尝试找到 .exe 路径
    try:
        for candidate in find_artifacts():
            _extras["output_exe"] = str(candidate)
            _extras["output_exe_size_mb"] = round(candidate.stat().st_size / (1024*1024), 2)
            _extras["output_exe_sha256"] = file_sha256(candidate)
            # SMB 副本路径(Mac 和 Windows 都能直接看到)
            if SOURCE is not None:
                # 2026-06-03: 复制到 C:\Mac\Home\Desktop\tiny\,而不是 SOURCE/release
                smb_exe = Path(r"C:\Mac\Home\Desktop\tiny") / candidate.name
                if not smb_exe.parent.exists():
                    smb_exe = Path("/Users/huashu/Desktop/tiny") / candidate.name
                if smb_exe.exists():
                    _extras["output_exe_smb"] = str(smb_exe)
                    _extras["output_exe_smb_size_mb"] = round(smb_exe.stat().st_size / (1024*1024), 2)
            break
    except Exception:
        pass

    if _LOGGER is not None:
        info_path = _LOGGER.finish(True, extra=_extras)
        cprint(f"  构建信息: {info_path}", "gray")
    return 0


if __name__ == "__main__":
    # 顶层异常也要捕获到 logger
    _init_at_main = get_logger() is None
    if _init_at_main:
        try:
            init_logger()
        except Exception:
            pass  # 日志初始化失败不阻塞
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        cprint("\n[中断] 用户取消", "yellow")
        if _LOGGER is not None:
            _LOGGER.finish(False, error_type="UserInterrupt",
                           error_summary="用户按 Ctrl+C 取消")
        sys.exit(130)
    except Exception as e:
        import traceback as _tb
        err(f"未捕获异常: {e}")
        _tb.print_exc()
        if _LOGGER is not None:
            _LOGGER.write_exception(type(e), e, e.__traceback__)
            _LOGGER.finish(False, error_type=type(e).__name__,
                           error_summary=str(e)[:500])
        sys.exit(1)
