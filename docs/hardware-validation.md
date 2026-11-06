# Hardware and Privacy Validation

**Status:** Open release evidence. Normal component tests remain hardware-free;
the checks below are opt-in and must be recorded before a stable support claim.

## Normal contract checks

```powershell
dotnet build Broiler.Input.slnx
dotnet run --project src/tests/Broiler.Input.Contract.Tests/Broiler.Input.Contract.Tests.csproj --no-build
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

## Scanner

- Flatbed and ADF scanners enumerate with stable opaque IDs (`windows:wia:scanner:<hash>`,
  `linux:sane:scanner:<hash>`, `android:scanner:<id>`) across `RefreshDevicesAsync`.
- Flatbed single-page acquisition at 150 DPI and 300 DPI (`Rgb24` and `Grayscale8`)
  produces top-down `ScannerPageLease` buffers with accurate plane metadata.
- Multi-page ADF simplex and duplex batch acquisition sets `ScannerPageFlags.DuplexBackSide`
  on odd pages and transitions smoothly to `ScannerCaptureState.Completed` when the
  feeder empties (`WIA_ERROR_PAPER_EMPTY` / `SANE_STATUS_NO_DOCS`) after page > 0, or
  faults with `ScannerFaultCode.FeederEmpty` when empty at job start.
- Physical paper jam, cover open, lamp failure, and multi-feed conditions map to the
  documented `InputErrorCategory` and `ScannerFaultCode` values.
- Slow consumer backpressure and `StopAsync()` / `Dispose()` mid-scan cancel the active
  transfer (`IWiaTransfer::Cancel` / `sane_cancel`) promptly without deadlocking or
  delivering late callbacks.
- Diagnostics and descriptor capabilities omit raw scanned image bytes and unredacted
  native device paths (`/dev/bus/usb/...`).
