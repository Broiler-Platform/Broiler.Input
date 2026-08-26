# Microphone Contracts and Windows Provider

`Broiler.Input.Microphone` owns the platform-neutral capture contract.
`Broiler.Input.Microphone.Windows` owns the first implementation and targets
`net10.0-windows`. Neither assembly owns encoding, playback, resampling, or UI.

## Runtime contract

- `MicrophoneInputDevice` extends `InputDevice` and requires
  `InputKind.Microphone`.
- `IMicrophoneInputProvider` opens devices with `MicrophoneOpenOptions`.
- `MicrophoneFormat` records sample rate, channels, bit depth, sample format,
  and derived byte/frame sizes.
- `MicrophoneBufferLease` owns copied capture bytes. Disposing the lease
  invalidates its memory.
- `MicrophoneSessionOptions` carries bounded delivery, silence/discontinuity
  reporting, and requested shared-mode latency.
- `MicrophoneCaptureStatistics` reports captured, delivered, dropped, silent,
  discontinuous, and queued packets.

Providers copy native buffers into owned leases before invoking callbacks. The
callback owns a delivered lease until it disposes it; the provider disposes
buffers dropped before delivery or observed after shutdown begins.

Privacy denial maps to `PermissionDenied`, busy endpoints to `DeviceBusy`,
unsupported formats to `UnsupportedCapability`, removal/invalidation to
`DeviceRemoved`, and otherwise-unclassified WASAPI failures to `NativeFailure`.

## Windows WASAPI implementation

`WindowsMicrophoneProvider` enumerates active endpoints and default Console,
Multimedia, and Communications roles. Native endpoint IDs stay inside a
Windows-specific capability; normal descriptors expose opaque Broiler IDs.

`WindowsMicrophoneInputDevice` owns a capture thread, initializes COM, activates
`IAudioClient`, reads the shared-mode mix format, uses shared/event capture, and
copies `IAudioCaptureClient` packets into leases. Stop/dispose waits for capture
shutdown and releases COM and native handles before returning.

Packet timestamps prefer WASAPI's QPC timestamp and fall back to the component
clock when WASAPI reports a timestamp error.

See [hardware validation](hardware-validation.md) for opt-in device checks.
