# Broiler.Input Roadmap

**Status:** Active preview. Keyboard/mouse providers exist for Windows and Linux;
Windows microphone and camera capture are implemented; Touch, Pen, Keyboard, and
Text providers exist for Android but have no hardware evidence yet. This file
tracks only work that is still open.

## Finish input ownership migration

- Move the remaining native message parsing and authoritative input route out of
  `Broiler.Graphics.Windows.Direct2DWindow`.
- Migrate remaining application and demo users of
  `StandardLegacyGraphicsInputAdapter` to explicit Broiler.Input providers.
- Remove Graphics-owned input callbacks and the legacy adapter only after every
  consumer has equivalent focus, capture, text, and pointer behavior.
- Keep browser DOM event construction and permission policy in the
  browser/application layer; Input owns devices, capture, timing, delivery, and
  faults.

## Complete provider coverage

- Add Windows Touch and Pen providers for the existing neutral contracts,
  including cancellation, capture loss, pressure/tilt capability reporting, DPI
  coordinates, and duplicate compatibility-mouse suppression.
- Decide whether Gamepad enters supported scope. If approved, define a neutral
  state contract and one Windows provider before creating packages.
- Treat Linux text/IME, touchpad policy, gestures, touch, and pen as separately
  approved follow-ups; do not infer support from the current evdev
  keyboard/mouse provider.

## Android providers

Android is the first consumer of the Touch, Pen, and Text contracts, so it is the
first real test of whether they are correct. The cross-component sequencing,
ownership, and exit gates are in
[the root roadmap](../../docs/ROADMAP.md#a2--real-touch-pen-and-ime-input) and
[the Android application architecture](../../docs/architecture/android.md).

**Landed.** `Broiler.Input.Android` plus the `Touch`, `Pen`, `Keyboard`, and
`Text` Android backends, and the missing neutral provider contracts they needed
(`ITouchInputProvider`/`TouchOpenOptions`, `IPenInputProvider`/`PenOpenOptions`,
`ITextInputProvider`/`TextInputOpenOptions`, mirroring the keyboard pattern).
These are the first implementations of `TouchInputDevice`, `PenInputDevice`, and
`TextInputDevice` on any platform. `Broiler.Input.Android.Tests` covers the
translation, provider lifecycle, and assembly boundary on any host.

Still open, and none of it can be closed in this container:

- Wire the providers to a real Activity and `SurfaceView`, and record hardware
  evidence: multi-touch identity, gesture latency, stylus pressure and tilt, and
  a CJK IME composing, converting, and committing into RichEdit. Emulator runs
  are not hardware evidence.
- Verify the stylus tilt conversion in `AndroidPenTilt` against a real digitizer.
  The polar-to-Cartesian formula is implemented and unit-tested for
  self-consistency, but its sign convention has not been confirmed on a device.
- Populate device descriptors from `InputDevice.getDeviceIds()` and
  `InputManager.InputDeviceListener` so capability reporting and hot-plug reflect
  the real device set rather than the `RegisterDefault*` fallbacks.
- Decide where the `AndroidTextEditRequest` surface lands once Broiler.UI grows an
  editor-side text contract. `deleteSurroundingText`, `setSelection`, and
  `setComposingRegion` have no neutral expression today, so they are currently
  raised on the Android device rather than through `TextInputDevice`.
- Add a Mouse provider if Android mouse and trackpad support enters scope. Tool
  routing already classifies mouse pointers and drops them, so nothing is
  silently delivered as touch in the meantime.
- Keep Android types inside the `.Android` assemblies. They currently reference no
  Android SDK at all — the host forwards primitive event data — and the boundary
  test pins that.

## Validate hardware and privacy

- Complete and retain evidence for the opt-in checks in
  [hardware-validation.md](hardware-validation.md).
- Add sustained start/stop, hot-plug, slow-consumer, handle-leak, and latency
  gates for camera, microphone, keyboard, and mouse.
- Supersede the initial buffer/delivery ADR wording where the shipped
  camera/microphone lease contracts are now more specific.

## Stabilize and release

- Review the public API baseline, names, XML documentation, trimming, and AOT
  behavior after real application migration.
- Validate packages from a feed without the aggregate repository and publish
  explicit native/runtime requirements.
- Complete dependency, license, privacy, and human review before stable support
  claims.
