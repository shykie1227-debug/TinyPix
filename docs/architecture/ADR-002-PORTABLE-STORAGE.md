# ADR-002: Explicit portable-root storage

- Status: Accepted
- Date: 2026-07-17

## Context

TinyPix must run after ZIP extraction without administrator rights, registry
configuration, hidden user-profile state, or a system runtime installation.

## Decision

Require `portable.flag` beside `TinyPix.exe`. Resolve Config, Data, Cache, and
Logs only beneath `AppContext.BaseDirectory`, probe every mutable directory
before accepting tasks, and stop with a relocation message when it is not
writable. SQLite stores at most 200 recent paths and 1000 processing records.
Preview and intermediate files belong only in Cache.

## Consequences

- Moving the whole folder moves the application state.
- Read-only deployment locations can display a clear startup explanation but
  cannot execute processing tasks.
- Source media is never copied into the database, history, or internal archive.
