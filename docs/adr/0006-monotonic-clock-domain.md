# ADR 0006 - Monotonic Clock Domain

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

## Context

Input events and future samples need ordering timestamps that are not affected
by wall-clock changes.

## Decision

`Broiler.Input` defines `InputTimestamp` and `IInputClock`.

The neutral fallback clock is `StopwatchInputClock`. Windows implementations use
`WindowsInputClock`, backed by `QueryPerformanceCounter` and
`QueryPerformanceFrequency` through `LibraryImport`.

Wall-clock time is not part of event ordering.

## Consequences

- Keyboard and mouse event ordering uses a documented monotonic domain.
- Future camera and microphone implementations can correlate timestamps without
  inventing a second clock contract.
- Diagnostics may add wall-clock fields later, but they do not replace the
  monotonic event timestamp.
