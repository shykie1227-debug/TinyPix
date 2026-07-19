# TinyPix 4.0 Architecture

## Decision

TinyPix 4.0 is a single-process, offline modular monolith built with C#,
.NET 10, WinUI 3, and Windows App SDK 2.2.0. It has no local web server,
WebView UI, cloud API, telemetry, updater, service, scheduled task, or dynamic
plugin loader.

The app references the modular `Microsoft.WindowsAppSDK.WinUI 2.2.1` and
`Microsoft.WindowsAppSDK.Runtime 2.2.0` packages instead of the full 2.2.0
metapackage. This intentionally excludes unused AI, ML, and Widgets components
from TinyPix's dependency graph while remaining on the Windows App SDK 2.2 line.

Decision records: `ADR-001-MODULAR-MONOLITH.md`,
`ADR-002-PORTABLE-STORAGE.md`, and `ADR-003-DESIGN-AND-WINDOWS-GATES.md`.
Current completion boundaries are tracked in `IMPLEMENTATION-STATUS.md`.

## Dependency direction

```text
TinyPix.App ───────► TinyPix.Core
     │                    ▲
     ├────────────► TinyPix.Media
     └────────────► TinyPix.Infrastructure
```

- `TinyPix.Core` owns tool metadata, job contracts, validation, queue policy,
  and use cases. It has no WinUI, SQLite, FFmpeg, or filesystem dependency.
- `TinyPix.Media` implements local media, preview, OCR, PDF, QR, and model
  contracts and only references Core.
- `TinyPix.Infrastructure` implements portable paths, JSON settings, SQLite
  history, cache, logs, and process supervision and only references Core.
- `TinyPix.App` is the composition root and WinUI surface. It references all
  three modules; no lower module references App.

## Runtime data flow

1. App resolves `AppContext.BaseDirectory` and requires `portable.flag`.
2. Infrastructure validates `Config`, `Data`, `Cache`, and `Logs` are writable.
3. Tool input becomes an immutable `JobRequest`; source paths are read-only.
4. Core validates output safety and queues the request by workload class.
5. Media executes through an injected handler and reports throttled progress.
6. Infrastructure persists only paths, parameter summaries, status, output
   paths, and timestamps. It never persists source media, OCR text, or face data.
7. On restart, non-terminal records become `Interrupted`; retry creates a new job.

## Release invariants

- `WindowsPackageType=None`, self-contained Windows App SDK and .NET runtime.
- Folder deployment only; `PublishSingleFile=false`.
- Windows x64 release; Windows 10 22H2 and Windows 11 are the test matrix.
- No registry, file association, service, task, installer, or runtime download.
- Engines, models, native files, notices, SBOM, and SHA-256 values are release
  inputs. Missing or mismatched required assets fail packaging.
- Existing Tauri/React/Rust source remains until behavioral parity and Windows
  release acceptance are proven.
