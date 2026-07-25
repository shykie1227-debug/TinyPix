# Windows feasibility gate

Status: **Windows 10 22H2 disposable-prototype sub-gate passed on 2026-07-22; the overall gate remains open because no clean Windows 11 x64 VM is available.** The prototype is technical evidence only and is not the TinyPix product UI.

## Verified Windows 10 environment

- Parallels VM `Windows 10`: Windows 10 22H2 x64, ordinary-user desktop.
- Build-only SDK: .NET SDK 10.0.302.
- Prototype packages: Windows App SDK WinUI 2.2.1 and Runtime 2.2.0.
- Prototype location: `%TEMP%\TinyPix-WinUI-Gate`; no prototype UI code may be copied into `TinyPix.App`.
- The executable launched unpackaged and self-contained from its folder with `portable.flag` beside it.

## Passed Windows 10 evidence

- Direct EXE launch, centered first activation, 1200×800 and effective 900×600 layouts.
- Real 100%, 125%, and 150% display-DPI runs with strict window/screen bounds and no compact-panel clipping.
- Real 200% Windows text scaling at 100% display DPI. The 900×600 compact state uses the accessible scroll region for the long output path; navigation, import, preview, parameter labels, and all action labels remain complete. The system setting was restored to 100% in `finally`.
- Light, Dark, and Windows High Contrast runs.
- One root-bound settings `ContentDialog`, invalid-save focus, successful atomic save, Escape, and trigger-focus return.
- F6 five-region traversal and Ctrl+J task-queue focus.
- Real Narrator process active during the keyboard walk; focused accessible names and control types matched all expected regions and the settings field.
- Button picker and real Explorer drag/drop shared the same import path.
- Real image preview, normal video preview, and FFV1/MKV system-decode failure followed by a visible local FFmpeg thumbnail.
- FFmpeg progress, cancellation, abnormal-exit isolation, process-tree cleanup, and unchanged source SHA-256.
- Writable portable root and read-only-root processing block.
- Five cold starts: median 530 ms, maximum 747 ms, target at most 5 seconds.
- With the physical network adapter disabled, settings/UIA and the complete media/FFmpeg run both returned 0; the adapter was restored in `finally`.
- Process Monitor 4.04 was downloaded from Microsoft Sysinternals and its Microsoft Authenticode signature was verified. The final trace contains 12 raw write candidates: 11 `RegCreateKey` calls all report `REG_OPENED_EXISTING_KEY`, zero new keys, zero application-owned writes, and one Windows-managed BAM execution-state value. No TinyPix setting, file association, installer, service, schedule, or application-owned registry value was written.
- All temporary `TinyPixWinUIGate*` scheduled tasks were removed after evidence capture.

## Evidence location

Final reports and screenshots are under:

```text
artifacts/windows/winui-feasibility-gate/
```

Important evidence includes:

- `uia-report-{100,125,150}percent-final.txt`
- `media-uia-report-final.txt`
- `narrator-uia-report-final.txt`
- `offline-gate-report-final.txt`
- `procmon-registry-report-final.txt`
- `tinypix-registry-write-candidates.csv`
- `cold-start-report-final.txt`
- `30-video-ffmpeg-thumbnail-fallback.png`
- `31-narrator-focus-walk.png`
- `32-compact-900x600-200percent-text.png`
- `text-scale-200-report-final.txt`

The full PML and 443 MB CSV remain locally available but are ignored by Git. Failed and exploratory evidence is retained under `diagnostics/2026-07-22/`.

## Still required

- Repeat launch, settings, themes, DPI, offline, registry, media fallback, and portable-root checks on a clean Windows 11 x64 VM.
- Run the final formal Portable ZIP on clean Windows 10 and Windows 11 machines without system .NET, Windows App Runtime, WebView2, Python, FFmpeg, or database installations.

The overall gate does not pass until the Windows 11 and final Portable evidence exists. Windows 10 evidence permits engineering conclusions about the prototype, but it does not make the prototype a product release or approve its visual design.
