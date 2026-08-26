# Camera Contracts and Windows Provider

`Broiler.Input.Camera` owns the platform-neutral camera contract.
`Broiler.Input.Camera.Windows` owns the first implementation and targets
`net10.0-windows`. Neither assembly depends on Graphics, UI, encoders, playback,
or third-party camera wrappers.

## Runtime contract

- `CameraInputDevice` extends `InputDevice` and requires `InputKind.Camera`.
- `ICameraInputProvider` opens devices with `CameraOpenOptions`.
- `CameraFormat` and `CameraCapability` describe dimensions, frame rate, pixel
  format, and the provider's native subtype.
- `CameraFrameLease` owns copied frame bytes and plane metadata. Disposing the
  lease invalidates its memory.
- `CameraSessionOptions` selects bounded latest-frame preview or an explicit
  loss-sensitive delivery policy.
- `CameraLatestFramePreviewAdapter` keeps only the latest frame without adding a
  Graphics dependency.

Latest-frame mode disposes an older queued frame before accepting a newer one.
Loss-sensitive mode uses the caller's queue capacity and overflow policy, and
reports drops in `CameraCaptureStatistics`.

Frames carry plane layout, rotation, color space, timestamp, frame number, and
discontinuity, format-change, end-of-stream, and timestamp-error flags.

## Windows Media Foundation implementation

`WindowsCameraProvider` uses Media Foundation device-source enumeration and
keeps the symbolic link only as a Windows-specific capability. Public device
identity is an opaque stable Broiler ID.

`WindowsCameraInputDevice` owns a background capture thread. It activates the
selected `IMFMediaSource`, creates an `IMFSourceReader`, negotiates the native or
exact requested format, copies contiguous samples into `CameraFrameLease`
instances, and releases the source and COM objects before stop/dispose returns.

Unsupported requested formats map to `UnsupportedCapability`; access denial maps
to `PermissionDenied`; source shutdown or removal maps to `DeviceRemoved`.

Media Foundation Source Reader remains the default provider. A separately named
`MediaFrameReader` provider is justified only if multi-source groups,
synchronized depth/infrared/color streams, or WinRT-specific sensor metadata
become first-class requirements.

See [hardware validation](hardware-validation.md) for opt-in device checks.
