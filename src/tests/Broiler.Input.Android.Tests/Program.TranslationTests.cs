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

        device.ProcessMotionEvent(new AndroidMotionSample(Down, [Pointer(1, 10, 20, Stylus, pressure: 0.5f)],
            100, 100, metaState: 0, buttonState: AndroidMotionEventConstants.ButtonStylusPrimary));

        AssertEqual(1, events.Count, "The stylus press produces one pen event.");
        AssertEqual(PenContactState.Pressed, events[0].State, "The pen reports a press.");
        AssertClose(0.5, events[0].Pressure, "Pressure is carried through.");
        AssertTrue(events[0].Buttons.HasFlag(PenButtons.Barrel), "The stylus primary button maps to the barrel button.");

        // An eraser is a tool type on Android, not a button.
        AssertTrue(AndroidPenInputDevice.ToPenButtons(0, Eraser).HasFlag(PenButtons.Eraser),
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

        device.ProcessKeyEvent(new AndroidKeyEventSample(AndroidKeyEventConstants.ActionDown, AndroidKeyEventConstants.KeycodeA,
            ScanCode: 30, MetaState: AndroidKeyEventConstants.MetaShiftOn | AndroidKeyEventConstants.MetaShiftLeftOn,
            UnicodeChar: 'A', EventTimeMilliseconds: 500));

        AssertEqual(1, keys.Count, "The press produces one key event.");
        AssertEqual("KeyA", keys[0].Key.Name, "The key carries its code name.");
        AssertEqual(KeyboardKeyTransition.Down, keys[0].Transition, "The transition is a press.");
        AssertEqual(AndroidKeyEventConstants.KeycodeA, keys[0].NativeKeyCode, "The native code is the Android key code.");
        AssertTrue(keys[0].Modifiers.HasFlag(KeyboardModifierState.LeftShift), "Modifiers come from the meta state.");
        AssertTrue(!keys[0].WasDown, "The first press reports the key was not already down.");

        AssertEqual(1, text.Count, "The press also produces text.");
        AssertEqual("A", text[0].Text, "The host-resolved unicode char is delivered as text.");

        device.ProcessKeyEvent(new AndroidKeyEventSample(AndroidKeyEventConstants.ActionUp,
            AndroidKeyEventConstants.KeycodeA, EventTimeMilliseconds: 520));
        
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
}
