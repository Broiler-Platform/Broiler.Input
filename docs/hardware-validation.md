# Hardware and Privacy Validation

**Status:** Open release evidence. Normal component tests remain hardware-free;
the checks below are opt-in and must be recorded before a stable support claim.

## Normal contract checks

```powershell
dotnet build Broiler.Input\Broiler.Input.slnx
dotnet run --project Broiler.Input\Broiler.Input.Contract.Tests\Broiler.Input.Contract.Tests.csproj --no-build
```

These checks use deterministic fake providers and must not require input
hardware, privacy permission, or a running capture service.

## Keyboard and mouse privacy

The standing security contract is foreground-only semantic/raw input by default,
explicit acknowledgement plus a target window for background Raw Input, opaque
physical IDs, system-key pass-through by default, and diagnostics that omit
typed text, movement timelines, and native device paths.

Hardware evidence must prove:

- two physical keyboards or mice remain distinguishable in raw mode;
- default options cannot enable background registration;
- system shortcuts still reach Windows unless explicitly consumed; and
- raw-input diagnostics contain no sensitive payload or native path.

## Microphone

- Built-in and USB microphones enumerate with stable opaque IDs.
- Default Console, Multimedia, and Communications roles are observable.
- Shared/event capture produces buffers and stays bounded under sustained load.
- Deliberate mute/unplug scenarios produce the expected silence,
  discontinuity, and invalidation results.
- Explicit endpoint capture does not silently follow a changed default.
- Stop/dispose releases the endpoint and produces no later callback.
- Privacy denial, busy endpoint, unsupported format, and invalidation map to the
  documented `InputErrorCategory` values.

## Camera

- Built-in and USB cameras enumerate even when display names collide.
- IDs remain stable across refreshes for the same symbolic link.
- Exact native format requests succeed or fail clearly.
- Slow preview consumers remain bounded; loss-sensitive mode reports drops.
- Format changes, stream ticks, end-of-stream, privacy denial, and shutdown map
  to documented flags or faults.
- Stop/dispose releases camera indicators/resources and produces no later frame
  callback.
