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
mapping. A sibling `Broiler.Native` checkout is used through project references;
set `BroilerNativeRoot` for another source location. Without sources, the build
uses the packages at `BroilerNativeVersion` (initially `0.1.0-preview.1`). Native
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

## Build

The solution carries six configurations. The platform-suffixed ones select which
provider family participates, so the neutral contracts stay buildable on a host
that has neither Windows nor Linux backends available:

| Configuration | Builds |
| --- | --- |
| `Debug` / `Release` | Neutral contracts, the Android backends, and their tests |
| `Debug-Windows` / `Release-Windows` | The above plus the Windows providers and the contract tests |
| `Debug-Linux` / `Release-Linux` | The above plus the Linux providers, the evdev diagnostic tool, and the Linux tests |

```bash
dotnet build Broiler.Input.slnx -c Release-Windows
```

A plain `dotnet build Broiler.Input.slnx` uses `Debug`, which deliberately skips
every Windows and Linux provider. Projects that carry no platform suffix declare
only `Debug` and `Release`, so the solution maps `*-Windows` and `*-Linux` onto
those base configurations — a neutral project built under `Release-Linux` still
writes to `bin/Release`.

## Validation

The suites are self-hosted console runners, not a test framework, so there is
nothing for `dotnet test` to discover. `eng/run-tests.sh` starts the ones that
apply to a configuration and is what CI calls:

```bash
dotnet build Broiler.Input.slnx -c Release-Windows --nologo
bash ./eng/run-tests.sh Release-Windows
```

The individual runners are below.

The contract runner is deterministic and hardware-free. It is a Windows target,
so it builds under the `-Windows` configurations:

```powershell
dotnet build Broiler.Input.slnx -c Release-Windows
dotnet run --project src\tests\Broiler.Input.Contract.Tests\Broiler.Input.Contract.Tests.csproj -c Release-Windows --no-build
```

The Android translation, provider lifecycle, and boundary tests run on any host,
because the Android backends carry no Android SDK dependency. That runner is
platform-neutral, so it uses the base `Release` configuration even when the
solution was built as `Release-Windows`:

```sh
dotnet run --project src/tests/Broiler.Input.Android.Tests/Broiler.Input.Android.Tests.csproj -c Release
```

The Linux runner needs the `-Linux` configurations and a Linux host:

```sh
dotnet run --project src/tests/Broiler.Input.Linux.Tests/Broiler.Input.Linux.Tests.csproj -c Release-Linux
```

The executable public API baseline remains at
[`docs/api-baseline.txt`](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/api-baseline.txt);
the contract-test project copies that file into its output and compares it with
the runtime assemblies.

Opt-in device checks and privacy gates are in
[hardware validation](https://github.com/Broiler-Platform/Broiler.Input/blob/main/docs/hardware-validation.md).

## Packing

Packing is per configuration, because each one contributes a different provider
family. Both runs emit the neutral packages; the builds are deterministic, so
the second run reproduces the first byte for byte:

```bash
dotnet pack Broiler.Input.slnx -c Release-Windows -o artifacts
dotnet pack Broiler.Input.slnx -c Release-Linux   -o artifacts
```

That produces 23 packages plus matching `.snupkg` symbol packages. Version comes
from `VersionPrefix` in `eng/Broiler.Packaging.props` and the `VersionSuffix`
override in `Directory.Build.props` (currently `preview.2`), shared by the suite.

The **Publish** workflow checks the published versions of every packable project
on NuGet.org and selects a shared version above the highest `preview.N` for the
configured `VersionPrefix`. For example, a published `0.1.0-preview.1` makes the
next publish `0.1.0-preview.2`, followed by `preview.3`, and so on. The configured
preview is the minimum; local packing keeps using the checked-in defaults.
GitHub Packages publishes also check that destination's versions. Feed lookup
failures stop publication, and all publish runs share one concurrency group.

Only `preview.N` releases are allowed. Leave the manual `version-suffix` input
empty for automatic selection, or supply an unused preview at least as high as
the automatically selected one. A release tag such as `v0.1.0-preview.2` requests
that exact version and must meet the same requirements; stable and other
prerelease tags are rejected. Use a manual dry run to inspect the selected
version and packages before publishing. No source version edit is needed.

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
