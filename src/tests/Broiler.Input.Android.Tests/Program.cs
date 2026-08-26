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

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new List<(string Name, Func<Task> Body)>
        {
            ("packed action splits into action and pointer index", () => RunSync(PackedActionSplits)),
            ("coordinate space converts physical pixels to dips", () => RunSync(CoordinateSpaceConvertsDensity)),
            ("coordinate space rejects a non-positive density", () => RunSync(CoordinateSpaceRejectsBadDensity)),
            ("key map matches the linux code vocabulary", () => RunSync(KeyMapMatchesCodeVocabulary)),
            ("key map falls back to a virtual-key name", () => RunSync(KeyMapFallsBackToVirtualKey)),
            ("meta state maps to modifiers with sides", () => RunSync(MetaStateMapsModifiers)),
            ("tool type routes fingers, styluses, and mice apart", () => RunSync(ToolTypeRoutes)),
            ("touch tracks two contacts by pointer id", TouchTracksTwoContacts),
            ("touch keeps identity when a finger lifts", TouchKeepsIdentityWhenFingerLifts),
            ("touch cancels every contact on capture loss", TouchCancelsOnCaptureLoss),
            ("touch ignores stylus and mouse pointers", TouchIgnoresNonFingerPointers),
            ("pen reports pressure, buttons, and eraser", PenReportsPressureAndButtons),
            ("pen tilt converts polar to cartesian degrees", () => RunSync(PenTiltConverts)),
            ("keyboard maps a key press to a name and text", KeyboardMapsPressAndText),
            ("keyboard filters control characters from text", () => RunSync(KeyboardFiltersControlCharacters)),
            ("keyboard releases held keys on focus loss", KeyboardReleasesHeldKeys),
            ("ime composition starts, updates, and commits once", ImeCompositionCommitsOnce),
            ("ime commit without composition raises text only", ImeCommitWithoutComposition),
            ("ime cancels composition on focus loss", ImeCancelsOnFocusLoss),
            ("ime resolves the android cursor position", () => RunSync(ImeResolvesCursorPosition)),
            ("ime surfaces edit requests the contract cannot express", ImeSurfacesEditRequests),
            ("provider open is idempotent per descriptor", ProviderOpenIsIdempotent),
            ("provider removal marks the open device unavailable", ProviderRemovalMarksUnavailable),
            ("provider rejects an unregistered descriptor", ProviderRejectsUnknownDescriptor),
            ("provider open honours cancellation", ProviderHonoursCancellation),
            ("device timestamps use the android uptime clock", TimestampsUseAndroidClock),
            ("android assemblies avoid android sdk references", () => RunSync(AssembliesAvoidAndroidSdkReferences)),
        };

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

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"All {tests.Count} Android input tests passed."
            : $"{failures} of {tests.Count} Android input tests failed.");
        return failures;
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

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
    private static int Packed(int action, int pointerIndex) =>
        action | (pointerIndex << AndroidMotionEventConstants.ActionPointerIndexShift);

    private static AndroidMotionSample Motion(
        int packedAction,
        long eventTime,
        params AndroidPointerSample[] pointers) =>
        new(packedAction, pointers, eventTime, pointers.Length > 0 ? eventTime : 0);

    private static AndroidPointerSample Pointer(
        int id,
        float x,
        float y,
        int toolType = Finger,
        float pressure = 1f,
        float tilt = 0f,
        float orientation = 0f) =>
        new(id, x, y, pressure, toolType, orientation, tilt);

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

    // ---- translation primitives -------------------------------------------------------------

    private static void PackedActionSplits()
    {
        int packed = Packed(PointerUp, 1);
        AssertEqual(PointerUp, AndroidMotionEventConstants.MaskedAction(packed), "Masked action drops the pointer index.");
        AssertEqual(1, AndroidMotionEventConstants.PointerIndex(packed), "Pointer index is recovered from the high byte.");
        AssertEqual(0, AndroidMotionEventConstants.PointerIndex(Down), "A plain action has pointer index zero.");
    }

    private static void CoordinateSpaceConvertsDensity()
    {
        AndroidCoordinateSpace space = new(2.75);
        InputPoint point = space.ToInputPoint(275, 550);

        AssertEqual("client-dip", point.CoordinateSpace, "Positions are reported in device-independent pixels.");
        AssertClose(100, point.X, "X divides by the display density.");
        AssertClose(200, point.Y, "Y divides by the display density.");
    }

    private static void CoordinateSpaceRejectsBadDensity()
    {
        AndroidCoordinateSpace space = new();
        AssertThrows<ArgumentOutOfRangeException>(() => space.Density = 0, "A zero density is rejected.");
        AssertThrows<ArgumentOutOfRangeException>(() => space.Density = double.NaN, "A NaN density is rejected.");
        AssertClose(1.0, space.Density, "A rejected assignment leaves the density unchanged.");
    }

    private static void KeyMapMatchesCodeVocabulary()
    {
        // These names are the W3C UI Events code values LinuxKeyboardEventTranslator emits, so a
        // control that recognises a key behaves the same on both platforms.
        AssertEqual("KeyA", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeA), "A maps to KeyA.");
        AssertEqual("KeyZ", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeZ), "Z maps to KeyZ.");
        AssertEqual("Digit0", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.Keycode0), "0 maps to Digit0.");
        AssertEqual("ArrowDown", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeDpadDown), "D-pad down maps to ArrowDown.");
        AssertEqual("Enter", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeEnter), "Enter maps to Enter.");
        AssertEqual("F12", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeF12), "F12 maps by offset.");
        AssertEqual("Numpad9", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeNumpad9), "Numpad 9 maps by offset.");

        // KEYCODE_DEL is backspace on Android; getting this backwards silently deletes the wrong side.
        AssertEqual("Backspace", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeDel), "KEYCODE_DEL is backspace.");
        AssertEqual("Delete", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeForwardDel), "KEYCODE_FORWARD_DEL is delete.");

        AssertEqual(KeyboardKeyLocation.Left, AndroidKeyboardKeyMap.ToKeyLocation(AndroidKeyEventConstants.KeycodeShiftLeft), "Left shift reports a left location.");
        AssertEqual(KeyboardKeyLocation.Right, AndroidKeyboardKeyMap.ToKeyLocation(AndroidKeyEventConstants.KeycodeCtrlRight), "Right control reports a right location.");
        AssertEqual(KeyboardKeyLocation.Numpad, AndroidKeyboardKeyMap.ToKeyLocation(AndroidKeyEventConstants.KeycodeNumpad0 + 5), "Numpad keys report a numpad location.");
        AssertTrue(AndroidKeyboardKeyMap.IsExtendedKey(AndroidKeyEventConstants.KeycodePageUp), "Navigation keys are extended.");
        AssertTrue(!AndroidKeyboardKeyMap.IsExtendedKey(AndroidKeyEventConstants.KeycodeA), "Letters are not extended.");
    }

    private static void KeyMapFallsBackToVirtualKey()
    {
        // 0x7FFF is not a real key code; the fallback is the form the Broiler.UI controls already
        // accept as a native-code escape hatch.
        AssertEqual("VirtualKey:32767", AndroidKeyboardKeyMap.ToKeyName(0x7FFF), "Unmapped codes use the VirtualKey form.");
        AssertEqual("AndroidBack", AndroidKeyboardKeyMap.ToKeyName(AndroidKeyEventConstants.KeycodeBack), "Back keeps a usable name for history navigation.");
    }

    private static void MetaStateMapsModifiers()
    {
        KeyboardModifierState shift = AndroidModifierTranslation.FromMetaState(
            AndroidKeyEventConstants.MetaShiftOn | AndroidKeyEventConstants.MetaShiftLeftOn);
        AssertTrue(shift.HasFlag(KeyboardModifierState.Shift), "The general shift flag is set.");
        AssertTrue(shift.HasFlag(KeyboardModifierState.LeftShift), "The left shift flag is set.");
        AssertTrue(!shift.HasFlag(KeyboardModifierState.RightShift), "The right shift flag stays clear.");

        KeyboardModifierState ctrlAlt = AndroidModifierTranslation.FromMetaState(
            AndroidKeyEventConstants.MetaCtrlOn | AndroidKeyEventConstants.MetaAltRightOn);
        AssertTrue(ctrlAlt.HasFlag(KeyboardModifierState.Control), "Control is set from the general bit.");
        AssertTrue(ctrlAlt.HasFlag(KeyboardModifierState.Alt), "A side-specific alt bit implies the general alt flag.");
        AssertTrue(ctrlAlt.HasFlag(KeyboardModifierState.RightAlt), "The right alt flag is set.");

        AssertEqual(KeyboardModifierState.None, AndroidModifierTranslation.FromMetaState(0), "An empty meta state has no modifiers.");
    }

    private static void ToolTypeRoutes()
    {
        AssertEqual(AndroidPointerDestination.Touch, AndroidPointerRouting.Classify(Finger), "Fingers route to touch.");
        AssertEqual(AndroidPointerDestination.Touch, AndroidPointerRouting.Classify(AndroidMotionEventConstants.ToolTypeUnknown), "Unknown tools route to touch.");
        AssertEqual(AndroidPointerDestination.Pen, AndroidPointerRouting.Classify(Stylus), "Styluses route to pen.");
        AssertEqual(AndroidPointerDestination.Pen, AndroidPointerRouting.Classify(Eraser), "Erasers route to pen.");
        AssertEqual(AndroidPointerDestination.Mouse, AndroidPointerRouting.Classify(MouseTool), "Mouse tools route to mouse.");

        AndroidMotionSample sample = Motion(Down, 10, Pointer(0, 5, 5, Stylus));
        AssertEqual(AndroidPointerDestination.Pen, AndroidPointerRouting.ClassifyActionPointer(sample), "The acting pointer decides the destination.");
        AssertTrue(!AndroidPointerRouting.HasPointerFor(sample, AndroidPointerDestination.Touch), "A stylus-only event has no touch pointer.");
    }

    // ---- touch --------------------------------------------------------------------------------

    private static async Task TouchTracksTwoContacts()
    {
        (_, AndroidTouchInputDevice device, List<TouchContactEvent> events) = await OpenTouchAsync(2.0).ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 100, Pointer(7, 20, 40)));
        device.ProcessMotionEvent(Motion(Packed(PointerDown, 1), 110, Pointer(7, 20, 40), Pointer(9, 60, 80)));
        device.ProcessMotionEvent(Motion(Move, 120, Pointer(7, 22, 40), Pointer(9, 62, 80)));

        AssertEqual(4, events.Count, "Two presses and one move over two contacts produce four events.");
        AssertEqual(7L, events[0].ContactId, "The first contact keeps its pointer id.");
        AssertEqual(TouchContactState.Pressed, events[0].State, "The first contact is pressed.");
        AssertEqual(9L, events[1].ContactId, "The second contact carries the new pointer id.");
        AssertEqual(TouchContactState.Pressed, events[1].State, "The second contact is pressed, not moved.");

        // The whole point of carrying ContactId: two fingers are distinguishable.
        AssertEqual(2, events.Skip(2).Select(static e => e.ContactId).Distinct().Count(), "The move reports both contacts separately.");
        AssertEqual(2, device.ActiveContacts.Count, "Both contacts are tracked as active.");
        AssertClose(10, events[0].Position.X, "Positions are converted to device-independent pixels.");
    }

    private static async Task TouchKeepsIdentityWhenFingerLifts()
    {
        (_, AndroidTouchInputDevice device, List<TouchContactEvent> events) = await OpenTouchAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 10, Pointer(3, 1, 1)));
        device.ProcessMotionEvent(Motion(Packed(PointerDown, 1), 20, Pointer(3, 1, 1), Pointer(4, 2, 2)));

        // The first finger lifts. Android renumbers the remaining pointer to index 0, so anything
        // keyed on index would now confuse the two contacts.
        device.ProcessMotionEvent(Motion(Packed(PointerUp, 0), 30, Pointer(3, 1, 1), Pointer(4, 2, 2)));

        TouchContactEvent released = events[^1];
        AssertEqual(TouchContactState.Released, released.State, "The lifting pointer is released.");
        AssertEqual(3L, released.ContactId, "The released contact is the one that lifted, not the one at index 0.");
        AssertEqual(1, device.ActiveContacts.Count, "One contact remains active.");
        AssertTrue(device.ActiveContacts.Contains(4), "The remaining contact is the second finger.");
    }

    private static async Task TouchCancelsOnCaptureLoss()
    {
        (AndroidTouchProvider provider, AndroidTouchInputDevice device, List<TouchContactEvent> events) =
            await OpenTouchAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 10, Pointer(1, 5, 5)));
        device.ProcessMotionEvent(Motion(Packed(PointerDown, 1), 20, Pointer(1, 5, 5), Pointer(2, 6, 6)));
        events.Clear();

        provider.NotifyCaptureLost("activity paused");

        AssertEqual(2, events.Count, "Every contact still down is cancelled.");
        AssertTrue(events.All(static e => e.State == TouchContactState.Cancelled), "Capture loss cancels rather than releases.");
        AssertEqual(0, device.ActiveContacts.Count, "No contact is left tracked after cancellation.");

        // A backgrounded application that never cancels leaves a contact pressed forever.
        AssertEqual(2, events.Select(static e => e.ContactId).Distinct().Count(), "Both contact ids are cancelled.");
    }

    private static async Task TouchIgnoresNonFingerPointers()
    {
        (_, AndroidTouchInputDevice device, List<TouchContactEvent> events) = await OpenTouchAsync().ConfigureAwait(false);

        bool stylusHandled = device.ProcessMotionEvent(Motion(Down, 10, Pointer(1, 5, 5, Stylus)));
        bool mouseHandled = device.ProcessMotionEvent(Motion(Down, 20, Pointer(2, 5, 5, MouseTool)));

        AssertTrue(!stylusHandled, "A stylus press is not consumed by the touch device.");
        AssertTrue(!mouseHandled, "A mouse press is not consumed by the touch device.");
        AssertEqual(0, events.Count, "Non-finger tools raise no touch contact, so no duplicate delivery.");
    }

    // ---- pen ----------------------------------------------------------------------------------

    private static async Task PenReportsPressureAndButtons()
    {
        (AndroidPenInputDevice device, List<PenContactEvent> events) = await OpenPenAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(new AndroidMotionSample(
            Down,
            [Pointer(1, 10, 20, Stylus, pressure: 0.5f)],
            100,
            100,
            metaState: 0,
            buttonState: AndroidMotionEventConstants.ButtonStylusPrimary));

        AssertEqual(1, events.Count, "The stylus press produces one pen event.");
        AssertEqual(PenContactState.Pressed, events[0].State, "The pen reports a press.");
        AssertClose(0.5, events[0].Pressure, "Pressure is carried through.");
        AssertTrue(events[0].Buttons.HasFlag(PenButtons.Barrel), "The stylus primary button maps to the barrel button.");

        // An eraser is a tool type on Android, not a button.
        AssertTrue(
            AndroidPenInputDevice.ToPenButtons(0, Eraser).HasFlag(PenButtons.Eraser),
            "The eraser tool sets the eraser flag with no button pressed.");

        device.ProcessMotionEvent(Motion(Up, 110, Pointer(1, 10, 20, Stylus)));
        AssertEqual(PenContactState.Released, events[^1].State, "The stylus lift reports a release.");
        AssertTrue(!device.IsContactDown, "The pen is no longer down after a release.");

        // Pressure above 1 is possible on a calibrated digitizer and must not escape the contract.
        device.ProcessMotionEvent(Motion(Down, 120, Pointer(1, 1, 1, Stylus, pressure: 3.5f)));
        AssertClose(1.0, events[^1].Pressure, "Out-of-range pressure is clamped.");
    }

    private static void PenTiltConverts()
    {
        (double flatX, double flatY) = AndroidPenTilt.ToDegrees(0, 0);
        AssertClose(0, flatX, "A perpendicular stylus has no X tilt.");
        AssertClose(0, flatY, "A perpendicular stylus has no Y tilt.");

        // Orientation pi/2 points the tip to the right, so the tilt lands on the X axis.
        (double rightX, double rightY) = AndroidPenTilt.ToDegrees(Math.PI / 4, Math.PI / 2);
        AssertClose(45, rightX, "Tilting to the right produces a positive X tilt.");
        AssertClose(0, rightY, "Tilting to the right leaves Y at zero.");

        // Orientation 0 points away from the user, which is negative TiltY in Pointer Events.
        (double awayX, double awayY) = AndroidPenTilt.ToDegrees(Math.PI / 4, 0);
        AssertClose(0, awayX, "Tilting away leaves X at zero.");
        AssertClose(-45, awayY, "Tilting away from the user produces a negative Y tilt.");
    }

    // ---- keyboard -----------------------------------------------------------------------------

    private static async Task KeyboardMapsPressAndText()
    {
        (AndroidKeyboardInputDevice device, List<KeyboardKeyEvent> keys, List<KeyboardTextEvent> text) =
            await OpenKeyboardAsync().ConfigureAwait(false);

        device.ProcessKeyEvent(new AndroidKeyEventSample(
            AndroidKeyEventConstants.ActionDown,
            AndroidKeyEventConstants.KeycodeA,
            ScanCode: 30,
            MetaState: AndroidKeyEventConstants.MetaShiftOn | AndroidKeyEventConstants.MetaShiftLeftOn,
            UnicodeChar: 'A',
            EventTimeMilliseconds: 500));

        AssertEqual(1, keys.Count, "The press produces one key event.");
        AssertEqual("KeyA", keys[0].Key.Name, "The key carries its code name.");
        AssertEqual(KeyboardKeyTransition.Down, keys[0].Transition, "The transition is a press.");
        AssertEqual(AndroidKeyEventConstants.KeycodeA, keys[0].NativeKeyCode, "The native code is the Android key code.");
        AssertTrue(keys[0].Modifiers.HasFlag(KeyboardModifierState.LeftShift), "Modifiers come from the meta state.");
        AssertTrue(!keys[0].WasDown, "The first press reports the key was not already down.");

        AssertEqual(1, text.Count, "The press also produces text.");
        AssertEqual("A", text[0].Text, "The host-resolved unicode char is delivered as text.");

        device.ProcessKeyEvent(new AndroidKeyEventSample(
            AndroidKeyEventConstants.ActionUp,
            AndroidKeyEventConstants.KeycodeA,
            EventTimeMilliseconds: 520));
        AssertEqual(KeyboardKeyTransition.Up, keys[^1].Transition, "The release reports an up transition.");
        AssertEqual(1, text.Count, "A release produces no additional text.");
    }

    private static void KeyboardFiltersControlCharacters()
    {
        // Enter, tab, and backspace already have key events; inserting their control characters as
        // text would put control codes into the document.
        AssertTrue(AndroidKeyboardInputDevice.ToText('\r') is null, "Carriage return is not delivered as text.");
        AssertTrue(AndroidKeyboardInputDevice.ToText('\t') is null, "Tab is not delivered as text.");
        AssertTrue(AndroidKeyboardInputDevice.ToText(0x7F) is null, "Delete is not delivered as text.");
        AssertTrue(AndroidKeyboardInputDevice.ToText(0) is null, "A key with no character produces no text.");

        // The high bit marks an unresolved dead key, which belongs to the IME path.
        AssertTrue(AndroidKeyboardInputDevice.ToText(unchecked((int)0x80000300)) is null, "A combining accent is not delivered as text.");

        AssertEqual("a", AndroidKeyboardInputDevice.ToText('a'), "A printable character is delivered.");
        AssertEqual("\U0001F600", AndroidKeyboardInputDevice.ToText(0x1F600), "A supplementary-plane character survives as a surrogate pair.");
    }

    private static async Task KeyboardReleasesHeldKeys()
    {
        (AndroidKeyboardInputDevice device, List<KeyboardKeyEvent> keys, _) = await OpenKeyboardAsync().ConfigureAwait(false);

        device.ProcessKeyEvent(new AndroidKeyEventSample(AndroidKeyEventConstants.ActionDown, AndroidKeyEventConstants.KeycodeShiftLeft));
        device.ProcessKeyEvent(new AndroidKeyEventSample(AndroidKeyEventConstants.ActionDown, AndroidKeyEventConstants.KeycodeA));
        keys.Clear();

        // Android delivers no key-up for keys held when the window loses focus, so a modifier would
        // otherwise stay stuck down forever.
        AssertTrue(device.ReleaseHeldKeys("window focus lost"), "Held keys are released on focus loss.");
        AssertEqual(2, keys.Count, "Both held keys are released.");
        AssertTrue(keys.All(static k => k.Transition == KeyboardKeyTransition.Up), "Synthesized events are releases.");
        AssertTrue(keys.All(static k => k.Source == InputEventSource.Synthetic), "Synthesized releases are marked synthetic, not raw.");
        AssertEqual(0, device.DownKeys.Count, "No key is left held.");
    }

    // ---- IME ----------------------------------------------------------------------------------

    private static async Task ImeCompositionCommitsOnce()
    {
        (AndroidTextInputDevice device, List<TextCompositionEvent> composition, List<TextInputEvent> text) =
            await OpenImeAsync().ConfigureAwait(false);

        device.SetComposingText("こ", 1, 1000);
        device.SetComposingText("こん", 1, 1010);
        device.CommitText("今", 1, 1020);

        AssertEqual(3, composition.Count, "Two updates and one commit produce three composition events.");
        AssertEqual(TextCompositionState.Started, composition[0].State, "The first composing text starts a composition.");
        AssertEqual(TextCompositionState.Updated, composition[1].State, "The second updates it.");
        AssertEqual(TextCompositionState.Committed, composition[2].State, "The commit terminates it.");
        AssertEqual("今", composition[2].Text, "The committed event carries the final text.");

        // The delivery rule: committed text arrives once, as a composition event, never also as a
        // text event. Emitting both would make an editor insert the text twice.
        AssertEqual(0, text.Count, "A commit that ends a composition raises no separate text event.");
        AssertTrue(!device.IsComposing, "The composition is finished after the commit.");
    }

    private static async Task ImeCommitWithoutComposition()
    {
        (AndroidTextInputDevice device, List<TextCompositionEvent> composition, List<TextInputEvent> text) =
            await OpenImeAsync().ConfigureAwait(false);

        device.CommitText("hello", 1, 2000);

        AssertEqual(1, text.Count, "Text committed with no composition arrives as a text event.");
        AssertEqual("hello", text[0].Text, "The committed text is delivered verbatim.");
        AssertEqual(0, composition.Count, "No composition event is raised when nothing was composing.");
    }

    private static async Task ImeCancelsOnFocusLoss()
    {
        (AndroidTextInputDevice device, List<TextCompositionEvent> composition, _) = await OpenImeAsync().ConfigureAwait(false);

        device.SetComposingText("partial", 1, 3000);
        composition.Clear();

        AssertTrue(device.NotifyFocusLost(), "Focus loss cancels a composition in flight.");
        AssertEqual(1, composition.Count, "Cancellation raises one event.");
        AssertEqual(TextCompositionState.Cancelled, composition[0].State, "The pre-edit is cancelled, not committed.");
        AssertTrue(!device.IsComposing, "No composition remains after cancellation.");

        // An empty composing text is Android's way of clearing the pre-edit.
        device.SetComposingText("again", 1, 3100);
        composition.Clear();
        device.SetComposingText(string.Empty, 1, 3110);
        AssertEqual(TextCompositionState.Cancelled, composition[^1].State, "Empty composing text clears the pre-edit.");
    }

    private static void ImeResolvesCursorPosition()
    {
        // Android: newCursorPosition > 0 counts from the end of the text, <= 0 from its start.
        AssertEqual(5, AndroidTextInputDevice.ResolveCaret(5, 1), "A value of 1 puts the caret after the text.");
        AssertEqual(0, AndroidTextInputDevice.ResolveCaret(5, 0), "A value of 0 puts the caret before the text.");
        AssertEqual(0, AndroidTextInputDevice.ResolveCaret(5, -3), "Negative values count back from the start and clamp.");
        AssertEqual(5, AndroidTextInputDevice.ResolveCaret(5, 9), "A caret past the end clamps to the text length.");
    }

    private static async Task ImeSurfacesEditRequests()
    {
        (AndroidTextInputDevice device, _, _) = await OpenImeAsync().ConfigureAwait(false);

        List<AndroidTextEditRequest> requests = [];
        device.EditRequested += requests.Add;

        device.DeleteSurroundingText(2, 1);
        device.SetSelection(4, 9);
        device.PerformEditorAction(5);

        AssertEqual(3, requests.Count, "Each mutation the neutral contract cannot express is surfaced.");
        AssertEqual(AndroidTextEditRequestKind.DeleteSurroundingText, requests[0].Kind, "Deletion is reported.");
        AssertEqual(2, requests[0].Start, "The before-length is carried.");
        AssertEqual(1, requests[0].End, "The after-length is carried.");
        AssertEqual(AndroidTextEditRequestKind.SetSelection, requests[1].Kind, "Selection changes are reported.");
        AssertEqual(AndroidTextEditRequestKind.EditorAction, requests[2].Kind, "Editor actions are reported.");

        AssertTrue(!device.DeleteSurroundingText(0, 0), "A no-op deletion raises nothing.");

        // With no editor attached the IME queries degrade to empty rather than throwing.
        AssertEqual(string.Empty, device.GetTextBeforeCursor(10), "An unattached editor returns no text.");
        AssertEqual(string.Empty, device.GetSelectedText(), "An unattached editor reports no selection text.");
    }

    // ---- provider lifecycle --------------------------------------------------------------------

    private static async Task ProviderOpenIsIdempotent()
    {
        AndroidTouchProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultTouchScreen();

        TouchInputDevice first = await provider.OpenAsync(descriptor, new TouchOpenOptions()).ConfigureAwait(false);
        TouchInputDevice second = await provider.OpenAsync(descriptor, new TouchOpenOptions()).ConfigureAwait(false);

        // Two devices for one screen would split contact tracking and desynchronise gestures.
        AssertTrue(ReferenceEquals(first, second), "Opening the same descriptor twice returns one device.");

        IReadOnlyList<InputDeviceDescriptor> devices = await provider.GetDevicesAsync().ConfigureAwait(false);
        AssertEqual(1, devices.Count, "The provider reports the registered device.");
        AssertEqual(InputKind.Touch, devices[0].Kind, "The descriptor is a touch descriptor.");
    }

    private static async Task ProviderRemovalMarksUnavailable()
    {
        (AndroidTouchProvider provider, AndroidTouchInputDevice device, List<TouchContactEvent> events) =
            await OpenTouchAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 10, Pointer(1, 5, 5)));
        events.Clear();

        InputDeviceChange? change = null;
        provider.DeviceChanged += observed => change = observed;

        AssertTrue(provider.RemoveDevice(device.Id), "The registered device is removed.");
        AssertEqual(InputDeviceState.Unavailable, device.State, "Removal marks the open device unavailable.");
        AssertEqual(InputErrorCategory.DeviceRemoved, device.LastFault?.Category, "Removal records a device-removed fault.");
        AssertEqual(InputDeviceChangeKind.Removed, change?.Kind, "The provider emits a removed change.");
        AssertEqual(1, events.Count, "The contact in flight is cancelled before the device goes away.");
        AssertEqual(TouchContactState.Cancelled, events[0].State, "The in-flight contact is cancelled.");

        AssertTrue(!provider.RemoveDevice(device.Id), "Removing an already-removed device reports no change.");
    }

    private static async Task ProviderRejectsUnknownDescriptor()
    {
        AndroidTouchProvider provider = new();
        InputDeviceDescriptor unknown = AndroidInputDescriptors.Touch(androidDeviceId: 99);

        try
        {
            await provider.OpenAsync(unknown, new TouchOpenOptions()).ConfigureAwait(false);
            throw new InvalidOperationException("Opening an unregistered descriptor did not throw.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("android:touch:99", StringComparison.Ordinal))
        {
        }
    }

    private static async Task ProviderHonoursCancellation()
    {
        AndroidTouchProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultTouchScreen();

        using CancellationTokenSource source = new();
        await source.CancelAsync().ConfigureAwait(false);

        try
        {
            await provider.OpenAsync(descriptor, new TouchOpenOptions(), source.Token).ConfigureAwait(false);
            throw new InvalidOperationException("A canceled open did not throw.");
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await provider.GetDevicesAsync(source.Token).ConfigureAwait(false);
            throw new InvalidOperationException("A canceled enumeration did not throw.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task TimestampsUseAndroidClock()
    {
        (_, AndroidTouchInputDevice device, List<TouchContactEvent> events) = await OpenTouchAsync().ConfigureAwait(false);

        device.ProcessMotionEvent(Motion(Down, 123456, Pointer(1, 5, 5)));

        InputTimestamp timestamp = events[0].Header.Timestamp;
        AssertEqual(AndroidUptimeInputClock.ClockName, timestamp.ClockName, "Events carry the Android uptime timebase.");
        AssertEqual(123456L, timestamp.Ticks, "The event's own time is preserved, not re-stamped on arrival.");
        AssertEqual(1000L, timestamp.Frequency, "Android reports uptime in milliseconds.");
        AssertTrue(events[0].Header.SequenceNumber > 0, "Events carry a monotonic sequence number.");
    }

    // ---- boundary ------------------------------------------------------------------------------

    private static void AssembliesAvoidAndroidSdkReferences()
    {
        // The Android backends translate primitives, so they must not drag in Mono.Android. That is
        // what lets them build and be tested without the android workload, and it keeps Android
        // types out of Broiler.Input and Broiler.UI as the architecture requires.
        string[] forbidden = ["Mono.Android", "Java.Interop", "Xamarin.Android", "Microsoft.Maui"];

        Assembly[] assemblies =
        [
            typeof(AndroidMotionSample).Assembly,
            typeof(AndroidTouchInputDevice).Assembly,
            typeof(AndroidPenInputDevice).Assembly,
            typeof(AndroidKeyboardInputDevice).Assembly,
            typeof(AndroidTextInputDevice).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                foreach (string name in forbidden)
                {
                    AssertTrue(
                        !reference.Name!.StartsWith(name, StringComparison.OrdinalIgnoreCase),
                        $"{assembly.GetName().Name} must not reference {reference.Name}.");
                }
            }
        }

        // Graphics and UI stay out too, matching the Linux boundary test.
        foreach (Assembly assembly in assemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                AssertTrue(
                    !reference.Name!.StartsWith("Broiler.Graphics", StringComparison.Ordinal) &&
                    !reference.Name.StartsWith("Broiler.UI", StringComparison.Ordinal),
                    $"{assembly.GetName().Name} must not reference {reference.Name}.");
            }
        }
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

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
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
