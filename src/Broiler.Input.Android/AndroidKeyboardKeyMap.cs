using System.Globalization;
using Broiler.Input.Keyboard;

namespace Broiler.Input.Android;

/// <summary>
/// Maps Android key codes onto Broiler's key names, key locations, and the extended-key flag.
/// </summary>
/// <remarks>
/// The names are W3C UI Events <c>code</c> values ("KeyA", "ArrowDown", "ShiftLeft"), matching what
/// <c>LinuxKeyboardEventTranslator</c> produces, so a control that recognises a key name behaves
/// the same on both platforms. Unmapped codes fall back to <c>"VirtualKey:&lt;code&gt;"</c>, which is
/// the form the Broiler.UI controls already accept as a native-code escape hatch.
/// </remarks>
public static class AndroidKeyboardKeyMap
{
    private static readonly string[] s_letterNames =
    [
        "KeyA", "KeyB", "KeyC", "KeyD", "KeyE", "KeyF", "KeyG", "KeyH", "KeyI",
        "KeyJ", "KeyK", "KeyL", "KeyM", "KeyN", "KeyO", "KeyP", "KeyQ", "KeyR",
        "KeyS", "KeyT", "KeyU", "KeyV", "KeyW", "KeyX", "KeyY", "KeyZ",
    ];

    private static readonly string[] s_digitNames =
    [
        "Digit0", "Digit1", "Digit2", "Digit3", "Digit4",
        "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
    ];

    private static readonly string[] s_numpadNames =
    [
        "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4",
        "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
    ];

    /// <summary>Returns the Broiler key name for an Android key code.</summary>
    public static KeyboardKey ToKeyboardKey(int keyCode) => KeyboardKey.FromName(ToKeyName(keyCode));

