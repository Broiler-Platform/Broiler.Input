using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Input.Linux;

namespace Broiler.Input.Keyboard.Linux;

public sealed class LinuxKeyboardEventTranslator
{
    private static readonly string[] s_digitNames =
    [
        "Digit1",
        "Digit2",
        "Digit3",
        "Digit4",
        "Digit5",
        "Digit6",
        "Digit7",
        "Digit8",
        "Digit9",
        "Digit0",
    ];

    private static readonly string[] s_topRowNames =
    [
        "KeyQ",
        "KeyW",
        "KeyE",
        "KeyR",
        "KeyT",
        "KeyY",
        "KeyU",
        "KeyI",
        "KeyO",
        "KeyP",
    ];

    private static readonly string[] s_homeRowNames =
    [
        "KeyA",
        "KeyS",
        "KeyD",
        "KeyF",
        "KeyG",
        "KeyH",
        "KeyJ",
        "KeyK",
        "KeyL",
    ];

    private static readonly string[] s_bottomRowNames =
    [
        "KeyZ",
        "KeyX",
        "KeyC",
        "KeyV",
        "KeyB",
        "KeyN",
        "KeyM",
    ];

    private readonly HashSet<int> _downKeys = [];

    public bool TryTranslate(
        in LinuxInputEvent inputEvent,
        Func<InputTimestamp, InputEventHeader> createHeader,
        out KeyboardKeyEvent keyboardEvent)
    {
        ArgumentNullException.ThrowIfNull(createHeader);
        keyboardEvent = default;

        if (inputEvent.Type != LinuxEvdevConstants.EvKey || inputEvent.Code >= LinuxEvdevConstants.BtnMisc)
            return false;

        if (inputEvent.Value is not (0 or 1 or 2))
            return false;

        int code = inputEvent.Code;
        bool wasDown = _downKeys.Contains(code);
        KeyboardKeyTransition transition = inputEvent.Value == 0
            ? KeyboardKeyTransition.Up
            : KeyboardKeyTransition.Down;

        if (transition == KeyboardKeyTransition.Up)
            _downKeys.Remove(code);
        else
            _downKeys.Add(code);

        keyboardEvent = new KeyboardKeyEvent(
            createHeader(inputEvent.Timestamp),
            KeyFromEvdevCode(inputEvent.Code),
            transition,
            ReadModifiers(),
            inputEvent.Code,
            inputEvent.Code,
            inputEvent.Value == 2 ? 2 : 1,
            IsExtended(inputEvent.Code),
            wasDown,
            LocationFromEvdevCode(inputEvent.Code),
            InputEventSource.Raw);
        return true;
    }

