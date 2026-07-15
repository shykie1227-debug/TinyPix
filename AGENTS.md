# TinyPix 3.5 Pro Agent Rules

## Source Of Truth
- Use `/Users/huashu/TinyPix/3.5pro` as the only active source directory.
- Treat `UI设计/*/screen.png`, `UI设计/*/code.html`, `UI设计/*/DESIGN.md`, root `DESIGN.md`, and `LOCAL_RULES.md` as product constraints.
- Record every meaningful implementation pass and lessons learned in `CHANGELOG.md`.

## Runtime Rules
- The final Windows EXE must run fully offline.
- Do not add runtime HTTP requests, cloud APIs, telemetry, update checks, remote fonts, CDN scripts, or remote images.
- Development and build-time downloads in `build.py` are allowed only for build dependencies such as Node, Rust, or FFmpeg.
- Keep Windows build caches outside the cleaned `TinyPixBuild` target. FFmpeg, npm cache, and Cargo target must be reusable across manual copy/build cycles.

## UI Rules
- Keep the app as a desktop tool, not a marketing page.
- Preserve the structure: left feature rail, center drag/preview workspace, right parameter panel, bottom status/execution area.
- The first screen must be the unified 视频输出 workbench.
- Video navigation has exactly three entries: 视频输出, GIF 制作, 视频剪辑.
- Image mode has exactly one 图片处理 workbench entry; do not restore edit/export dual panels.
- After any supported file is added, the center area must show a real preview or a clear local fallback, never the empty drag prompt.
- If WebView cannot play a video codec, use local FFmpeg thumbnail fallback and avoid wording that implies FFmpeg cannot process the file.
- Image editing uses the offline `react-image-crop` dependency for crop interaction; do not introduce commercial or online image editors.
- Settings contains output path, embedded engine status/cache cleanup, and open-source notices.
- Use the TinyPix design tokens: black primary actions, lime active/progress states, off-white workspace, white rounded cards, restrained shadows.
- Do not reintroduce the old brown `pro-green` palette or remote Stitch/Google font dependencies.

## Code Rules
- Frontend components live in `src/components`; shared helpers live in `src/utils`, `src/hooks`, or `src/stores`.
- Tauri commands live in `src-tauri/src/commands`; keep the mirrored `src/commands` copy in sync when a command file already exists in both places.
- UI should call Tauri through explicit command arguments; tests must cover command names and parameter objects for media actions.
- Prefer existing components such as `ToolOptionCard`, `MediaPreviewStage`, `DropZone`, and the current store before adding new abstractions.

## Testing Rules
- Follow TDD for behavior changes: write or update the test first, verify the failure, then implement the minimum fix.
- Before claiming completion, run the targeted Vitest suite, Python build-script tests, TypeScript check, Vite build, and Rust `cargo check`.
- For UI changes, verify 1200×800 and 900×600 at 100%, 125%, and 150% zoom; check all three video tools, the single image workbench, settings, focus, overflow, and status states.

## Figma / Design Translation Rules
- Treat Figma or generated design output as reference, not final code style.
- Map all colors, spacing, typography, and surfaces back to root `DESIGN.md` and local tokens.
- Reuse project components instead of pasting standalone generated screens.
- If a Figma MCP screenshot/context is used, validate the implemented UI visually against it before marking complete.
