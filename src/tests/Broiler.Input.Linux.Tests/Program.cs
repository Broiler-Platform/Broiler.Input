using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Linux;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Linux;

namespace Broiler.Input.Linux.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<Task> Body)>
        {
            ("linux input dependency probe is stable", () => RunSync(DependencyProbeIsStable)),
            ("event-device access probe reports readable fixtures", () => RunSync(EventDeviceAccessProbeReportsReadableFixtures)),
            ("input_event parser reads 64-bit evdev records", () => RunSync(InputEventParserReads64BitRecords)),
            ("sysfs discovery filters keyboard and mouse devices", SysfsDiscoveryFiltersKeyboardAndMouseDevices),
            ("sysfs discovery classifies a touchpad as an absolute pointer", SysfsDiscoveryClassifiesTouchpad),
            ("mouse translator converts touchpad motion and taps", () => RunSync(MouseTranslatorConvertsTouchpadMotionAndTaps)),
            ("linux providers report refresh add and removal", LinuxProvidersReportRefreshAddAndRemoval),
            ("linux providers require raw input acknowledgement", LinuxProvidersRequireRawInputAcknowledgement),
            ("keyboard translator maps keys modifiers and repeat", () => RunSync(KeyboardTranslatorMapsKeysModifiersAndRepeat)),
            ("mouse translator maps movement buttons and wheels", () => RunSync(MouseTranslatorMapsMovementButtonsAndWheels)),
            ("linux input assemblies avoid graphics and windows references", () => RunSync(LinuxAssembliesAvoidGraphicsAndWindowsReferences)),
        };

        if (string.Equals(Environment.GetEnvironmentVariable("BROILER_LINUX_EVDEV_HARDWARE_TEST"), "1", StringComparison.Ordinal))
            tests.Add(("optional hardware evdev open smoke", OptionalHardwareEvdevOpenSmoke));

        int failures = 0;
        foreach ((string name, Func<Task> body) in tests)
        {
            try
            {
                await body().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        return failures;
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static void DependencyProbeIsStable()
    {
        LinuxInputDependencyReport report = LinuxInputDependencies.CheckBaseline();
        AssertTrue(report.NativeLibraries.Any(static status => status.Id == "udev"),
            "Input dependency probe should include udev.");
        AssertTrue(report.NativeLibraries.All(static status => !string.IsNullOrWhiteSpace(status.Diagnostic)),
            "Native dependency statuses should carry diagnostics.");
        AssertTrue(!string.IsNullOrWhiteSpace(report.EventDevices.Diagnostic),
            "Event-device access status should carry diagnostics.");
    }

    private static void EventDeviceAccessProbeReportsReadableFixtures()
    {
        string directory = Path.Combine(Path.GetTempPath(), "broiler-input-linux-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "event0"), []);
            File.WriteAllBytes(Path.Combine(directory, "not-an-event"), []);

            LinuxEventDeviceAccessStatus status = LinuxEventDeviceAccessProbe.CheckEventDeviceAccess(directory);
            AssertTrue(status.DirectoryExists, "Fixture directory should exist.");
            AssertEqual(1, status.EventDeviceCount, "Only event* files should be counted.");
            AssertEqual(1, status.ReadableEventDeviceCount, "Readable fixture event device should be counted.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void InputEventParserReads64BitRecords()
    {
        byte[] bytes = Combine(
            Event64(1, 2, LinuxEvdevConstants.EvKey, LinuxEvdevConstants.KeyA, 1),
            Event64(1, 3, LinuxEvdevConstants.EvSyn, LinuxEvdevConstants.SynReport, 0));

        List<LinuxInputEvent> events = [];
        int consumed = LinuxInputEventParser.ReadAll64(bytes, events);

        AssertEqual(bytes.Length, consumed, "All complete evdev records should be consumed.");
        AssertEqual(2, events.Count, "Two evdev records should be parsed.");
        AssertEqual(LinuxEvdevConstants.EvKey, events[0].Type, "Event type is preserved.");
        AssertEqual(LinuxEvdevConstants.KeyA, events[0].Code, "Event code is preserved.");
        AssertEqual(1, events[0].Value, "Event value is preserved.");
        AssertEqual(1_000_002L, events[0].Timestamp.Ticks, "Event timestamp is converted to microsecond ticks.");
    }

    private static async Task SysfsDiscoveryFiltersKeyboardAndMouseDevices()
    {
        using LinuxFixture fixture = LinuxFixture.Create();
        fixture.AddKeyboard("event0", "Fixture Keyboard");
        fixture.AddMouse("event1", "Fixture Mouse");
        fixture.AddNoise("event2", "Relative Noise");

        LinuxEvdevProviderOptions options = fixture.Options with { AcknowledgeRawBackgroundInput = true };
        LinuxKeyboardProvider keyboardProvider = new(options);
        LinuxMouseProvider mouseProvider = new(options);

        IReadOnlyList<InputDeviceDescriptor> keyboards = await keyboardProvider.GetDevicesAsync().ConfigureAwait(false);
        IReadOnlyList<InputDeviceDescriptor> mice = await mouseProvider.GetDevicesAsync().ConfigureAwait(false);

        AssertEqual(1, keyboards.Count, "Only the keyboard fixture should be returned as a keyboard.");
        AssertEqual(1, mice.Count, "Only the mouse fixture should be returned as a mouse.");
        AssertEqual("Fixture Keyboard", keyboards[0].DisplayName, "Keyboard display name should come from sysfs.");
        AssertEqual("Fixture Mouse", mice[0].DisplayName, "Mouse display name should come from sysfs.");
        AssertEqual("event0", CapabilityValue(keyboards[0], "event-device"), "Keyboard descriptor exposes only the sanitized event name.");
        AssertEqual("event1", CapabilityValue(mice[0], "event-device"), "Mouse descriptor exposes only the sanitized event name.");
        AssertFalse(keyboards[0].Capabilities.Any(capability => capability.Value.Contains(fixture.Root, StringComparison.Ordinal)),
            "Descriptor diagnostics must not leak raw fixture paths.");
    }

    private static async Task SysfsDiscoveryClassifiesTouchpad()
    {
        using LinuxFixture fixture = LinuxFixture.Create();
        fixture.AddTouchpad("event0", "Fixture Touchpad");

        LinuxEvdevProviderOptions options = fixture.Options with { AcknowledgeRawBackgroundInput = true };
        LinuxMouseProvider mouseProvider = new(options);

        IReadOnlyList<InputDeviceDescriptor> mice = await mouseProvider.GetDevicesAsync().ConfigureAwait(false);

        AssertEqual(1, mice.Count, "A touchpad should be discovered through the mouse pipeline.");
        AssertEqual("Fixture Touchpad", mice[0].DisplayName, "Touchpad display name should come from sysfs.");
        AssertEqual("absolute-touchpad", CapabilityValue(mice[0], "movement"), "A touchpad reports absolute movement.");
        AssertEqual("touchpad", CapabilityValue(mice[0], "pointer"), "A touchpad advertises its pointer type.");
    }

    private static void MouseTranslatorConvertsTouchpadMotionAndTaps()
    {
        // Resolution 0 with a 0..1000 range maps a full-pad traversal to a fixed
        // pixel budget, so 10 units => 16 px (1.6 px/unit).
        LinuxMouseEventTranslator translator = new(
            LinuxPointerMotionMode.AbsoluteTouchpad,
            new LinuxAbsAxis(0, 1000, 0),
            new LinuxAbsAxis(0, 1000, 0));
        List<LinuxMouseTranslatedEvent> output = [];
        long sequence = 0;
        InputDeviceId id = InputDeviceId.FromOpaqueValue("linux:test:touchpad");
        MouseOpenOptions options = new();
        InputEventHeader Header(InputTimestamp timestamp) => new(id, timestamp, ++sequence);

        // Finger down then a first frame only establishes the baseline (no jump).
        translator.Process(EventAt(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.BtnTouch, 1, 0), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvAbs, LinuxEvdevConstants.AbsX, 100, 0), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvAbs, LinuxEvdevConstants.AbsY, 100, 0), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvSyn, LinuxEvdevConstants.SynReport, 0, 0), Header, options, output);
        AssertEqual(0, output.Count, "The first frame of a touch establishes a baseline without moving the pointer.");

        // Second frame produces a relative move from the finger delta.
        translator.Process(EventAt(LinuxEvdevConstants.EvAbs, LinuxEvdevConstants.AbsX, 110, 5_000), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvAbs, LinuxEvdevConstants.AbsY, 105, 5_000), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvSyn, LinuxEvdevConstants.SynReport, 0, 5_000), Header, options, output);

        MouseMoveEvent move = output.Single(input => input.Kind == LinuxMouseTranslatedEventKind.Move).Move
            ?? throw new InvalidOperationException("Touchpad move event missing.");
        AssertEqual(16.0, move.Position.X, "10 units of finger motion should scale to 16 px.");
        AssertEqual(8.0, move.Position.Y, "5 units of finger motion should scale to 8 px.");
        AssertFalse(output.Any(input => input.Kind == LinuxMouseTranslatedEventKind.Button),
            "BTN_TOUCH tracking must not emit button events while the finger moves.");

        // A brief, near-stationary contact release is a tap => synthetic left click.
        output.Clear();
        translator.Process(EventAt(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.BtnTouch, 1, 20_000), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvAbs, LinuxEvdevConstants.AbsX, 200, 20_000), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvAbs, LinuxEvdevConstants.AbsY, 200, 20_000), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvSyn, LinuxEvdevConstants.SynReport, 0, 20_000), Header, options, output);
        translator.Process(EventAt(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.BtnTouch, 0, 100_000), Header, options, output);

        MouseButtonEvent[] buttons = output
            .Where(input => input.Kind == LinuxMouseTranslatedEventKind.Button)
            .Select(input => input.Button ?? throw new InvalidOperationException("Button event missing."))
            .ToArray();
        AssertEqual(2, buttons.Length, "A tap should emit a left button down and up.");
        AssertEqual(MouseButtonTransition.Down, buttons[0].Transition, "Tap begins with a left button down.");
        AssertEqual(MouseButtonTransition.Up, buttons[1].Transition, "Tap ends with a left button up.");
        AssertEqual(MouseButton.Left, buttons[0].Button, "Tap synthesizes a left click.");
    }

    private static async Task LinuxProvidersReportRefreshAddAndRemoval()
    {
        using LinuxFixture fixture = LinuxFixture.Create();
        fixture.AddKeyboard("event0", "Fixture Keyboard");

        LinuxKeyboardProvider provider = new(fixture.Options);
        List<InputDeviceChange> changes = [];
        provider.DeviceChanged += change => changes.Add(change);

        await provider.RefreshDevicesAsync().ConfigureAwait(false);
        AssertTrue(changes.Any(static change => change.Kind == InputDeviceChangeKind.Added),
            "Initial refresh should report an added keyboard.");

        File.Delete(Path.Combine(fixture.InputDirectory, "event0"));
        await provider.RefreshDevicesAsync().ConfigureAwait(false);
        AssertTrue(changes.Any(static change => change.Kind == InputDeviceChangeKind.Removed),
            "Second refresh should report a removed keyboard.");
    }

    private static async Task LinuxProvidersRequireRawInputAcknowledgement()
    {
        using LinuxFixture fixture = LinuxFixture.Create();
        fixture.AddKeyboard("event0", "Fixture Keyboard");

        LinuxKeyboardProvider acknowledged = new(fixture.Options with { AcknowledgeRawBackgroundInput = true });
        InputDeviceDescriptor descriptor = (await acknowledged.GetDevicesAsync().ConfigureAwait(false))[0];
        LinuxKeyboardProvider unacknowledged = new(fixture.Options with { AcknowledgeRawBackgroundInput = false });

        await AssertThrowsAsync<InvalidOperationException>(
            () => unacknowledged.OpenAsync(descriptor, new KeyboardOpenOptions()).AsTask(),
            "Linux evdev raw access should require explicit acknowledgement.").ConfigureAwait(false);
    }

    private static void KeyboardTranslatorMapsKeysModifiersAndRepeat()
    {
        LinuxKeyboardEventTranslator translator = new();
        long sequence = 0;
        InputDeviceId id = InputDeviceId.FromOpaqueValue("linux:test:keyboard");
        InputEventHeader Header(InputTimestamp timestamp) => new(id, timestamp, ++sequence);

        AssertTrue(translator.TryTranslate(Event(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.KeyLeftShift, 1), Header, out KeyboardKeyEvent shiftDown),
            "Left shift down should translate.");
        AssertTrue((shiftDown.Modifiers & KeyboardModifierState.LeftShift) != 0, "Left shift modifier should be tracked.");

        AssertTrue(translator.TryTranslate(Event(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.KeyA, 1), Header, out KeyboardKeyEvent keyDown),
            "A down should translate.");
        AssertEqual("KeyA", keyDown.Key.Name, "KEY_A should map to KeyA.");
        AssertEqual(KeyboardKeyTransition.Down, keyDown.Transition, "Value 1 is a key down transition.");
        AssertEqual(LinuxEvdevConstants.KeyA, keyDown.NativeKeyCode, "Native key code is preserved.");
        AssertEqual(LinuxEvdevConstants.KeyA, keyDown.ScanCode, "Scan code uses the evdev code for Phase 2.");
        AssertTrue((keyDown.Modifiers & KeyboardModifierState.Shift) != 0, "Current modifier state should be included.");

        AssertTrue(translator.TryTranslate(Event(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.KeyA, 2), Header, out KeyboardKeyEvent repeat),
            "A repeat should translate.");
        AssertEqual(KeyboardKeyTransition.Down, repeat.Transition, "Value 2 is represented as another key down.");
        AssertEqual(2, repeat.RepeatCount, "Repeat metadata preserves the evdev repeat value.");
        AssertTrue(repeat.WasDown, "Repeat should record that the key was already down.");

        AssertTrue(translator.TryTranslate(Event(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.KeyA, 0), Header, out KeyboardKeyEvent keyUp),
            "A up should translate.");
        AssertEqual(KeyboardKeyTransition.Up, keyUp.Transition, "Value 0 is a key up transition.");
        AssertTrue(keyUp.WasDown, "Key up should report the previous down state.");
    }

    private static void MouseTranslatorMapsMovementButtonsAndWheels()
    {
        LinuxMouseEventTranslator translator = new();
        List<LinuxMouseTranslatedEvent> output = [];
        long sequence = 0;
        InputDeviceId id = InputDeviceId.FromOpaqueValue("linux:test:mouse");
        MouseOpenOptions options = new();
        InputEventHeader Header(InputTimestamp timestamp) => new(id, timestamp, ++sequence);

        translator.Process(Event(LinuxEvdevConstants.EvKey, LinuxEvdevConstants.BtnLeft, 1), Header, options, output);
        AssertEqual(1, output.Count, "Button down should dispatch immediately.");
        AssertEqual(MouseButton.Left, output[0].Button?.Button, "BTN_LEFT maps to MouseButton.Left.");
        AssertTrue((output[0].Button?.Buttons & MouseButtons.Left) != 0, "Button state includes left after down.");

        output.Clear();
        translator.Process(Event(LinuxEvdevConstants.EvRel, LinuxEvdevConstants.RelX, 5), Header, options, output);
        translator.Process(Event(LinuxEvdevConstants.EvRel, LinuxEvdevConstants.RelY, -2), Header, options, output);
        translator.Process(Event(LinuxEvdevConstants.EvRel, LinuxEvdevConstants.RelWheel, 1), Header, options, output);
        translator.Process(Event(LinuxEvdevConstants.EvRel, LinuxEvdevConstants.RelWheelHiRes, 60), Header, options, output);
        translator.Process(Event(LinuxEvdevConstants.EvSyn, LinuxEvdevConstants.SynReport, 0), Header, options, output);

        MouseMoveEvent move = output.Single(input => input.Kind == LinuxMouseTranslatedEventKind.Move).Move ?? throw new InvalidOperationException("Move event missing.");
        MouseWheelEvent wheel = output.Single(input => input.Kind == LinuxMouseTranslatedEventKind.Wheel).Wheel ?? throw new InvalidOperationException("Wheel event missing.");
        AssertEqual(5.0, move.Position.X, "REL_X should be batched until SYN_REPORT.");
        AssertEqual(-2.0, move.Position.Y, "REL_Y should be batched until SYN_REPORT.");
        AssertEqual("raw-relative-counts", move.Position.CoordinateSpace, "Raw evdev movement should use an explicit coordinate space.");
        AssertEqual(0.5, wheel.DeltaNotches, "REL_WHEEL_HI_RES should convert 120 units to one detent.");
        AssertEqual(MouseWheelAxis.Vertical, wheel.Axis, "Vertical wheel axis is preserved.");
        AssertTrue((wheel.Buttons & MouseButtons.Left) != 0, "Wheel event carries current button state.");
    }

    private static void LinuxAssembliesAvoidGraphicsAndWindowsReferences()
    {
        Assembly[] assemblies =
        [
            typeof(LinuxInputDependencies).Assembly,
            typeof(LinuxKeyboardProvider).Assembly,
            typeof(LinuxMouseProvider).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            string[] references = assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .ToArray();
            AssertFalse(references.Any(static reference =>
                    reference.Contains("Broiler.Graphics", StringComparison.Ordinal) ||
                    reference.Contains("Windows", StringComparison.OrdinalIgnoreCase)),
                $"{assembly.GetName().Name} must not reference Graphics or Windows assemblies.");
        }
    }

    private static async Task OptionalHardwareEvdevOpenSmoke()
    {
        if (!OperatingSystem.IsLinux())
            return;

        LinuxEvdevProviderOptions options = new(AcknowledgeRawBackgroundInput: true, PollTimeoutMilliseconds: 20);
        LinuxKeyboardProvider keyboardProvider = new(options);
        LinuxMouseProvider mouseProvider = new(options);
        IReadOnlyList<InputDeviceDescriptor> keyboards = await keyboardProvider.GetDevicesAsync().ConfigureAwait(false);
        IReadOnlyList<InputDeviceDescriptor> mice = await mouseProvider.GetDevicesAsync().ConfigureAwait(false);

        if (keyboards.Count > 0)
        {
            KeyboardInputDevice keyboard = await keyboardProvider.OpenAsync(keyboards[0], new KeyboardOpenOptions()).ConfigureAwait(false);
            try
            {
                await keyboard.StartAsync().ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
                await keyboard.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await keyboard.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (mice.Count > 0)
        {
            MouseInputDevice mouse = await mouseProvider.OpenAsync(mice[0], new MouseOpenOptions()).ConfigureAwait(false);
            try
            {
                await mouse.StartAsync().ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
                await mouse.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await mouse.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static LinuxInputEvent Event(ushort type, ushort code, int value) =>
        new(new InputTimestamp(1, LinuxInputEventParser.TimestampFrequency, LinuxInputEventParser.TimestampClockName), type, code, value);

    private static LinuxInputEvent EventAt(ushort type, ushort code, int value, long microseconds) =>
        new(new InputTimestamp(microseconds, LinuxInputEventParser.TimestampFrequency, LinuxInputEventParser.TimestampClockName), type, code, value);

    private static string CapabilityValue(InputDeviceDescriptor descriptor, string name) =>
        descriptor.Capabilities.First(capability => capability.Name == name).Value;

    private static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
            throw new InvalidOperationException(message);
        }
        catch (TException)
        {
        }
    }

    private static byte[] Event64(long seconds, long microseconds, ushort type, ushort code, int value)
    {
        byte[] bytes = new byte[LinuxInputEventParser.InputEvent64Size];
        WriteInt64(bytes.AsSpan(0, 8), seconds);
        WriteInt64(bytes.AsSpan(8, 8), microseconds);
        WriteUInt16(bytes.AsSpan(16, 2), type);
        WriteUInt16(bytes.AsSpan(18, 2), code);
        WriteInt32(bytes.AsSpan(20, 4), value);
        return bytes;
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        byte[] combined = new byte[arrays.Sum(static array => array.Length)];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            Buffer.BlockCopy(array, 0, combined, offset, array.Length);
            offset += array.Length;
        }

        return combined;
    }

    private static void WriteInt64(Span<byte> target, long value)
    {
        if (BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteInt64LittleEndian(target, value);
        else
            BinaryPrimitives.WriteInt64BigEndian(target, value);
    }

    private static void WriteInt32(Span<byte> target, int value)
    {
        if (BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteInt32LittleEndian(target, value);
        else
            BinaryPrimitives.WriteInt32BigEndian(target, value);
    }

    private static void WriteUInt16(Span<byte> target, ushort value)
    {
        if (BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(target, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(target, value);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
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

    private sealed class LinuxFixture : IDisposable
    {
        private LinuxFixture(string root, string inputDirectory, string sysfsRoot)
        {
            Root = root;
            InputDirectory = inputDirectory;
            SysfsRoot = sysfsRoot;
            Options = new LinuxEvdevProviderOptions(
                inputDirectory,
                sysfsRoot,
                AcknowledgeRawBackgroundInput: true,
                PollTimeoutMilliseconds: 10);
        }

        public string Root { get; }

        public string InputDirectory { get; }

        public string SysfsRoot { get; }

        public LinuxEvdevProviderOptions Options { get; }

        public static LinuxFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "broiler-linux-evdev-" + Guid.NewGuid().ToString("N"));
            string inputDirectory = Path.Combine(root, "dev", "input");
            string sysfsRoot = Path.Combine(root, "sys", "class", "input");
            Directory.CreateDirectory(inputDirectory);
            Directory.CreateDirectory(sysfsRoot);
            return new LinuxFixture(root, inputDirectory, sysfsRoot);
        }

        public void AddKeyboard(string eventName, string displayName)
        {
            AddEventDevice(
                eventName,
                displayName,
                [LinuxEvdevConstants.EvKey],
                [LinuxEvdevConstants.KeyA, LinuxEvdevConstants.KeyEnter, LinuxEvdevConstants.KeyLeftShift],
                [],
                []);
        }

        public void AddMouse(string eventName, string displayName)
        {
            AddEventDevice(
                eventName,
                displayName,
                [LinuxEvdevConstants.EvKey, LinuxEvdevConstants.EvRel],
                [LinuxEvdevConstants.BtnLeft, LinuxEvdevConstants.BtnRight, LinuxEvdevConstants.BtnMiddle],
                [LinuxEvdevConstants.RelX, LinuxEvdevConstants.RelY, LinuxEvdevConstants.RelWheel, LinuxEvdevConstants.RelWheelHiRes],
                []);
        }

        public void AddTouchpad(string eventName, string displayName)
        {
            AddEventDevice(
                eventName,
                displayName,
                [LinuxEvdevConstants.EvKey, LinuxEvdevConstants.EvAbs],
                [LinuxEvdevConstants.BtnLeft, LinuxEvdevConstants.BtnTouch, LinuxEvdevConstants.BtnToolFinger],
                [],
                [LinuxEvdevConstants.AbsX, LinuxEvdevConstants.AbsY]);
        }

        public void AddNoise(string eventName, string displayName)
        {
            AddEventDevice(
                eventName,
                displayName,
                [LinuxEvdevConstants.EvRel],
                [],
                [LinuxEvdevConstants.RelX],
                []);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private void AddEventDevice(
            string eventName,
            string displayName,
            IEnumerable<int> eventTypes,
            IEnumerable<int> keyCodes,
            IEnumerable<int> relativeAxes,
            IEnumerable<int> absoluteAxes)
        {
            File.WriteAllBytes(Path.Combine(InputDirectory, eventName), []);
            string deviceDirectory = Path.Combine(SysfsRoot, eventName, "device");
            string capabilityDirectory = Path.Combine(deviceDirectory, "capabilities");
            Directory.CreateDirectory(capabilityDirectory);
            Directory.CreateDirectory(Path.Combine(deviceDirectory, "id"));
            File.WriteAllText(Path.Combine(deviceDirectory, "name"), displayName);
            File.WriteAllText(Path.Combine(capabilityDirectory, "ev"), Bitmap(eventTypes));
            File.WriteAllText(Path.Combine(capabilityDirectory, "key"), Bitmap(keyCodes));
            File.WriteAllText(Path.Combine(capabilityDirectory, "rel"), Bitmap(relativeAxes));
            File.WriteAllText(Path.Combine(capabilityDirectory, "abs"), Bitmap(absoluteAxes));
            File.WriteAllText(Path.Combine(deviceDirectory, "id", "bustype"), "0003");
            File.WriteAllText(Path.Combine(deviceDirectory, "id", "vendor"), "1209");
            File.WriteAllText(Path.Combine(deviceDirectory, "id", "product"), "0001");
            File.WriteAllText(Path.Combine(deviceDirectory, "id", "version"), "0001");
        }

        private static string Bitmap(IEnumerable<int> bits)
        {
            int[] values = bits.ToArray();
            if (values.Length == 0)
                return "0";

            int maxWord = values.Max() / 64;
            ulong[] words = new ulong[maxWord + 1];
            foreach (int bit in values)
                words[bit / 64] |= 1UL << (bit % 64);

            Array.Reverse(words);
            return string.Join(" ", words.Select(static word => word.ToString("x", CultureInfo.InvariantCulture)));
        }
    }
}
