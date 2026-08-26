# ADR 0003 - Windows Support Matrix

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

## Context

The first operating-system implementation is Windows. The user request for this
slice limits runtime implementation work to keyboard and mouse.

## Decision

Windows projects target `net10.0-windows` and use .NET runtime interop only.
The first supported Windows input mechanisms are:

| Mechanism | Owner | Phase 0 behavior |
|---|---|---|
| Window keyboard messages | `Broiler.Input.Keyboard.Windows` | Translate key down/up and text |
| Window mouse messages | `Broiler.Input.Mouse.Windows` | Translate movement, buttons, leave, vertical and horizontal wheel |
| Raw Input registration | `Broiler.Input.Windows` | Explicit process-wide registration lease |
| Monotonic timestamps | `Broiler.Input.Windows` | Query Performance Counter clock |

No WPF, Windows Forms, WinRT, Direct2D, Media Foundation, WASAPI, or XInput
dependency is introduced by this slice.

## Consequences

- Graphics can later forward HWND messages without Input referencing Graphics.
- Keyboard and mouse can move first without pulling in camera or microphone
  capture dependencies.
- Native code stays auditable through `DllImport` and `LibraryImport`.
