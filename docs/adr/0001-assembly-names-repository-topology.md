# ADR 0001 - Assembly Names And Repository Topology

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

## Context

The roadmap requires `Broiler.Input` to become a standalone component family
with separately selectable abstractions and platform implementations.

## Decision

Create `Broiler.Input/` as a root component directory with its own solution
file. The aggregate solution references the Phase 0 projects under
`/Dependencies/Input/`.

The approved first assembly set is:

```text
Broiler.Input
Broiler.Input.Windows
Broiler.Input.Keyboard
Broiler.Input.Keyboard.Windows
Broiler.Input.Mouse
Broiler.Input.Mouse.Windows
```

Every runtime assembly is also the future package name.

## Consequences

- The aggregate workspace can build the component atomically with the browser
  host during migration.
- The component can later become a submodule or external repository without
  changing assembly names.
- Camera, microphone, touch, pen, and gamepad packages remain reserved but are
  not scaffolded in this keyboard/mouse-first slice.
