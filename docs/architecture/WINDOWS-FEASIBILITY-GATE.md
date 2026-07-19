# Windows feasibility gate

Status: **Windows 10 environment preflight executed; disposable prototype not yet executed**. macOS source generation or static tests cannot satisfy this gate.

## 2026-07-19 environment evidence

- Parallels VM `Windows 10` is running; Parallels Tools 26.3.1 is installed.
- Guest reports `Microsoft Windows 10.0.19045.6466` (Windows 10 22H2).
- Repository is visible inside the guest at `\\Mac\Home\TinyPix\3.5pro`.
- Guest Python 3.12 is available; `dotnet` is not currently found on guest PATH.
- No disposable WinUI prototype executable has been built, so window launch, UIA,
  FFmpeg interaction, registry behavior and portable deployment remain unverified.
- Installing a build SDK is a build-environment action only; the final portable
  runtime must still pass on a clean VM without system .NET or Windows App Runtime.

## Required environment

- Windows 10 22H2 x64 ordinary-user VM.
- Windows 11 x64 ordinary-user VM.
- Visual Studio/Build Tools with the current WinUI C# workload for build only.
- A clean test snapshot without system .NET, Windows App Runtime, WebView2,
  Python, FFmpeg, or database installations for release verification.

## Disposable prototype evidence

Before formal business pages are accepted, create a throwaway unpackaged,
self-contained WinUI project under `%TEMP%` using the official `dotnet new winui`
template. Do not copy prototype UI code into TinyPix. Record:

1. Direct EXE launch and a responsive top-level window.
2. Four-region workbench, 900×600 minimum client area, and 1200×800 layout.
3. File picker and drag/drop parity.
4. Image preview, video preview, and local FFmpeg-thumbnail fallback.
5. FFmpeg progress parsing, cancellation, and abnormal-exit handling.
6. `portable.flag` root resolution and writable-directory blocking behavior.
7. Process Monitor and registry snapshots showing no TinyPix registry writes.

## Release acceptance

- Run the generated Portable ZIP with outbound traffic blocked.
- Verify light, dark, Windows High Contrast, Narrator, 100/125/150% display
  scaling, and 200% text scaling.
- Verify F6 five-region traversal and Ctrl+J task-queue focus.
- Hash source files before and after every representative tool workflow.
- Exercise Chinese paths, spaces, long paths, read-only output, disk full,
  corrupt inputs, missing engines, and corrupt models.
- Measure cold start from EXE launch to responsive shell; target is at most 5s.

The gate passes only when evidence from both OS versions is attached. Until
then, WinUI build, launch, registry safety, and accessibility remain unverified.
