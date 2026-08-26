using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Broiler.Input.Camera;
using Broiler.Input.Camera.Windows;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Windows;
using Broiler.Input.Legacy;
using Broiler.Input.Microphone;
using Broiler.Input.Microphone.Windows;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Windows;
using Broiler.Input.Testing;
using Broiler.Input.Windows;

namespace Broiler.Input.Contract.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var checks = new List<(string Name, Func<Task> Check)>
        {
            ("fake lifecycle", () => InputContractAssert.ProvesLifecycleAsync(new FakeInputProvider())),
            ("fake cancellation", () => InputContractAssert.ProvesCancellationAsync(new FakeInputProvider())),
            ("fake removal", () => InputContractAssert.ProvesRemovalAsync(new FakeInputProvider())),
            ("fake bounded delivery", () => InputContractAssert.ProvesBoundedDeliveryAsync(new FakeInputProvider())),
            ("fake diagnostics", InputContractAssert.ProvesDiagnosticsAsync),
            ("windows keyboard cooked translation", WindowsKeyboardCookedTranslationMatchesPhase2Contract),
            ("windows mouse cooked translation", WindowsMouseCookedTranslationMatchesPhase2Contract),
            ("windows provider hot plug messages", WindowsProvidersReportHotPlugMessages),
            ("legacy window adapter", LegacyWindowAdapterMatchesCallbackCategories),
            ("dispatcher stops after handled", () => RunSync(DispatcherStopsAfterHandledMessage)),
            ("windows keyboard phase3 text hardening", WindowsKeyboardPhase3TextHardening),
            ("windows keyboard phase3 composition and layout", WindowsKeyboardPhase3CompositionAndLayout),
            ("raw input background acknowledgement", () => RunSync(RawInputBackgroundRequiresAcknowledgement)),
            ("raw mouse coalescing and identity", () => RunSync(RawMouseCoalescingPreservesPhysicalIdentity)),
            ("microphone synthetic bounded capture", MicrophoneSyntheticCaptureHasBoundedDelivery),
            ("microphone lease ownership", () => RunSync(MicrophoneLeaseDisposalInvalidatesMemory)),
            ("microphone default device changes", () => RunSync(MicrophoneDefaultDeviceChangeIsObservable)),
            ("windows microphone isolation", () => RunSync(WindowsMicrophoneContractsAreIsolated)),
            ("camera synthetic preview latest frame", CameraSyntheticPreviewKeepsLatestFrame),
            ("camera loss-sensitive bounded delivery", CameraLossSensitiveModeDropsNewest),
            ("camera frame lease ownership", () => RunSync(CameraFrameLeaseDisposalInvalidatesMemory)),
            ("camera preview adapter", CameraLatestFramePreviewAdapterKeepsOnlyLatest),
            ("windows camera isolation", () => RunSync(WindowsCameraContractsAreIsolated)),
            ("core has no windows references", () => RunSync(CoreHasNoWindowsReferences)),
            ("keyboard mouse no media references", () => RunSync(KeyboardMouseDoNotReferenceCameraOrMicrophone)),
            ("projects have no package references", () => RunSync(ProjectsHaveNoPackageReferences)),
            ("pointer modifiers mirror keyboard modifiers", () => RunSync(PointerModifiersMirrorKeyboardModifiers)),
            ("mouse does not reference keyboard", () => RunSync(MouseDoesNotReferenceKeyboard)),
            ("pointer events carry modifiers", () => RunSync(PointerEventsCarryModifiers)),
            ("public api baseline", () => RunSync(PublicApiBaselineMatches)),
        };

        foreach ((string name, Func<Task> check) in checks)
        {
            try
            {
                await check().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
                return 1;
            }
        }

        return 0;
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static void CoreHasNoWindowsReferences()
    {
        string[] references = typeof(InputDevice).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        AssertFalse(references.Any(static reference => reference.Contains("Windows", StringComparison.OrdinalIgnoreCase)),
            "Broiler.Input must not reference Windows assemblies.");
    }

    private static async Task WindowsKeyboardCookedTranslationMatchesPhase2Contract()
    {
        ManualInputClock clock = new();
        WindowsKeyboardInputDevice device = new(
            new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:keyboard"), InputKind.Keyboard, "Test keyboard"),
            new KeyboardOpenOptions(),
            clock);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        KeyboardKeyEvent? keyEvent = null;
        KeyboardTextEvent? textEvent = null;
        device.KeyChanged += inputEvent => keyEvent = inputEvent;
        device.TextInput += inputEvent => textEvent = inputEvent;

        bool handled = device.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.SysKeyDown,
            new IntPtr(0x10),
            MakeKeyLParam(repeatCount: 2, scanCode: 0x36, isExtended: false, wasDown: true),
            clock.Advance(1)));

        AssertFalse(handled, "System-key messages should not be consumed by default.");
        AssertTrue(keyEvent is not null, "Keyboard key event should be emitted.");
        KeyboardKeyEvent key = keyEvent ?? throw new InvalidOperationException("Keyboard key event should be emitted.");
        AssertEqual(0x10, key.NativeKeyCode, "Keyboard native key is preserved.");
        AssertEqual(2, key.RepeatCount, "Keyboard repeat count is preserved.");
        AssertEqual(0x36, key.ScanCode, "Keyboard scan code is preserved.");
        AssertTrue(key.WasDown, "Keyboard previous-state bit is preserved.");
        AssertTrue(key.IsSystemKey, "System-key messages are marked.");
        AssertEqual(KeyboardKeyLocation.Right, key.Location, "Right Shift location is derived from scan code.");
        AssertEqual(InputEventSource.Semantic, key.Source, "Cooked keyboard messages are semantic events.");

        handled = device.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.Char,
            new IntPtr('z'),
            IntPtr.Zero,
            clock.Advance(1)));

        AssertTrue(handled, "Keyboard text message should be handled.");
        AssertEqual("z", textEvent?.Text, "Keyboard text input preserves translated text.");
        AssertEqual(InputEventSource.Semantic, textEvent?.Source, "Keyboard text input is semantic.");
    }

    private static async Task WindowsMouseCookedTranslationMatchesPhase2Contract()
    {
        ManualInputClock clock = new();
        WindowsMouseInputDevice device = new(
            new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:mouse"), InputKind.Mouse, "Test mouse"),
            new MouseOpenOptions(),
            clock,
            new WindowsMouseMessageOptions(2.0, "client-dip", ConvertWheelScreenPointToClient: false));
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        MouseButtonEvent? buttonEvent = null;
        MouseWheelEvent? wheelEvent = null;
        MouseCaptureLostEvent? captureLostEvent = null;
        device.ButtonChanged += inputEvent => buttonEvent = inputEvent;
        device.WheelChanged += inputEvent => wheelEvent = inputEvent;
        device.CaptureLost += inputEvent => captureLostEvent = inputEvent;

        bool handled = device.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.XButtonDown,
            MakeWParam(lowWord: 0x0020, highWord: 1),
            MakeLParam(20, 10),
            clock.Advance(1)));

        AssertTrue(handled, "Mouse X button message should be handled.");
        AssertTrue(buttonEvent is not null, "Mouse button event should be emitted.");
        MouseButtonEvent button = buttonEvent ?? throw new InvalidOperationException("Mouse button event should be emitted.");
        AssertEqual(MouseButton.X1, button.Button, "XBUTTON1 is preserved.");
        AssertTrue((button.Buttons & MouseButtons.X1) != 0, "XBUTTON1 button state is preserved.");
        AssertEqual(10.0, button.Position.X, "Mouse X coordinate is scaled.");
        AssertEqual(5.0, button.Position.Y, "Mouse Y coordinate is scaled.");
        AssertEqual("client-dip", button.Position.CoordinateSpace, "Coordinate-space label is preserved.");
        AssertEqual(InputEventSource.Semantic, button.Source, "Cooked mouse messages are semantic events.");

        handled = device.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.MouseHorizontalWheel,
            MakeWParam(lowWord: 0, highWord: -120),
            MakeLParam(40, 20),
            clock.Advance(1)));

        AssertTrue(handled, "Horizontal wheel message should be handled.");
        AssertEqual(MouseWheelAxis.Horizontal, wheelEvent?.Axis, "Horizontal wheel axis is preserved.");
        AssertEqual(-1.0, wheelEvent?.DeltaNotches, "Wheel delta is normalized to notches.");

        handled = device.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.CaptureChanged,
            IntPtr.Zero,
            IntPtr.Zero,
            clock.Advance(1)));

        AssertTrue(handled, "Capture-changed message should be handled.");
        AssertTrue(captureLostEvent is not null, "Mouse capture-lost event should be emitted.");
    }

    private static Task WindowsProvidersReportHotPlugMessages()
    {
        ManualInputClock clock = new();
        WindowsKeyboardProvider keyboard = new(clock);
        WindowsMouseProvider mouse = new(clock);
        InputDeviceChange? keyboardChange = null;
        InputDeviceChange? mouseChange = null;
        keyboard.DeviceChanged += change => keyboardChange = change;
        mouse.DeviceChanged += change => mouseChange = change;

        bool keyboardHandled = keyboard.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.InputDeviceChange,
            new IntPtr(1),
            IntPtr.Zero,
            clock.Advance(1)));
        bool mouseHandled = mouse.ProcessMessage(new WindowsInputMessage(
            IntPtr.Zero,
            WindowsMessageIds.InputDeviceChange,
            new IntPtr(2),
            IntPtr.Zero,
            clock.Advance(1)));

        AssertFalse(keyboardHandled, "Hot-plug observers should not consume the shared device-change message.");
        AssertFalse(mouseHandled, "Hot-plug observers should not consume the shared device-change message.");
        AssertEqual(InputDeviceChangeKind.Added, keyboardChange?.Kind, "Keyboard arrival is reported.");
        AssertEqual(InputDeviceChangeKind.Removed, mouseChange?.Kind, "Mouse removal is reported.");
        return Task.CompletedTask;
    }

    private static async Task LegacyWindowAdapterMatchesCallbackCategories()
    {
        ManualInputClock clock = new();
        WindowsKeyboardInputDevice keyboard = new(
            new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:legacy-keyboard"), InputKind.Keyboard, "Legacy keyboard"),
            new KeyboardOpenOptions(),
            clock);
        WindowsMouseInputDevice mouse = new(
            new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:legacy-mouse"), InputKind.Mouse, "Legacy mouse"),
            new MouseOpenOptions(),
            clock,
            new WindowsMouseMessageOptions(ConvertWheelScreenPointToClient: false));
        await keyboard.OpenAsync().ConfigureAwait(false);
        await keyboard.StartAsync().ConfigureAwait(false);
        await mouse.OpenAsync().ConfigureAwait(false);
        await mouse.StartAsync().ConfigureAwait(false);

        using LegacyWindowInputAdapter adapter = new(keyboard, mouse);
        LegacyKeyEvent? keyDown = null;
        LegacyTextInputEvent? textInput = null;
        LegacyPointerEvent? pointerDown = null;
        LegacyMouseWheelEvent? mouseWheel = null;
        bool captureLost = false;
        adapter.KeyDown += inputEvent => keyDown = inputEvent;
        adapter.TextInput += inputEvent => textInput = inputEvent;
        adapter.PointerDown += inputEvent => pointerDown = inputEvent;
        adapter.MouseWheel += inputEvent => mouseWheel = inputEvent;
        adapter.PointerCaptureLost += () => captureLost = true;

        keyboard.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.KeyDown, new IntPtr(0x41), MakeKeyLParam(1, 0x1E, false, false), clock.Advance(1)));
        keyboard.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.Char, new IntPtr('a'), IntPtr.Zero, clock.Advance(1)));
        mouse.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.LeftButtonDown, MakeWParam(0x0001, 0), MakeLParam(4, 6), clock.Advance(1)));
        mouse.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.MouseWheel, MakeWParam(0, 120), MakeLParam(4, 6), clock.Advance(1)));
        mouse.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.CaptureChanged, IntPtr.Zero, IntPtr.Zero, clock.Advance(1)));

        AssertEqual(0x41, keyDown?.VirtualKey, "Legacy key down preserves virtual key.");
        AssertEqual("a", textInput?.Text, "Legacy text input preserves text.");
        AssertEqual(MouseButton.Left, pointerDown?.ChangedButton, "Legacy pointer down preserves changed button.");
        AssertEqual(1.0, mouseWheel?.DeltaNotches, "Legacy wheel preserves vertical wheel notches.");
        AssertTrue(captureLost, "Legacy adapter exposes capture lost.");
    }

    private static void DispatcherStopsAfterHandledMessage()
    {
        ManualWindowsInputHost host = new();
        using WindowsInputMessageDispatcher dispatcher = new(host);
        CountingSink first = new(handled: true);
        CountingSink second = new(handled: true);

        using WindowsInputMessageSubscription firstSubscription = dispatcher.AddSink(first);
        using WindowsInputMessageSubscription secondSubscription = dispatcher.AddSink(second);

        host.Emit(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.MouseMove, IntPtr.Zero, IntPtr.Zero));
        AssertEqual(1, first.Count, "First sink receives the message.");
        AssertEqual(0, second.Count, "Handled messages do not continue to later sinks.");
    }

    private static async Task WindowsKeyboardPhase3TextHardening()
    {
        ManualInputClock clock = new();
        WindowsKeyboardInputDevice device = new(
            new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:keyboard-phase3-text"), InputKind.Keyboard, "Phase 3 keyboard"),
            new KeyboardOpenOptions(),
            clock);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        List<KeyboardTextEvent> textEvents = [];
        KeyboardDeadKeyEvent? deadKey = null;
        device.TextInput += inputEvent => textEvents.Add(inputEvent);
        device.DeadKeyInput += inputEvent => deadKey = inputEvent;

        device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.Char, new IntPtr(0xD83D), IntPtr.Zero, clock.Advance(1)));
        AssertEqual(0, textEvents.Count, "High surrogate is buffered until its low surrogate arrives.");
        device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.Char, new IntPtr(0xDE00), IntPtr.Zero, clock.Advance(1)));
        AssertEqual(1, textEvents.Count, "Surrogate pair is emitted as one text event.");
        AssertEqual(char.ConvertFromUtf32(0x1F600), textEvents[0].Text, "Surrogate pair text is preserved as a Unicode scalar.");

        bool handled = device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.DeadChar, new IntPtr('^'), IntPtr.Zero, clock.Advance(1)));
        AssertTrue(handled, "Dead-key message is handled.");
        AssertEqual("^", deadKey?.Text, "Dead-key text is reported separately from committed text.");

        device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.Char, new IntPtr('ê'), IntPtr.Zero, clock.Advance(1)));
        AssertEqual("ê", textEvents[^1].Text, "Composed character after dead key is reported as committed text.");
    }

    private static async Task WindowsKeyboardPhase3CompositionAndLayout()
    {
        ManualInputClock clock = new();
        WindowsKeyboardInputDevice device = new(
            new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:keyboard-phase3-composition"), InputKind.Keyboard, "Phase 3 keyboard"),
            new KeyboardOpenOptions(),
            clock);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        List<KeyboardCompositionEvent> compositionEvents = [];
        KeyboardLayoutChangedEvent? layoutChanged = null;
        device.CompositionChanged += inputEvent => compositionEvents.Add(inputEvent);
        device.LayoutChanged += inputEvent => layoutChanged = inputEvent;

        AssertFalse(device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.ImeStartComposition, IntPtr.Zero, IntPtr.Zero, clock.Advance(1))),
            "IME start is observed but not consumed.");
        AssertFalse(device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.ImeComposition, IntPtr.Zero, IntPtr.Zero, clock.Advance(1))),
            "IME composition is observed but not consumed.");
        AssertFalse(device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.ImeEndComposition, IntPtr.Zero, IntPtr.Zero, clock.Advance(1))),
            "IME end is observed but not consumed.");

        AssertEqual(3, compositionEvents.Count, "IME milestone events are emitted.");
        AssertEqual(KeyboardCompositionState.Started, compositionEvents[0].State, "IME start state is reported.");
        AssertEqual(KeyboardCompositionState.Unsupported, compositionEvents[1].State, "IME detail is explicitly marked unsupported.");
        AssertEqual(KeyboardCompositionState.Cancelled, compositionEvents[2].State, "IME end state is reported.");

        AssertFalse(device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.InputLanguageChange, new IntPtr(1252), new IntPtr(0x04090409), clock.Advance(1))),
            "Input-language change is observed but not consumed.");
        AssertEqual(1252, layoutChanged?.CharacterSet, "Keyboard layout change preserves character set.");
        AssertEqual(new IntPtr(0x04090409), layoutChanged?.NativeKeyboardLayout, "Keyboard layout change preserves native HKL.");
    }

    private static void RawInputBackgroundRequiresAcknowledgement()
    {
        WindowsRawInputRegistrationOptions background = new(ReceiveInputWhenNotFocused: true);
        try
        {
            background.Validate(new IntPtr(1));
            throw new InvalidOperationException("Background raw input should require explicit acknowledgement.");
        }
        catch (InvalidOperationException)
        {
        }

        WindowsRawInputRegistrationOptions acknowledged = new(
            ReceiveInputWhenNotFocused: true,
            AcknowledgeBackgroundInput: true);
        acknowledged.Validate(new IntPtr(1));
    }

    private static void RawMouseCoalescingPreservesPhysicalIdentity()
    {
        ManualInputClock clock = new();
        WindowsRawMouseBuffer buffer = new(capacity: 4);
        WindowsRawInputDeviceIdentity firstDevice = new(new IntPtr(0x101));
        WindowsRawInputDeviceIdentity secondDevice = new(new IntPtr(0x202));

        buffer.Enqueue(new WindowsRawMouseReport(firstDevice, clock.Advance(1), 1, 2, 0, 0, 0, IsAbsolute: false));
        buffer.Enqueue(new WindowsRawMouseReport(firstDevice, clock.Advance(1), 3, 4, 0, 0, 0, IsAbsolute: false));
        buffer.Enqueue(new WindowsRawMouseReport(secondDevice, clock.Advance(1), 5, 6, 0, 0, 0, IsAbsolute: false));

        WindowsRawMouseBufferMetrics metrics = buffer.Metrics;
        AssertEqual(3L, metrics.AcceptedCount, "All raw mouse reports are counted.");
        AssertEqual(1L, metrics.CoalescedCount, "Adjacent relative movement for one physical device is coalesced.");
        AssertEqual(2, metrics.QueueDepth, "Different physical devices remain distinguishable.");

        AssertTrue(buffer.TryDequeue(out WindowsRawMouseBufferedEvent first), "First raw mouse event is readable.");
        AssertEqual(firstDevice, first.Device, "First physical device identity is preserved.");
        AssertEqual(4, first.DeltaX, "Coalesced X delta is accumulated.");
        AssertEqual(6, first.DeltaY, "Coalesced Y delta is accumulated.");

        AssertTrue(buffer.TryDequeue(out WindowsRawMouseBufferedEvent second), "Second raw mouse event is readable.");
        AssertEqual(secondDevice, second.Device, "Second physical device identity is preserved.");
        AssertEqual("windows:raw:202", second.Device.ToInputDeviceId().Value, "Raw device identity becomes an opaque input device ID.");
    }

    private static async Task MicrophoneSyntheticCaptureHasBoundedDelivery()
    {
        ManualInputClock clock = new();
        FakeMicrophoneProvider provider = new(clock);
        MicrophoneFormat format = new(48_000, 1, 16, MicrophoneSampleFormat.Pcm16);
        MicrophoneOpenOptions options = new(
            sessionOptions: new MicrophoneSessionOptions(
                new InputDeliveryOptions(2, InputDeliveryOverflowPolicy.DropOldest)));

        FakeMicrophoneInputDevice device = (FakeMicrophoneInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor,
            options).ConfigureAwait(false);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        AssertTrue(device.TryCapture([1, 0], format), "First microphone packet should be accepted.");
        AssertTrue(device.TryCapture([2, 0], format, MicrophoneBufferFlags.Silent), "Second microphone packet should be accepted.");
        AssertTrue(device.TryCapture([3, 0], format, MicrophoneBufferFlags.Discontinuous), "Drop-oldest accepts the newest microphone packet.");

        MicrophoneCaptureStatistics statistics = device.CaptureStatistics;
        AssertEqual(3L, statistics.CapturedCount, "All attempted microphone packets are counted.");
        AssertEqual(1L, statistics.DroppedOldestCount, "Bounded microphone delivery drops the oldest packet.");
        AssertEqual(1L, statistics.SilentCount, "Silent microphone packets are counted.");
        AssertEqual(1L, statistics.DiscontinuousCount, "Discontinuous microphone packets are counted.");
        AssertEqual(2, statistics.QueueDepth, "Microphone queue depth is bounded by options.");

        AssertTrue(device.TryRead(out MicrophoneBufferLease? first), "First remaining microphone packet should be readable.");
        using (first)
        {
            AssertEqual((byte)2, first?.Memory.Span[0], "Oldest microphone packet was dropped.");
            AssertTrue((first?.Flags & MicrophoneBufferFlags.Silent) != 0, "Silence flag is preserved.");
        }

        MicrophoneBufferReadyEvent? ready = null;
        device.BufferReady += inputEvent => ready = inputEvent;
        AssertTrue(device.DrainNext(), "Second remaining microphone packet should be delivered.");
        AssertEqual((byte)3, ready?.Buffer.Memory.Span[0], "Microphone buffer event preserves packet memory.");
        AssertTrue((ready?.Buffer.Flags & MicrophoneBufferFlags.Discontinuous) != 0, "Discontinuity flag is preserved.");
        ready?.Buffer.Dispose();

        AssertEqual(2L, device.CaptureStatistics.DeliveredCount, "Read and drained microphone packets are counted as delivered.");
        await device.StopAsync().ConfigureAwait(false);
    }

    private static void MicrophoneLeaseDisposalInvalidatesMemory()
    {
        MicrophoneBufferLease lease = new(
            [1, 2, 3, 4],
            new MicrophoneFormat(48_000, 1, 16, MicrophoneSampleFormat.Pcm16),
            new InputTimestamp(10, 1_000, "test"),
            20,
            MicrophoneBufferFlags.None);

        AssertEqual(2, lease.FrameCount, "Microphone lease derives frame count from format.");
        lease.Dispose();
        AssertTrue(lease.IsDisposed, "Microphone lease reports disposal.");

        try
        {
            _ = lease.Memory;
            throw new InvalidOperationException("Disposed microphone leases should not expose memory.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void MicrophoneDefaultDeviceChangeIsObservable()
    {
        FakeMicrophoneProvider provider = new();
        List<InputDeviceChange> changes = [];
        provider.DeviceChanged += changes.Add;

        InputDeviceDescriptor second = provider.AddDevice("fake:microphone:communications", "Fake communications microphone");
        provider.SwitchDefaultDevice(second);

        AssertEqual(InputDeviceChangeKind.DefaultChanged, changes[^1].Kind, "Microphone default-device changes are observable.");
        AssertEqual(second.Id, changes[^1].Descriptor.Id, "Default-device change names the new endpoint.");
    }

    private static void WindowsMicrophoneContractsAreIsolated()
    {
        Assembly windowsMicrophone = typeof(WindowsMicrophoneProvider).Assembly;
        string[] references = windowsMicrophone.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        AssertFalse(references.Any(static reference =>
                reference.Contains("NAudio", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("MediaFoundation", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Broiler.Graphics", StringComparison.Ordinal)),
            "Windows microphone provider must not introduce encoder, playback, UI, or graphics dependencies.");
    }

    private static async Task CameraSyntheticPreviewKeepsLatestFrame()
    {
        FakeCameraProvider provider = new();
        CameraFormat format = new(2, 2, 30, 1, CameraPixelFormat.Bgra32);
        FakeCameraInputDevice device = (FakeCameraInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor,
            CameraOpenOptions.Default).ConfigureAwait(false);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        AssertTrue(device.TryCapture([1, 0, 0, 0], format), "First preview frame should be accepted.");
        AssertTrue(device.TryCapture([2, 0, 0, 0], format), "Latest-frame preview accepts the replacement frame.");
        AssertTrue(device.TryCapture([3, 0, 0, 0], format, flags: CameraFrameFlags.Discontinuous), "Latest-frame preview accepts the newest frame.");

        CameraCaptureStatistics statistics = device.CaptureStatistics;
        AssertEqual(3L, statistics.CapturedCount, "All camera frames are counted.");
        AssertEqual(2L, statistics.DroppedOldestCount, "Preview mode drops older frames.");
        AssertEqual(1L, statistics.DiscontinuousCount, "Discontinuity is counted.");
        AssertEqual(1, statistics.QueueDepth, "Preview mode keeps bounded latest-frame memory.");

        AssertTrue(device.TryRead(out CameraFrameLease? frame), "Latest camera frame should be readable.");
        using (frame)
        {
            AssertEqual((byte)3, frame?.Memory.Span[0], "Only the newest preview frame remains.");
            AssertEqual(2, frame?.Format.Width, "Negotiated camera format is preserved on the frame.");
            AssertEqual(CameraFrameFlags.Discontinuous, frame?.Flags & CameraFrameFlags.Discontinuous, "Frame flags are preserved.");
        }

        await device.StopAsync().ConfigureAwait(false);
    }

    private static async Task CameraLossSensitiveModeDropsNewest()
    {
        FakeCameraProvider provider = new();
        CameraFormat format = new(2, 2, 30, 1, CameraPixelFormat.Bgra32);
        CameraOpenOptions options = new(
            sessionOptions: new CameraSessionOptions(
                new InputDeliveryOptions(2, InputDeliveryOverflowPolicy.DropNewest),
                CameraFrameDeliveryMode.LossSensitive));
        FakeCameraInputDevice device = (FakeCameraInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor,
            options).ConfigureAwait(false);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        AssertTrue(device.TryCapture([1], format), "First loss-sensitive frame should be accepted.");
        AssertTrue(device.TryCapture([2], format), "Second loss-sensitive frame should be accepted.");
        AssertFalse(device.TryCapture([3], format), "Loss-sensitive mode should reject newest frame when full.");

        CameraCaptureStatistics statistics = device.CaptureStatistics;
        AssertEqual(1L, statistics.DroppedNewestCount, "Loss-sensitive overflow drops the newest frame.");
        AssertEqual(2, statistics.QueueDepth, "Loss-sensitive queue remains bounded.");

        AssertTrue(device.TryRead(out CameraFrameLease? first), "First loss-sensitive frame should remain queued.");
        using (first)
            AssertEqual((byte)1, first?.Memory.Span[0], "Oldest loss-sensitive frame is preserved.");
        AssertTrue(device.TryRead(out CameraFrameLease? second), "Second loss-sensitive frame should remain queued.");
        using (second)
            AssertEqual((byte)2, second?.Memory.Span[0], "Second loss-sensitive frame is preserved.");

        await device.StopAsync().ConfigureAwait(false);
    }

    private static void CameraFrameLeaseDisposalInvalidatesMemory()
    {
        CameraFrameLease frame = new(
            [1, 2, 3, 4],
            new CameraFormat(1, 1, 30, 1, CameraPixelFormat.Bgra32),
            [new CameraFramePlane(0, 4, 4, 1, 1)],
            new InputTimestamp(10, 1_000, "test"),
            7,
            CameraFrameFlags.FormatChanged,
            CameraRotation.Rotate90,
            CameraColorSpace.Rec709);

        AssertEqual(1, frame.Planes.Count, "Camera frame plane metadata is preserved.");
        AssertEqual(CameraRotation.Rotate90, frame.Rotation, "Camera rotation metadata is preserved.");
        AssertEqual(CameraColorSpace.Rec709, frame.ColorSpace, "Camera color metadata is preserved.");
        frame.Dispose();
        AssertTrue(frame.IsDisposed, "Camera frame lease reports disposal.");

        try
        {
            _ = frame.Memory;
            throw new InvalidOperationException("Disposed camera frames should not expose memory.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task CameraLatestFramePreviewAdapterKeepsOnlyLatest()
    {
        FakeCameraProvider provider = new();
        CameraFormat format = new(1, 1, 30, 1, CameraPixelFormat.Gray8);
        FakeCameraInputDevice device = (FakeCameraInputDevice)await provider.OpenAsync(
            provider.DefaultDescriptor,
            CameraOpenOptions.Default).ConfigureAwait(false);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        using CameraLatestFramePreviewAdapter adapter = new(device);
        device.TryCapture([1], format);
        AssertTrue(device.DrainNext(), "First preview frame should drain into adapter.");
        device.TryCapture([2], format);
        AssertTrue(device.DrainNext(), "Second preview frame should replace adapter frame.");

        AssertTrue(adapter.TryAcquireLatest(out CameraFrameLease? latest), "Preview adapter should expose latest frame.");
        using (latest)
            AssertEqual((byte)2, latest?.Memory.Span[0], "Preview adapter keeps the latest frame only.");
        AssertFalse(adapter.TryAcquireLatest(out _), "Preview adapter transfers ownership when acquired.");

        await device.StopAsync().ConfigureAwait(false);
    }

    private static void WindowsCameraContractsAreIsolated()
    {
        Assembly windowsCamera = typeof(WindowsCameraProvider).Assembly;
        string[] references = windowsCamera.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        AssertFalse(references.Any(static reference =>
                reference.Contains("AForge", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("OpenCv", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("NAudio", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Broiler.Graphics", StringComparison.Ordinal)),
            "Windows camera provider must not introduce third-party, playback, UI, or graphics dependencies.");
    }

    private static void KeyboardMouseDoNotReferenceCameraOrMicrophone()
    {
        Assembly[] assemblies =
        [
            typeof(KeyboardInputDevice).Assembly,
            typeof(WindowsKeyboardInputDevice).Assembly,
            typeof(MouseInputDevice).Assembly,
            typeof(WindowsMouseInputDevice).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            string[] references = assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .ToArray();
            AssertFalse(references.Any(static reference =>
                    reference.Contains("Camera", StringComparison.Ordinal) ||
                    reference.Contains("Microphone", StringComparison.Ordinal)),
                $"{assembly.GetName().Name} must not reference Camera or Microphone assemblies.");
        }
    }

    private static void ProjectsHaveNoPackageReferences()
    {
        string componentRoot = FindComponentRoot();
        string[] projects = Directory.GetFiles(componentRoot, "*.csproj", SearchOption.AllDirectories);

        foreach (string project in projects)
        {
            XDocument document = XDocument.Load(project);
            bool hasPackageReference = document.Descendants("PackageReference").Any();
            AssertFalse(hasPackageReference, $"PackageReference is not allowed in {Path.GetRelativePath(componentRoot, project)}.");
        }
    }

    /// <summary>
    /// <see cref="InputModifiers"/> and <c>KeyboardModifierState</c> are declared
    /// separately — the root cannot depend on the keyboard package, and the keyboard
    /// enum is published API that cannot move — so the UI layer casts between them.
    /// That cast is only safe while the two layouts agree, which this pins: a member
    /// added to one and not the other fails here rather than silently mistranslating
    /// a modifier at runtime.
    /// </summary>
    private static void PointerModifiersMirrorKeyboardModifiers()
    {
        string[] pointer = Enum.GetNames<InputModifiers>().OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        string[] keyboard = Enum.GetNames<KeyboardModifierState>().OrderBy(static name => name, StringComparer.Ordinal).ToArray();

        if (!pointer.SequenceEqual(keyboard, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"InputModifiers and KeyboardModifierState must declare the same members. " +
                $"Only in InputModifiers: {string.Join(", ", pointer.Except(keyboard, StringComparer.Ordinal))}. " +
                $"Only in KeyboardModifierState: {string.Join(", ", keyboard.Except(pointer, StringComparer.Ordinal))}.");
        }

        foreach (string name in pointer)
        {
            int pointerValue = (int)Enum.Parse<InputModifiers>(name);
            int keyboardValue = (int)Enum.Parse<KeyboardModifierState>(name);
            AssertEqual(keyboardValue, pointerValue, $"InputModifiers.{name} must have the same value as KeyboardModifierState.{name}.");
        }
    }

    /// <summary>
    /// Every device abstraction depends on the root and on nothing else (ADR 0001).
    /// Modifier state on pointer events is the obvious temptation to break that, so
    /// the rule is asserted rather than trusted.
    /// </summary>
    private static void MouseDoesNotReferenceKeyboard()
    {
        string[] references = typeof(MouseInputDevice).Assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        AssertFalse(
            references.Any(static reference => reference.Contains("Keyboard", StringComparison.Ordinal)),
            "Broiler.Input.Mouse must not reference Broiler.Input.Keyboard; modifier state lives in the root.");
    }

    /// <summary>
    /// A pointer event carries the modifiers held with it, so a Ctrl-click can be told
    /// from a plain one. Defaulting to <see cref="InputModifiers.None"/> keeps every
    /// existing construction source-compatible.
    /// </summary>
    private static void PointerEventsCarryModifiers()
    {
        InputEventHeader header = new(
            InputDeviceId.FromOpaqueValue("mouse"),
            new InputTimestamp(1, TimeSpan.TicksPerSecond, "contract"),
            1);
        InputPoint position = InputPoint.ClientDeviceIndependentPixels(4, 8);

        MouseButtonEvent button = new(
            header, position, MouseButtons.Left, MouseButton.Left, MouseButtonTransition.Down,
            Modifiers: InputModifiers.Control | InputModifiers.Shift);
        AssertEqual(InputModifiers.Control | InputModifiers.Shift, button.Modifiers, "Mouse button events carry modifiers.");

        MouseMoveEvent move = new(header, position, MouseButtons.None, Modifiers: InputModifiers.Alt);
        AssertEqual(InputModifiers.Alt, move.Modifiers, "Mouse move events carry modifiers.");

        MouseWheelEvent wheel = new(
            header, position, MouseButtons.None, MouseWheelAxis.Vertical, 1, Modifiers: InputModifiers.Control);
        AssertEqual(InputModifiers.Control, wheel.Modifiers, "Mouse wheel events carry modifiers.");

        MouseButtonEvent unmodified = new(
            header, position, MouseButtons.Left, MouseButton.Left, MouseButtonTransition.Down);
        AssertEqual(InputModifiers.None, unmodified.Modifiers, "Modifiers default to None.");
    }

    private static void PublicApiBaselineMatches()
    {
        string baselinePath = Path.Combine(AppContext.BaseDirectory, "api-baseline.txt");
        string[] expected = File.ReadAllLines(baselinePath)
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
            .OrderBy(static line => line, StringComparer.Ordinal)
            .ToArray();

        Assembly[] assemblies =
        [
            typeof(InputDevice).Assembly,
            typeof(CameraInputDevice).Assembly,
            typeof(WindowsCameraProvider).Assembly,
            typeof(WindowsInputMessage).Assembly,
            typeof(KeyboardInputDevice).Assembly,
            typeof(WindowsKeyboardInputDevice).Assembly,
            typeof(LegacyWindowInputAdapter).Assembly,
            typeof(MicrophoneInputDevice).Assembly,
            typeof(WindowsMicrophoneProvider).Assembly,
            typeof(MouseInputDevice).Assembly,
            typeof(WindowsMouseInputDevice).Assembly,
        ];

        string[] actual = assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes()
                .Select(type => $"{assembly.GetName().Name}:{type.FullName}"))
            .OrderBy(static line => line, StringComparer.Ordinal)
            .ToArray();

        if (!expected.SequenceEqual(actual))
        {
            string missing = string.Join(Environment.NewLine, expected.Except(actual, StringComparer.Ordinal));
            string added = string.Join(Environment.NewLine, actual.Except(expected, StringComparer.Ordinal));
            throw new InvalidOperationException($"API baseline mismatch. Missing:{Environment.NewLine}{missing}{Environment.NewLine}Added:{Environment.NewLine}{added}");
        }
    }

    private static string FindComponentRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broiler.Input.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Broiler.Input component root not found from {AppContext.BaseDirectory}.");
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static IntPtr MakeKeyLParam(int repeatCount, int scanCode, bool isExtended, bool wasDown)
    {
        int value = repeatCount & 0xFFFF;
        value |= (scanCode & 0xFF) << 16;
        if (isExtended)
            value |= 1 << 24;
        if (wasDown)
            value |= 1 << 30;
        return new IntPtr(value);
    }

    private static IntPtr MakeLParam(int lowWord, int highWord)
    {
        int value = (lowWord & 0xFFFF) | (highWord << 16);
        return new IntPtr(value);
    }

    private static IntPtr MakeWParam(int lowWord, int highWord)
    {
        int value = (lowWord & 0xFFFF) | (highWord << 16);
        return new IntPtr(value);
    }

    private sealed class CountingSink : IWindowsInputMessageSink
    {
        private readonly bool _handled;

        public CountingSink(bool handled)
        {
            _handled = handled;
        }

        public int Count { get; private set; }

        public bool ProcessMessage(in WindowsInputMessage message)
        {
            Count++;
            return _handled;
        }
    }

    private sealed class ManualWindowsInputHost : IWindowsInputHost
    {
        public event Action<WindowsInputMessage>? MessageReceived;

        public IntPtr MessageWindowHandle => IntPtr.Zero;

        public bool IsOnHostThread => true;

        public bool TryPost(Action callback)
        {
            callback();
            return true;
        }

        public void Emit(WindowsInputMessage message)
        {
            MessageReceived?.Invoke(message);
        }
    }
}
