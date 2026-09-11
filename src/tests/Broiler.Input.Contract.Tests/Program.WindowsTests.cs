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

internal static partial class Program
{
    private static async Task WindowsKeyboardCookedTranslationMatchesPhase2Contract()
    {
        ManualInputClock clock = new();
        WindowsKeyboardInputDevice device =
            new(new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:keyboard"), InputKind.Keyboard, "Test keyboard"),
            new KeyboardOpenOptions(), clock);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        KeyboardKeyEvent? keyEvent = null;
        KeyboardTextEvent? textEvent = null;

        device.KeyChanged += inputEvent => keyEvent = inputEvent;
        device.TextInput += inputEvent => textEvent = inputEvent;

        bool handled = device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.SysKeyDown, new IntPtr(0x10),
            MakeKeyLParam(repeatCount: 2, scanCode: 0x36, isExtended: false, wasDown: true), clock.Advance(1)));

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

        handled = device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.Char,
            new IntPtr('z'), IntPtr.Zero, clock.Advance(1)));

        AssertTrue(handled, "Keyboard text message should be handled.");
        AssertEqual("z", textEvent?.Text, "Keyboard text input preserves translated text.");
        AssertEqual(InputEventSource.Semantic, textEvent?.Source, "Keyboard text input is semantic.");
    }

    private static async Task WindowsMouseCookedTranslationMatchesPhase2Contract()
    {
        ManualInputClock clock = new();
        WindowsMouseInputDevice device = new(new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:mouse"), InputKind.Mouse, "Test mouse"),
            new MouseOpenOptions(), clock, new WindowsMouseMessageOptions(2.0, "client-dip", ConvertWheelScreenPointToClient: false));

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        MouseButtonEvent? buttonEvent = null;
        MouseWheelEvent? wheelEvent = null;
        MouseCaptureLostEvent? captureLostEvent = null;

        device.ButtonChanged += inputEvent => buttonEvent = inputEvent;
        device.WheelChanged += inputEvent => wheelEvent = inputEvent;
        device.CaptureLost += inputEvent => captureLostEvent = inputEvent;

        bool handled = device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.XButtonDown,
            MakeWParam(lowWord: 0x0020, highWord: 1), MakeLParam(20, 10), clock.Advance(1)));

        AssertTrue(handled, "Mouse X button message should be handled.");
        AssertTrue(buttonEvent is not null, "Mouse button event should be emitted.");
        MouseButtonEvent button = buttonEvent ?? throw new InvalidOperationException("Mouse button event should be emitted.");
        AssertEqual(MouseButton.X1, button.Button, "XBUTTON1 is preserved.");
        AssertTrue((button.Buttons & MouseButtons.X1) != 0, "XBUTTON1 button state is preserved.");
        AssertEqual(10.0, button.Position.X, "Mouse X coordinate is scaled.");
        AssertEqual(5.0, button.Position.Y, "Mouse Y coordinate is scaled.");
        AssertEqual("client-dip", button.Position.CoordinateSpace, "Coordinate-space label is preserved.");
        AssertEqual(InputEventSource.Semantic, button.Source, "Cooked mouse messages are semantic events.");

        handled = device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.MouseHorizontalWheel,
            MakeWParam(lowWord: 0, highWord: -120), MakeLParam(40, 20), clock.Advance(1)));

        AssertTrue(handled, "Horizontal wheel message should be handled.");
        AssertEqual(MouseWheelAxis.Horizontal, wheelEvent?.Axis, "Horizontal wheel axis is preserved.");
        AssertEqual(-1.0, wheelEvent?.DeltaNotches, "Wheel delta is normalized to notches.");

        handled = device.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.CaptureChanged,
            IntPtr.Zero, IntPtr.Zero, clock.Advance(1)));

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

        bool keyboardHandled = keyboard.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.InputDeviceChange,
            new IntPtr(1), IntPtr.Zero, clock.Advance(1)));
        bool mouseHandled = mouse.ProcessMessage(new WindowsInputMessage(IntPtr.Zero, WindowsMessageIds.InputDeviceChange,
            new IntPtr(2), IntPtr.Zero, clock.Advance(1)));

        AssertFalse(keyboardHandled, "Hot-plug observers should not consume the shared device-change message.");
        AssertFalse(mouseHandled, "Hot-plug observers should not consume the shared device-change message.");
        AssertEqual(InputDeviceChangeKind.Added, keyboardChange?.Kind, "Keyboard arrival is reported.");
        AssertEqual(InputDeviceChangeKind.Removed, mouseChange?.Kind, "Mouse removal is reported.");

        return Task.CompletedTask;
    }

    private static async Task LegacyWindowAdapterMatchesCallbackCategories()
    {
        ManualInputClock clock = new();
        WindowsKeyboardInputDevice keyboard = new(new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:legacy-keyboard"),
            InputKind.Keyboard, "Legacy keyboard"), new KeyboardOpenOptions(), clock);
        WindowsMouseInputDevice mouse = new(new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:legacy-mouse"),
            InputKind.Mouse, "Legacy mouse"), new MouseOpenOptions(), clock, new WindowsMouseMessageOptions(ConvertWheelScreenPointToClient: false));

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
        WindowsKeyboardInputDevice device = new(new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:keyboard-phase3-text"), InputKind.Keyboard, "Phase 3 keyboard"),
            new KeyboardOpenOptions(), clock);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        List<KeyboardTextEvent> textEvents = [];
        KeyboardDeadKeyEvent? deadKey = null;

        device.TextInput += textEvents.Add;
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
        WindowsKeyboardInputDevice device = new(new InputDeviceDescriptor(InputDeviceId.FromOpaqueValue("test:keyboard-phase3-composition"), InputKind.Keyboard, "Phase 3 keyboard"),
            new KeyboardOpenOptions(), clock);

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        List<KeyboardCompositionEvent> compositionEvents = [];
        KeyboardLayoutChangedEvent? layoutChanged = null;

        device.CompositionChanged += compositionEvents.Add;
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

        WindowsRawInputRegistrationOptions acknowledged = new(ReceiveInputWhenNotFocused: true, AcknowledgeBackgroundInput: true);
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

    private sealed class CountingSink(bool handled) : IWindowsInputMessageSink
    {
        public int Count { get; private set; }

        public bool ProcessMessage(in WindowsInputMessage message)
        {
            Count++;
            return handled;
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

        public void Emit(WindowsInputMessage message) => MessageReceived?.Invoke(message);
    }
}
