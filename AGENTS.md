# TinyPix 4.0 Migration Agent Rules

## Quick Start

1. Use `/Users/huashu/TinyPix/3.5pro` as the canonical checkout.
2. Read `PROJECT-INDEX.md` for fast task routing, source ownership, tests, and Git workflow.
3. Read `LOCAL_RULES.md` before changing runtime, storage, networking, packaging, or source-file behavior.
4. Treat TinyPix 4.0 WinUI as the active target. 旧 Tauri/React/Rust implementation is a protected behavior baseline, not the target architecture.
5. Check `docs/architecture/IMPLEMENTATION-STATUS.md`; file presence does not prove a feature is complete.
6. Use Pencil MCP only for `design/TinyPix-4.0.pen`; never parse or edit `.pen` with text tools.

## Source Of Truth
- Use `PROJECT-INDEX.md` as the retrieval router, not as a replacement for task-specific facts.
- Product/runtime hard constraints: `LOCAL_RULES.md`.
- UI source: `design/TinyPix-4.0.pen`, `design/UI-SPEC.md`, `design/FEATURE-MATRIX.md`, `design/WINUI-CONTROL-MAPPING.md`, and root `DESIGN.md`.
- Architecture/status: `docs/architecture/` ADRs, `TINPIX-4-ARCHITECTURE.md`, `IMPLEMENTATION-STATUS.md`, and `WINDOWS-FEASIBILITY-GATE.md`.
- Tests and current Windows evidence outrank historical plans and screenshots for completion claims.
- Record every meaningful implementation pass and lessons learned in `CHANGELOG.md`.

## Runtime Rules
- The final Windows EXE must run fully offline.
- Do not add runtime HTTP requests, cloud APIs, telemetry, update checks, remote fonts, CDN scripts, or remote images.
- Development and build-time downloads in `build.py` are allowed only for build dependencies such as Node, Rust, or FFmpeg.
- Keep Windows build caches outside the cleaned `TinyPixBuild` target. FFmpeg, npm cache, and Cargo target must be reusable across manual copy/build cycles.

## UI Rules
- Keep the app as a desktop tool, not a marketing page.
- Preserve the TinyPix 4.0 structure: top category navigation; left file area; center preview/editor; right parameters; bottom task queue.
- The first screen must be the unified 视频输出 workbench.
- Video navigation has exactly three entries: 视频输出, GIF 制作, 视频剪辑.
- Image mode has exactly one 图片处理 workbench entry; do not restore edit/export dual panels.
- After any supported file is added, the center area must show a real preview or a clear local fallback, never the empty drag prompt.
- Legacy Tauri/WebView only: if WebView cannot play a video codec, use the local FFmpeg thumbnail fallback and avoid wording that implies FFmpeg cannot process the file. The WinUI target must not add WebView UI.
- Settings is one modal `SettingsDialog`, not a media workbench or duplicate page.
- Video trim, split, and merge share one video-editing Page/ViewModel; keep handlers independent.
- Use the TinyPix design tokens: black primary actions, lime active/progress states, off-white workspace, white rounded cards, restrained shadows.
- Do not reintroduce the old brown `pro-green` palette or remote Stitch/Google font dependencies.

## Code Rules
- `TinyPix.Core` owns models, interfaces, validation, tool catalog, jobs, and use cases; it must not depend on WinUI, SQLite, FFmpeg, or filesystem implementations.
- `TinyPix.Media` and `TinyPix.Infrastructure` only depend on Core; `TinyPix.App` is the WinUI composition root.
- Do not add Web API, local server, WebView UI, service, updater, dynamic plugins, or a separate backend process.
- Legacy React components in `src/components` and Rust commands in `src-tauri` remain for behavior comparison until Windows parity and release acceptance.
- For legacy fixes only, preserve existing React/Tauri patterns and tests; do not let legacy structure redefine the WinUI architecture.

## Testing Rules
- Follow TDD for behavior changes: write or update the test first, verify the failure, then implement the minimum fix.
- New architecture changes require the relevant C# tests plus `tests/test_v4_design_contracts.py` and `tests/test_v4_architecture_contract.py`.
- Legacy behavior changes require targeted Vitest/Rust tests, TypeScript check, Vite build, and `cargo check`.
- For UI changes, verify 1200×800 and 900×600 at 100%, 125%, and 150% zoom; check all three video tools, the single image workbench, settings, focus, overflow, and status states.
- Windows-only claims require the Windows feasibility gate; macOS source checks cannot prove WinUI launch, UIA, registry safety, or Portable execution.

## Pencil / Design Translation Rules
- Treat Pencil as the visual and interaction source; automated tests and real Windows behavior are the functional source.
- Map all colors, spacing, typography, and surfaces back to root `DESIGN.md` and local tokens.
- Reuse project components instead of pasting standalone generated screens.
- If a Figma or generated screenshot is used, validate it against the repository Pencil source before implementation.

## Git Rules

- Start task branches from synchronized `main` and use the `codex/` prefix by default.
- Inspect status and worktrees before switching, merging, deleting, or cleaning.
- Merge only after tests and diff checks; delete a branch with safe `git branch -d` only after it has no commits outside `main`.
- Never force-push, hard-reset, or broadly clean without explicit user authorization.
