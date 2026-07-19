# ADR-001: Single-process modular monolith

- Status: Accepted
- Date: 2026-07-17

## Context

TinyPix must remain offline, portable, understandable after long periods without
maintenance, and extensible with additional built-in tools. A web server,
background service, and dynamic plugin host add deployment and security surfaces
without improving the local media workflow.

## Decision

Use four projects with one-way dependencies: Core owns policies and contracts;
Media and Infrastructure implement Core contracts; App composes them and owns
WinUI. All work runs in one ordinary-user process except explicitly supervised
local engine child processes such as FFmpeg.

Reference only the Windows App SDK 2.2 WinUI and Runtime component packages;
do not reference the full metapackage, because TinyPix does not use its AI, ML,
or Widgets components.

## Consequences

- No API versioning, service installation, port management, or IPC is required.
- A new tool is a static `ToolDescriptor` plus an `IToolHandler`, not a runtime plugin.
- Core remains testable on non-Windows hosts and cannot reference WinUI, SQLite,
  filesystem, or media packages.