    /// <summary>Returns the Broiler key name for an Android key code.</summary>
    public static string ToKeyName(int keyCode)
    {
        if (keyCode >= AndroidKeyEventConstants.KeycodeA && keyCode <= AndroidKeyEventConstants.KeycodeZ)
            return s_letterNames[keyCode - AndroidKeyEventConstants.KeycodeA];

        if (keyCode >= AndroidKeyEventConstants.Keycode0 && keyCode <= AndroidKeyEventConstants.Keycode9)
            return s_digitNames[keyCode - AndroidKeyEventConstants.Keycode0];

        if (keyCode >= AndroidKeyEventConstants.KeycodeNumpad0 && keyCode <= AndroidKeyEventConstants.KeycodeNumpad9)
            return s_numpadNames[keyCode - AndroidKeyEventConstants.KeycodeNumpad0];

        if (keyCode >= AndroidKeyEventConstants.KeycodeF1 && keyCode <= AndroidKeyEventConstants.KeycodeF12)
            return "F" + (keyCode - AndroidKeyEventConstants.KeycodeF1 + 1).ToString(CultureInfo.InvariantCulture);

        return keyCode switch
        {
            AndroidKeyEventConstants.KeycodeDpadUp => "ArrowUp",
            AndroidKeyEventConstants.KeycodeDpadDown => "ArrowDown",
            AndroidKeyEventConstants.KeycodeDpadLeft => "ArrowLeft",
            AndroidKeyEventConstants.KeycodeDpadRight => "ArrowRight",
            AndroidKeyEventConstants.KeycodeDpadCenter => "Enter",
            AndroidKeyEventConstants.KeycodeEnter => "Enter",
            AndroidKeyEventConstants.KeycodeNumpadEnter => "NumpadEnter",
            AndroidKeyEventConstants.KeycodeTab => "Tab",
            AndroidKeyEventConstants.KeycodeSpace => "Space",
            AndroidKeyEventConstants.KeycodeEscape => "Escape",

            // KEYCODE_DEL is backspace on Android; KEYCODE_FORWARD_DEL is the Delete key.
            AndroidKeyEventConstants.KeycodeDel => "Backspace",
            AndroidKeyEventConstants.KeycodeForwardDel => "Delete",
            AndroidKeyEventConstants.KeycodeInsert => "Insert",

            AndroidKeyEventConstants.KeycodeMoveHome => "Home",
            AndroidKeyEventConstants.KeycodeMoveEnd => "End",
            AndroidKeyEventConstants.KeycodePageUp => "PageUp",
            AndroidKeyEventConstants.KeycodePageDown => "PageDown",

            AndroidKeyEventConstants.KeycodeShiftLeft => "ShiftLeft",
            AndroidKeyEventConstants.KeycodeShiftRight => "ShiftRight",
            AndroidKeyEventConstants.KeycodeCtrlLeft => "ControlLeft",
            AndroidKeyEventConstants.KeycodeCtrlRight => "ControlRight",
            AndroidKeyEventConstants.KeycodeAltLeft => "AltLeft",
            AndroidKeyEventConstants.KeycodeAltRight => "AltRight",
            AndroidKeyEventConstants.KeycodeMetaLeft => "MetaLeft",
            AndroidKeyEventConstants.KeycodeMetaRight => "MetaRight",

            AndroidKeyEventConstants.KeycodeCapsLock => "CapsLock",
            AndroidKeyEventConstants.KeycodeNumLock => "NumLock",
            AndroidKeyEventConstants.KeycodeScrollLock => "ScrollLock",

            AndroidKeyEventConstants.KeycodeComma => "Comma",
            AndroidKeyEventConstants.KeycodePeriod => "Period",
            AndroidKeyEventConstants.KeycodeMinus => "Minus",
            AndroidKeyEventConstants.KeycodeEquals => "Equal",
            AndroidKeyEventConstants.KeycodeLeftBracket => "BracketLeft",
            AndroidKeyEventConstants.KeycodeRightBracket => "BracketRight",
            AndroidKeyEventConstants.KeycodeBackslash => "Backslash",
            AndroidKeyEventConstants.KeycodeSemicolon => "Semicolon",
            AndroidKeyEventConstants.KeycodeApostrophe => "Quote",
            AndroidKeyEventConstants.KeycodeSlash => "Slash",
            AndroidKeyEventConstants.KeycodeGrave => "Backquote",

            AndroidKeyEventConstants.KeycodeNumpadDivide => "NumpadDivide",
            AndroidKeyEventConstants.KeycodeNumpadMultiply => "NumpadMultiply",
            AndroidKeyEventConstants.KeycodeNumpadSubtract => "NumpadSubtract",
            AndroidKeyEventConstants.KeycodeNumpadAdd => "NumpadAdd",
            AndroidKeyEventConstants.KeycodeNumpadDot => "NumpadDecimal",
            AndroidKeyEventConstants.KeycodeNumpadComma => "NumpadComma",
            AndroidKeyEventConstants.KeycodeNumpadEquals => "NumpadEqual",

            // Android-specific keys with no desktop code name. They keep their own names so a host
            // can act on them (Back drives history navigation) instead of losing them to the
            // numeric fallback.
            AndroidKeyEventConstants.KeycodeBack => "AndroidBack",
            AndroidKeyEventConstants.KeycodeHome => "AndroidHome",
            AndroidKeyEventConstants.KeycodeMenu => "AndroidMenu",
            AndroidKeyEventConstants.KeycodeSearch => "AndroidSearch",
            AndroidKeyEventConstants.KeycodeVolumeUp => "AudioVolumeUp",
            AndroidKeyEventConstants.KeycodeVolumeDown => "AudioVolumeDown",

            _ => "VirtualKey:" + keyCode.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Returns the physical location Broiler reports for an Android key code.</summary>
    public static KeyboardKeyLocation ToKeyLocation(int keyCode)
    {
        if (keyCode >= AndroidKeyEventConstants.KeycodeNumpad0 && keyCode <= AndroidKeyEventConstants.KeycodeNumpadEquals)
            return KeyboardKeyLocation.Numpad;

        return keyCode switch
        {
            AndroidKeyEventConstants.KeycodeShiftLeft or
            AndroidKeyEventConstants.KeycodeCtrlLeft or
            AndroidKeyEventConstants.KeycodeAltLeft or
            AndroidKeyEventConstants.KeycodeMetaLeft => KeyboardKeyLocation.Left,

            AndroidKeyEventConstants.KeycodeShiftRight or
            AndroidKeyEventConstants.KeycodeCtrlRight or
            AndroidKeyEventConstants.KeycodeAltRight or
            AndroidKeyEventConstants.KeycodeMetaRight => KeyboardKeyLocation.Right,

            _ => KeyboardKeyLocation.Standard,
        };
    }

    /// <summary>
    /// Reports the extended-key flag for the navigation and right-hand modifier cluster, matching
    /// the set the Linux translator marks extended.
    /// </summary>
    public static bool IsExtendedKey(int keyCode)
    {
        if (keyCode is AndroidKeyEventConstants.KeycodeNumpadEnter or AndroidKeyEventConstants.KeycodeNumpadDivide)
            return true;

        return keyCode switch
        {
            AndroidKeyEventConstants.KeycodeCtrlRight or
            AndroidKeyEventConstants.KeycodeAltRight or
            AndroidKeyEventConstants.KeycodeDpadUp or
            AndroidKeyEventConstants.KeycodeDpadDown or
            AndroidKeyEventConstants.KeycodeDpadLeft or
            AndroidKeyEventConstants.KeycodeDpadRight or
            AndroidKeyEventConstants.KeycodeMoveHome or
            AndroidKeyEventConstants.KeycodeMoveEnd or
            AndroidKeyEventConstants.KeycodePageUp or
            AndroidKeyEventConstants.KeycodePageDown or
            AndroidKeyEventConstants.KeycodeInsert or
            AndroidKeyEventConstants.KeycodeForwardDel => true,
            _ => false,
        };
    }
}
