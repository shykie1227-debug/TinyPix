# Design System: TinyPix 3.5 Pro
**Project ID:** Local UI reference set in `/Users/huashu/TinyPix/3.5pro/UI设计`

## 1. Visual Theme & Atmosphere
TinyPix 3.5 Pro uses a precise, tactile desktop-tool aesthetic inspired by Apple's design language: calm off-white workspaces, crisp black primary actions in pill-shaped buttons, soft layered control cards with 18px rounded corners, and a vivid lime accent for active states and progress. The interface should feel efficient and local-first rather than promotional. The first screen is the actual media workbench, not a landing page.

## 2. Color Palette & Roles
- **Pitch Black (#000000):** Primary action buttons, key headings, and strong command emphasis.
- **Vibrant Lime (#B4F400):** Active selections, progress, success emphasis, and high-confidence affordances.
- **Deep Utility Green (#4B6700):** Text or icon color on lime surfaces when stronger contrast is needed.
- **Apple Gray (#F5F5F7):** Main app background and large content surfaces (aligned to Apple background-200).
- **Card White (#FFFFFF):** Primary parameter panels and focused work cards.
- **Sidebar Surface (#F2F2F7):** Left sidebar background (aligned to Apple background-200).
- **Neutral Tier (#E5E5EA):** Inactive controls, hover states, status bar background, and dividers (aligned to Apple background-300).
- **Ink Black (#1D1D1F):** Body text and labels (aligned to Apple text-800).
- **Muted Graphite (#6E6E73):** Supporting copy, status notes, and secondary metadata (aligned to Apple text-500).
- **Outline (#D1D1D6):** Subtle borders and disabled control outlines (aligned to Apple background-400).
- **Error Red (#BA1A1A):** Failure states only.

## 3. Typography Rules
Use local system font stack only: `-apple-system, BlinkMacSystemFont, "SF Pro Display", "SF Pro Text", "Segoe UI Variable", "Segoe UI", "Microsoft YaHei UI", "PingFang SC", system-ui, sans-serif`. Do not add remote font links. Monospace uses `SF Mono, Menlo, Consolas, Monaco, monospace`.

**Type scale (aligned to Apple UI Kit):**
| Level | Size | Weight | Line-height | Letter-spacing |
|-------|------|--------|-------------|----------------|
| Display | 48px | 600 | 1.05 | -0.015em |
| Headline | 24px | 600 | 1.14 | 0.007em |
| Body | 16px | 400 | 1.47 | -0.022em |
| Caption | 14px | 400 | 1.43 | -0.016em |
| Label | 12px | 600 | 1.33 | -0.01em |
| Status | 11px | 500 | 1.33 | -0.01em |

Letter spacing must be zero for large Chinese headings and modest only for small technical labels.

## 4. Component Stylings
* **Buttons:** Primary commands use Apple pill shape (`border-radius: 980px`, `min-height: 44px`, `padding: 8px 22px`). Black fill with white text for primary actions. Transparent with black border for secondary. Hover uses `opacity: 0.8` (180ms ease), no scale transforms. Text links use `#0066cc` with underline on hover.
* **Cards/Containers:** Main tool panels use white cards with 18px rounded corners and very soft shadows. Avoid nested card-on-card layouts unless the inner surface is a true control group.
* **Inputs/Forms:** Use neutral filled surfaces, subtle borders, lime active state, and black or green slider thumbs. Focus uses `box-shadow: 0 0 0 3px rgba(0, 113, 227, 0.3)`. Numeric and option controls must not shift layout while interacting.
* **Navigation:** Keep the left rail grouped by 图片工具 and 视频工具. Video tools are exactly: 视频输出, GIF 制作, 视频剪辑. Image tools contain one 图片处理 entry. Nav items use `opacity: 0.6` on hover (Apple style).
* **Status:** Progress and local-engine feedback use lime accents and concise wording. Errors are short, visible, and local to the failing operation.

## 5. Layout Principles
Use the provided demo structure: 184–224px left function area, `minmax(0,1fr)` center drag/preview area, and 280–340px right parameter area. The workbench opens directly to 视频输出. Each column scrolls independently and the page must never scroll horizontally. Settings contains output path, engine status/cache cleanup, and licenses. Runtime UI must remain fully offline with no remote fonts, images, APIs, telemetry, or update checks.

## 6. Spacing & Radius System
- **Base grid:** 8px
- **Container padding:** 32px
- **Card gap:** 24px
- **Card internal padding:** 24px (standardized)
- **Control stack:** 12px
- **Card/container radius:** 18px
- **Input/select radius:** 12px
- **Button radius:** 980px (pill)
- **Small chip/tag radius:** 980px (pill)

## 7. Interaction Standards
- **Hover:** `opacity: 0.6` for navigation links, `opacity: 0.8` for primary buttons (180ms ease)
- **Active/Pressed:** `opacity: 0.7` for all interactive elements
- **Focus:** Blue ring `box-shadow: 0 0 0 3px rgba(0, 113, 227, 0.3)` on inputs
- **No scale transforms** on any interactive element (removed per Apple design language)

## 8. Media Preview & Mature Component Rules
- A selected file must replace the empty drag zone with a real preview immediately.
- Image tools use one 图片处理 workbench. Preview, crop, rotate, mirror, dimensions, color, quality, format conversion, EXIF cleanup, and output path belong to the same flow.
- Image crop interaction uses `react-image-crop`, an offline React component with a permissive license; do not replace it with commercial SDKs or online editors.
- Video tools share the same local preview stage. 视频输出, GIF 制作, and 视频剪辑 must all keep the loaded video visible while their right panel changes.
- If a video codec cannot be played by the embedded browser, call local FFmpeg to generate a thumbnail fallback. The UI should say the embedded player cannot play this codec, while making clear that FFmpeg processing can continue.
- Lime badges should not float in the main title area. Use lime for selected tools, active presets, progress, and concise bottom status feedback.
