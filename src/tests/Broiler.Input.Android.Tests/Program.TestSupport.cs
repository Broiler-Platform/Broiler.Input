using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Android;
using Broiler.Input.Pen;
using Broiler.Input.Pen.Android;
using Broiler.Input.Text;
using Broiler.Input.Text.Android;
using Broiler.Input.Touch;
using Broiler.Input.Touch.Android;

namespace Broiler.Input.Android.Tests;

internal static partial class Program
{
    // ---- shared constants and helpers -------------------------------------------------------

    private const int Down = AndroidMotionEventConstants.ActionDown;
    private const int Up = AndroidMotionEventConstants.ActionUp;
    private const int Move = AndroidMotionEventConstants.ActionMove;
    private const int Cancel = AndroidMotionEventConstants.ActionCancel;
    private const int PointerDown = AndroidMotionEventConstants.ActionPointerDown;
    private const int PointerUp = AndroidMotionEventConstants.ActionPointerUp;
    private const int Finger = AndroidMotionEventConstants.ToolTypeFinger;
    private const int Stylus = AndroidMotionEventConstants.ToolTypeStylus;
    private const int Eraser = AndroidMotionEventConstants.ToolTypeEraser;
    private const int MouseTool = AndroidMotionEventConstants.ToolTypeMouse;

    /// <summary>Packs an action code and a pointer index the way <c>MotionEvent.getAction()</c> does.</summary>
    private static int Packed(int action, int pointerIndex) => action | (pointerIndex << AndroidMotionEventConstants.ActionPointerIndexShift);

    private static AndroidMotionSample Motion(int packedAction, long eventTime, params AndroidPointerSample[] pointers) =>
        new(packedAction, pointers, eventTime, pointers.Length > 0 ? eventTime : 0);

    private static AndroidPointerSample Pointer(int id, float x, float y, int toolType = Finger,
        float pressure = 1f, float tilt = 0f, float orientation = 0f) => new(id, x, y, pressure, toolType, orientation, tilt);

    private static async Task<(AndroidTouchProvider Provider, AndroidTouchInputDevice Device, List<TouchContactEvent> Events)>
        OpenTouchAsync(double density = 1.0)
    {
        AndroidTouchProvider provider = new(new AndroidCoordinateSpace(density));
        InputDeviceDescriptor descriptor = provider.RegisterDefaultTouchScreen();
        TouchInputDevice opened = await provider.OpenAsync(descriptor, new TouchOpenOptions()).ConfigureAwait(false);
        var device = (AndroidTouchInputDevice)opened;

        List<TouchContactEvent> events = [];
        device.ContactChanged += events.Add;

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        return (provider, device, events);
    }

    private static async Task<(AndroidPenInputDevice Device, List<PenContactEvent> Events)> OpenPenAsync()
    {
        AndroidPenProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultStylus(supportsTilt: true, supportsEraser: true);
        PenInputDevice opened = await provider.OpenAsync(descriptor, new PenOpenOptions()).ConfigureAwait(false);
        var device = (AndroidPenInputDevice)opened;

        List<PenContactEvent> events = [];
        device.ContactChanged += events.Add;

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        return (device, events);
    }

    private static async Task<(AndroidKeyboardInputDevice Device, List<KeyboardKeyEvent> Keys, List<KeyboardTextEvent> Text)>
        OpenKeyboardAsync()
    {
        AndroidKeyboardProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultKeyboard();
        KeyboardInputDevice opened = await provider.OpenAsync(descriptor, new KeyboardOpenOptions()).ConfigureAwait(false);
        var device = (AndroidKeyboardInputDevice)opened;

        List<KeyboardKeyEvent> keys = [];
        List<KeyboardTextEvent> text = [];
        device.KeyChanged += keys.Add;
        device.TextInput += text.Add;

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        return (device, keys, text);
    }

    private static async Task<(AndroidTextInputDevice Device, List<TextCompositionEvent> Composition, List<TextInputEvent> Text)>
        OpenImeAsync()
    {
        AndroidTextInputProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterInputMethod();
        TextInputDevice opened = await provider.OpenAsync(descriptor, new TextInputOpenOptions()).ConfigureAwait(false);
        var device = (AndroidTextInputDevice)opened;

        List<TextCompositionEvent> composition = [];
        List<TextInputEvent> text = [];
        device.CompositionChanged += composition.Add;
        device.TextInput += text.Add;

        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);
        return (device, composition, text);
    }

    // ---- assertions ----------------------------------------------------------------------------

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 1e-6)
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
