# ADR 0005 - Buffer Ownership

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

> **Implementation update:** Camera frames and microphone packets are copied
> into disposable owned leases before delivery. See [camera.md](../camera.md)
> and [microphone.md](../microphone.md).

## Context

Keyboard and mouse events are small value payloads. Camera frames and microphone
buffers will require explicit ownership and lifetime rules.

## Decision

The Phase 0 keyboard/mouse slice uses immutable value payloads and no native
buffer exposure.

Future camera and microphone work must choose between immutable copies and
disposable pooled leases before public API stabilization. Native pointers must
not appear in platform-neutral abstraction payloads.

## Consequences

- This slice has no pooled buffer implementation.
- Event consumers can retain keyboard and mouse payloads safely.
- Later sample APIs cannot smuggle callback-scoped native memory through the
  neutral contracts.
