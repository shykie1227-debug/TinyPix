# ADR-003: Pencil and Windows evidence gates

- Status: Accepted
- Date: 2026-07-17

## Context

WinUI business pages cannot be responsibly finalized from screenshots alone,
and macOS cannot prove unpackaged WinUI startup, registry behavior, Narrator,
Windows High Contrast, or Windows App SDK self-contained deployment.

## Decision

Pencil remains the visual and interaction source. Formal business pages are
blocked until the shared components and exports are synchronized and reviewed.
Windows acceptance separately requires the disposable prototype and VM evidence
listed in `WINDOWS-FEASIBILITY-GATE.md`. Core, media-process, persistence, and
packaging contracts may progress without weakening either gate.

## Consequences

- Static contracts can be implemented and tested cross-platform now.
- A macOS build or mock screenshot can never be reported as Windows acceptance.
- Navigation, template, or primary-flow changes return to Pencil before code.
