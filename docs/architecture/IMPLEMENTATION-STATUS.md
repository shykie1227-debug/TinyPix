# TinyPix 4.0 implementation status

Updated: 2026-07-19

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
  35 top-level frames and 25 reusable components. The 2026-07-19 representative
  recheck and current screenshots are recorded in
  `docs/audit/2026-07-19-recheck/`.
- 2026-07-19 local regression: Core 25, Media 5, Infrastructure 9 .NET tests;
  App locked restore with Windows targeting; 640 Vitest, 120 Rust, 26 Pytest,
  frontend production build, and Rust check passed.
- 2026-07-19 retrieval/audit contract recheck: 23 focused Python contracts
  passed and `git diff --check` passed.

## Gated, not claimed complete

- UI design freeze: not passed. Standard and compact settings-modal evidence
  leaves the top navigation outside the scrim, while `UI-SPEC.md`,
  `WINUI-CONTROL-MAPPING.md`, and the high-contrast evidence require the scrim
  to cover the complete `XamlRoot`. Correct the repository Pencil source and
  regenerate affected exports before freezing.
- WinUI shell and formal business pages: blocked by the remaining design-freeze
  correction and disposable Windows prototype.
- App project restore/build, Portable ZIP, SBOM execution, startup, drag/drop,
  preview, registry, firewall, Narrator, scaling, and cold-start evidence:
  require the Windows 10/11 gate.
- Media/OCR/PDF/model handlers and all 50 end-user workflows are contracts or
  catalog entries, not complete feature implementations yet.

See `WINDOWS-FEASIBILITY-GATE.md` and ADR-003 for the exact evidence required.
