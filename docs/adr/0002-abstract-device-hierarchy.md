# ADR 0002 - Abstract Device Hierarchy

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

## Context

The roadmap calls for one abstract root class and one direct abstract device
base class per input kind. The hierarchy must not turn into an untyped universal
device framework.

## Decision

`Broiler.Input` owns the root `InputDevice` class. It contains identity,
descriptor, lifecycle, diagnostics, disposal, and timestamp sequencing only.

`Broiler.Input.Keyboard` owns `KeyboardInputDevice : InputDevice`.
`Broiler.Input.Mouse` owns `MouseInputDevice : InputDevice`.

No public intermediate base class is approved for pointer devices or Windows
devices.

## Consequences

- Keyboard and mouse payload behavior stays in typed assemblies.
- Core Input has no universal `Read` operation and no `object` payload event.
- Future touch, pen, camera, microphone, and gamepad abstractions can be added
  without reshaping the root base class.
