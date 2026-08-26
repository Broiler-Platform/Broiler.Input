# ADR 0008 - Media Interop Boundary

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

## Context

The roadmap separates live input capture from stored media codecs and playback.
Camera and microphone are not part of this keyboard/mouse-first implementation
slice, but their boundary must be recorded before those assemblies appear.

## Decision

Input owns live device enumeration, opening, capture lifecycle, timestamps,
bounded delivery, and privacy/fault state for camera and microphone when those
phases begin.

Media owns codecs, containers, playback, encoding, file I/O, and rendering or
preview presentation. No Input assembly may reference a concrete Media codec or
player.

## Consequences

- This Phase 0 slice introduces no Media Foundation or WASAPI implementation.
- Future camera and microphone Windows assemblies can use native OS APIs through
  interop, but their neutral abstractions remain codec-free.
- Synchronized audio/video capture belongs in a future orchestration package,
  not hidden coupling between camera and microphone providers.
