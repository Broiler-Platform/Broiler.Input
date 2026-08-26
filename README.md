# Broiler.Input

Broiler.Input is the platform-neutral device-input component family for .NET 10.
It owns device discovery, lifecycle, timing, bounded delivery, capture leases,
diagnostics, and platform providers. Graphics, DOM event construction, browser
permissions, encoding, playback, and presentation remain outside the component.

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

Windows providers use .NET runtime interop for Win32 Raw Input, QPC timing,
WASAPI microphone capture, and Media Foundation camera capture. Linux providers
use libc `open`, `read`, `poll`, and `ioctl` over `/dev/input/event*`. Android
providers make no native or Java calls at all: the host performs the Android API
calls and forwards primitive event data. Native handles, pointers, endpoint IDs,
and device paths do not appear in platform-neutral public payloads.

Background Raw Input and evdev event streaming require explicit acknowledgement.
Diagnostics must not emit typed text, movement timelines, or native device
paths by default.

## Validation

The normal contract runner is deterministic and hardware-free:

```powershell
dotnet build Broiler.Input\Broiler.Input.slnx
dotnet run --project Broiler.Input\Broiler.Input.Contract.Tests\Broiler.Input.Contract.Tests.csproj --no-build
```

The Android translation, provider lifecycle, and boundary tests run on any host,
because the Android backends carry no Android SDK dependency:

```sh
dotnet run --project Broiler.Input/Broiler.Input.Android.Tests/Broiler.Input.Android.Tests.csproj
```

The executable public API baseline remains at
[`docs/api-baseline.txt`](docs/api-baseline.txt); the contract-test
project copies that file into its output and compares it with the runtime
assemblies.

Opt-in device checks and privacy gates are in
[hardware validation](docs/hardware-validation.md).

## Documentation

- [Current roadmap](docs/roadmap.md)
- [ADR index](docs/adr/README.md)
- [Camera contracts and Windows provider](docs/camera.md)
- [Microphone contracts and Windows provider](docs/microphone.md)
- [Hardware and privacy validation](docs/hardware-validation.md)

The current Linux keyboard/mouse scope is intentionally limited. Layout-aware
text input, IME, touchpad policy, gestures, touch, pen, and gamepad require
separately approved provider work; see the roadmap rather than inferring support
from the presence of neutral contract assemblies.
