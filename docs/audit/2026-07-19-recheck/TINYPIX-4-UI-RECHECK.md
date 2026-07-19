# TinyPix 4.0 UI recheck — 2026-07-19

## Scope and evidence

- Source: repository `design/TinyPix-4.0.pen`, opened and read through Pencil MCP.
- Inventory: 35 top-level frames and 25 reusable components.
- Current-run evidence: eight exported frames under `screenshots/`.
- Structural scan: video output, wide/compact timeline, ID photo, wide/compact
  settings, high-contrast settings, and compact QR generator all returned
  `No layout problems` from Pencil `snapshot_layout(problemsOnly=true)`.
- Evidence boundary: static frames can prove visible composition and specified
  states. They cannot prove real WinUI focus order, keyboard input, Narrator,
  display scaling, FFmpeg behavior, QR decodability, registry safety, or runtime
  theme resource resolution.

## Findings

1. **Video output — visually coherent; runtime pending.**
   The four-region shell, selected category, source item, preview, output
   controls, preset selection, primary action, and task queue have a consistent
   hierarchy. Lime surfaces use dark text/icons. No structural issue was
   reported in the 1200×800 frame.

   ![Video output](screenshots/rUrqE.png)

2. **Video editing timeline — product direction is coherent; interaction gate
   remains.**
   The design uses one non-destructive single-track editor for trim, split, and
   multi-clip assembly instead of separate merge/trim pages. It exposes in/out
   range, playhead, kept/excluded segments, split, undo/redo, zoom, duration,
   and export settings. Wide and compact frames have no Pencil layout warning.
   Windows validation must still prove pointer hit targets, keyboard parity,
   scrolling/zooming, exact-frame vs keyframe behavior, undo, and export-range
   correctness.

   ![Wide video timeline](screenshots/fj5RE.png)
   ![Compact video timeline](screenshots/F8byft.png)

3. **Smart ID photo — visually coherent; model workflow pending.**
   Source identity, detected-face status, physical template, background,
   pixels/DPI, head-ratio feedback, output directory, preset cards, and task
   state are aligned. Static evidence does not prove no-face/multi-face/side-face
   recovery or print-layout output.

   ![Smart ID photo](screenshots/cQnR3.png)

4. **Settings — blocking consistency defect.**
   The standard and compact frames dim only the work area below the top
   navigation. This contradicts `UI-SPEC.md` and
   `WINUI-CONTROL-MAPPING.md`, which require one modal `SettingsDialog` whose
   scrim covers the complete application `XamlRoot`, including navigation. The
   high-contrast frame uses full-window modality, so the three evidence frames
   do not currently describe one visual tree/state system. Correct the wide and
   compact Pencil frames; do not create another settings page or ViewModel.

   ![Standard settings](screenshots/7VTHb.png)
   ![Compact settings](screenshots/78swl.png)
   ![High-contrast settings](screenshots/kLfdv.png)

5. **QR generator — visually coherent; scan result pending.**
   The compact frame uses a tool-specific source, live preview, output format,
   payload, size, error-correction level, output path, primary action, and task
   status. The exported QR could not be decoded in this macOS checkout because
   no QR decoder is installed; actual ZXing output and round-trip scan belong
   in the Windows/media integration gate.

   ![QR generator](screenshots/C00rHV.png)

## Decision

UI health is **yellow, not frozen**. The representative frames are internally
clean enough to proceed after one source-of-truth correction, but the settings
modal evidence is a release-blocking contradiction. Runtime UX remains gated by
the disposable Windows prototype and must not be inferred from Pencil.

## Required next action

1. Update only the repository Pencil source so wide and compact settings use a
   full-`XamlRoot` scrim, preserving the single dialog and responsive navigation.
2. Re-export affected 1200×800 and 900×600 frames and rerun layout/contrast
   review; update this report with the corrected evidence.
3. Mark UI frozen only when Pencil, UI spec, control mapping, and exports agree.
4. Create a new task branch from synchronized `main` for the disposable Windows
   feasibility prototype; do not extend static pages while runtime gates remain.
