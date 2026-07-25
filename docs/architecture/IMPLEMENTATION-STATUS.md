# TinyPix 4.0 implementation status

Updated: 2026-07-22

## Implemented and locally verified

- Four-project modular-monolith solution and one-way project references.
- Frozen 50-item built-in tool catalog and eight template categories; archive
  compression/extraction is intentionally excluded from Toolbox.
- Job state machine, workload concurrency, cancellation, failure isolation,
  interrupted recovery, and retry-as-new-job behavior.
- Output preflight for unwritable directories, insufficient space, existing
  output conflicts, and unconditional source-overwrite rejection.
- FFmpeg progress parsing, bounded stderr capture, abnormal exit result, and
  process-tree cancellation.
- Required `portable.flag`, portable mutable directories, write probes, atomic
  JSON settings, SQLite recent/history retention, and restart interruption.
- Portable publication scripts for folder publish, manifest, dependency list,
  CycloneDX SBOM, ZIP, SHA-256, licenses, and offline-security notice.
- Design contracts for semantic foregrounds, interactive borders, media-stage
  text, five-region F6 traversal, Ctrl+J, task states, and dark/high-contrast separation.
- Repository Pencil source reopened through Pencil MCP; the current source has
  36 top-level nodes (35 boards plus the root-level reusable `SettingsDialog`)
  and 26 reusable components, including the single reusable
  `SettingsDialog` referenced by all three settings evidence frames. The 2026-07-19 representative
  recheck and current screenshots are recorded in
  `docs/audit/2026-07-19-recheck/`.
- 2026-07-19 local regression: Core 25, Media 5, Infrastructure 9 .NET tests;
  App locked restore with Windows targeting; 640 Vitest, 120 Rust, 26 Pytest,
  frontend production build, and Rust check passed.
- 2026-07-19 retrieval/audit contract recheck: 24 focused Python contracts
  passed and `git diff --check` passed.
- 2026-07-22 Windows 10 disposable WinUI gate: unpackaged/self-contained
  launch, 100/125/150% display DPI, Light/Dark/High Contrast, settings dialog,
  F6/Ctrl+J, Narrator-active focus walk, picker/drag-drop, image/video/FFmpeg
  fallback, progress/cancel/failure isolation, source hashing, read-only root,
  offline execution, registry classification, cold start, and 200% text scaling
  passed. The gate UI is explicitly marked non-final and is not an implementation source.
- Process Monitor final classification: 12 raw candidates, 11 existing-system-key
  opens, zero new keys, zero TinyPix-owned writes, and one Windows BAM
  execution-state value. Full raw evidence is retained locally and the compact
  candidate/report evidence is in `artifacts/windows/winui-feasibility-gate/`.

## Gated, not claimed complete

- Static UI design freeze: passed. Standard, compact, and High Contrast settings
  evidence now uses one responsive `SettingsDialog` with a complete-root scrim;
  the repository Pencil source, exports, UI specification, and control mapping
  agree. This static result does not prove runtime focus or input behavior.
- WinUI shell and formal business pages: not implemented. Windows 10 prototype
  behavior passed, but the overall gate remains open until a clean Windows 11
  x64 run exists.
- App project restore/build and the disposable prototype build passed on the
  configured Windows 10 VM. The formal App, Portable ZIP, SBOM execution, and
  clean-machine runtime acceptance remain incomplete.
- Media/OCR/PDF/model handlers and all 50 end-user workflows are contracts or
  catalog entries, not complete feature implementations yet.

See `WINDOWS-FEASIBILITY-GATE.md` and ADR-003 for the exact evidence required.