    private KeyboardModifierState ReadModifiers()
    {
        KeyboardModifierState modifiers = KeyboardModifierState.None;

        if (_downKeys.Contains(LinuxEvdevConstants.KeyLeftShift))
            modifiers |= KeyboardModifierState.Shift | KeyboardModifierState.LeftShift;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyRightShift))
            modifiers |= KeyboardModifierState.Shift | KeyboardModifierState.RightShift;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyLeftCtrl))
            modifiers |= KeyboardModifierState.Control | KeyboardModifierState.LeftControl;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyRightCtrl))
            modifiers |= KeyboardModifierState.Control | KeyboardModifierState.RightControl;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyLeftAlt))
            modifiers |= KeyboardModifierState.Alt | KeyboardModifierState.LeftAlt;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyRightAlt))
            modifiers |= KeyboardModifierState.Alt | KeyboardModifierState.RightAlt;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyLeftMeta))
            modifiers |= KeyboardModifierState.LeftWindows;
        if (_downKeys.Contains(LinuxEvdevConstants.KeyRightMeta))
            modifiers |= KeyboardModifierState.RightWindows;

        return modifiers;
    }

    private static KeyboardKey KeyFromEvdevCode(int code)
    {
        if (code >= LinuxEvdevConstants.Key1 && code <= LinuxEvdevConstants.Key0)
            return KeyboardKey.FromName(s_digitNames[code - LinuxEvdevConstants.Key1]);

        if (code >= LinuxEvdevConstants.KeyQ && code <= LinuxEvdevConstants.KeyP)
            return KeyboardKey.FromName(s_topRowNames[code - LinuxEvdevConstants.KeyQ]);

        if (code >= LinuxEvdevConstants.KeyA && code <= LinuxEvdevConstants.KeyL)
            return KeyboardKey.FromName(s_homeRowNames[code - LinuxEvdevConstants.KeyA]);

        if (code >= LinuxEvdevConstants.KeyZ && code <= LinuxEvdevConstants.KeyM)
            return KeyboardKey.FromName(s_bottomRowNames[code - LinuxEvdevConstants.KeyZ]);

        if (code >= LinuxEvdevConstants.KeyF1 && code <= LinuxEvdevConstants.KeyF10)
            return KeyboardKey.FromName("F" + (code - LinuxEvdevConstants.KeyF1 + 1).ToString(CultureInfo.InvariantCulture));

        return code switch
        {
            LinuxEvdevConstants.KeyEsc => KeyboardKey.FromName("Escape"),
            LinuxEvdevConstants.KeyMinus => KeyboardKey.FromName("Minus"),
            LinuxEvdevConstants.KeyEqual => KeyboardKey.FromName("Equal"),
            LinuxEvdevConstants.KeyBackspace => KeyboardKey.FromName("Backspace"),
            LinuxEvdevConstants.KeyTab => KeyboardKey.FromName("Tab"),
            LinuxEvdevConstants.KeyLeftBrace => KeyboardKey.FromName("BracketLeft"),
            LinuxEvdevConstants.KeyRightBrace => KeyboardKey.FromName("BracketRight"),
            LinuxEvdevConstants.KeyEnter => KeyboardKey.FromName("Enter"),
            LinuxEvdevConstants.KeyLeftCtrl => KeyboardKey.FromName("ControlLeft"),
            LinuxEvdevConstants.KeySemicolon => KeyboardKey.FromName("Semicolon"),
            LinuxEvdevConstants.KeyApostrophe => KeyboardKey.FromName("Quote"),
            LinuxEvdevConstants.KeyGrave => KeyboardKey.FromName("Backquote"),
            LinuxEvdevConstants.KeyLeftShift => KeyboardKey.FromName("ShiftLeft"),
            LinuxEvdevConstants.KeyBackslash => KeyboardKey.FromName("Backslash"),
            LinuxEvdevConstants.KeyComma => KeyboardKey.FromName("Comma"),
            LinuxEvdevConstants.KeyDot => KeyboardKey.FromName("Period"),
            LinuxEvdevConstants.KeySlash => KeyboardKey.FromName("Slash"),
            LinuxEvdevConstants.KeyRightShift => KeyboardKey.FromName("ShiftRight"),
            LinuxEvdevConstants.KeyKpAsterisk => KeyboardKey.FromName("NumpadMultiply"),
            LinuxEvdevConstants.KeyLeftAlt => KeyboardKey.FromName("AltLeft"),
            LinuxEvdevConstants.KeySpace => KeyboardKey.FromName("Space"),
            LinuxEvdevConstants.KeyCapsLock => KeyboardKey.FromName("CapsLock"),
            LinuxEvdevConstants.KeyNumLock => KeyboardKey.FromName("NumLock"),
            LinuxEvdevConstants.KeyScrollLock => KeyboardKey.FromName("ScrollLock"),
            LinuxEvdevConstants.KeyKp7 => KeyboardKey.FromName("Numpad7"),
            LinuxEvdevConstants.KeyKp8 => KeyboardKey.FromName("Numpad8"),
            LinuxEvdevConstants.KeyKp9 => KeyboardKey.FromName("Numpad9"),
            LinuxEvdevConstants.KeyKpMinus => KeyboardKey.FromName("NumpadSubtract"),
            LinuxEvdevConstants.KeyKp4 => KeyboardKey.FromName("Numpad4"),
            LinuxEvdevConstants.KeyKp5 => KeyboardKey.FromName("Numpad5"),
            LinuxEvdevConstants.KeyKp6 => KeyboardKey.FromName("Numpad6"),
            LinuxEvdevConstants.KeyKpPlus => KeyboardKey.FromName("NumpadAdd"),
            LinuxEvdevConstants.KeyKp1 => KeyboardKey.FromName("Numpad1"),
            LinuxEvdevConstants.KeyKp2 => KeyboardKey.FromName("Numpad2"),
            LinuxEvdevConstants.KeyKp3 => KeyboardKey.FromName("Numpad3"),
            LinuxEvdevConstants.KeyKp0 => KeyboardKey.FromName("Numpad0"),
            LinuxEvdevConstants.KeyKpDot => KeyboardKey.FromName("NumpadDecimal"),
            LinuxEvdevConstants.KeyF11 => KeyboardKey.FromName("F11"),
            LinuxEvdevConstants.KeyF12 => KeyboardKey.FromName("F12"),
            LinuxEvdevConstants.KeyKpEnter => KeyboardKey.FromName("NumpadEnter"),
            LinuxEvdevConstants.KeyRightCtrl => KeyboardKey.FromName("ControlRight"),
            LinuxEvdevConstants.KeyKpSlash => KeyboardKey.FromName("NumpadDivide"),
            LinuxEvdevConstants.KeyRightAlt => KeyboardKey.FromName("AltRight"),
            LinuxEvdevConstants.KeyHome => KeyboardKey.FromName("Home"),
            LinuxEvdevConstants.KeyUp => KeyboardKey.FromName("ArrowUp"),
            LinuxEvdevConstants.KeyPageUp => KeyboardKey.FromName("PageUp"),
            LinuxEvdevConstants.KeyLeft => KeyboardKey.FromName("ArrowLeft"),
            LinuxEvdevConstants.KeyRight => KeyboardKey.FromName("ArrowRight"),
            LinuxEvdevConstants.KeyEnd => KeyboardKey.FromName("End"),
            LinuxEvdevConstants.KeyDown => KeyboardKey.FromName("ArrowDown"),
            LinuxEvdevConstants.KeyPageDown => KeyboardKey.FromName("PageDown"),
            LinuxEvdevConstants.KeyInsert => KeyboardKey.FromName("Insert"),
            LinuxEvdevConstants.KeyDelete => KeyboardKey.FromName("Delete"),
            LinuxEvdevConstants.KeyLeftMeta => KeyboardKey.FromName("MetaLeft"),
            LinuxEvdevConstants.KeyRightMeta => KeyboardKey.FromName("MetaRight"),
            _ => KeyboardKey.FromName("KEY_" + code.ToString(CultureInfo.InvariantCulture)),
        };
    }

    private static KeyboardKeyLocation LocationFromEvdevCode(int code) =>
        code switch
        {
            LinuxEvdevConstants.KeyLeftShift or LinuxEvdevConstants.KeyLeftCtrl or LinuxEvdevConstants.KeyLeftAlt or LinuxEvdevConstants.KeyLeftMeta => KeyboardKeyLocation.Left,
            LinuxEvdevConstants.KeyRightShift or LinuxEvdevConstants.KeyRightCtrl or LinuxEvdevConstants.KeyRightAlt or LinuxEvdevConstants.KeyRightMeta => KeyboardKeyLocation.Right,
            >= LinuxEvdevConstants.KeyKp7 and <= LinuxEvdevConstants.KeyKpDot => KeyboardKeyLocation.Numpad,
            LinuxEvdevConstants.KeyKpAsterisk or LinuxEvdevConstants.KeyKpEnter or LinuxEvdevConstants.KeyKpSlash => KeyboardKeyLocation.Numpad,
            _ => KeyboardKeyLocation.Standard,
        };

    private static bool IsExtended(int code) =>
        code is LinuxEvdevConstants.KeyRightCtrl or LinuxEvdevConstants.KeyRightAlt or
            LinuxEvdevConstants.KeyKpEnter or LinuxEvdevConstants.KeyKpSlash or
            LinuxEvdevConstants.KeyHome or LinuxEvdevConstants.KeyUp or LinuxEvdevConstants.KeyPageUp or
            LinuxEvdevConstants.KeyLeft or LinuxEvdevConstants.KeyRight or
            LinuxEvdevConstants.KeyEnd or LinuxEvdevConstants.KeyDown or LinuxEvdevConstants.KeyPageDown or
            LinuxEvdevConstants.KeyInsert or LinuxEvdevConstants.KeyDelete;
}
