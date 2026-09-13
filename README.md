# Broiler.Input

Broiler.Input is the platform-neutral device-input component family for .NET 10.
It owns device discovery, lifecycle, timing, bounded delivery, capture leases,
diagnostics, and platform providers. Graphics, DOM event construction, browser
permissions, encoding, playback, and presentation remain outside the component.

## Packages

The suite ships as one package per assembly, versioned in lockstep. The current
release is a **prerelease**, so package managers need the prerelease flag:

```bash
dotnet add package Broiler.Input.All --prerelease
```

`Broiler.Input.All` is a dependencies-only meta-package that pulls in the
platform-neutral contracts. Platform backends are deliberately separate, so a
consumer takes only the ones it runs on:

| Package | Contents |
| --- | --- |
| `Broiler.Input` | Device lifecycle, discovery, timing, and delivery contracts |
| `Broiler.Input.All` | Meta-package over the neutral contracts below |
| `Broiler.Input.Camera` · `.Keyboard` · `.Microphone` · `.Mouse` · `.Pen` · `.Text` · `.Touch` | Per-kind neutral abstractions |
| `Broiler.Input.Legacy` | Window-callback compatibility adapter for migration |
| `Broiler.Input.Windows` + `Broiler.Input.<Kind>.Windows` | Windows providers (`net10.0-windows`) |
| `Broiler.Input.Linux` + `Broiler.Input.<Kind>.Linux` | Linux evdev providers |
| `Broiler.Input.Android` + `Broiler.Input.<Kind>.Android` | Android translation layer (no Android SDK dependency) |

`Broiler.Input.Testing`, `Broiler.Input.Linux.Diagnostic`, and the `*.Tests`
runners are development-only and are not published.

## Repository layout

```text
Broiler.Input.slnx          solution
Directory.Build.props       component-level build and packaging overrides
eng/Broiler.Packaging.props vendored, suite-wide packaging metadata
eng/icon.png                package icon
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

Platform-neutral projects target `net10.0`. Windows implementation projects
target `net10.0-windows`. Linux keyboard and mouse providers use direct evdev
event-device reads for the current preview.

The Android providers also target plain `net10.0` and take primitive event data
— the `int` and `float` values read from `MotionEvent`, `KeyEvent`, and
`InputConnection` — rather than referencing `Mono.Android`. The host owns the
Activity and the View, reads the real Android objects, and forwards the
primitives. That keeps `Android.Views` and `Java.Lang` types out of the neutral
contracts, and it makes the whole translation layer buildable and testable
without the `android` workload installed. A boundary test asserts the absence of
those references.

## Dependency rules

Typed platform providers depend on their matching abstraction and shared
platform support:

```text
Broiler.Input.<Kind>.Windows -> Broiler.Input.<Kind> -> Broiler.Input
Broiler.Input.<Kind>.Windows -> Broiler.Input.Windows -> Broiler.Input

Broiler.Input.<Kind>.Linux   -> Broiler.Input.<Kind> -> Broiler.Input
Broiler.Input.<Kind>.Linux   -> Broiler.Input.Linux -> Broiler.Input

Broiler.Input.<Kind>.Android -> Broiler.Input.<Kind> -> Broiler.Input
Broiler.Input.<Kind>.Android -> Broiler.Input.Android -> Broiler.Input
```

Input assemblies do not reference Graphics, HTML, DOM, JavaScript, WPF,
Windows Forms, or application projects. Camera and microphone capture do not
own codecs, playback, or preview UI.

## Native boundaries

The Windows and Linux native declarations live in `Broiler.Native.Windows` and
`Broiler.Native.Linux`. Providers retain their device/session behavior and error
mapping. Versions are managed in `Directory.Packages.props`; sibling checkouts
do not replace package references. Native and the camera provider's Media dependencies
must be published before the updated Input packages are released.

Windows providers use .NET runtime interop for Win32 Raw Input, QPC timing,
WASAPI microphone capture, and Media Foundation camera capture. Linux providers
use libc `open`, `read`, `poll`, and `ioctl` over `/dev/input/event*`. Android
providers make no native or Java calls at all: the host performs the Android API
calls and forwards primitive event data. Native handles, pointers, endpoint IDs,
and device paths do not appear in platform-neutral public payloads.

Background Raw Input and evdev event streaming require explicit acknowledgement.
Diagnostics must not emit typed text, movement timelines, or native device
paths by default.

## Build and validation

Use the .NET 10 SDK and the standard `Debug` or `Release` configuration:

```bash
dotnet build Broiler.Input.slnx -c Release
bash ./eng/run-tests.sh Release
```

The solution builds the neutral contracts and Android providers. The test script
also builds and runs the contract suite on Windows or the evdev suite on Linux.
These are console runners, so `dotnet test` does not discover them. The contract
suite verifies the public API against `docs/api-baseline.txt`. Optional device
checks are described in [hardware validation](docs/hardware-validation.md).

## Packing and publishing

```sh
pwsh -File eng/pack.ps1
```

This produces all 23 packages, including the Windows and Linux providers excluded
from the normal solution build. The dependencies-only meta-package has no symbols;
runtime packages include `.snupkg` files. CI tests Linux and Windows; Publish reuses
CI, verifies consumer restore, and publishes those artifacts. Manual runs default
to a dry run with automatic preview selection.

See [CI, packages, and releases](docs/packaging.md) for central dependency versions,
GitHub Packages credentials, NuGet.org setup, and preview tags.

## Documentation

- [Current roadmap](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/roadmap.md)
- [ADR index](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/adr/README.md)
- [Camera contracts and Windows provider](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/camera.md)
- [Microphone contracts and Windows provider](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/microphone.md)
- [Hardware and privacy validation](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/hardware-validation.md)

The current Linux keyboard/mouse scope is intentionally limited. Layout-aware
text input, IME, touchpad policy, gestures, touch, pen, and gamepad require
separately approved provider work; see the roadmap rather than inferring support
from the presence of neutral contract assemblies.
