# ADR 0007 - Raw Input Registration Ownership

**Status:** Approved for Phase 1  
**Date:** 2026-07-02

## Context

Windows Raw Input registration is process-wide for a top-level collection. If
multiple providers register independently, one provider can steal or suppress
messages from another.

## Decision

`Broiler.Input.Windows` owns `WindowsRawInputRegistrationCoordinator`.
Registration returns a `WindowsRawInputRegistrationLease` that unregisters on
dispose. Keyboard and mouse Windows providers expose convenience methods that
delegate to this coordinator.

The coordinator allows one live keyboard lease and one live mouse lease for the
process. Conflicting registrations fail immediately.

## Consequences

- Raw Input ownership is explicit at the application composition root.
- A later Graphics bridge can borrow the same coordinator instead of registering
  behind Input's back.
- Background input requires an explicit target window and options object.
