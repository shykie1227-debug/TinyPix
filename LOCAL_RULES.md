# LOCAL_RULES.md - TinyPix 本仓库运行原则

> **本文件仅适用于本仓库（tinypix-pro）**
> 用户（王萧铭）于 2026-06-01 16:35 明确声明：
> **构建成功的 TinyPix.exe 在其他电脑运行时**，完全不联网、不上传、完全本地运行。
> **开发期 / 构建期 / 本机测试期 允许联网**（npm install / cargo build / git push / 下载 Node.js/Rust 等都正常进行）。

---

## 🔒 硬性约束（仅针对 exe 在其他电脑运行时）

### 1. exe 运行时不联网

- ❌ **打包后 exe 运行时** 禁止 访问任何 HTTP(S) / WebSocket / FTP / SMTP
- ❌ **exe 运行时** 禁止 检查更新 / 报告使用数据 / 任何 telemetry
- ❌ **exe 运行时** 禁止 自动从 CDN 下载字体/图标/JS bundle
- ❌ **exe 运行时** 禁止 调用任何云端 API / 云服务
- ✅ **开发/构建/测试期** 允许联网（pip install / npm install / curl / wget 等都正常）

### 2. exe 运行时不上传

- ❌ **exe 运行时** 禁止 上传任何用户文件/数据到云
- ❌ **exe 运行时** 禁止 同步到云盘 / 云存储
- ✅ **开发/构建/测试期** 允许上传（git push / gh release / 任何代码托管）

### 3. exe 运行时无外联（V4.6.1 承诺清单）

打包后的 TinyPix.exe 在其他电脑运行时：
- ✅ 无任何云端 API 调用代码
- ✅ 无任何 telemetry / 埋点代码
- ✅ 无任何自动更新 / 版本检查代码
- ✅ 所有图标、字体、图片、Vite bundle 内嵌
- ✅ 所有依赖打包在 exe 内（无运行时下载）

### 🔍 代码审计检查清单（Tauri + React + Rust）

前端 (React/TypeScript)：
- ❌ `fetch()` / `XMLHttpRequest` 调外网 URL
- ❌ `axios` / `swr` / `react-query` 等请求外网
- ❌ `<img src="https://...">` 远程图片
- ❌ Google Fonts / 任何 CDN
- ❌ Sentry / LogRocket / Mixpanel 等埋点 SDK

### 🗓 审计日志

- **2026-06-02** — 移除 `index.html` 中 3 行 Google Fonts `<link>`（Hanken Grotesk / Manrope / Geist），收紧 `tauri.conf.json` CSP（移除 `fonts.googleapis.com` 与 `fonts.gstatic.com` 白名单），扩展 `src/index.css` 字体回退链到 `Segoe UI Variable / Inter / Microsoft YaHei UI / PingFang SC / Noto Sans CJK SC / system-ui`。提交前已 grep 全仓库确认无其他 `googleapis` / `gstatic` / `cdn.` 引用。

后端 (Rust/Tauri)：
- ❌ `reqwest` / `hyper` / `surf` 等联网库
- ❌ `ureq` / `attohttpc` / `isahc` 等
- ❌ `tokio::net` / `std::net` 直接 socket 通讯外网
- ❌ 系统命令调用 `curl` / `wget`
- ❌ 文件系统监控后向远程 POST 报告

依赖审计：
- ❌ `package.json` 引入含 telemetry 的 npm 包
- ❌ `Cargo.toml` 引入含联网代码的 crate

### ✅ 开发/构建/测试期允许的操作

- ✅ `npm install` 安装前端依赖（联网拉 npm registry 正常）
- ✅ `cargo build` / `cargo tauri build` 编译 Rust 依赖（联网拉 crates.io 正常）
- ✅ `curl` / `wget` 下载构建工具（Node.js / Rust / 7zip / NSIS 等）
- ✅ `git push` 推送代码到 GitHub / 任何 Git 远程
- ✅ `gh release create` 发布 Release
- ✅ 开发本机手动运行 exe 调试（仅测试）
- ✅ 开发者本机手动发邮件/上传测试数据

---

## 🚨 误操作修复指引

如果 AI 不小心做了以下事情：
1. **在 exe 运行时引入了联网代码** → 立刻告知用户，提供 git revert 指引
2. **打包了含 `reqwest` / `fetch(外网)` 的代码到 exe** → 立刻告知用户，重新打包
3. **在 exe 中加入了 telemetry 埋点** → 立刻告知用户

---

## 🛡 与 SOUL.md 的优先级关系

- SOUL.md 说「不轻易求助」「不需每步确认」「自主决策」是默认模式
- 本 LOCAL_RULES.md 的「exe 运行时禁止联网」是**例外**——必须先确认
- 冲突时，**本规则优先**（保护用户数据 > 自动化效率）

---

## 📅 原则确立时间

- 2026-06-01 16:35 由用户（王萧铭）口头声明（修正 outlook-img-slicer 过度收紧版本）
- 已同步至 MEMORY.md（永久记录）
- 本规则永久生效，除非用户主动撤销
