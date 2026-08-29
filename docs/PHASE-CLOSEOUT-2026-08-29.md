# TinyPix 4.0 phase closeout report

Date: 2026-08-29
Branch at audit time: `codex/phase-closeout-cleanup`

## 1. Progress record

### Completed and committed locally

- The repository is organized around the TinyPix 4.0 WinUI direction while keeping the old Tauri implementation as the behavior baseline.
- The v3.5.1 baseline tag exists as `v3.5.1-tauri-final`.
- The formal .NET solution exists with the intended project split:
  - `src/TinyPix.App`
  - `src/TinyPix.Core`
  - `src/TinyPix.Media`
  - `src/TinyPix.Infrastructure`
  - matching test projects under `tests/`
- The WinUI App now has a formal shell skeleton, reusable controls, and initial business pages for:
  - video output
  - GIF making
  - unified video editing
- Video editing keeps one user-facing workbench for trim, split, merge, geometry, speed/volume, audio, overlay, frames, and metadata. This matches the product decision to avoid separate top-level pages for each edit tool.
- The Media layer has FFmpeg-based handlers for phase 1 to phase 3 video workflows.
- Portable build and release scripts exist for the Windows direction, including manifest, dependency list, SBOM, ZIP, hash, license, and offline-security outputs.
- Windows 10 disposable WinUI feasibility evidence is documented and retained.
- Large raw Windows registry evidence files are ignored by Git and remain local diagnostic evidence.
- Obvious ignored `.DS_Store` temporary files were removed from the working tree.

### Verification performed in this closeout pass

- `python3 -m pytest -q tests/test_v4_design_contracts.py tests/test_v4_architecture_contract.py`
  - Result: 34 passed, 1 dependency warning from `requests`.
- `git diff --check`
  - Result: passed.
- Git remote check:
  - `origin/main` has no new commits beyond the local base.
- `main` and `origin/main` are synchronized at the start of this cleanup pass.

### Gated, not complete

- Windows 11 clean VM gate was explicitly skipped by user direction and remains unverified.
- The final Portable ZIP and final `TinyPix.exe` release are not complete until verified on clean Windows 10 and Windows 11 machines.
- Current WinUI video pages and handlers are contract-level and build-script-level implementation. They still need full Windows runtime UIA, real FFmpeg media runs, cancellation/failure isolation, and source-hash verification.
- Image, ID photo, background removal, PDF, OCR, QR/barcode, screen recording, rename, checksum, color, and metadata workflows are not yet complete as end-user workflows.

## 2. Next plan

Priority 1: Windows build and runtime proof

- Run the formal App restore/build/test path on Windows.
- Launch the formal WinUI app, not the disposable prototype.
- Verify 1200x800, 900x600, 100/125/150% DPI, Light/Dark/High Contrast, settings dialog, F6, Ctrl+J, Esc, Enter, and focus return.
- Run at least one real FFmpeg job for video output, GIF, and video edit, then verify source SHA-256 remains unchanged.

Estimated effort: 1-2 focused sessions if the Windows VM already has the SDK and shared folder working.

Priority 2: Phase 4 image workbench

- Add image engine abstraction and handlers for resize, crop, rotate, flip, convert, compress, adjustment, watermark, EXIF view/clear, collage/contact sheet, and batch processing.
- Keep the product boundary: one image workbench entry, no duplicate edit/export pages.

Estimated effort: 2-4 sessions depending on image library integration and Windows validation.

Priority 3: ID photo and local model gate

- Add model runtime validation, license checks, hash checks, and CPU-only inference boundaries.
- Implement ID photo templates, face positioning, background replacement, DPI/mm/pixel validation, and print layouts.

Estimated effort: 3-5 sessions because model licensing and offline runtime proof are hard gates.

Priority 4: Toolbox workflows

- Implement QR/barcode generation and recognition, PDF page management, OCR, screen recording, batch rename, checksum, color tools, clipboard image save, and metadata tools.
- Do not reintroduce duplicate-file detection or archive compression tools.

Estimated effort: 3-6 sessions, split by tool family.

Priority 5: final release gate

- Run Windows 10 and Windows 11 clean-machine acceptance.
- Generate the final Portable ZIP, SHA-256, SBOM, manifest, third-party notices, and offline security instructions.
- Only after this gate may the project claim a final runnable TinyPix 4.0 EXE.

Estimated effort: 1-2 sessions after all workflows are complete.

## 3. Implementation guidance

- Keep using the branch flow: create `codex/<task>`, make one scoped change, run targeted tests, commit, merge to `main`, then push.
- Before deleting any branch, confirm `git merge-base --is-ancestor <branch> main` succeeds.
- Do not delete the Tauri/React/Rust source until the WinUI app reaches feature parity and final Portable validation passes.
- Do not parse or manually edit `design/TinyPix-4.0.pen`; use Pencil MCP only.
- Keep large raw Windows evidence files out of Git. The ignored PML/CSV evidence is useful locally but should not be pushed.
- Do not claim Windows 11 support, final Portable release, or a final `TinyPix.exe` until those exact runtime checks have been performed.
- Any UI issue found in screenshots should be fixed at the design/source component level first, then verified through WinUI runtime screenshots and UIA.
