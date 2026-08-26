# ADR 0004 - Event And Stream Delivery

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

> **Implementation update:** Camera and microphone now use bounded delivery with
> owned disposable leases. Their current contracts are documented in
> [camera.md](../camera.md) and [microphone.md](../microphone.md).

## Context

Discrete devices such as keyboards and mice have small ordered transitions.
Camera and microphone later need bounded high-throughput streams.

## Decision

Keyboard and mouse abstractions expose typed discrete events on their typed
device classes. Each event carries an `InputEventHeader` with device ID,
monotonic timestamp, and sequence number.

High-throughput camera and microphone stream shape is not implemented in this
slice. The approved direction remains bounded asynchronous streams with an
explicit drop or backpressure policy.

## Consequences

- Keyboard and mouse do not require unbounded queues for Phase 0.
- The root `InputDevice` stays free of payload-specific event members.
- Later sample-capture APIs must declare queue bounds and loss behavior before
  becoming public.
