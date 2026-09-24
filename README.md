# Broiler.Input

Broiler.Input is the platform-neutral device-input component family for .NET 10.
It owns device discovery, lifecycle, timing, bounded delivery, capture leases,
diagnostics, and platform providers. Graphics, DOM event construction, browser
permissions, encoding, playback, and presentation remain outside the component.

## Packages

The suite ships as one package per assembly, published to [NuGet.org](https://www.nuget.org/) and versioned in lockstep. Because current releases are **prereleases**, pass `--prerelease` when adding packages:

```bash
dotnet add package Broiler.Input.All --prerelease
```

`Broiler.Input.All` is a dependencies-only meta-package that pulls in all platform-neutral contracts. Platform backends are deliberately separate packages, so a consumer references only the ones they target:

| Package | Contents | Platform |
| --- | --- | --- |
| `Broiler.Input` | Core device lifecycle, discovery, timing, and bounded delivery contracts | Neutral (`net10.0`) |
| `Broiler.Input.All` | Meta-package referencing all neutral contracts below | Neutral (`net10.0`) |
| `Broiler.Input.Camera` | Neutral camera contract, formats, and frame leases | Neutral (`net10.0`) |
| `Broiler.Input.Keyboard` | Neutral keyboard contract, key maps, layout, and text | Neutral (`net10.0`) |
| `Broiler.Input.Microphone` | Neutral audio capture contract, formats, and buffer leases | Neutral (`net10.0`) |
| `Broiler.Input.Mouse` | Neutral mouse and pointer contracts, position, and buttons | Neutral (`net10.0`) |
| `Broiler.Input.Pen` | Neutral stylus and pen contracts, pressure, tilt, and eraser | Neutral (`net10.0`) |
| `Broiler.Input.Text` | Neutral IME and text input contracts | Neutral (`net10.0`) |
| `Broiler.Input.Touch` | Neutral multi-touch contracts and pointer tracking | Neutral (`net10.0`) |
| `Broiler.Input.Legacy` | Window-callback compatibility adapter for migration | Neutral (`net10.0`) |
| `Broiler.Input.Windows` + `Broiler.Input.<Kind>.Windows` | Windows providers (Raw Input, WASAPI, Media Foundation) | Windows (`net10.0-windows`) |
| `Broiler.Input.Linux` + `Broiler.Input.<Kind>.Linux` | Linux evdev event-device providers | Linux (`net10.0`) |
| `Broiler.Input.Android` + `Broiler.Input.<Kind>.Android` | Android translation layer (primitive event data, no SDK dependency) | Neutral / Android (`net10.0`) |

`Broiler.Input.Testing`, `Broiler.Input.Linux.Diagnostic`, and the `*.Tests` runners are internal development projects and are not published.

## Quick Start (End-User Guide)

### 1. Keyboard Input

Reference `Broiler.Input.Keyboard` and the target platform provider (e.g. `Broiler.Input.Keyboard.Windows`):

```csharp
using System;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Windows;

// Instantiate the platform provider
IKeyboardInputProvider provider = new WindowsKeyboardProvider();

// Enumerate available devices
var devices = await provider.GetDevicesAsync();
if (devices.Count > 0)
{
    // Open a device session
    using KeyboardInputDevice keyboard = await provider.OpenAsync(devices[0], new KeyboardOpenOptions());

    // Subscribe to key change and text events
    keyboard.KeyChanged += e =>
    {
        Console.WriteLine($"Key: {e.Key}, Down: {e.IsDown}, Modifiers: {e.Modifiers}");
    };

    keyboard.TextInput += e =>
    {
        Console.WriteLine($"Text: {e.Text}");
    };
}
```

### 2. Camera Frame Capture

Reference `Broiler.Input.Camera` and `Broiler.Input.Camera.Windows`:

```csharp
using Broiler.Input.Camera;
using Broiler.Input.Camera.Windows;

ICameraInputProvider provider = new WindowsCameraProvider();
var devices = await provider.GetDevicesAsync();

if (devices.Count > 0)
{
    using CameraInputDevice camera = await provider.OpenAsync(devices[0], new CameraOpenOptions());

    // The consumer receives frame leases and MUST dispose them when done
    camera.FrameReceived += lease =>
    {
        using (lease)
        {
            ReadOnlySpan<byte> frameBytes = lease.Memory.Span;
            // Process contiguous frame pixel data...
        }
    };

    // Start preview or loss-sensitive capture
    await camera.StartCaptureAsync(new CameraSessionOptions());
}
```

### 3. Microphone Audio Capture

Reference `Broiler.Input.Microphone` and `Broiler.Input.Microphone.Windows`:

```csharp
using Broiler.Input.Microphone;
using Broiler.Input.Microphone.Windows;

IMicrophoneInputProvider provider = new WindowsMicrophoneProvider();
var devices = await provider.GetDevicesAsync();

if (devices.Count > 0)
{
    using MicrophoneInputDevice microphone = await provider.OpenAsync(devices[0], new MicrophoneOpenOptions());

    microphone.BufferReceived += lease =>
    {
        using (lease)
        {
            ReadOnlySpan<byte> audioBytes = lease.Memory.Span;
            // Process captured PCM audio bytes...
        }
    };

    await microphone.StartCaptureAsync(new MicrophoneSessionOptions());
}
```

## Repository Layout

```text
Broiler.Input.slnx          solution file
Directory.Build.props       component-level build and packaging overrides
Directory.Packages.props    central package version management (CPVM)
NuGet.config                NuGet.org package source configuration
eng/Broiler.Packaging.props vendored, suite-wide packaging metadata
eng/icon.png                package icon
eng/pack.ps1                package pack and verification script
eng/verify-feed.ps1         consumer restore verification script
eng/resolve-preview-version.mjs preview version resolver
src/<project>/              shipping projects
src/tests/<project>/        test runners and test support
docs/                       ADRs, roadmap, API baseline, validation notes
```

## Projects

```text
Broiler.Input
Broiler.Input.All
Broiler.Input.Windows
Broiler.Input.Linux
Broiler.Input.Linux.Diagnostic
Broiler.Input.Camera
Broiler.Input.Camera.Windows
Broiler.Input.Keyboard
Broiler.Input.Keyboard.Windows
Broiler.Input.Keyboard.Linux
Broiler.Input.Legacy
Broiler.Input.Microphone
Broiler.Input.Microphone.Windows
Broiler.Input.Mouse
Broiler.Input.Mouse.Windows
Broiler.Input.Mouse.Linux
Broiler.Input.Pen
Broiler.Input.Text
Broiler.Input.Touch
Broiler.Input.Android
Broiler.Input.Keyboard.Android
Broiler.Input.Pen.Android
Broiler.Input.Text.Android
Broiler.Input.Touch.Android
Broiler.Input.Testing
Broiler.Input.Contract.Tests
Broiler.Input.Linux.Tests
Broiler.Input.Android.Tests
```

Platform-neutral projects target `net10.0`. Windows implementation projects target `net10.0-windows`. Linux keyboard and mouse providers use direct evdev event-device reads for the current preview.

The Android providers also target plain `net10.0` and take primitive event data — the `int` and `float` values read from `MotionEvent`, `KeyEvent`, and `InputConnection` — rather than referencing `Mono.Android`. The host owns the Activity and the View, reads the real Android objects, and forwards the primitives. That keeps `Android.Views` and `Java.Lang` types out of the neutral contracts, and it makes the whole translation layer buildable and testable without the `android` workload installed. A boundary test asserts the absence of those references.

## Dependency Rules

Typed platform providers depend on their matching abstraction and shared platform support:

```text
Broiler.Input.<Kind>.Windows -> Broiler.Input.<Kind> -> Broiler.Input
Broiler.Input.<Kind>.Windows -> Broiler.Input.Windows -> Broiler.Input

Broiler.Input.<Kind>.Linux   -> Broiler.Input.<Kind> -> Broiler.Input
Broiler.Input.<Kind>.Linux   -> Broiler.Input.Linux -> Broiler.Input

Broiler.Input.<Kind>.Android -> Broiler.Input.<Kind> -> Broiler.Input
Broiler.Input.<Kind>.Android -> Broiler.Input.Android -> Broiler.Input
```

Input assemblies do not reference Graphics, HTML, DOM, JavaScript, WPF, Windows Forms, or application projects. Camera and microphone capture do not own codecs, playback, or preview UI.

## Native Boundaries

The Windows and Linux native declarations live in `Broiler.Native.Windows` and `Broiler.Native.Linux`. Providers retain their device/session behavior and error mapping. Versions are managed in `Directory.Packages.props`; sibling checkouts do not replace package references. Native and the camera provider's Media dependencies must be published before the updated Input packages are released.

Windows providers use .NET runtime interop for Win32 Raw Input, QPC timing, WASAPI microphone capture, and Media Foundation camera capture. Linux providers use libc `open`, `read`, `poll`, and `ioctl` over `/dev/input/event*`. Android providers make no native or Java calls at all: the host performs the Android API calls and forwards primitive event data. Native handles, pointers, endpoint IDs, and device paths do not appear in platform-neutral public payloads.

Background Raw Input and evdev event streaming require explicit acknowledgement. Diagnostics must not emit typed text, movement timelines, or native device paths by default.

## Developer Guide

### Building and Testing

Use the .NET 10 SDK and the standard `Debug` or `Release` configuration:

```bash
dotnet build Broiler.Input.slnx -c Release
bash ./eng/run-tests.sh Release
```

Or run individual suites with `dotnet run`:

```bash
# Android translation layer and boundary unit tests
dotnet run --project src/tests/Broiler.Input.Android.Tests/Broiler.Input.Android.Tests.csproj -c Release

# Windows contract and platform tests (on Windows)
dotnet run --project src/tests/Broiler.Input.Contract.Tests/Broiler.Input.Contract.Tests.csproj -c Release

# Preview version calculation unit tests (requires Node.js 24)
node --test eng/resolve-preview-version.test.mjs
```

The contract suite verifies the public API against `docs/api-baseline.txt`. Optional physical device checks are described in [hardware validation](docs/hardware-validation.md).

### Packing and Verification

```powershell
powershell -File eng/pack.ps1
```

This builds and validates all 23 shipping packages into the `artifacts/` folder, verifying their nuspec manifests, versions, internal dependency bounds, documentation XMLs, icon, README, and symbol packages (`.snupkg`).

To verify consumer restore against NuGet.org in an isolated sandbox with a fresh cache:

```powershell
powershell -File eng/verify-feed.ps1 -Packages artifacts
```

### CI and Publishing

All builds and packages restore exclusively from **NuGet.org**.

Publishing is driven by `.github/workflows/publish.yml`:
- Trigger manually via `workflow_dispatch` (defaults to `dry-run: true` to validate version resolution, packing, and consumer restore without pushing).
- Trigger automatically by pushing a git release tag (`v0.1.0-preview.N`).
- Publishes exclusively to **NuGet.org** using the `NUGET_TOKEN` (or `NUGET_API_KEY`) repository secret.

See [CI, packages, and releases](docs/packaging.md) for complete details on central package management, feed verification, and preview tags.

## Documentation Index

- [Current Roadmap](docs/roadmap.md)
- [ADR Index](docs/adr/README.md)
- [CI, Packages, and Releases](docs/packaging.md)
- [Camera Contracts and Windows Provider](docs/camera.md)
- [Microphone Contracts and Windows Provider](docs/microphone.md)
- [Hardware and Privacy Validation](docs/hardware-validation.md)

The current Linux keyboard/mouse scope is intentionally limited. Layout-aware text input, IME, touchpad policy, gestures, touch, pen, and gamepad require separately approved provider work; see the roadmap rather than inferring support from the presence of neutral contract assemblies.
